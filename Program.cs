using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization options
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();
app.UseStaticFiles();

// In-memory storage for parsed cards (in production, use a database)
var cardStore = new Dictionary<string, List<FlashCard>>();

// Get configuration
app.MapGet("/api/config", () =>
{
    return Results.Ok(new
    {
        elevenLabsApiKey = app.Configuration["ElevenLabs:ApiKey"] ?? "",
        geminiApiKey = app.Configuration["Gemini:ApiKey"] ?? "",
        defaultVoiceId = app.Configuration["ElevenLabs:DefaultVoiceId"] ?? "pNInz6obpgDQGcFmaJgB",
        ankiConnectUrl = app.Configuration["AnkiConnect:Url"] ?? "http://localhost:8765",
        defaultDeck = app.Configuration["AnkiConnect:DefaultDeck"] ?? "Default",
        defaultModel = app.Configuration["AnkiConnect:DefaultModel"] ?? "Basic"
    });
});

// Serve the UI
app.MapGet("/", () => Results.Content(File.ReadAllText("wwwroot/index.html"), "text/html"));

// Parse text input
app.MapPost("/api/cards/parse", (ParseTextRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest("No text provided");

    var lines = request.Text.Split('\n')
                      .Select(l => l.Trim())
                      .Where(l => !string.IsNullOrWhiteSpace(l))
                      .ToList();
    
    var cards = new List<FlashCard>();
    
    // Process in pairs: Line 1 = Greek + English, Line 2 = Russian
    for (int i = 0; i < lines.Count; i += 2)
    {
        var firstLine = lines[i];
        
        // Find where Greek ends and English begins
        // Greek uses characters in ranges: 0370–03FF (Greek and Coptic), 1F00–1FFF (Greek Extended)
        var greek = "";
        var english = "";
        
        var lastGreekCharIndex = -1;
        
        // Find the last Greek letter (not punctuation)
        for (int j = 0; j < firstLine.Length; j++)
        {
            var c = firstLine[j];
            if ((c >= '\u0370' && c <= '\u03FF') || (c >= '\u1F00' && c <= '\u1FFF'))
            {
                lastGreekCharIndex = j;
            }
        }
        
        if (lastGreekCharIndex > -1)
        {
            // Include Greek letters and any punctuation immediately after (. ; !)
            var endOfGreek = lastGreekCharIndex;
            for (int j = lastGreekCharIndex + 1; j < firstLine.Length; j++)
            {
                var c = firstLine[j];
                if (c == '.' || c == ';' || c == '!' || c == '?' || c == ' ')
                {
                    if (c != ' ') endOfGreek = j; // Include punctuation but not trailing spaces
                }
                else
                {
                    break; // Stop at first non-punctuation, non-space character
                }
            }
            
            greek = firstLine.Substring(0, endOfGreek + 1).Trim();
            
            // Everything after Greek (skip spaces) is English
            if (endOfGreek + 1 < firstLine.Length)
            {
                english = firstLine.Substring(endOfGreek + 1).Trim();
            }
        }
        
        // Get Russian from next line
        var russian = "";
        if (i + 1 < lines.Count)
        {
            russian = lines[i + 1].Trim();
        }
        
        // Combine English and Russian for the translation
        var translation = english;
        if (!string.IsNullOrWhiteSpace(russian))
        {
            translation += string.IsNullOrWhiteSpace(translation) ? russian : $"\n{russian}";
        }
        
        if (!string.IsNullOrWhiteSpace(greek))
        {
            cards.Add(new FlashCard
            {
                Id = Guid.NewGuid().ToString(),
                Greek = greek,
                Translation = translation,
                Selected = true
            });
        }
    }
    
    var sessionId = Guid.NewGuid().ToString();
    cardStore[sessionId] = cards;
    
    return Results.Ok(new { sessionId, cards });
});

// Get cards for a session
app.MapGet("/api/cards/{sessionId}", (string sessionId) =>
{
    if (!cardStore.ContainsKey(sessionId))
        return Results.NotFound();
    
    return Results.Ok(cardStore[sessionId]);
});

// Update a card
app.MapPut("/api/cards/{sessionId}/{cardId}", (string sessionId, string cardId, FlashCard updatedCard) =>
{
    if (!cardStore.ContainsKey(sessionId))
        return Results.NotFound();
    
    var card = cardStore[sessionId].FirstOrDefault(c => c.Id == cardId);
    if (card == null)
        return Results.NotFound();
    
    card.Greek = updatedCard.Greek;
    card.Translation = updatedCard.Translation;
    card.RussianExplanation = updatedCard.RussianExplanation;
    card.Selected = updatedCard.Selected;
    
    return Results.Ok(card);
});

// Check for duplicates in Anki
app.MapPost("/api/cards/check-duplicates", async (IHttpClientFactory httpClientFactory, DuplicateCheckRequest request, ILogger<Program> logger) =>
{
    var ankiConnectUrl = request.AnkiConnectUrl ?? "http://localhost:8765";
    var http = httpClientFactory.CreateClient();
    
    if (!cardStore.ContainsKey(request.SessionId))
        return Results.NotFound("Session not found");
    
    var cards = cardStore[request.SessionId];
    var duplicates = new Dictionary<string, bool>();
    
    // Build a batch query to check all cards at once
    var deckFilter = !string.IsNullOrEmpty(request.DeckName) ? $"deck:\\\"{request.DeckName}\\\" " : "";
    var expressions = cards.Select(c => c.Greek.Replace("\"", "\\\"")).ToList();
    
    // Create an OR query for all expressions
    var queryParts = expressions.Select(exp => $"(Expression:\\\"{exp}\\\")");
    var combinedQuery = $"{deckFilter}({string.Join(" OR ", queryParts)})";
    
    var json = $"{{\"action\": \"findNotes\", \"version\": 6, \"params\": {{\"query\": \"{combinedQuery}\"}}}}";
    
    logger.LogInformation($"Batch checking {cards.Count} cards for duplicates");
    
    try
    {
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await http.PostAsync(ankiConnectUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();
        
        logger.LogInformation($"Batch duplicate check response: {responseBody}");
        
        var result = System.Text.Json.JsonSerializer.Deserialize<AnkiConnectResponse<List<long>>>(responseBody, jsonOptions);
        var foundNoteIds = result?.Result ?? new List<long>();
        
        if (foundNoteIds.Count > 0)
        {
            // Get the actual field values for these notes to match them to our cards
            var notesInfoJson = $"{{\"action\": \"notesInfo\", \"version\": 6, \"params\": {{\"notes\": [{string.Join(",", foundNoteIds)}]}}}}";
            var notesInfoContent = new StringContent(notesInfoJson, System.Text.Encoding.UTF8, "application/json");
            var notesInfoResponse = await http.PostAsync(ankiConnectUrl, notesInfoContent);
            var notesInfoBody = await notesInfoResponse.Content.ReadAsStringAsync();
            
            logger.LogInformation($"Notes info response: {notesInfoBody}");
            
            var notesInfoResult = System.Text.Json.JsonSerializer.Deserialize<AnkiConnectResponse<List<Dictionary<string, object>>>>(notesInfoBody, jsonOptions);
            var existingExpressions = new HashSet<string>();
            
            if (notesInfoResult?.Result != null)
            {
                foreach (var noteInfo in notesInfoResult.Result)
                {
                    if (noteInfo.ContainsKey("fields"))
                    {
                        var fieldsElement = (System.Text.Json.JsonElement)noteInfo["fields"];
                        if (fieldsElement.TryGetProperty("Expression", out var expressionProp) && 
                            expressionProp.TryGetProperty("value", out var expressionValue))
                        {
                            existingExpressions.Add(expressionValue.GetString() ?? "");
                        }
                    }
                }
            }
            
            // Now mark duplicates based on the expressions we found
            foreach (var card in cards)
            {
                var isDuplicate = existingExpressions.Contains(card.Greek);
                duplicates[card.Id] = isDuplicate;
                
                if (isDuplicate)
                {
                    card.Selected = false;
                    logger.LogInformation($"Found duplicate, deselecting: {card.Greek}");
                }
            }
        }
        else
        {
            // No duplicates found
            foreach (var card in cards)
            {
                duplicates[card.Id] = false;
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error checking duplicates");
        foreach (var card in cards)
        {
            duplicates[card.Id] = false;
        }
    }
    
    return Results.Ok(duplicates);
});

// Generate audio from ElevenLabs
app.MapPost("/api/audio/generate", async (IHttpClientFactory httpClientFactory, AudioGenerateRequest request) =>
{
    var http = httpClientFactory.CreateClient();
    
    if (string.IsNullOrEmpty(request.ApiKey))
        return Results.BadRequest("ElevenLabs API key is required");
    
    var voiceId = request.VoiceId ?? "pNInz6obpgDQGcFmaJgB"; // Default voice (Adam)
    var url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";
    
    var requestBody = new
    {
        text = request.Text,
        model_id = "eleven_multilingual_v2",
        voice_settings = new
        {
            stability = 0.5,
            similarity_boost = 0.75
        }
    };
    
    var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
    httpRequest.Headers.Add("xi-api-key", request.ApiKey);
    httpRequest.Content = JsonContent.Create(requestBody);
    
    try
    {
        var response = await http.SendAsync(httpRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return Results.BadRequest($"ElevenLabs API error: {error}");
        }
        
        var audioBytes = await response.Content.ReadAsByteArrayAsync();
        var base64Audio = Convert.ToBase64String(audioBytes);
        
        return Results.Ok(new { audio = base64Audio });
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Error generating audio: {ex.Message}");
    }
});

// Create a single card in Anki
app.MapPost("/api/cards/create-single", async (IHttpClientFactory httpClientFactory, CreateSingleCardRequest request, ILogger<Program> logger) =>
{
    var http = httpClientFactory.CreateClient();
    var ankiConnectUrl = request.AnkiConnectUrl ?? "http://localhost:8765";
    
    if (!cardStore.ContainsKey(request.SessionId))
        return Results.NotFound(new { success = false, error = "Session not found" });
    
    var card = cardStore[request.SessionId].FirstOrDefault(c => c.Id == request.CardId);
    if (card == null)
        return Results.NotFound(new { success = false, error = "Card not found" });
    
    try
    {
        // Generate audio
        string? audioBase64 = null;
        if (!string.IsNullOrEmpty(request.ElevenLabsApiKey))
        {
            var audioUrl = $"https://api.elevenlabs.io/v1/text-to-speech/{request.VoiceId ?? "ejJ1ETWS2ohLMMeCu1H3"}";
            var audioRequest = new HttpRequestMessage(HttpMethod.Post, audioUrl);
            audioRequest.Headers.Add("xi-api-key", request.ElevenLabsApiKey);
            audioRequest.Content = JsonContent.Create(new
            {
                text = card.Greek,
                model_id = "eleven_multilingual_v2",
                voice_settings = new { stability = 0.5, similarity_boost = 0.75 }
            });
            
            var audioResponse = await http.SendAsync(audioRequest);
            if (audioResponse.IsSuccessStatusCode)
            {
                var audioBytes = await audioResponse.Content.ReadAsByteArrayAsync();
                audioBase64 = Convert.ToBase64String(audioBytes);
            }
        }
        
        // Create the note in Anki
        var audioFileName = $"greek_{card.Id}.mp3";
        
        // Convert newlines to HTML breaks for Anki
        var meaningHtml = card.Translation.Replace("\n", "<br>");
        var explanationHtml = card.RussianExplanation.Replace("\n", "<br>");
        
        var fields = new Dictionary<string, string>
        {
            ["Expression"] = card.Greek,
            ["Meaning"] = meaningHtml,
            ["RussianExplanation"] = explanationHtml,
            ["Audio"] = "" // AnkiConnect will populate this when processing the audio array
        };
        
        var ankiRequest = new
        {
            action = "addNote",
            version = 6,
            @params = new
            {
                note = new
                {
                    deckName = request.DeckName ?? "Default",
                    modelName = request.ModelName ?? "Basic",
                    fields = fields,
                    tags = new[] { "greek", "elevenlabs" },
                    audio = !string.IsNullOrEmpty(audioBase64) ? new[]
                    {
                        new
                        {
                            data = audioBase64,
                            filename = audioFileName,
                            fields = new[] { "Audio" }
                        }
                    } : null
                }
            }
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(ankiRequest);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await http.PostAsync(ankiConnectUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();
        var result = System.Text.Json.JsonSerializer.Deserialize<AnkiConnectResponse<long?>>(responseBody, jsonOptions);
        
        return Results.Ok(new
        {
            success = result?.Result != null && result.Result > 0,
            error = result?.Error
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, $"Error creating card: {card.Greek}");
        return Results.Ok(new
        {
            success = false,
            error = ex.Message
        });
    }
});

// Create cards in Anki (batch)
app.MapPost("/api/cards/create", async (IHttpClientFactory httpClientFactory, CreateCardsRequest request, ILogger<Program> logger) =>
{
    var http = httpClientFactory.CreateClient();
    var ankiConnectUrl = request.AnkiConnectUrl ?? "http://localhost:8765";
    
    if (!cardStore.ContainsKey(request.SessionId))
        return Results.NotFound("Session not found");
    
    var cards = cardStore[request.SessionId].Where(c => c.Selected).ToList();
    logger.LogInformation($"Creating {cards.Count} cards in Anki");
    var results = new List<CardCreationResult>();
    
    foreach (var card in cards)
    {
        try
        {
            // Generate audio
            string? audioBase64 = null;
            if (!string.IsNullOrEmpty(request.ElevenLabsApiKey))
            {
                var audioUrl = $"https://api.elevenlabs.io/v1/text-to-speech/{request.VoiceId ?? "pNInz6obpgDQGcFmaJgB"}";
                var audioRequest = new HttpRequestMessage(HttpMethod.Post, audioUrl);
                audioRequest.Headers.Add("xi-api-key", request.ElevenLabsApiKey);
                audioRequest.Content = JsonContent.Create(new
                {
                    text = card.Greek,
                    model_id = "eleven_multilingual_v2",
                    voice_settings = new { stability = 0.5, similarity_boost = 0.75 }
                });
                
                var audioResponse = await http.SendAsync(audioRequest);
                if (audioResponse.IsSuccessStatusCode)
                {
                    var audioBytes = await audioResponse.Content.ReadAsByteArrayAsync();
                    audioBase64 = Convert.ToBase64String(audioBytes);
                }
            }
            
            // Create the note in Anki
            var audioFileName = $"greek_{card.Id}.mp3";
            
            // Convert newlines to HTML breaks for Anki
            var meaningHtml = card.Translation.Replace("\n", "<br>");
            var explanationHtml = card.RussianExplanation.Replace("\n", "<br>");
            
            var fields = new Dictionary<string, string>
            {
                ["Expression"] = card.Greek,
                ["Meaning"] = meaningHtml,
                ["RussianExplanation"] = explanationHtml,
                ["Audio"] = "" // AnkiConnect will populate this when processing the audio array
            };
            
            var ankiRequest = new
            {
                action = "addNote",
                version = 6,
                @params = new
                {
                    note = new
                    {
                        deckName = request.DeckName ?? "Default",
                        modelName = request.ModelName ?? "Basic",
                        fields = fields,
                        tags = new[] { "greek", "elevenlabs" },
                        audio = !string.IsNullOrEmpty(audioBase64) ? new[]
                        {
                            new
                            {
                                data = audioBase64,
                                filename = audioFileName,
                                fields = new[] { "Audio" }
                            }
                        } : null
                    }
                }
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(ankiRequest);
            logger.LogInformation($"Creating card: {card.Greek}");
            
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync(ankiConnectUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            
            logger.LogInformation($"Anki response: {responseBody}");
            
            var result = System.Text.Json.JsonSerializer.Deserialize<AnkiConnectResponse<long?>>(responseBody, jsonOptions);
            
            results.Add(new CardCreationResult
            {
                CardId = card.Id,
                Success = result?.Result != null && result.Result > 0,
                Error = result?.Error
            });
            
            logger.LogInformation($"Card creation result - Success: {result?.Result != null && result.Result > 0}, Error: {result?.Error}");
        }
        catch (Exception ex)
        {
            results.Add(new CardCreationResult
            {
                CardId = card.Id,
                Success = false,
                Error = ex.Message
            });
        }
    }
    
    return Results.Ok(results);
});

// Generate Russian explanation using Gemini API
app.MapPost("/api/gemini/explain", async (IHttpClientFactory httpClientFactory, GeminiExplainRequest request, ILogger<Program> logger) =>
{
    var http = httpClientFactory.CreateClient();
    
    if (string.IsNullOrEmpty(request.ApiKey))
        return Results.BadRequest("Gemini API key is required");
    
    var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={request.ApiKey}";
    
    var prompt = $@"Ты - преподаватель греческого языка. Дай краткое объяснение на русском языке (максимум 2-3 предложения) для следующего греческого слова или фразы.

Греческое слово/фраза: {request.Greek}
Английский перевод: {request.English}

Дай краткое, полезное объяснение на русском языке, которое поможет запомнить это слово. Включи информацию о контексте использования, если уместно.";

    var requestBody = new
    {
        contents = new[]
        {
            new
            {
                parts = new[]
                {
                    new { text = prompt }
                }
            }
        },
        generationConfig = new
        {
            temperature = 0.7,
            maxOutputTokens = 200
        }
    };
    
    try
    {
        logger.LogInformation($"Calling Gemini API for: {request.Greek}");
        
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = JsonContent.Create(requestBody);
        
        var response = await http.SendAsync(httpRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            logger.LogError($"Gemini API error: {error}");
            return Results.BadRequest($"Gemini API error: {error}");
        }
        
        var responseBody = await response.Content.ReadAsStringAsync();
        logger.LogInformation($"Gemini response: {responseBody}");
        
        var result = System.Text.Json.JsonSerializer.Deserialize<GeminiResponse>(responseBody, jsonOptions);
        
        var explanation = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text?.Trim() ?? "";
        
        return Results.Ok(new { explanation });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error calling Gemini API");
        return Results.BadRequest($"Error generating explanation: {ex.Message}");
    }
});

// Test AnkiConnect connection
app.MapGet("/api/anki/test", async (IHttpClientFactory httpClientFactory, string? url) =>
{
    var http = httpClientFactory.CreateClient();
    var ankiConnectUrl = url ?? "http://localhost:8765";
    
    try
    {
        var json = "{\"action\": \"version\", \"version\": 6}";
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await http.PostAsync(ankiConnectUrl, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return Results.Ok(new { connected = false, error = $"HTTP {response.StatusCode}: {error}" });
        }
        
        var responseBody = await response.Content.ReadAsStringAsync();
        var result = System.Text.Json.JsonSerializer.Deserialize<AnkiConnectResponse<int>>(responseBody, jsonOptions);
        
        if (result?.Error != null)
        {
            return Results.Ok(new { connected = false, error = $"AnkiConnect error: {result.Error}" });
        }
        
        return Results.Ok(new { connected = true, version = result?.Result ?? 0 });
    }
    catch (System.Net.Http.HttpRequestException ex)
    {
        return Results.Ok(new { 
            connected = false, 
            error = "Cannot connect to AnkiConnect. Is Anki running?",
            details = ex.Message
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { 
            connected = false, 
            error = ex.Message,
            type = ex.GetType().Name
        });
    }
});

app.Run();

// Models
record FlashCard
{
    public string Id { get; set; } = "";
    public string Greek { get; set; } = "";
    public string Translation { get; set; } = "";
    public string RussianExplanation { get; set; } = "";
    public bool Selected { get; set; } = true;
}

record ParseTextRequest(string Text);
record DuplicateCheckRequest(string SessionId, string? AnkiConnectUrl, string? DeckName);
record AudioGenerateRequest(string Text, string ApiKey, string? VoiceId);
record GeminiExplainRequest(string Greek, string English, string ApiKey);
record CreateSingleCardRequest(
    string SessionId,
    string CardId,
    string ElevenLabsApiKey,
    string? VoiceId,
    string? DeckName,
    string? ModelName,
    string? AnkiConnectUrl
);
record CreateCardsRequest(
    string SessionId,
    string ElevenLabsApiKey,
    string? VoiceId,
    string? DeckName,
    string? ModelName,
    string? AnkiConnectUrl
);
record CardCreationResult
{
    public string CardId { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
}

record AnkiConnectResponse<T>
{
    public T? Result { get; set; }
    public string? Error { get; set; }
}

record GeminiResponse
{
    public List<GeminiCandidate>? Candidates { get; set; }
}

record GeminiCandidate
{
    public GeminiContent? Content { get; set; }
}

record GeminiContent
{
    public List<GeminiPart>? Parts { get; set; }
}

record GeminiPart
{
    public string? Text { get; set; }
}


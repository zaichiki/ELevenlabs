# Greek Flashcard Generator with ElevenLabs Audio & AI Explanations

A .NET 8 minimal API application that creates Anki flashcards with Greek audio from ElevenLabs and Russian explanations powered by Google Gemini AI.

## Features

- 📤 Paste Greek text with English and Russian translations
- 🔊 Generate Greek audio using ElevenLabs API
- 🤖 Auto-generate Russian explanations using Google Gemini AI
- 🎴 Create flashcards directly in Anki via AnkiConnect
- ✅ Check for duplicates before creating cards
- ✏️ Review and edit cards before creation
- 🎯 Select which cards to create
- 🎨 Modern, user-friendly web interface

## Prerequisites

1. **.NET 8 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **Anki Desktop** with **AnkiConnect** add-on installed
   - Install Anki: https://apps.ankiweb.net/
   - Install AnkiConnect: Tools → Add-ons → Get Add-ons → Code: `2055492159`
3. **ElevenLabs Account** with API access
   - Sign up: https://elevenlabs.io/
   - Get API key: https://elevenlabs.io/app/settings/api-keys
4. **Google Gemini API Key** (for Russian explanations)
   - Get your free API key: https://aistudio.google.com/app/apikey

## Setup

1. **Clone/Download** this project

2. **Configure API Keys** in `appsettings.json`:
   ```json
   {
     "ElevenLabs": {
       "ApiKey": "your_elevenlabs_api_key_here"
     },
     "Gemini": {
       "ApiKey": "your_gemini_api_key_here"
     }
   }
   ```

3. **Setup Anki Note Type**:
   - Open Anki Desktop
   - Go to Tools → Manage Note Types
   - Select your note type (or create "EL Custom")
   - Click **Fields** and ensure you have:
     - `Expression` (Greek text)
     - `Meaning` (Translation)
     - `RussianExplanation` (AI-generated explanation)
     - `Audio` (Audio file)

4. **Run the application**:
   ```bash
   dotnet run
   ```

5. **Open your browser**:
   - Navigate to: http://localhost:5000 (or the URL shown in the console)

## Usage

### 1. Prepare Your Text

Format your text with alternating lines:
```
Καλημέρα; Good morning!
Доброе утро!
Θέλω νερό. I want water.
Хочу воды.
Ευχαριστώ! Thank you!
Спасибо!
```

**Format:** 
- Line 1: Greek + English (separated naturally)
- Line 2: Russian translation
- Line 3: Next Greek + English
- Line 4: Next Russian translation
- etc.

### 2. Configure the App

1. Make sure Anki is running
2. Check the AnkiConnect connection status (green dot)
3. Select your preferred ElevenLabs voice

### 3. Create Flashcards

1. **Paste your text** into the input area
2. Click **"Process Text"**
3. Review the parsed cards (edit if needed)
4. **Generate AI Explanations**:
   - Click **"🤖 Generate All Explanations"** for batch generation
   - Or click individual **"🤖 Auto-Generate"** buttons per card
5. Duplicate cards are automatically detected and deselected
6. Select/deselect cards as needed
7. Click **"Create Selected Cards"**

The app will:
- Generate Greek audio for each card using ElevenLabs
- Include the Russian translation and AI-generated explanation
- Create the cards in Anki with audio attached
- Show you the results

## AI-Generated Explanations

The app uses **Google Gemini 2.0 Flash** to automatically generate helpful Russian explanations for each Greek word or phrase. These explanations:

- Provide context about how the word is used
- Help with memorization
- Are brief (2-3 sentences)
- Appear in a subtle, non-distracting style on the card back

**Example:**
- **Greek:** Καλημέρα
- **Translation:** Good morning! / Доброе утро!
- **AI Explanation:** *"Это приветствие используется утром до полудня. Состоит из 'καλή' (хорошая) и 'ημέρα' (день). Очень распространённое повседневное выражение."*

## AnkiConnect Configuration

By default, AnkiConnect runs on `http://localhost:8765`. If you've changed this, update it in the UI or in `appsettings.json`.

## ElevenLabs Voice Selection

The app uses a default multilingual voice. To use a different voice:
1. Go to https://elevenlabs.io/app/voice-library
2. Find a voice you like
3. Copy its Voice ID
4. Enter it in the "Voice ID" field in the UI

## Troubleshooting

### AnkiConnect not connecting
- Make sure Anki Desktop is running
- Verify AnkiConnect is installed (Tools → Add-ons)
- Check that the URL is correct (default: http://localhost:8765)

### ElevenLabs API errors
- Verify your API key is correct
- Check you have sufficient credits in your account
- Ensure you're using a voice that supports Greek (multilingual voices)

### Gemini API errors
- Verify your API key is correct in `appsettings.json`
- Check you haven't exceeded the free tier limits (15 requests/min, 1,500/day)
- Make sure you have internet connectivity

### Cards not created
- Check that the deck name exists in Anki
- Verify the note type has these fields: `Expression`, `Meaning`, `RussianExplanation`, `Audio`
- Look at the browser console for specific error messages

### RussianExplanation field not found
- You need to add the `RussianExplanation` field to your Anki note type
- Go to: Tools → Manage Note Types → [Your Model] → Fields → Add
- Add a field named exactly: `RussianExplanation`

## Project Structure

```
ELevenlabs/
├── Program.cs                      # Main API endpoints
├── ELevenlabs.csproj              # Project file
├── appsettings.json               # Configuration (API keys)
├── wwwroot/
│   ├── index.html                 # Web UI
│   └── anki-card-template.html    # Anki card template with styling
├── README.md                      # This file
└── GEMINI_INTEGRATION.md          # Detailed Gemini AI integration docs
```

## API Endpoints

- `GET /api/config` - Get configuration (API keys, defaults)
- `POST /api/cards/parse` - Parse text input into cards
- `GET /api/cards/{sessionId}` - Get parsed cards
- `PUT /api/cards/{sessionId}/{cardId}` - Update a card
- `POST /api/cards/check-duplicates` - Check for duplicates in Anki
- `POST /api/cards/create-single` - Create single card in Anki
- `POST /api/cards/create` - Create multiple cards in Anki
- `POST /api/gemini/explain` - Generate Russian explanation using Gemini AI
- `GET /api/anki/test` - Test AnkiConnect connection

## Card Template

The included Anki card template (`anki-card-template.html`) provides:
- Clean, modern design with animations
- Greek text prominently displayed
- English and Russian translations
- AI-generated explanation in subtle, italicized style
- Audio playback button
- Reversed cards (translation → Greek)
- Night mode support
- Mobile responsive

Copy the template sections from `anki-card-template.html` into your Anki note type's Front/Back/Styling fields.

## Rate Limits

**Google Gemini Free Tier:**
- 15 requests per minute
- 1,500 requests per day

The batch generation feature includes automatic 500ms delays between requests to stay within limits.

**ElevenLabs:**
- Varies by plan
- Check your account for current limits

## Technologies Used

- **.NET 8** - Minimal API
- **ElevenLabs API** - Greek text-to-speech
- **Google Gemini 2.0 Flash** - AI-powered Russian explanations
- **AnkiConnect** - Anki integration

## License

MIT


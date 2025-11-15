# Greek Flashcard Generator with ElevenLabs Audio

A .NET 8 minimal API application that creates Anki flashcards with Greek audio from ElevenLabs.

## Features

- 📤 Upload text files with Greek text and translations
- 🔊 Generate Greek audio using ElevenLabs API
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

## Setup

1. **Clone/Download** this project

2. **Configure ElevenLabs** (optional - can also be set in the UI):
   - Open `appsettings.json`
   - Add your ElevenLabs API key

3. **Run the application**:
   ```bash
   dotnet run
   ```

4. **Open your browser**:
   - Navigate to: http://localhost:5000 (or the URL shown in the console)

## Usage

### 1. Prepare Your Text File

Create a `.txt` file with this format:
```
Καλημέρα
Good morning
Ευχαριστώ
Thank you
Παρακαλώ
Please
```

Format: Line 1 = Greek, Line 2 = Translation, Line 3 = Greek, Line 4 = Translation, etc.

### 2. Configure the App

1. Make sure Anki is running
2. Test the AnkiConnect connection
3. Enter your ElevenLabs API key
4. Select your target deck and note type

### 3. Create Flashcards

1. Upload your text file
2. Review the parsed cards (edit if needed)
3. Click "Check Duplicates" to find existing cards
4. Select/deselect cards as needed
5. Click "Create Selected Cards"

The app will:
- Generate Greek audio for each card using ElevenLabs
- Create the cards in Anki with the audio attached
- Show you the results

## File Format Variations

The current parser expects alternating lines (Greek, Translation, Greek, Translation...).

To support other formats in the future, you can modify the parsing logic in the `/api/cards/parse` endpoint in `Program.cs`.

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

### Cards not created
- Check that the deck name exists in Anki
- Verify the note type (model) has "Expression", "Meaning", and "Audio" fields
- Look at the error messages for specific issues

## Project Structure

```
ELevenlabs/
├── Program.cs              # Main API endpoints
├── ELevenlabs.csproj      # Project file
├── appsettings.json       # Configuration
├── wwwroot/
│   └── index.html         # Web UI
└── README.md              # This file
```

## API Endpoints

- `POST /api/cards/parse` - Parse uploaded text file
- `GET /api/cards/{sessionId}` - Get parsed cards
- `PUT /api/cards/{sessionId}/{cardId}` - Update a card
- `POST /api/cards/check-duplicates` - Check for duplicates in Anki
- `POST /api/cards/create` - Create selected cards in Anki
- `GET /api/anki/test` - Test AnkiConnect connection
- `GET /api/anki/decks` - Get list of Anki decks
- `GET /api/anki/models` - Get list of Anki note types

## License

MIT


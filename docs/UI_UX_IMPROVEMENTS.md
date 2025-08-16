# Brainarr UI/UX Implementation Guide

## Overview
This document describes the implemented UI/UX features in Brainarr v1.0.0 and documents how the configuration interface works for AI-powered music recommendations in Lidarr.

## Key UX Principles

### 1. **Local-First Philosophy**
- Local providers (Ollama, LM Studio) are listed first in the dropdown
- Default selection is Ollama to encourage privacy-conscious usage
- Clear visual indicators (emojis) differentiate provider types

### 2. **Progressive Disclosure**
- Settings are conditionally shown based on selected provider
- Advanced settings are hidden by default
- Sections organize related settings

### 3. **Guided Setup**
- Quick Start Guide at the top of settings
- Step-by-step instructions for each provider
- Visual feedback through info panels

## UI Components & Features

### Provider Selection Dropdown (✅ Implemented)
The following 9 providers are implemented and available in v1.0.0:
```
🏠 Ollama (Local, Private) - Run AI models locally - 100% private
🖥️ LM Studio (Local, GUI) - User-friendly local AI with GUI
🌐 OpenRouter (200+ Models) - Access all providers with one key
💰 DeepSeek (Ultra Cheap) - 10-20x cheaper than GPT-4
🆓 Google Gemini (Free Tier) - Free tier available, great for testing
⚡ Groq (Ultra Fast) - 10x faster responses
🔍 Perplexity (Web Search) - Real-time web search for current music
🤖 OpenAI (GPT-4) - Premium quality, higher cost
🧠 Anthropic (Claude) - Best reasoning, premium quality
```

### Conditional Field Visibility (✅ Implemented)
Fields are dynamically shown/hidden based on provider selection using Lidarr's `Hidden` attribute:
```csharp
[FieldDefinition(4, Label = "Ollama URL", Hidden = "Provider != 0")]
[FieldDefinition(5, Label = "OpenAI API Key", Hidden = "Provider != 1")]
```

### Sectioned Layout (✅ Implemented)
The configuration interface is organized into logical sections:
```
📋 Basic Configuration
├── Name, Enable Automatic Add, Monitor, Root Folder
├── Quality Profile, Metadata Profile, Tags

🤖 AI Provider Configuration  
├── Provider Selection (dropdown with 9 options)
├── --- Provider-Specific Settings --- [Dynamic based on selection]
│   ├── API Keys (for cloud providers)
│   ├── URLs (for local providers)
│   └── Model Selection (auto-populated after test)

🎯 Discovery Configuration
├── Discovery Mode (Similar/Adjacent/Exploratory)
├── Max Recommendations (5-50 range)
├── Sampling Strategy (Minimal/Balanced/Comprehensive)
├── Music Preferences (Genres, Moods, Eras)

⚙️ Advanced Settings
├── Cache Duration (30-180 minutes)
├── Auto-Detect Models (Yes/No)
├── Enable Debugging (Yes/No)
└── Rate Limiting Settings
```

### Help Text Enhancements

#### Before:
```
"URL of your Ollama instance"
```

#### After:
```
"URL of your Ollama instance. 
Default: http://localhost:11434
📝 Setup: curl -fsSL https://ollama.com/install.sh | sh
Then run: ollama pull llama3"
```

### Test Connection Guidance
Special emphasis on the Test button with clear instructions:
```
⚠️ IMPORTANT: Always click 'Test' after configuring!
This will:
✅ Verify your connection
✅ Populate available models
✅ Check API key validity
✅ Estimate response speed
```

## Implementation Details

### 1. Field Types Available in Lidarr
- `FieldType.Info` - Informational text panels
- `FieldType.Select` - Dropdowns with options
- `FieldType.Textbox` - Text input
- `FieldType.Password` - Masked text input
- `FieldType.Number` - Numeric input
- `FieldType.Checkbox` - Boolean toggle
- `FieldType.Path` - File/folder picker
- `FieldType.Tag` - Tag selection

### 2. Field Attributes
- `Label` - Field label
- `HelpText` - Detailed help (supports multiline)
- `Hidden` - Conditional visibility expression
- `Advanced` - Show in advanced section
- `SelectOptions` - Enum type for dropdowns
- `SelectOptionsProviderAction` - Dynamic options from API
- `Privacy` - Password masking level

### 3. Validation Messages
Enhanced validation messages with actionable guidance:
```csharp
RuleFor(c => c.OllamaUrl)
    .NotEmpty()
    .WithMessage("Ollama URL is required. Default is http://localhost:11434")
    .Must(BeValidUrl)
    .WithMessage("Please enter a valid URL (e.g., http://localhost:11434)");
```

### 4. Dynamic Model Population
Models are populated after successful connection test:
```csharp
[FieldDefinition(5, Label = "Ollama Model", 
    SelectOptionsProviderAction = "getOllamaOptions")]
```

The `getOllamaOptions` action should be implemented in the ImportList class to fetch available models.

## User Journey

### First-Time Setup (Local)
1. User sees Quick Start Guide
2. Selects "🏠 Ollama (Local, Private)"
3. Only Ollama settings appear
4. Sees setup instructions in help text
5. Enters URL (or uses default)
6. Clicks "Test" button
7. Models populate in dropdown
8. Selects model
9. Configures discovery settings
10. Saves

### Switching Providers
1. User changes provider dropdown
2. Previous provider settings hide
3. New provider settings appear
4. Relevant help text shows
5. Clear instructions for API key

## Accessibility & Best Practices

### Visual Hierarchy
- Emojis for quick recognition
- Sections with clear headers
- Progressive disclosure
- Consistent labeling

### Error Prevention
- URL validation
- API key format checking
- Clear error messages
- Default values where sensible

### Recovery
- Test button for verification
- Clear troubleshooting steps
- Failover options
- Cache for reliability

## Implementation Status (v1.0.0)

### ✅ Completed Features
- [x] Provider dropdown with clear descriptions and emojis
- [x] Conditional fields show/hide based on provider selection
- [x] Test button populates available models automatically
- [x] Comprehensive validation with helpful error messages
- [x] Provider-specific configuration sections
- [x] Help text with setup instructions and examples
- [x] URL validation for local providers
- [x] API key fields with proper masking
- [x] Sensible default values for all settings
- [x] Dynamic model detection for Ollama and LM Studio
- [x] Configuration validation with detailed error messages

### 🎯 Key Implemented UX Features

#### Smart Provider Detection
- **Auto-Model Detection**: Automatically discovers available models for Ollama and LM Studio
- **Connection Testing**: "Test" button validates provider connectivity and populates model options
- **Health Monitoring**: Real-time status checking for provider availability

#### Intuitive Configuration Flow
1. **Provider Selection**: Clear dropdown with descriptive labels and emojis
2. **Dynamic Settings**: Only relevant settings appear based on provider choice
3. **Guided Setup**: Help text provides setup instructions and examples
4. **Validation Feedback**: Immediate feedback on configuration issues
5. **Test & Verify**: Built-in testing before saving configuration

#### User-Friendly Features
- **Privacy-First Defaults**: Ollama is the default selection
- **Cost Awareness**: Provider descriptions include cost implications
- **Setup Instructions**: Embedded help text with installation commands
- **Error Prevention**: Validation prevents common configuration mistakes

## Current Limitations & Design Decisions

### Lidarr Plugin Framework Constraints
Working within Lidarr's plugin framework, we are limited to:
- **Field Types**: Only Lidarr's predefined field types (textbox, dropdown, checkbox, etc.)
- **No Custom UI**: Cannot add custom JavaScript or interactive elements
- **Layout Constraints**: Must work within Lidarr's settings page layout
- **No Modals**: Cannot display popup dialogs

### Effective Workarounds Implemented
- **Info Fields**: Used for section headers and detailed instructions
- **Conditional Visibility**: Dynamic field showing/hiding based on selections
- **Rich Help Text**: Multi-line help text with setup commands and examples
- **Emoji Visual Cues**: Clear visual differentiation between provider types
- **Smart Defaults**: Sensible default values reduce configuration burden

## Future Enhancement Roadmap

### Version 1.1.0 Potential Improvements
1. **Enhanced Validation**: More sophisticated API key format validation
2. **Model Recommendations**: Suggest optimal models based on library size
3. **Cost Estimation**: Display estimated monthly costs for cloud providers
4. **Connection Status**: Real-time connection status indicators
5. **Quick Presets**: Pre-configured settings for common use cases

### Version 1.2.0 Advanced Features
1. **Setup Wizard**: Multi-step guided configuration process
2. **Provider Comparison**: Side-by-side provider feature comparison
3. **Health Dashboard**: Provider status and performance metrics
4. **Configuration Import/Export**: Share optimal settings between instances
5. **A/B Testing Interface**: Compare provider effectiveness

### Long-term Vision (v2.0+)
1. **Standalone Web UI**: Dedicated web interface for advanced configuration
2. **Mobile Companion**: Mobile app for configuration management
3. **Visual Configuration**: Drag-and-drop provider setup
4. **Interactive Tutorials**: In-app guided tours and tutorials

## Conclusion

Brainarr v1.0.0 successfully implements a user-friendly configuration experience that balances simplicity with powerful functionality. The local-first approach with clear provider distinctions helps users make informed choices about privacy and cost. The dynamic configuration interface adapts to user selections, reducing complexity while maintaining access to advanced features.

The implementation demonstrates how effective UX can be achieved within the constraints of a plugin framework through thoughtful design decisions, clear communication, and smart use of available features.
# Brainarr UI/UX Improvements Guide

## Overview

This document outlines the UI/UX improvements for the Brainarr Lidarr plugin settings page to ensure users can properly configure and use AI-powered music recommendations.

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

### Provider Selection Dropdown

```text
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

### Conditional Field Visibility

Fields are shown/hidden based on provider selection using the `Hidden` attribute:

```csharp
[FieldDefinition(4, Label = "Ollama URL", Hidden = "Provider != 0")]
```

### Sectioned Layout

```text
🚀 Quick Start Guide
├── Provider Selection
├── --- Ollama Settings (Local) --- [Shown only when Ollama selected]
│   ├── URL
│   └── Model (populated after Test)
├── --- Discovery Settings ---
│   ├── Number of Recommendations
│   └── Discovery Mode
└── --- Advanced Settings --- [Collapsed by default]
    ├── Auto-Detect Model
    ├── Cache Recommendations
    └── Enable Failover
```

### Help Text Enhancements

#### Before

```text
"URL of your Ollama instance"
```

#### After

```text
"URL of your Ollama instance.
Default: http://localhost:11434
📝 Setup: curl -fsSL https://ollama.com/install.sh | sh
Then run: ollama pull llama3"
```

### Test Connection Guidance

Special emphasis on the Test button with clear instructions:

```text
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

## Testing Checklist

- [ ] Quick Start Guide displays on first open
- [ ] Provider dropdown shows clear descriptions
- [ ] Conditional fields show/hide correctly
- [ ] Test button populates models
- [ ] Validation messages are helpful
- [ ] Advanced settings are hidden by default
- [ ] Help text provides actionable guidance
- [ ] URL validation works
- [ ] API key fields are masked
- [ ] Default values are sensible

## Future Enhancements

### Potential Improvements

1. **Setup Wizard**: Multi-step guided setup for first-time users
2. **Provider Comparison Table**: Show cost/speed/privacy comparison
3. **Model Recommendations**: Suggest best model based on library size
4. **Cost Calculator**: Estimate monthly costs based on settings
5. **Connection Status Indicator**: Live status badge
6. **Quick Actions**: Preset configurations for common use cases
7. **Import/Export Settings**: Share configurations
8. **Provider Health Dashboard**: Show provider status/uptime

### Plugin Limitations

As a Lidarr plugin, we're limited to:

- Field types provided by Lidarr
- No custom JavaScript/interactive elements
- No popup modals (but Info fields work well)
- Settings page layout constraints

### Workarounds

- Use Info fields for detailed instructions
- Conditional visibility for progressive disclosure
- Multi-line help text for comprehensive guidance
- Section headers using Info fields
- Emojis for visual enhancement

## Conclusion

These UX improvements make Brainarr more approachable for users of all technical levels while maintaining the power and flexibility of multiple AI providers. The local-first approach with clear guidance ensures users can get started quickly while understanding their privacy options.
### Music Styles TagSelect

- Field: `Music Styles` (TagSelect)
- Source: `styles/getoptions` endpoint
- Behavior:
  - Typeahead returns up to 50 matching styles from the dynamic catalog (aliases supported).
  - When the query is empty, options default to top-in-library styles with coverage counts (e.g., "Progressive Rock — 27").
  - Soft cap is applied (default 10). If exceeded, the most prevalent styles in the user’s library are kept; the rest are ignored with a log entry.
  - Help text clarifies that leaving empty keeps prompts library-centric without generic content.
- Strict enforcement:
  - When any styles are selected, recommendations are strictly filtered to those styles across sampling, prompt rules, and a post-generation validator guardrail.
  - Hidden toggle `RelaxStyleMatching` allows parent/adjacent widening; default OFF.

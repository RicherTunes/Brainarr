# 🧠 Brainarr 🧠 - AI-Powered Music Discovery for Lidarr

[![License](https://img.shields.io/github/license/Brainarr/brainarr)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0%2B-blue)](https://dotnet.microsoft.com/download)
[![Lidarr](https://img.shields.io/badge/Lidarr-Plugin-green)](https://lidarr.audio/)
[![Version](https://img.shields.io/badge/version-1.0.0-brightgreen)](plugin.json)

Brainarr is a multi-provider AI-powered import list plugin for Lidarr that generates intelligent music recommendations using both local and cloud AI models. It supports 9 different AI providers, from privacy-focused local options to powerful cloud services, with automatic failover and health monitoring.

## Features

### Privacy & Flexibility
- **Local-First**: Privacy-focused local providers (Ollama, LM Studio) available
- **Multi-Provider Support**: 9 AI providers including OpenAI, Anthropic, Google Gemini
- **Gateway Access**: OpenRouter integration for 200+ models with one API key
- **Cost Options**: Budget-friendly options like DeepSeek and free-tier Gemini

### Intelligence & Performance
- **Auto-Detection**: Automatically discovers available AI models
- **Smart Caching**: Reduces redundant AI processing with configurable cache duration
- **Library Analysis**: Deep analysis of your music library for personalized recommendations
- **Discovery Modes**: Similar, Adjacent, or Exploratory recommendation styles
- **Health Monitoring**: Real-time provider availability and performance tracking
- **Rate Limiting**: Built-in rate limiting to prevent API overuse
- **Automatic Failover**: Seamless switching between providers on failures

## Prerequisites

- **Lidarr**: Version 4.0.0 or higher
- **.NET Runtime**: 6.0 or higher
- **AI Provider**: At least one of the following:
  - Local: Ollama or LM Studio (for privacy)
  - Cloud: API key for OpenAI, Anthropic, Google Gemini, etc.

## Installation

### From Build

1. Build the plugin using the included scripts
2. Extract the built plugin to your Lidarr plugins directory:
   - Windows: `C:\ProgramData\Lidarr\plugins\Brainarr\`
   - Linux: `/var/lib/lidarr/plugins/Brainarr/`
   - Docker: `/config/plugins/Brainarr/`
3. Restart Lidarr
4. Navigate to Settings → Import Lists → Add New → Brainarr

### From Source

```bash
# Clone/extract the project
cd Brainarr

# Build the plugin
dotnet build -c Release

# Copy to Lidarr plugins directory  
cp -r Brainarr.Plugin/bin/Release/net6.0/* /path/to/lidarr/plugins/Brainarr/

# Restart Lidarr
systemctl restart lidarr
```

## Configuration

### Basic Configuration

1. In Lidarr, go to Settings → Import Lists → Brainarr
2. Configure the following basic settings:

```yaml
Name: "AI Music Recommendations"
Enable Automatic Add: Yes
Monitor: All Albums
Root Folder: /music
Quality Profile: Any
Metadata Profile: Standard
Tags: ai-recommendations
```

### Supported AI Providers

Brainarr supports 9 different AI providers, categorized by privacy and cost:

#### 🏠 Local Providers (Privacy-First)
**Ollama**
- **Privacy**: 100% local, no data leaves your network
- **Cost**: Free
- **Setup**: `curl -fsSL https://ollama.ai/install.sh | sh && ollama pull llama3`
- **URL**: `http://localhost:11434`

**LM Studio**  
- **Privacy**: 100% local with GUI interface
- **Cost**: Free
- **Setup**: Download from lmstudio.ai, load model, start server
- **URL**: `http://localhost:1234`

#### 🌐 Gateway Provider
**OpenRouter**
- **Access**: 200+ models with one API key
- **Cost**: Variable pricing per model
- **Models**: Claude, GPT-4, Gemini, Llama, DeepSeek, and more
- **Setup**: Get API key at openrouter.ai/keys

#### 💰 Budget-Friendly Providers  
**DeepSeek**
- **Cost**: 10-20x cheaper than GPT-4
- **Models**: DeepSeek-Chat, DeepSeek-Coder, DeepSeek-Reasoner
- **Quality**: Comparable to GPT-4 for many tasks

**Google Gemini**
- **Cost**: Free tier available
- **Models**: Gemini 1.5 Flash, Gemini 1.5 Pro (2M context)
- **Setup**: Free API key at aistudio.google.com/apikey

**Groq**
- **Speed**: 10x faster inference
- **Models**: Llama 3.3 70B, Mixtral, Gemma
- **Cost**: Very affordable

#### 🤖 Premium Providers
**OpenAI**
- **Quality**: Industry-leading GPT-4o models
- **Models**: GPT-4o, GPT-4o-mini, GPT-3.5-turbo
- **Cost**: Higher but best quality

**Anthropic**
- **Reasoning**: Best reasoning capabilities
- **Models**: Claude 3.5 Sonnet, Claude 3.5 Haiku, Claude 3 Opus
- **Safety**: Strong safety features

**Perplexity**
- **Features**: Web-enhanced responses
- **Models**: Sonar Large, Sonar Small, Sonar Huge
- **Specialty**: Real-time web information

### Discovery Modes

Configure how adventurous the recommendations should be:

- **Similar**: Recommends artists very similar to your library
- **Adjacent**: Explores related genres and styles
- **Exploratory**: Discovers new genres and musical territories

### Advanced Settings

#### Caching Configuration
```yaml
Cache Duration: 60 minutes
Max Recommendations: 20
Auto-Detect Models: Yes
```

## Usage

### Manual Recommendations

1. Go to Import Lists → Brainarr
2. Click "Test" to preview recommendations
3. Click "Fetch Now" to generate new recommendations

### Automatic Recommendations

Brainarr will automatically generate recommendations based on your configured schedule:

```yaml
Interval: Every 7 days
Time: 2:00 AM
Max Recommendations: 20
```

### Monitoring Recommendations

View recommendation history and statistics:

1. Go to Activity → History
2. Filter by "Brainarr" tag
3. View recommendation reasons and confidence scores

## Provider Comparison

| Provider | Privacy | Cost | Setup | Best For |
|----------|---------|------|-------|----------|
| **Ollama** | 🟢 Perfect | Free | Easy | Privacy-conscious users |
| **LM Studio** | 🟢 Perfect | Easy | Easy | GUI users who want privacy |
| **OpenRouter** | 🟡 Cloud | Variable | Easy | Access to 200+ models |
| **DeepSeek** | 🟡 Cloud | Very Low | Easy | Budget-conscious users |
| **Gemini** | 🟡 Cloud | Free/Low | Easy | Free tier users |
| **Groq** | 🟡 Cloud | Low | Easy | Speed-focused users |
| **OpenAI** | 🟡 Cloud | High | Easy | Quality-focused users |
| **Anthropic** | 🟡 Cloud | High | Easy | Reasoning tasks |
| **Perplexity** | 🟡 Cloud | Medium | Easy | Web-enhanced responses |

## Troubleshooting

### Common Issues

#### Provider Not Detected
```bash
# Check if local providers are running
curl http://localhost:11434/api/tags  # Ollama
curl http://localhost:1234/v1/models  # LM Studio

# Check Lidarr logs
tail -f /var/log/lidarr/lidarr.txt | grep Brainarr
```

#### No Recommendations Generated
- Ensure your library has at least 10 artists
- Click "Test" in settings to verify provider connection
- Check API keys are valid for cloud providers
- Review discovery mode settings
- Verify model is selected/loaded

#### High API Costs (Cloud Providers)
- Use local providers (Ollama/LM Studio) for free operation
- Enable caching to reduce API calls
- Use budget providers like DeepSeek or Gemini free tier
- Reduce recommendation frequency
- Lower max recommendations per sync

#### Connection Issues
- For local providers: Ensure service is running
- For cloud providers: Verify API key format and permissions
- Check firewall/network restrictions
- Review rate limiting settings

### Debug Mode

Enable debug logging for detailed troubleshooting:

```yaml
Log Level: Debug
Log Provider Requests: Yes
Log Token Usage: Yes
```

## Development

### Building from Source

```bash
# Navigate to project directory
cd Brainarr

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Create release package
dotnet publish -c Release -o dist/
```

### Running Tests

The project includes comprehensive tests covering all components:

```bash
# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration  # Requires active providers
dotnet test --filter Category=EdgeCase

# Test specific components
dotnet test --filter "FullyQualifiedName~ProviderTests"
dotnet test --filter "FullyQualifiedName~ConfigurationTests"
```

### Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Architecture

Brainarr uses a sophisticated multi-provider architecture with comprehensive testing:

```
Brainarr.Plugin/
├── Configuration/          # Provider settings and validation
│   ├── Constants.cs        # Configuration constants
│   ├── ProviderConfiguration.cs
│   └── Providers/          # Per-provider configuration classes
├── Services/
│   ├── Core/              # Core orchestration services
│   │   ├── AIProviderFactory.cs    # Provider instantiation
│   │   ├── AIService.cs            # Multi-provider orchestration  
│   │   ├── LibraryAnalyzer.cs      # Music library analysis
│   │   ├── ProviderRegistry.cs     # Provider registration
│   │   └── RecommendationSanitizer.cs
│   ├── Providers/         # AI provider implementations (9 providers)
│   │   ├── AnthropicProvider.cs
│   │   ├── OpenAIProvider.cs
│   │   ├── GeminiProvider.cs
│   │   └── ... (6 more)
│   ├── Support/           # Supporting services
│   │   ├── MinimalResponseParser.cs
│   │   ├── RecommendationHistory.cs
│   │   └── VoidResult.cs
│   ├── LocalAIProvider.cs         # Local provider coordination
│   ├── ModelDetectionService.cs   # Auto model detection
│   ├── ProviderHealth.cs          # Health monitoring
│   ├── RateLimiter.cs            # API rate limiting
│   ├── RecommendationCache.cs     # Response caching
│   ├── RetryPolicy.cs            # Failure handling
│   └── StructuredLogger.cs       # Enhanced logging
├── BrainarrImportList.cs          # Main Lidarr integration
└── BrainarrSettings.cs            # Configuration UI

Brainarr.Tests/                    # Comprehensive test suite
├── Configuration/         # Configuration validation tests
├── Services/Core/         # Core service tests
├── Services/              # Provider and support tests  
├── Integration/           # End-to-end tests
├── EdgeCases/            # Edge case and error handling
└── Helpers/              # Test utilities
```

### Key Components

- **Multi-Provider System**: 9 AI providers with automatic failover
- **Provider Factory Pattern**: Dynamic provider instantiation based on configuration
- **Health Monitoring**: Real-time provider availability tracking with metrics
- **Rate Limiting**: Configurable rate limiting per provider to prevent overuse
- **Intelligent Caching**: Smart caching system reducing redundant API calls
- **Auto-Detection**: Automatic model discovery for local providers
- **Retry Policies**: Exponential backoff retry with circuit breaker patterns
- **Library Analysis**: Deep music library analysis for personalized recommendations

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Lidarr team for the excellent media management platform
- All AI provider teams for their amazing models
- Community contributors and testers

## Support

For technical issues and feature requests, please review the documentation in the `docs/` folder:
- **Architecture**: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- **Setup Guide**: [docs/USER_SETUP_GUIDE.md](docs/USER_SETUP_GUIDE.md)
- **Provider Guide**: [docs/PROVIDER_GUIDE.md](docs/PROVIDER_GUIDE.md)
- **Contributing**: [CONTRIBUTING.md](CONTRIBUTING.md)

## Project Status

**Current Version**: 1.0.0 (Production Ready)

✅ **Completed Features:**
- Multi-provider AI support (9 providers)
- Local and cloud provider integration
- Auto-detection and health monitoring
- Comprehensive test suite
- Rate limiting and caching
- Advanced configuration validation

📋 **Roadmap** (see [docs/ROADMAP.md](docs/ROADMAP.md) for details):
- Additional cloud providers (AWS Bedrock, Azure OpenAI)
- Cost monitoring and optimization tools
- A/B testing framework for provider comparison
- Enhanced music analysis algorithms
- Plugin marketplace distribution
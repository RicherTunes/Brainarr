# Brainarr - Local AI Music Discovery for Lidarr

[![License](https://img.shields.io/github/license/Brainarr/brainarr)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0%2B-blue)](https://dotnet.microsoft.com/download)
[![Lidarr](https://img.shields.io/badge/Lidarr-Plugin-green)](https://lidarr.audio/)
[![Version](https://img.shields.io/badge/version-1.0.0-brightgreen)](https://github.com/Brainarr/brainarr/releases)

Brainarr is a privacy-focused AI-powered import list plugin for Lidarr that generates intelligent music recommendations using local AI models. It exclusively supports local providers (Ollama, LM Studio) to ensure your music preferences never leave your network.

## Features

- **100% Local AI**: Your data never leaves your network
- **Auto-Detection**: Automatically discovers available AI models
- **Smart Caching**: Reduces redundant AI processing
- **Library Analysis**: Analyzes your music library for personalized recommendations
- **Discovery Modes**: Similar, Adjacent, or Exploratory recommendation styles
- **Health Monitoring**: Tracks provider availability and performance
- **Zero Cloud Dependency**: No API keys, no subscriptions, no data sharing

## Prerequisites

- **Lidarr**: Version 4.0.0 or higher
- **.NET Runtime**: 6.0 or higher
- **Local AI Provider**: Either Ollama or LM Studio installed and running

## Installation

### From Release (Recommended)

1. Download the latest `Brainarr.Plugin.zip` from [Releases](https://github.com/Brainarr/brainarr/releases)
2. Extract the ZIP file to your Lidarr plugins directory:
   - Windows: `C:\ProgramData\Lidarr\plugins\`
   - Linux: `/var/lib/lidarr/plugins/`
   - Docker: `/config/plugins/`
3. Restart Lidarr
4. Navigate to Settings → Import Lists → Add New → Brainarr

### From Source

```bash
# Clone the repository
git clone https://github.com/Brainarr/brainarr.git
cd brainarr

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

#### Ollama (Recommended)
The preferred choice for privacy-conscious users.

**Installation:**
```bash
# Install Ollama
curl -fsSL https://ollama.ai/install.sh | sh

# Pull a recommended model
ollama pull llama2:latest
# Or try other models:
ollama pull mistral
ollama pull qwen:7b

# Verify it's running
curl http://localhost:11434/api/tags
```

**Configuration in Brainarr:**
- Provider: Ollama
- URL: `http://localhost:11434`
- Model: Auto-detected or manually specify
- Auto-detect will find the best available model

#### LM Studio
Great for users who prefer a GUI for model management.

**Installation:**
1. Download from https://lmstudio.ai
2. Load any GGUF model from HuggingFace
3. Start the local server (usually on port 1234)

**Configuration in Brainarr:**
- Provider: LM Studio
- URL: `http://localhost:1234`
- Model: Auto-detected from loaded model

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

| Provider | Privacy | Cost | Setup | Model Selection |
|----------|---------|------|-------|-----------------|
| Ollama | 🟢 Excellent | Free | Easy | Multiple models available |
| LM Studio | 🟢 Excellent | Free | Easy | Any GGUF model from HuggingFace |

## Troubleshooting

### Common Issues

#### Provider Not Detected
```bash
# Check if provider is running
curl http://localhost:11434/api/tags  # Ollama
curl http://localhost:1234/v1/models  # LM Studio

# Check Lidarr logs
tail -f /var/log/lidarr/lidarr.txt | grep Brainarr
```

#### No Recommendations Generated
- Ensure your library has at least 10 artists
- Check provider API limits
- Verify API keys are correct
- Review discovery mode settings

#### High API Costs
- Enable caching
- Increase recommendation interval
- Use local providers as primary
- Reduce max tokens setting

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
# Clone repository
git clone https://github.com/Brainarr/brainarr.git
cd brainarr

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

```bash
# Unit tests
dotnet test Brainarr.Plugin.Tests/Brainarr.Plugin.Tests.csproj

# Integration tests (requires providers)
dotnet test Brainarr.Plugin.Tests/Brainarr.Plugin.Tests.csproj --filter Category=Integration
```

### Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Architecture

```
Brainarr.Plugin/
├── Configuration/          # Provider-specific settings
├── Services/
│   ├── Core/              # Library analysis, caching, orchestration
│   └── Providers/         # AI provider implementations
├── Models/                # Data models and DTOs
└── Utilities/             # Helpers and extensions
```

### Key Components

- **IAIProvider**: Interface for all AI providers
- **AIProviderFactory**: Creates and manages provider instances
- **AIService**: Orchestrates providers with failover logic
- **PromptBuilder**: Generates optimized prompts for recommendations
- **CacheService**: Manages recommendation caching

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Lidarr team for the excellent media management platform
- All AI provider teams for their amazing models
- Community contributors and testers

## Support

- **Issues**: [GitHub Issues](https://github.com/Brainarr/brainarr/issues)
- **Discussions**: [GitHub Discussions](https://github.com/Brainarr/brainarr/discussions)
- **Wiki**: [Documentation Wiki](https://github.com/Brainarr/brainarr/wiki)

## Roadmap

- [ ] V2.0: Additional providers (Databricks, AWS Bedrock, Azure OpenAI)
- [ ] V2.1: Cost monitoring dashboard
- [ ] V2.2: A/B testing for provider comparison
- [ ] V3.0: Genre-specific fine-tuning
- [ ] V3.1: Collaborative filtering with other Brainarr users
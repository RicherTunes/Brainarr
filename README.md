# 🧠 Brainarr - AI-Powered Music Discovery for Lidarr

<!-- CI Fix: 2025-08-15 - Test compilation fixes -->

<div align="center">

[![License](https://img.shields.io/github/license/Brainarr/brainarr)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0%2B-blue)](https://dotnet.microsoft.com/download)
[![Lidarr](https://img.shields.io/badge/Lidarr-4.0%2B-green)](https://lidarr.audio/)
[![Version](https://img.shields.io/badge/version-1.0.0-brightgreen)](plugin.json)
[![Production Ready](https://img.shields.io/badge/status-production%20ready-success)](CHANGELOG.md)
[![AI Providers](https://img.shields.io/badge/AI%20providers-9-informational)](#supported-ai-providers)

**Intelligent music discovery for your media server**  
*Harness the power of AI to expand your music library with personalized recommendations*

[🚀 Quick Start](#quick-start) • [📖 Documentation](#documentation) • [🔧 Installation](#installation) • [🤝 Contributing](#contributing)

</div>

---

## 📑 Table of Contents

- [🎵 What is Brainarr?](#-what-is-brainarr)
- [🚀 Quick Start](#-quick-start)
- [Prerequisites](#prerequisites)
- [🔧 Installation](#-installation)
- [⚙️ Configuration](#️-configuration)
- [🎯 Supported AI Providers](#-supported-ai-providers)
- [🎨 Discovery Modes](#-discovery-modes)
- [🔄 Usage](#-usage)
- [📊 Provider Comparison](#-provider-comparison)
- [🔍 Troubleshooting](#-troubleshooting)
- [🛠️ Development](#️-development)
- [🏗️ Architecture](#️-architecture)
- [📖 Documentation](#-documentation)
- [🤝 Contributing](#-contributing)
- [📄 License](#-license)

---

## 🎵 What is Brainarr?

Brainarr is a **production-ready** multi-provider AI-powered import list plugin for Lidarr that revolutionizes music discovery. By analyzing your existing music library, it generates intelligent, personalized recommendations using state-of-the-art AI models.

### ✨ Key Highlights
- **🔒 Privacy-First**: Local AI options (Ollama, LM Studio) keep your data private
- **🌐 Multi-Provider**: 9 AI providers from local to cloud with automatic failover
- **🎯 Intelligent**: Deep library analysis for truly personalized recommendations
- **⚡ Production-Ready**: Comprehensive test suite with 30+ test files
- **💰 Cost-Effective**: Free local options and budget-friendly cloud providers

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

## 🚀 Quick Start

**Get Brainarr running in under 5 minutes!**

### Option 1: Free & Private (Recommended)
```bash
# Install Ollama
curl -fsSL https://ollama.ai/install.sh | sh

# Pull a music-capable model
ollama pull qwen2.5:latest

# Download and install Brainarr
# [Installation steps below]
```

### Option 2: Cloud-Based (Instant Setup)
1. Get a free API key from [Google Gemini](https://aistudio.google.com/apikey)
2. Install Brainarr (see below)
3. Configure with your API key

📖 **Need detailed guidance?** See our [Quick Start Guide](QUICKSTART.md) for step-by-step instructions.

---

## Prerequisites

- **Lidarr**: Version 4.0.0 or higher
- **.NET Runtime**: 6.0 or higher (included with Lidarr)
- **Music Library**: At least 20 artists for optimal recommendations
- **AI Provider**: Choose one or more:
  - **Local (FREE)**: Ollama or LM Studio
  - **Cloud**: OpenAI, Anthropic, Google Gemini, etc.

## 🔧 Installation

### Method 1: Pre-built Release (Recommended)

**Windows (PowerShell):**
```powershell
# Download the latest release
$release = Invoke-RestMethod "https://api.github.com/repos/yourusername/brainarr/releases/latest"
$downloadUrl = $release.assets | Where-Object { $_.name -eq "Brainarr-v1.0.0.zip" } | Select-Object -ExpandProperty browser_download_url
Invoke-WebRequest $downloadUrl -OutFile "Brainarr.zip"

# Extract to Lidarr plugins directory
Expand-Archive "Brainarr.zip" -DestinationPath "C:\ProgramData\Lidarr\plugins\Brainarr\"

# Restart Lidarr service
Restart-Service Lidarr
```

**Linux/macOS:**
```bash
# Download and extract
wget https://github.com/yourusername/brainarr/releases/latest/download/Brainarr-v1.0.0.zip
unzip Brainarr-v1.0.0.zip -d brainarr-temp
sudo cp -r brainarr-temp/* /var/lib/lidarr/plugins/Brainarr/

# Fix permissions and restart
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr/
sudo systemctl restart lidarr
```

**Docker:**
```bash
# Download and copy to container
wget https://github.com/yourusername/brainarr/releases/latest/download/Brainarr-v1.0.0.zip
unzip Brainarr-v1.0.0.zip -d brainarr-temp
docker cp brainarr-temp/* lidarr:/config/plugins/Brainarr/
docker restart lidarr
```

### Method 2: Build from Source

```bash
# Clone the repository
git clone https://github.com/yourusername/brainarr.git
cd brainarr

# Build the plugin
dotnet build -c Release

# Copy to Lidarr plugins directory
sudo cp -r Brainarr.Plugin/bin/Release/net6.0/* /var/lib/lidarr/plugins/Brainarr/

# Set correct permissions
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr/

# Restart Lidarr
sudo systemctl restart lidarr
```

### Verification

After installation, verify Brainarr is loaded:
1. Open Lidarr web interface
2. Go to **Settings** → **Import Lists**
3. Click the **+** button
4. Look for **"Brainarr AI Music Discovery"** in the list

❌ **Not seeing Brainarr?** Check our [Troubleshooting Guide](TROUBLESHOOTING.md#installation-issues)

## ⚙️ Configuration

### 🎯 Quick Setup (5 steps)

1. **Add Brainarr Import List**
   - Lidarr → Settings → Import Lists → **+ Add**
   - Select **"Brainarr AI Music Discovery"**

2. **Basic Settings**
   ```yaml
   Name: AI Music Discovery
   Enable Automatic Add: Yes
   Monitor: All Albums
   Root Folder: [Your music folder]
   Quality Profile: [Your preferred quality]
   Metadata Profile: Standard
   Tags: ai-recommendations  # Optional but recommended
   ```

3. **Choose AI Provider** (see [Provider Setup](#provider-setup) below)

4. **Configure Discovery Mode**
   ```yaml
   Discovery Mode: Similar      # Start conservative
   Max Recommendations: 10      # Start small
   Sampling Strategy: Balanced  # Good balance
   ```

5. **Test & Save**
   - Click **Test** button (should show ✅ success)
   - Click **Save**

### Provider Setup

Choose one or more AI providers based on your needs:

## 🎯 Supported AI Providers

Brainarr supports **9 different AI providers**, categorized by privacy and cost considerations:

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

## 🎨 Discovery Modes

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

## 🔄 Usage

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

## 📊 Provider Comparison

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

## 🔍 Troubleshooting

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

## 🛠️ Development

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

## 📖 Documentation

### 📚 Complete Guides

| Guide | Description | Best For |
|-------|-------------|----------|
| [🚀 Quick Start Guide](QUICKSTART.md) | 5-minute setup guide | New users |
| [🔧 Troubleshooting Guide](TROUBLESHOOTING.md) | Comprehensive problem solving | Issue resolution |
| [🏗️ Architecture Guide](docs/ARCHITECTURE.md) | Technical deep-dive | Developers |
| [🎯 Provider Guide](docs/PROVIDER_GUIDE.md) | AI provider comparison | Provider selection |
| [📈 User Setup Guide](docs/USER_SETUP_GUIDE.md) | Advanced configuration | Power users |

### 🔗 Additional Resources

- **🛠️ Build Instructions**: [BUILD.md](BUILD.md)
- **📋 Contributing Guide**: [CONTRIBUTING.md](CONTRIBUTING.md)
- **📝 Changelog**: [CHANGELOG.md](CHANGELOG.md)
- **🗺️ Roadmap**: [docs/ROADMAP.md](docs/ROADMAP.md)
- **📊 Recommendations Guide**: [docs/RECOMMENDATIONS.md](docs/RECOMMENDATIONS.md)

## 🏗️ Architecture

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

## 🤝 Contributing

We welcome contributions from the community! Here's how you can help:

### 🚀 Ways to Contribute

- **🐛 Report bugs** in our [Issue Tracker](https://github.com/yourusername/brainarr/issues)
- **💡 Suggest features** in [GitHub Discussions](https://github.com/yourusername/brainarr/discussions)
- **📝 Improve documentation** - documentation is always appreciated
- **🧪 Test new releases** and provide feedback
- **💻 Submit pull requests** for bug fixes or new features

### 📋 Development Process

1. **Fork** the repository
2. **Create** a feature branch: `git checkout -b feature/amazing-feature`
3. **Make** your changes with tests
4. **Run** the test suite: `dotnet test`
5. **Commit** your changes: `git commit -m 'Add amazing feature'`
6. **Push** to your branch: `git push origin feature/amazing-feature`
7. **Open** a Pull Request

See our [Contributing Guide](CONTRIBUTING.md) for detailed information.

### 🎯 Project Status

**Current Version**: 1.0.0 (Production Ready)

#### ✅ Completed Features
- ✅ Multi-provider AI support (9 providers)
- ✅ Local and cloud provider integration  
- ✅ Auto-detection and health monitoring
- ✅ Comprehensive test suite (30+ test files)
- ✅ Rate limiting and intelligent caching
- ✅ Advanced configuration validation
- ✅ MusicBrainz integration
- ✅ Recommendation sanitization

#### 🛣️ Roadmap 
*See [docs/ROADMAP.md](docs/ROADMAP.md) for complete details*
- 🔜 Additional cloud providers (AWS Bedrock, Azure OpenAI)
- 🔜 Cost monitoring and optimization dashboard
- 🔜 A/B testing framework for provider comparison
- 🔜 Enhanced music analysis algorithms
- 🔜 Plugin marketplace distribution

## 🆘 Support & Community

### 🔗 Getting Help

| Resource | Purpose | Link |
|----------|---------|------|
| 📖 **Documentation** | Complete guides and references | [docs/](docs/) |
| 🚀 **Quick Start** | Get up and running in 5 minutes | [QUICKSTART.md](QUICKSTART.md) |
| 🔧 **Troubleshooting** | Solve common issues | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) |
| 🐛 **Bug Reports** | Report issues and bugs | [GitHub Issues](https://github.com/yourusername/brainarr/issues) |
| 💬 **Discussions** | Community support and ideas | [GitHub Discussions](https://github.com/yourusername/brainarr/discussions) |
| 💡 **Feature Requests** | Suggest new features | [GitHub Discussions](https://github.com/yourusername/brainarr/discussions/categories/ideas) |

### 🙏 Acknowledgments

- **Lidarr Team** - For creating an amazing media management platform
- **AI Provider Teams** - OpenAI, Anthropic, Google, and others for their incredible models
- **Ollama Team** - For making local AI accessible to everyone
- **Community Contributors** - All the developers, testers, and users who help improve Brainarr
- **Music Metadata Providers** - MusicBrainz and others for comprehensive music data

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

### 🛡️ Security & Privacy

- **Local providers** (Ollama, LM Studio) ensure **100% privacy** - no data leaves your network
- **API keys** are stored securely using Lidarr's configuration system
- **No telemetry** or usage tracking - your music listening habits remain private
- **Open source** - full transparency in how your data is processed

---

<div align="center">

**🎵 Transform your music discovery experience with AI 🎵**

*Built with ❤️ by the Brainarr community*

[![Star on GitHub](https://img.shields.io/github/stars/yourusername/brainarr?style=social)](https://github.com/yourusername/brainarr)
[![Fork on GitHub](https://img.shields.io/github/forks/yourusername/brainarr?style=social)](https://github.com/yourusername/brainarr/fork)
[![Follow on GitHub](https://img.shields.io/github/followers/yourusername?style=social)](https://github.com/yourusername)

</div>
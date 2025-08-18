# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Brainarr is a **production-ready** multi-provider AI-powered import list plugin for Lidarr that generates intelligent music recommendations. The project supports 9 different AI providers ranging from privacy-focused local models to powerful cloud services.

## Development Status

**Current Status**: Production-ready v1.0.0 - Full implementation with comprehensive test suite

The project includes:
- Complete implementation with 9 AI providers
- Comprehensive test suite (30+ test files)
- Production-ready architecture with advanced features
- Full documentation in `docs/` folder

## Architecture Overview

The implemented architecture includes:

### Multi-Provider AI System
- **Local-First Options**: Privacy-focused local providers (Ollama, LM Studio)
- **Cloud Integration**: 9 total providers including OpenAI, Anthropic, Google Gemini, etc.
- **Provider Failover**: Automatic failover with health monitoring
- **Dynamic Detection**: Auto-detects available models for local providers

### Implemented Architecture
```
Brainarr.Plugin/
├── Configuration/          # Provider settings and validation
│   ├── Constants.cs
│   ├── ProviderConfiguration.cs
│   └── Providers/          # Per-provider configuration classes
├── Services/
│   ├── Core/              # Core orchestration services
│   │   ├── AIProviderFactory.cs
│   │   ├── AIService.cs
│   │   ├── LibraryAnalyzer.cs
│   │   └── ProviderRegistry.cs
│   ├── Providers/         # AI provider implementations (9 providers)
│   ├── Support/           # Supporting services
│   ├── LocalAIProvider.cs
│   ├── ModelDetectionService.cs
│   ├── ProviderHealth.cs
│   ├── RateLimiter.cs
│   ├── RecommendationCache.cs
│   └── RetryPolicy.cs
├── BrainarrImportList.cs  # Main Lidarr integration
└── BrainarrSettings.cs    # Configuration UI

Brainarr.Tests/            # Comprehensive test suite
├── Configuration/         # Configuration tests
├── Services/Core/         # Core service tests
├── Services/              # Provider tests
├── Integration/           # End-to-end tests
└── EdgeCases/            # Edge case handling
```

### Key Technical Patterns
- **Provider Pattern**: Each AI service implements `IAIProvider` interface
- **Factory Pattern**: `AIProviderFactory` manages provider instantiation  
- **Registry Pattern**: `ProviderRegistry` for extensible provider management
- **Health Monitoring**: Real-time provider availability tracking
- **Rate Limiting**: Per-provider rate limiting with configurable limits
- **Caching**: Intelligent recommendation caching to reduce API calls
- **Retry Policies**: Exponential backoff retry with circuit breaker patterns

## Implemented Features

### Core Functionality
- ✅ 9 AI providers (local + cloud)
- ✅ Auto-detection of local models
- ✅ Provider health monitoring
- ✅ Rate limiting and caching
- ✅ Comprehensive configuration validation
- ✅ Library analysis and profiling
- ✅ Recommendation sanitization

### Technology Stack
- **Platform**: .NET 6+ (Lidarr plugin framework)
- **HTTP Client**: Lidarr's IHttpClient for provider communication
- **Configuration**: Lidarr's field definition system with validation
- **Logging**: NLog integration with structured logging
- **Testing**: Comprehensive test suite covering all components

## Development Workflow

For ongoing development:

1. **Build**: `dotnet build` 
2. **Test**: `dotnet test` (30+ test files)
3. **Deploy**: Copy to Lidarr plugins directory
4. **Debug**: Enable debug logging in Lidarr settings

### Common Development Commands
```bash
# Build plugin
dotnet build -c Release

# Run full test suite  
dotnet test

# Run specific test categories
dotnet test --filter Category=Integration
dotnet test --filter Category=EdgeCase

# Package for deployment
dotnet publish -c Release
```

## CI/CD Pipeline Solution

**RESOLVED**: The GitHub Actions CI was previously failing due to missing Lidarr assembly dependencies. This has been definitively solved using a **stable assembly download approach**.

### The Working Solution

The CI workflow now uses pre-built Lidarr assemblies instead of trying to build from source:

```yaml
- name: Download Lidarr Assemblies  
  run: |
    echo "Downloading Lidarr assemblies from latest release..."
    mkdir -p ext/Lidarr/_output/net6.0
    
    # Get latest release URL dynamically
    LIDARR_URL=$(curl -s https://api.github.com/repos/Lidarr/Lidarr/releases/latest | grep "browser_download_url.*linux-core-x64.tar.gz" | cut -d '"' -f 4 | head -1)
    
    if [ -n "$LIDARR_URL" ]; then
      curl -L "$LIDARR_URL" -o lidarr.tar.gz
    else
      # Fallback to known version
      curl -L "https://github.com/Lidarr/Lidarr/releases/download/v2.13.1.4681/Lidarr.main.2.13.1.4681.linux-core-x64.tar.gz" -o lidarr.tar.gz
    fi
    
    tar -xzf lidarr.tar.gz
    
    # Copy required assemblies
    cp Lidarr/Lidarr.Core.dll ext/Lidarr/_output/net6.0/
    cp Lidarr/Lidarr.Common.dll ext/Lidarr/_output/net6.0/
    cp Lidarr/Lidarr.Http.dll ext/Lidarr/_output/net6.0/
    cp Lidarr/Lidarr.Api.V1.dll ext/Lidarr/_output/net6.0/
```

### Why This Works

1. **Stable Dependencies**: Uses official Lidarr release binaries that are tested and stable
2. **No Compilation**: Eliminates complex build dependencies and potential source compilation failures
3. **Fast & Reliable**: Simple download and extract, much faster than building from source
4. **Cross-Platform**: Works identically across Ubuntu, Windows, and macOS runners
5. **Maintainable**: Clear, understandable workflow that's easy to debug

### Key Learnings

- **Never build Lidarr from source in CI** - it's complex, slow, and error-prone
- **Use pre-built assemblies** - download from GitHub releases instead
- **Dynamic URL detection** - use GitHub API to get latest release URLs with fallbacks
- **Proper error handling** - validate downloads and extractions with clear error messages
- **Cross-platform testing** - test on multiple OS/runtime combinations

### Local Development Setup

The project's `.csproj` file has sophisticated Lidarr path resolution that automatically finds assemblies in:
1. Command line: `-p:LidarrPath=...`
2. Environment: `LIDARR_PATH` 
3. Local submodule: `ext/Lidarr/_output/net6.0`
4. System installations: `/opt/Lidarr`, `C:\ProgramData\Lidarr\bin`, etc.

For local development, ensure Lidarr assemblies are present in `ext/Lidarr/_output/net6.0/` or set the `LIDARR_PATH` environment variable.

### CI Status: ✅ WORKING

The CI pipeline now successfully:
- ✅ Downloads Lidarr assemblies from GitHub releases
- ✅ Builds plugin across 6 environments (Ubuntu/Windows/macOS × .NET 6.0.x/8.0.x)
- ✅ Runs comprehensive test suite (30+ test files)
- ✅ Performs security analysis with CodeQL
- ✅ Creates release packages on tagged releases

**This solution is definitive and should not require further changes.**

## Local Development Setup

1. **Prerequisites**:
   - .NET 6+ SDK
   - Lidarr development environment
   - At least one AI provider (Ollama recommended for testing)

2. **Development Environment**:
   - IDE: Visual Studio, VS Code, or JetBrains Rider
   - Testing: Local Lidarr instance for plugin testing
   - AI Providers: Local Ollama installation recommended

## Security Considerations

- API keys stored securely through Lidarr's configuration system
- Local providers prioritized to avoid data transmission
- No sensitive music library data logged or transmitted unnecessarily
- Rate limiting and error handling for cloud providers
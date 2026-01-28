# Brainarr Documentation

Brainarr is an AI-powered recommendation plugin for Lidarr that helps you discover new music using artificial intelligence. This documentation is organized by audience to help you find the information you need quickly.

## Quick Navigation

### 🚀 Getting Started
- **[User Setup Guide](USER_SETUP_GUIDE.md)** - Installation and initial configuration
- **[First Run Guide](../wiki-content/First-Run-Guide.md)** - Quick start tutorial
- **[Installation Guide](../wiki-content/Installation.md)** - Detailed installation instructions

### 🔧 User Guides
- **[Provider Guide](PROVIDER_GUIDE.md)** - Choosing and configuring AI providers
- **[Configuration Reference](configuration.md)** - All settings explained
- **[Cloud Providers](../wiki-content/Cloud-Providers.md)** - Cloud provider setup
- **[Local Providers](../wiki-content/Local-Providers.md)** - Local provider setup (Ollama, LM Studio)

### 🛠️ Technical Documentation
- **[Architecture](ARCHITECTURE.md)** - High-level system design
- **[Planner & Cache](planner-and-cache.md)** - Prompt planning and caching behavior
- **[Tokenization](tokenization-and-estimates.md)** - Token budgeting and estimation
- **[Correlation Tracking](CORRELATION_TRACKING.md)** - Request tracing system

### 🚀 Operations & Deployment
- **[Deployment Guide](DEPLOYMENT.md)** - Manual, Docker, and CI/CD deployment
- **[Performance Tuning](PERFORMANCE_TUNING.md)** - Optimization guide
- **[Security](SECURITY.md)** - Security best practices
- **[Migration Guide](MIGRATION_GUIDE.md)** - Upgrading between versions

### 👨‍💻 Development
- **[Testing Guide](TESTING_GUIDE.md)** - Running and writing tests
- **[CI/CD Guide](CI_CD_IMPROVEMENTS.md)** - Continuous integration
- **[Release Process](RELEASE_PROCESS.md)** - How releases are made
- **[Plugin Lifecycle](PLUGIN_LIFECYCLE.md)** - Plugin startup and shutdown
- **[Contributing](../CONTRIBUTING.md)** - Development guidelines and contribution process

### 🔍 Reference
- **[API Reference](API_REFERENCE.md)** - Interfaces and classes
- **[Metrics Reference](METRICS_REFERENCE.md)** - All metrics with tags
- **[Provider Matrix](PROVIDER_MATRIX.md)** - Provider status (generated)
- **[Changelog](../CHANGELOG.md)** - Release history

## Documentation Structure

### 📚 User Documentation (Wiki-First)

**Primary documentation is in the GitHub Wiki:**
- **[Home](../wiki-content/Home.md)** - Welcome page with quick overview
- **[Installation Guide](../wiki-content/Installation.md)** - Step-by-step setup instructions
- **[First Run Guide](../wiki-content/First-Run-Guide.md)** - Get started in 5 minutes
- **[Provider Basics](../wiki-content/Provider-Basics.md)** - Understanding AI providers
- **[Provider Setup Guide](../wiki-content/Provider-Setup-Guide.md)** - Detailed provider configuration
- **[Cloud Providers](../wiki-content/Cloud-Providers.md)** - OpenAI, Anthropic, Gemini, etc.
- **[Local Providers](../wiki-content/Local-Providers.md)** - Ollama, LM Studio setup
- **[Advanced Settings](../wiki-content/Advanced-Settings.md)** - Configuration options
- **[Settings Best Practices](../wiki-content/Settings-Best-Practices.md)** - Optimization tips
- **[Review Queue](../wiki-content/Review-Queue.md)** - Understanding recommendations
- **[Troubleshooting](../wiki-content/Troubleshooting.md)** - Common issues and solutions
- **[Observability](../wiki-content/Observability-and-Metrics.md)** - Monitoring and metrics

### 🏗️ Technical Documentation (docs/)

**Detailed technical information for developers and advanced users:**
- **Architecture** - System design and implementation details
- **Development** - Setup, testing, and contribution guidelines
- **Operations** - Deployment, performance, and security
- **Reference** - API documentation and metrics

### 🔄 Synchronization

**The wiki is automatically synchronized from `wiki-content/` directory:**
- Updates happen automatically on releases and content changes
- Manual updates available via GitHub Actions
- See [WIKI-SYNC.md](development/WIKI-SYNC.md) for details

## Getting Help

### Documentation Levels

| Level | Target Audience | Content Type |
|-------|----------------|--------------|
| **Wiki** | End users | Setup, configuration, basic usage |
| **docs/** | Developers, advanced users | Technical details, API, architecture |
| **README** | Project visitors | Overview, quick start, ecosystem |

### Support Channels

- **Wiki**: [GitHub Wiki](https://github.com/RicherTunes/Brainarr/wiki) - User documentation
- **Issues**: [GitHub Issues](https://github.com/RicherTunes/Brainarr/issues) - Bug reports
- **Discussions**: [GitHub Discussions](https://github.com/RicherTunes/Brainarr/discussions) - Q&A
- **Discord**: [RicherTunes Community](https://discord.gg/richertunes) - Live chat

## Ecosystem Integration

### Related Plugins

Brainarr is part of the RicherTunes plugin ecosystem:

| Plugin | Description | Status |
|--------|-------------|--------|
| **[Tidalarr](https://github.com/RicherTunes/tidalarr)** | Tidal streaming integration | ✅ Production |
| **[Qobuzarr](https://github.com/RicherTunes/qobuzarr)** | Qobuz streaming with ML | ✅ Production |
| **[AppleMusicarr](https://github.com/RicherTunes/AppleMusicarr)** | Apple Music library sync | ✅ Production |

### Shared Foundation

All plugins use the [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common) shared library for:
- Plugin hosting and lifecycle management
- HTTP client with resilience and retry policies
- Caching infrastructure
- Settings management and validation

### Project Resources

| Resource | Description |
|----------|-------------|
| **[Main README](../README.md)** | Project overview, features, and installation |
| **[Changelog](../CHANGELOG.md)** | Detailed release history |
| **[Contributing Guide](../CONTRIBUTING.md)** | Development guidelines and contribution process |
| **[Security Policy](../SECURITY.md)** | Vulnerability reporting and security practices |

## Documentation Maintenance

### Updating Documentation

1. **User-facing changes** - Update `wiki-content/` files
2. **Technical changes** - Update `docs/` files
3. **Version changes** - Update both locations
4. **Automated sync** - GitHub Actions handles wiki updates

### Quality Checks

- **Markdown linting** - Enforced in CI
- **Link validation** - All links checked automatically
- **Consistency checks** - Version badges and references verified
- **Automated updates** - Provider matrix and version references

### Contributing

See [CONTRIBUTING.md](../CONTRIBUTING.md) for:
- Documentation contribution guidelines
- Setup requirements
- Testing procedures
- Pull request process

---

**Note**: Most users should start with the [GitHub Wiki](https://github.com/RicherTunes/Brainarr/wiki) for user documentation. Technical documentation in `docs/` is intended for developers and advanced users.

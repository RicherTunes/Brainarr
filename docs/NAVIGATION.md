# Brainarr Navigation Guide

This guide helps you navigate Brainarr's documentation ecosystem. Choose your path based on your role and needs.

## Quick Start

### New to Brainarr?
1. **Start here**: [GitHub Wiki Home](https://github.com/RicherTunes/Brainarr/wiki/Home)
2. **5-minute setup**: [First Run Guide](wiki-content/First-Run-Guide.md)
3. **Install properly**: [Installation Guide](wiki-content/Installation.md)

### Need help now?
- **Troubleshooting**: [Wiki Troubleshooting](wiki-content/Troubleshooting.md)
- **Ask questions**: [GitHub Discussions](https://github.com/RicherTunes/Brainarr/discussions)
- **Report issues**: [GitHub Issues](https://github.com/RicherTunes/Brainarr/issues)

## Documentation Paths

### Path 1: End User (Most Users)
```
GitHub Wiki → Setup Guide → Configure Providers → Use Brainarr
```

**Core Documentation**:
- [Home](wiki-content/Home.md) - Welcome and overview
- [Installation](wiki-content/Installation.md) - Get installed
- [First Run Guide](wiki-content/First-Run-Guide.md) - Quick start
- [Provider Basics](wiki-content/Provider-Basics.md) - Understand providers
- [Provider Setup](wiki-content/Provider-Setup-Guide.md) - Configure providers
- [Advanced Settings](wiki-content/Advanced-Settings.md) - Customize options
- [Review Queue](wiki-content/Review-Queue.md) - Understand results
- [Troubleshooting](wiki-content/Troubleshooting.md) - Fix problems

**Provider-Specific**:
- [Local Providers](wiki-content/Local-Providers.md) - Ollama, LM Studio
- [Cloud Providers](wiki-content/Cloud-Providers.md) - OpenAI, Anthropic, etc.

### Path 2: Advanced User / Power User
```
GitHub Wiki → Advanced Docs → Performance → Monitoring
```

**Extended Documentation**:
- [User Setup Guide](USER_SETUP_GUIDE.md) - Detailed setup
- [Configuration Reference](configuration.md) - All settings
- [Settings Best Practices](wiki-content/Settings-Best-Practices.md) - Optimization
- [Observability](wiki-content/Observability-and-Metrics.md) - Monitoring

**Additional Resources**:
- [Architecture](ARCHITECTURE.md) - How it works
- [Metrics Reference](METRICS_REFERENCE.md) - Technical metrics
- [Provider Guide](PROVIDER_GUIDE.md) - Deep dive on providers

### Path 3: Developer / Contributor
```
README → Contributing → Development → Technical Docs
```

**Getting Started**:
- [Main README](../README.md) - Project overview
- [Contributing Guide](../CONTRIBUTING.md) - How to contribute
- [Development Setup](development/DEVELOPMENT.md) - Build environment

**Technical Documentation**:
- [Architecture](ARCHITECTURE.md) - System design
- [Testing Guide](TESTING_GUIDE.md) - Testing procedures
- [CI/CD Guide](CI_CD_IMPROVEMENTS.md) - Build and deploy
- [Release Process](RELEASE_PROCESS.md) - Release workflow
- [Plugin Lifecycle](PLUGIN_LIFECYCLE.md) - Internal details

**Reference**:
- [API Reference](API_REFERENCE.md) - Code interfaces
- [Code Repository](https://github.com/RicherTunes/Brainarr) - Source code
- [Shared Library](https://github.com/RicherTunes/Lidarr.Plugin.Common) - Dependencies

### Path 4: System Administrator / DevOps
```
README → Deployment → Operations → Security
```

**Operations Documentation**:
- [Deployment Guide](DEPLOYMENT.md) - Install and deploy
- [Performance Tuning](PERFORMANCE_TUNING.md) - Optimize performance
- [Security](SECURITY.md) - Security practices
- [Migration Guide](MIGRATION_GUIDE.md) - Upgrade process

## Documentation Types

### Primary Sources

| Type | Location | Audience | Updates |
|------|----------|----------|---------|
| **User Wiki** | GitHub Wiki | End users | Manual updates |
| **Technical Docs** | `docs/` directory | Developers/advanced | Automated with code |
| **Code Comments** | Source files | Developers | With development |
| **README** | Root | All visitors | Major releases |

### Sync Status

- **GitHub Wiki**: Auto-synced from `wiki-content/` on releases and content changes
- **Technical Docs**: Updated with development
- **Links**: Validated in CI pipeline
- **Version References**: Updated on releases

## Key Files by Topic

### Installation & Setup
- [Installation Guide](wiki-content/Installation.md) (Wiki)
- [User Setup Guide](USER_SETUP_GUIDE.md) (docs)
- [First Run Guide](wiki-content/First-Run-Guide.md) (Wiki)

### Configuration
- [Provider Setup Guide](wiki-content/Provider-Setup-Guide.md) (Wiki)
- [Configuration Reference](configuration.md) (docs)
- [Advanced Settings](wiki-content/Advanced-Settings.md) (Wiki)

### Troubleshooting
- [Troubleshooting Guide](wiki-content/Troubleshooting.md) (Wiki)
- [Provider Guide](PROVIDER_GUIDE.md) (docs)
- [Observability](wiki-content/Observability-and-Metrics.md) (Wiki)

### Development
- [Contributing Guide](../CONTRIBUTING.md) (Root)
- [Testing Guide](TESTING_GUIDE.md) (docs)
- [Architecture](ARCHITECTURE.md) (docs)

### Operations
- [Deployment Guide](DEPLOYMENT.md) (docs)
- [Performance Tuning](PERFORMANCE_TUNING.md) (docs)
- [Security](SECURITY.md) (docs)

## Related Projects

### RicherTunes Ecosystem
- **Brainarr** (this project) - AI music recommendations
- **Tidalarr** - Tidal streaming integration
- **Qobuzarr** - Qobuz streaming with ML
- **AppleMusicarr** - Apple Music library sync

### Shared Foundation
- **Lidarr.Plugin.Common** - Common plugin infrastructure
- **Lidarr** - Core media management platform

## Search & Find Content

### By Keyword
- **Setup**: Look for "Installation" or "Setup" in wiki or docs
- **Providers**: Look for "Provider Setup" or specific provider names
- **Errors**: Check "Troubleshooting" and error-specific docs
- **Code**: Search in `docs/` or source code with grep
- **Config**: Look in "Configuration" or "Settings" sections

### By Role
- **User**: Start with GitHub Wiki
- **Developer**: Start with README → Contributing
- **Admin**: Start with Deployment Guide
- **Support**: Start with Troubleshooting Wiki

## Where Not to Look

- **Development setup** not in Wiki (wrong audience)
- **Basic installation** not in `docs/` (too technical)
- **API details** not in Wiki (for developers only)
- **Code examples** in Wiki (unless very simple)

## Getting Help

If you can't find what you need:

1. **Search** the documentation first
2. **Check GitHub Discussions** for similar questions
3. **Search existing Issues** for your problem
4. **Create a new Issue** with details
5. **Join Discord** for live help

## Update Process

### For Users
- Check GitHub Wiki for latest information
- Releases update both wiki and technical docs
- Wiki pages stable, rarely change

### For Developers
- Update `wiki-content/` for user-facing changes
- Update `docs/` for technical changes
- Run sync scripts before PR
- Review all documentation changes

### For Maintainers
- Monitor documentation completeness
- Update on releases
- Handle user feedback
- Keep links current

---

This navigation guide helps you find the right information for your needs. Most users should start with the GitHub Wiki, while developers and advanced users can dive into the technical documentation in `docs/`.
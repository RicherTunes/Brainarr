# Changelog

All notable changes to the Brainarr plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Comprehensive API reference documentation
- Testing guide with examples and best practices  
- Plugin manifest documentation
- Deployment and CI/CD documentation
- Troubleshooting guide with common issues and solutions
- Performance tuning documentation

### Improved
- Enhanced inline XML documentation for all public interfaces and classes
- Added detailed comments to provider implementations
- Expanded troubleshooting section with debug procedures
- Added security best practices documentation

### Documentation
- Created `/docs/API_REFERENCE.md` - Complete API documentation
- Created `/docs/TESTING_GUIDE.md` - Testing strategies and examples
- Created `/docs/PLUGIN_MANIFEST.md` - Plugin.json field descriptions
- Created `/docs/DEPLOYMENT.md` - Deployment and CI/CD pipelines
- Created `/docs/TROUBLESHOOTING.md` - Comprehensive troubleshooting guide
- Updated existing documentation for accuracy against codebase

## [1.0.0] - 2025-01-12

### Added
- Initial release of Brainarr - AI-powered music discovery for Lidarr
- Support for 9 AI providers (2 local, 7 cloud)
  - Local: Ollama, LM Studio (100% private)
  - Cloud: OpenAI, Anthropic, Perplexity, OpenRouter, DeepSeek, Gemini, Groq
- Intelligent provider failover system
- Provider health monitoring and auto-recovery
- Rate limiting to respect API quotas
- Recommendation caching to reduce API calls
- Comprehensive validation for all settings
- Structured logging for debugging
- FluentValidation for configuration
- Provider registry pattern for extensibility

### Security
- API keys stored securely with PrivacyLevel.Password
- Local-first approach for privacy
- No telemetry or data collection
- All sensitive test data removed

### Technical
- .NET 6.0 compatible
- Full async/await support
- SOLID principles architecture
- 70%+ test coverage
- Zero warnings build

### Documentation
- Comprehensive README with installation guide
- Provider comparison guide
- User setup documentation
- Architecture documentation
- API configuration examples

## [0.0.1] - 2024-12-01

### Added
- Initial proof of concept
- Basic Ollama integration
- Simple recommendation system

---

## Roadmap

### [1.1.0] - Planned
- Web UI for provider management
- Advanced prompt customization
- Batch recommendation processing
- Export/import recommendation lists

### [1.2.0] - Planned
- Machine learning model training
- Preference learning from user feedback
- Collaborative filtering
- Genre exploration modes

### [2.0.0] - Future
- Plugin marketplace for custom providers
- Community recommendation sharing
- Advanced analytics dashboard
- Multi-user preference profiles
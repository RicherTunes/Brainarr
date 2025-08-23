# Changelog

All notable changes to the Brainarr plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Recommendation Modes** - New user-configurable setting to choose between recommending specific albums vs entire artist discographies
- **Correlation Context Tracking** - End-to-end request tracing with correlation IDs for better debugging and monitoring
- **Enhanced Debug Logging** - Comprehensive logging for AI provider interactions with correlation tracking
- **Improved Rate Limiting** - RateLimiterImproved implementation with better provider-specific controls
- **Library Sampling Strategy** - Configurable library analysis depth (Minimal/Balanced/Comprehensive)
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
- Corrected provider documentation accuracy (8 providers, not 9)
- Updated test count references (33 test files)

### Fixed
- Library sampling strategy token allocation for optimal AI context usage
- NLog version conflict breaking Lidarr startup
- CI build issues with conditional NLog references

### Security
- Enhanced security with PBKDF2 encryption for sensitive data
- ReDoS (Regular Expression Denial of Service) protection added
- URL sanitization in correlation context logging

### Documentation
- Created `/docs/RECOMMENDATION_MODES.md` - Guide for Album vs Artist recommendation modes
- Created `/docs/CORRELATION_TRACKING.md` - Correlation context and request tracking guide
- Created `/docs/DOCUMENTATION_STATUS.md` - Single source of truth for documentation health
- Created `/docs/API_REFERENCE.md` - Complete API documentation
- Created `/docs/TESTING_GUIDE.md` - Testing strategies and examples
- Created `/docs/PLUGIN_MANIFEST.md` - Plugin.json field descriptions
- Created `/docs/DEPLOYMENT.md` - Deployment and CI/CD pipelines
- Created `/docs/TROUBLESHOOTING.md` - Comprehensive troubleshooting guide
- Updated existing documentation for accuracy against codebase
- Consolidated 11 redundant audit reports into single status document

## [1.0.0] - 2025-01-12

### Added
- Initial release of Brainarr - AI-powered music discovery for Lidarr
- Support for 8 AI providers (2 local, 6 cloud)
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
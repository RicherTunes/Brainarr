# Changelog

All notable changes to the Brainarr plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

### Changed

### Fixed

### Security

### Documentation

## [1.2.0] - 2025-09-04

### Added
- Guarantee Exact Target setting to push for exact N recommendations when under target.
- Library-aware iterative top-up uses current artists/albums to avoid duplicates up front.
- LM Studio provider: artist-mode system prompt for artist-only recommendations.
- New wiki page: Provider Basics (Choosing a Provider, Configuration URL, API Keys).

### Changed
- Adaptive top-up requests larger batches and may allow one extra iteration when Guarantee Exact Target is enabled.
- Normalized settings HelpText to ASCII and added HelpLinks to relevant wiki sections.
- Simplified budget messages (ASCII-only) in TokenCostEstimator.

### Fixed
- Removed circular DI by dropping IImportListFactory from Brainarr provider constructor.
- Top-up no longer stops early due to batch-unique items that still duplicate the library.

### Documentation
- Advanced Settings expanded with Recommendations (exact-target behavior), Model Selection, Iterative Top-Up, Guarantee Exact Target tips.
- Provider Setup guidance consolidated under Provider Basics with Docker host URL tips.

## [1.1.1] - 2025-09-03

### Changed
- Review UX: Approve Suggestions now uses TagSelect (chip-style multi-select) with a clear placeholder and help text.
- Review UX: Grouped queue-related controls under a "Review Queue" section for discoverability.
- Review UX: Review Summary converted to TagSelect with dynamic counts via provider options.

### Fixed
- Settings: Approve Suggestions was rendered as a free-text field; now a proper multi-select list backed by options.
- Actions: Normalized action routing to lowercase to ensure options load (review/getoptions, review/getsummaryoptions, rejectselected, neverselected).

## [1.1.0] - 2025-09-02

### Added
- Recommendation Modes – user can choose specific albums vs entire artists
- Correlation Context Tracking – end-to-end request tracing via correlation IDs
- Enhanced Debug Logging – comprehensive provider interaction logs with correlation
- Improved Rate Limiting – provider-specific controls and stability improvements
- Library Sampling Strategy – Minimal/Balanced/Comprehensive analysis depth
- Comprehensive API reference, testing guide, plugin manifest docs
- Deployment + CI/CD documentation
- Troubleshooting and performance tuning guides

### Improved
- Richer inline XML docs for public interfaces and classes
- Provider implementations clarified with comments
- Expanded troubleshooting with actionable debug procedures
- Security best practices documentation
- Documentation consistency and test count references updated

### Fixed
- Token allocation in sampling strategy for better AI context usage
- NLog version conflict that could break Lidarr startup
- CI build issues with conditional NLog references

### Security
- PBKDF2 protection for sensitive data
- ReDoS protection on regex validations
- URL sanitization within correlation logging

### Documentation
- Added docs: RECOMMENDATION_MODES, CORRELATION_TRACKING, DOCUMENTATION_STATUS,
  API_REFERENCE, TESTING_GUIDE, PLUGIN_MANIFEST, DEPLOYMENT, TROUBLESHOOTING
- Updated existing docs to reflect current code
- Consolidated 11 redundant audit reports into a single status doc

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

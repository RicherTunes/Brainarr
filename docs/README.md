# Brainarr Documentation

Brainarr is an AI-powered recommendation plugin for Lidarr. This documentation is organized by audience.

## User Documentation

| Document | Description |
|----------|-------------|
| [User Setup Guide](USER_SETUP_GUIDE.md) | Installation and initial configuration |
| [Is It Working?](IS_IT_WORKING.md) | Verification checklist for your installation |
| [Configuration Reference](configuration.md) | All settings explained |
| [Provider Guide](PROVIDER_GUIDE.md) | Choosing and configuring AI providers |
| [Troubleshooting](troubleshooting.md) | Common issues, FAQ, and provider-specific errors |

## Technical Documentation

| Document | Description |
|----------|-------------|
| [Architecture](ARCHITECTURE.md) | High-level system design |
| [Planner & Cache](planner-and-cache.md) | Prompt planning and caching behavior |
| [Tokenization](tokenization-and-estimates.md) | Token budgeting and estimation |
| [Correlation Tracking](CORRELATION_TRACKING.md) | Request tracing system |

## Operations & Deployment

| Document | Description |
|----------|-------------|
| [Deployment Guide](DEPLOYMENT.md) | Manual, Docker, and CI/CD deployment |
| [Performance Tuning](PERFORMANCE_TUNING.md) | Optimization guide |
| [Security](SECURITY.md) | Security best practices |
| [Migration Guide](MIGRATION_GUIDE.md) | Upgrading between versions |

## Development

| Document | Description |
|----------|-------------|
| [Testing Guide](TESTING_GUIDE.md) | Running and writing tests |
| [CI/CD Guide](CI_CD_IMPROVEMENTS.md) | Continuous integration |
| [Release Process](RELEASE_PROCESS.md) | How releases are made |
| [Plugin Lifecycle](PLUGIN_LIFECYCLE.md) | Plugin startup and shutdown |

## Reference

| Document | Description |
|----------|-------------|
| [API Reference](API_REFERENCE.md) | Interfaces and classes |
| [Metrics Reference](METRICS_REFERENCE.md) | All metrics with tags |
| [Provider Matrix](PROVIDER_MATRIX.md) | Provider status (generated) |

## Architecture Details (`architecture/`)

- [Orchestrator Blueprint](architecture/brainarr-orchestrator-blueprint.md)
- [Configuration Validation](architecture/configuration-validation-tests.md)
- [Shared Library Integration](architecture/shared-library-integration.md)
- [Source Set Hygiene](architecture/source-set-hygiene.md)

## Related Resources

### Shared Library
Brainarr uses the [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common) shared library for:
- Plugin hosting and lifecycle management
- HTTP client with resilience and retry policies
- Caching infrastructure
- Settings management and validation

See the [shared library documentation](https://github.com/RicherTunes/Lidarr.Plugin.Common/tree/main/docs) for:
- [Build Your First Plugin](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/tutorials/BUILD_YOUR_FIRST_PLUGIN.md) - Plugin development tutorial
- [Key Services Reference](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/reference/KEY_SERVICES.md) - HTTP, resilience, caching APIs
- [Compatibility Matrix](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/COMPATIBILITY.md) - Version compatibility

### Sister Plugins
- [Tidalarr](https://github.com/RicherTunes/tidalarr) - Tidal streaming plugin
- [Qobuzarr](https://github.com/RicherTunes/qobuzarr) - Qobuz streaming plugin with ML optimization

### Project Resources
- [README](../README.md) - Project overview
- [CHANGELOG](../CHANGELOG.md) - Release history

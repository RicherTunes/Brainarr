# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Brainarr is a multi-provider AI-powered import list plugin for Lidarr that generates intelligent music recommendations. The project is in early planning/design phase and focuses on supporting both local AI providers (like Ollama) and cloud providers (OpenAI, Anthropic, Google, etc.) with a privacy-first approach.

## Development Phase

**Current Status**: Pre-development phase - project consists of technical design documents and roadmap only. No implementation code exists yet.

The project is structured around comprehensive planning documents:
- `docs/TDD.md` - Detailed Technical Design Document with full architecture specification
- `docs/ROADMAP.md` - 5-week development roadmap with implementation phases

## Architecture Overview

Based on the technical design document, the planned architecture includes:

### Multi-Provider AI System
- **Local-First Philosophy**: Prioritizes privacy with local AI models (Ollama, LM Studio, Jan.ai)
- **Cloud Fallback**: Supports 15+ cloud providers (OpenAI, Anthropic, Google, etc.)
- **Provider Failover**: Automatic fallback chain when primary provider fails
- **Dynamic Detection**: Auto-detects available local providers and their models

### Core Components (Planned)
```
Brainarr.Plugin/
├── Configuration/          # Provider-specific settings
├── Services/
│   ├── Core/              # Library analysis and caching
│   └── AI/                # Multi-provider AI orchestration
├── Models/                # Data models and DTOs  
├── Utilities/             # Prompt building and response parsing
└── Resources/             # Templates and documentation
```

### Key Technical Patterns
- **Provider Pattern**: Each AI service implements `IAIProvider` interface
- **Factory Pattern**: `AIServiceFactory` manages provider instantiation
- **Chain of Responsibility**: Failover between providers on errors
- **Configuration-Driven**: Provider settings managed through Lidarr UI

## Development Approach

### Implementation Phases
1. **Phase 1**: Ollama + OpenAI + Anthropic providers with basic failover
2. **Phase 2**: Additional local providers (LM Studio, Jan.ai) and cloud providers  
3. **Phase 3**: Advanced features (cost optimization, A/B testing)

### Technology Stack
- **Platform**: .NET (Lidarr plugin framework)
- **HTTP Client**: For AI provider API communication
- **Configuration**: Lidarr's field definition system
- **Logging**: Lidarr's built-in logging framework

## Key Design Principles

1. **Privacy First**: Local providers preferred, cloud providers as fallback
2. **Zero Configuration**: Auto-detect available providers and suggest optimal setup
3. **Fail Gracefully**: Multiple provider fallbacks prevent total failure
4. **Cost Conscious**: Track token usage and provide cost estimates
5. **User Guidance**: Interactive setup wizard and provider recommendations

## Development Workflow

Since no code exists yet, initial development should:

1. **Start with Core Interfaces**: Define `IAIProvider`, `IAIService` contracts
2. **Implement Simple Provider**: Begin with Ollama provider as proof of concept
3. **Add Configuration System**: Provider settings with Lidarr field definitions
4. **Build Provider Manager**: Failover logic and provider orchestration
5. **Follow TDD Principles**: Referenced in `docs/TDD.md` for comprehensive design

## Local Development

The project targets the Lidarr plugin ecosystem, so development will require:
- Lidarr development environment setup
- .NET SDK for plugin compilation
- Local AI providers (Ollama recommended) for testing

When implementation begins, common commands will likely include:
- Build: Standard .NET build process
- Test: Unit tests for provider implementations
- Deploy: Plugin installation to Lidarr instance

## Security Considerations

- API keys stored securely through Lidarr's configuration system
- Local providers prioritized to avoid data transmission
- No sensitive music library data logged or transmitted unnecessarily
- Rate limiting and error handling for cloud providers
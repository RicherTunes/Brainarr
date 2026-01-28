# Documentation Structure & Strategy

This document explains the documentation structure for Brainarr, including the intentional duplication between `docs/` and `wiki-content/` directories.

## Overview

Brainarr uses a two-part documentation system designed to serve different audiences while maintaining content integrity:

1. **`docs/`** - Technical documentation for developers and advanced users
2. **`wiki-content/`** - User documentation for end users (synchronized to GitHub Wiki)

## Why Two Documentation Systems?

### Target Audiences

| Directory | Audience | Purpose | Complexity |
|-----------|----------|---------|------------|
| **`docs/`** | Developers, advanced users | Technical implementation, API reference, development setup | High |
| **`wiki-content/`** | End users | Installation, configuration, basic troubleshooting | Low-medium |

### User Journey

```
New User → GitHub Wiki → Basic Setup
Advanced User → wiki-content/ → Advanced Configuration
Developer → docs/ → Implementation Details
```

## Content Strategy

### What Goes Where

#### `wiki-content/` (User Documentation)

**Purpose**: Help users install, configure, and use Brainarr
**Audience**: End users, not developers
**Tone**: Friendly, supportive, practical
**Examples**: Step-by-step guides, screenshots, troubleshooting

**Content included**:
- Installation instructions
- Setup guides for each provider
- Configuration options with explanations
- Basic troubleshooting steps
- Best practices for common use cases
- Getting started tutorials

**Content excluded**:
- Technical architecture details
- Development setup instructions
- API documentation
- Code examples
- Build processes

#### `docs/` (Technical Documentation)

**Purpose**: Provide detailed technical information for developers and advanced users
**Audience**: Developers, system administrators, advanced users
**Tone**: Professional, precise, comprehensive
**Examples**: API references, architecture diagrams, configuration examples

**Content included**:
- System architecture and design
- Development setup and environment
- API references and interfaces
- Deployment and operations
- Performance tuning
- Security considerations
- Testing procedures
- Release processes

**Content excluded**:
- Basic installation instructions (already in wiki)
- Simple user tutorials
- Non-technical troubleshooting

## Intentional Duplication

### What's Duplicated

Some content appears in both directories, but with different focus:

| Topic | `wiki-content/` Focus | `docs/` Focus |
|-------|----------------------|---------------|
| **Installation** | Simple steps for end users | Technical deployment details |
| **Configuration** | Basic options with explanations | Advanced options and internals |
| **Troubleshooting** | Common user issues | Technical debugging and diagnostics |
| **Providers** | How to set up each provider | Implementation details and testing |

### Why Duplicate?

1. **Separation of Concerns**
   - Users don't need technical details
   - Developers don't need basic setup instructions
   - Different audiences, different needs

2. **Maintenance Efficiency**
   - User docs rarely change
   - Technical docs update with development
   - Reduced cognitive load

3. **Navigation**
   - Clear path for each audience
   - No overwhelming amount of information
   - Context-appropriate details

### How to Maintain

#### When Adding New Content

1. **Determine audience**
   - Will users or developers need this?
   - Should it go in `wiki-content/` or `docs/`?

2. **Check for duplicates**
   - Similar content may already exist
   - Decide if duplication is intentional

3. **Add to appropriate location**
   - User-facing: `wiki-content/`
   - Technical: `docs/`

4. **Update references**
   - Link between related documents
   - Update navigation as needed

#### Content Updates

- **User updates**: Primarily affect `wiki-content/`
- **Technical updates**: Primarily affect `docs/`
- **Version updates**: May require changes in both
- **Breaking changes**: Document in both locations

## File Organization

### `wiki-content/` Structure

```
wiki-content/
├── Home.md                    # Main welcome page
├── First-Run-Guide.md        # 5-minute quick start
├── Installation.md           # Detailed installation
├── Provider-Basics.md         # Understanding providers
├── Provider-Setup-Guide.md    # All providers guide
├── Local-Providers.md         # Ollama, LM Studio
├── Cloud-Providers.md         # OpenAI, Anthropic, etc.
├── Advanced-Settings.md       # Configuration options
├── Settings-Best-Practices.md # Optimization tips
├── Review-Queue.md           # Understanding results
├── Troubleshooting.md         # Common issues
└── Observability-and-Metrics.md # Monitoring
```

### `docs/` Structure

```
docs/
├── README.md                 # Navigation hub (this file)
├── architecture/             # System design
├── development/              # Dev guidelines and processes
│   ├── WIKI-SYNC.md         # Wiki synchronization
│   └── ...
├── operations/              # Deployment and ops
├── reference/               # API and technical refs
└── archive/                 # Historical docs
```

## Synchronization Process

### Automated Sync

The GitHub Wiki is automatically synchronized from `wiki-content/`:

- **Triggers**: Release tags, content changes, manual dispatch
- **Process**: Copies files, converts names, updates versions
- **Location**: `.github/workflows/wiki-update.yml`

### Manual Sync

For content that needs manual updates:

1. **Update `wiki-content/`** for user changes
2. **Update `docs/`** for technical changes
3. **Test locally** before committing
4. **Verify sync** after changes

### Best Practices

#### Writing for Users

```markdown
# Good (wiki-content/)
## Install Brainarr
1. Download from GitHub
2. Extract to plugin folder
3. Restart Lidarr

# Bad (wiki-content/)
## Plugin Deployment
The plugin should be deployed to the appropriate directory structure
considering the operating system's file system conventions.
```

#### Writing for Developers

```markdown
# Good (docs/)
## Architecture Overview
Brainarr uses a provider pattern with IAIProvider interface
implementations for each AI service.

```csharp
public interface IAIProvider
{
    Task<bool> TestConnectionAsync();
    Task<List<ImportListItemInfo>> GetRecommendationsAsync();
}
```

# Bad (docs/)
## How It Works
The plugin gets recommendations from AI providers.
```

## Future Considerations

### Potential Improvements

1. **Automated documentation generation**
   - Extract from code comments
   - Generate API docs automatically
   - Update version references programmatically

2. **Better user experience**
   - Single documentation search
   - Context-aware navigation
   - Progressive disclosure of complexity

3. **Maintenance automation**
   - Automated link checking
   - Content validation
   - Version bump automation

### When to Change

Consider changing the documentation strategy when:

1. **User feedback** indicates confusion or duplication
2. **Development needs** outgrow current structure
3. **Team size** increases and coordination becomes difficult
4. **Documentation maintenance** becomes unsustainable

## Related Resources

- [WIKI-SYNC.md](development/WIKI-SYNC.md) - Wiki synchronization process
- [CONTRIBUTING.md](../../CONTRIBUTING.md) - Contribution guidelines
- [README.md](../../README.md) - Project overview
- [GitHub Wiki](https://github.com/RicherTunes/Brainarr/wiki) - Published user docs

## Questions & Feedback

If you have questions about the documentation structure:

1. Check the appropriate directory (`docs/` or `wiki-content/`)
2. Review the synchronization process
3. Consult with the development team
4. Open an issue if needed

---

**Note**: This documentation is part of the ongoing effort to maintain clear, organized documentation for Brainarr. The structure may evolve based on user needs and development requirements.
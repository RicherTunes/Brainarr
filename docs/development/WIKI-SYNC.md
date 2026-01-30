# Wiki Sync Process

This document explains the wiki synchronization process for Brainarr, including the automated workflow and manual procedures.

## Overview

Brainarr uses a two-part documentation system:
- **Primary source**: `docs/` and `wiki-content/` directories in the main repository
- **Published wiki**: GitHub Wiki (automatically synchronized)

The wiki sync automation keeps the GitHub Wiki updated with content changes from the repository.

## Canonical Ecosystem Docs

Some ecosystem-wide guidance lives in `lidarr.plugin.common` (shared across plugins). When referencing shared decisions (like streaming architecture), prefer the canonical docs in `lidarr.plugin.common/docs/` over duplicating them in individual plugin repos.

## Automated Wiki Sync

### Workflow Triggers

The wiki update workflow is triggered automatically when:

1. **Release tags are pushed** - Updates version references and documentation
2. **Wiki content files are modified** - Syncs changes to the wiki
3. **Manual workflow dispatch** - Force update all wiki pages

### Workflow Configuration

Located at `.github/workflows/wiki-update.yml`

#### Key Features

- **Automatic wiki creation** - Creates the wiki repository if it doesn't exist
- **Version reference updates** - Automatically updates version numbers on releases
- **File name conversion** - Converts hyphens to spaces for GitHub Wiki page names
- **Smart commit messages** - Contextual messages based on update type
- **No unnecessary commits** - Only commits when actual changes exist

#### Process Flow

1. **Check if wiki exists**
   - Tests connectivity to the wiki repository
   - Provides setup instructions if missing

2. **Determine update type**
   - Release: Version updates and downloads
   - Content: General documentation changes
   - Force: Manual complete update

3. **Clone and sync**
   - Clones wiki repository with authentication
   - Copies files from `wiki-content/`
   - Converts filenames (hyphens to spaces)
   - Updates version references for releases

4. **Commit and push**
   - Commits with contextual message
   - Pushes to wiki repository

### File Organization

#### Wiki Content Location

Source files are stored in `wiki-content/`:
```
wiki-content/
├── Home.md                    # Main wiki page
├── Installation.md            # User setup guide
├── First-Run-Guide.md        # Getting started tutorial
├── Provider-Setup-Guide.md   # Provider configuration
├── Advanced-Settings.md       # Configuration options
├── Cloud-Providers.md         # Cloud provider setup
├── Local-Providers.md        # Local provider setup
├── Provider-Basics.md        # Provider fundamentals
├── Settings-Best-Practices.md # Configuration tips
├── Review-Queue.md           # Understanding recommendations
├── Troubleshooting.md         # Common issues
└── Observability-and-Metrics.md # Monitoring guide
```

#### Page Conventions

- **File names**: Use hyphens (e.g., `First-Run-Guide.md`)
- **Wiki names**: Use spaces (e.g., "First Run Guide")
- **Structure**: Each page is self-contained with clear headings
- **Audience**: Focused on end users, not developers

## Manual Sync Procedures

### Setting Up the Wiki

If the wiki doesn't exist yet:

1. **Create the wiki repository**
   ```bash
   # Go to: https://github.com/RicherTunes/Brainarr/wiki
   # Click "Create the first page"
   ```

2. **Copy initial content**
   - Copy content from `wiki-content/Home.md`
   - Save as "Home"
   - Add other pages as needed

3. **Enable automation**
   - Future updates will be automatic
   - No manual intervention needed

### Manual Update Commands

#### Using the Workflow

1. **Go to GitHub Actions**
   - Navigate to `Actions` tab
   - Select "📚 Wiki Auto-Update"
   - Click "Run workflow"
   - Check "Force update all wiki pages"

2. **Using local script**
   ```bash
   # Run the auto-upload script
   ./scripts/auto-upload-wiki.sh
   ```

#### Manual Process

For direct wiki updates:

1. **Clone the wiki**
   ```bash
   git clone https://github.com/RicherTunes/Brainarr.wiki.git
   cd Brainarr.wiki
   ```

2. **Update content**
   - Copy files from `../wiki-content/`
   - Update version references if needed
   - Convert filenames (hyphens to spaces)

3. **Commit and push**
   ```bash
   git add .
   git commit -m "docs: Update wiki content"
   git push origin master
   ```

### Development Workflow

#### Before Contributing

1. **Check wiki content**
   - Review `wiki-content/` for existing documentation
   - Don't duplicate content that already exists

2. **Update appropriate files**
   - Add to `wiki-content/` for user-facing documentation
   - Add to `docs/` for technical documentation

#### After Changes

1. **Test locally**
   ```bash
   # Run linting
   markdownlint --config .markdownlint.yml wiki-content/**/*.md

   # Check links
   lychee --config .lychee.toml wiki-content/**/*.md
   ```

2. **Verify sync**
   - The workflow should trigger automatically
   - Check the Actions tab for status

## Content Guidelines

### Wiki Content

**Target audience**: End users, not developers
**Purpose**: How to use Brainarr, configure providers, and troubleshoot
**Tone**: Friendly, supportive, practical

**Content to include**:
- Installation instructions
- Setup guides for each provider
- Configuration options
- Troubleshooting steps
- Best practices

**Content to exclude**:
- Technical architecture details
- Development setup instructions
- API documentation
- Code examples

### Documentation Split

#### docs/ Directory (Technical Documentation)

- **Audience**: Developers and advanced users
- **Content**: Architecture, development, deployment, troubleshooting
- **Examples**: Code samples, API references, configuration examples
- **Link**: Included in the wiki for advanced users

#### wiki-content/ Directory (User Documentation)

- **Audience**: End users
- **Content**: Setup, configuration, basic troubleshooting
- **Examples**: Configuration UI screenshots, step-by-step guides
- **Link**: Primary documentation for most users

### Best Practices

#### Writing for Users

- **Start with the problem**: "Want to find new music?" not "Brainarr is an AI plugin"
- **Use clear headings**: "Install Brainarr" not "Installation Process"
- **Include screenshots**: Where helpful, show the UI
- **Provide step-by-step**: Numbered steps for complex processes
- **Explain why**: Briefly explain the purpose of each step

#### Maintenance

- **Keep content current**: Update when features change
- **Check links**: Test all links periodically
- **Update screenshots**: When UI changes, update screenshots
- **Gather user feedback**: Learn what users find confusing

### Troubleshooting Sync Issues

#### Common Issues

1. **Wiki not updating**
   - Check Actions tab for errors
   - Verify file permissions
   - Ensure GitHub token has write access

2. **Wrong content**
   - Check file paths in `wiki-content/`
   - Verify filename conversions
   - Review commit messages for clues

3. **Version references wrong**
   - Check for manual changes to wiki
   - Ensure workflow triggered on release
   - Verify version format in plugin.json

#### Debug Commands

```bash
# Check workflow status
gh run list --workflow wiki-update.yml

# View workflow logs
gh run view [run-id]

# Test local sync
./scripts/auto-upload-wiki.sh --dry-run

# Check wiki content
curl -s https://github.com/RicherTunes/Brainarr/wiki | grep -E "(Home|Installation)"
```

## Related Resources

- [GitHub Wiki documentation](https://docs.github.com/en/repositories/managing-your-repositorys-wiki-and-pages/about-wikis)
- [Markdown syntax guide](https://guides.github.com/features/mastering-markdown/)
- [Brainarr Documentation](../../README.md)
- [Development Guide](../DEVELOPMENT.md)

## Contributing

When contributing to wiki content:

1. Follow the content guidelines
2. Update both `wiki-content/` and relevant `docs/` files
3. Test all links and formatting
4. Include clear examples
5. Update screenshots when needed

See [CONTRIBUTING.md](../../CONTRIBUTING.md) for full contribution guidelines.

# Contributing to Brainarr

First off, thank you for considering contributing to Brainarr! It's people like you that make Brainarr such a great tool for the Lidarr community.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
- [Development Setup](#development-setup)
- [Code Style Guidelines](#code-style-guidelines)
- [Pull Request Guidelines](#pull-request-guidelines)
- [Testing Requirements](#testing-requirements)
- [Documentation Standards](#documentation-standards)
- [Reporting Security Issues](#reporting-security-issues)

## Code of Conduct

By participating in this project, you are expected to uphold our values of respect, inclusivity, and collaboration.

## How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check existing issues as you might find that you don't need to create one. When you are creating a bug report, please include as many details as possible:

- **Use a clear and descriptive title**
- **Describe the exact steps to reproduce the problem**
- **Provide specific examples**
- **Include your Lidarr version and Brainarr version**
- **Include relevant logs** (with sensitive information redacted)

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion, please include:

- **Use a clear and descriptive title**
- **Provide a detailed description of the proposed enhancement**
- **Explain why this enhancement would be useful**
- **List any alternative solutions you've considered**

### Adding New AI Providers

To add a new AI provider:

1. Create a new provider class in `Brainarr.Plugin/Services/Providers/`
2. Implement the `IAIProvider` interface
3. Register it in `ProviderRegistry.cs`
4. Add configuration fields and enum value to `BrainarrSettings.cs`
5. Add comprehensive tests in `Brainarr.Tests/Services/`
6. Update the provider documentation

Example provider template:
```csharp
public class YourProvider : IAIProvider
{
    public string ProviderName => "Your Provider";

    public async Task<bool> TestConnectionAsync()
    {
        // Test provider connectivity
    }

    public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
    {
        // Implementation
    }
}
```

Provider requirements:
- Must handle errors gracefully
- Should implement proper rate limiting
- Must validate API responses
- Should log meaningful debug information

### Pull Request Guidelines

#### Before Submitting

1. **Check existing issues** - Look for similar problems or enhancements
2. **Update documentation** - Include relevant examples and guides
3. **Write tests** - Ensure new code is covered by tests
4. **Run full test suite** - Verify all tests pass
5. **Check documentation consistency** - Run `pwsh ./scripts/sync-provider-matrix.ps1` and `bash ./scripts/check-docs-consistency.sh`
6. **Update README** - If needed, update version badges and links

#### Pull Request Template

When creating a PR, include:

- **Clear title** - Use imperative mood ("Add feature" not "Added feature")
- **Detailed description** - What changed and why
- **Breaking changes** - Note any API changes
- **Testing instructions** - How to test the changes
- **References** - Link to related issues and discussions

#### Pull Request Review

- **Be respectful** - Code reviews are collaborative
- **Be thorough** - Test the changes locally if possible
- **Be patient** - Maintainers are busy
- **Be constructive** - Suggest improvements, don't just criticize

## Documentation Contributions

We treat the code as the source of truth and keep documentation aligned via automated checks. Run the docs workflow before sending any PR, even if you only touched code.

### Required docs workflow
1. `pwsh ./scripts/sync-provider-matrix.ps1`
2. `pwsh ./scripts/check-docs-consistency.ps1` (or `bash ./scripts/check-docs-consistency.sh`)
3. `pre-commit run --all-files`

### Local Setup

- Install pre-commit and enable hooks:
  - macOS/Linux: `pip install pre-commit && pre-commit install`
  - Windows (PowerShell): `py -m pip install pre-commit; pre-commit install`
- Optional: install markdownlint-cli for the best local feedback:
  - `npm i -g markdownlint-cli`

### What runs locally (and in CI)

- Markdown lint: headings, lists, code fences (language required), spacing
- Docs consistency: README badge = plugin.json version, minimumVersion matches across docs, owner/name plugin paths only
- Link checker (CI): validates links in README/docs/wiki with a small safelist for provider APIs

Run checks manually:

```bash
# Lint markdown
markdownlint --config .markdownlint.yml README.md docs/**/*.md wiki-content/**/*.md

# Docs consistency checks
pwsh scripts/check-docs-consistency.ps1    # use bash version on POSIX if preferred

# Link check (CI runs this)
lychee --config .lychee.toml README.md docs/**/*.md wiki-content/**/*.md
```

### Style Guidelines (docs/wiki)

- Headings: plain text (no emoji or trailing bold markers)
- Navigation: use `Settings → Section → Subsection` arrows consistently
- Code fences: always specify a language
  - bash for shell (Linux/macOS), powershell for Windows, yaml for compose/config, json for payloads, console for sample output, text for directory trees
- Paths: owner/name layout everywhere
  - Linux: `/var/lib/lidarr/plugins/RicherTunes/Brainarr/`
  - Windows: `C:\ProgramData\Lidarr\plugins\RicherTunes\Brainarr`
  - Docker: `/config/plugins/RicherTunes/Brainarr`
- Compatibility: include "Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch" on entry pages

### Upgrading Code Fences

We've annotated fences across the repo. If you add new examples, pick the most specific language. To relabel unlabeled fences in bulk:

```powershell
pwsh -File scripts/add-codefence-langs.ps1 -Root .
```

## Development Setup

### Prerequisites

- .NET 6.0 SDK or later
- Visual Studio 2022 or VS Code with C# extension
- A Lidarr instance for testing (optional but recommended)
- At least one AI provider configured (Ollama recommended for local testing)

### Setting Up Your Development Environment

1. Extract or clone the repository:
   ```bash
   cd Brainarr
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. Run tests:
   ```bash
   dotnet test
   ```

4. For integration testing, configure your test providers in Lidarr settings

### Testing Your Changes

1. **Unit Tests**: Run `dotnet test` in the project root
2. **Integration Tests**: Requires active AI providers (local Ollama recommended)
3. **Manual Testing**: Build the plugin and install in a test Lidarr instance
4. **Provider Tests**: Test specific provider functionality with `dotnet test --filter ProviderTests`

### Code Style Guidelines

#### C# Code Style

- Use 4 spaces for indentation (no tabs)
- Follow C# naming conventions (PascalCase for public, camelCase for private)
- Keep methods small and focused (ideally under 20 lines)
- Write descriptive commit messages
- Add XML documentation comments for public APIs
- Use async/await for all I/O operations
- Prefer LINQ over traditional loops when appropriate

#### Code Formatting

```csharp
// Good example
public async Task<List<ImportListItemInfo>> GetRecommendationsAsync()
{
    if (_cache.TryGetValue(cacheKey, out var cached))
    {
        return cached;
    }

    var recommendations = await GenerateRecommendationsAsync();
    _cache.Set(cacheKey, recommendations);
    return recommendations;
}
```

#### Error Handling

- Use specific exception types instead of generic Exception
- Log errors with sufficient context
- Implement retry policies for transient failures
- Validate inputs before processing

## Testing Requirements

#### Test Categories

- **Unit Tests**: Test individual components in isolation
- **Integration Tests**: Test component interactions
- **Provider Tests**: Test specific AI provider functionality
- **Edge Cases**: Test error conditions and boundary scenarios

#### Test Guidelines

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task Provider_Should_HandleFailover_WhenPrimaryUnavailable()
{
    // Arrange: Mock primary provider failure
    // Act: Trigger failover scenario
    // Assert: Verify secondary provider usage
}
```

Test requirements:
- All new features must have tests
- Test coverage should be maintained above 80%
- Integration tests require active AI providers
- Edge cases should be thoroughly tested

#### Manual Testing Checklist

Before submitting a PR:

- [ ] Build the plugin successfully
- [ ] Install in test Lidarr instance
- [ ] Test with all configured AI providers
- [ ] Verify no regressions in existing functionality
- [ ] Check logs for unexpected errors
- [ ] Test on multiple platforms if possible

## Documentation Standards

#### Documentation Types

- **User Guides**: Simple, step-by-step instructions for end users
- **Technical Docs**: Architecture details and implementation notes
- **API Docs**: Class and method documentation
- **Examples**: Code samples and configuration examples

#### Writing Guidelines

- **Be clear** - Use simple language, avoid jargon when possible
- **Be concise** - Get to the point quickly
- **Be accurate** - Keep documentation synchronized with code
- **Be consistent** - Use consistent terminology and formatting

#### Markdown Formatting

```markdown
# Use clear headings
## Subsections as needed

- Use bullet points for lists
- Use code blocks for examples:
  ```csharp
  // Code examples with syntax highlighting
  ```

> Use blockquotes for important notes

| Tables | For | Structured | Data |
|--------|-----|-----------|------|
| Header | Row | Data      | Cell |
```

#### Documentation Updates

- Update README.md if needed
- Add XML comments to public methods
- Update provider guides for new providers
- Include examples in documentation
- Reference existing docs when possible

## Reporting Security Issues

Security vulnerabilities should be reported privately to the maintainers. Please do not report security issues in public issues or pull requests.

### Reporting Process

1. Email security@richertunes.com with "SECURITY VULNERABILITY" in the subject
2. Include detailed information about the vulnerability
3. Provide steps to reproduce the issue
4. Suggest a potential fix if possible

## Getting Help

If you need help:

1. Check existing issues and discussions
2. Read the documentation in `docs/`
3. Enable debug logging and gather information
4. Create a new issue with details (Lidarr version, plugin version, steps to reproduce)

## Related Projects

This project is part of the RicherTunes plugin ecosystem:

- **Tidalarr** - Tidal streaming integration for lossless audio downloads
- **Qobuzarr** - Qobuz streaming with ML-powered optimization
- **AppleMusicarr** - Apple Music library sync and metadata

**Shared foundation**: [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common)

## License

By contributing to Brainarr, you agree that your contributions will be licensed under the MIT License.

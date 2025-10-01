# Contributing to Brainarr

First off, thank you for considering contributing to Brainarr! It's people like you that make Brainarr such a great tool for the Lidarr community.

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

### Pull Requests

1. Fork the repo and create your branch from `main`
2. If you've added code that should be tested, add tests
3. Ensure the test suite passes
4. Make sure your code follows the existing code style
5. Issue that pull request!

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
- Compatibility: include “Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch” on entry pages

### Upgrading Code Fences

We’ve annotated fences across the repo. If you add new examples, pick the most specific language. To relabel unlabeled fences in bulk:

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

### Code Style

- Use 4 spaces for indentation (no tabs)
- Follow C# naming conventions
- Keep methods small and focused
- Write descriptive commit messages
- Add XML documentation comments for public APIs
- Use async/await for all I/O operations

### Commit Messages

- Use the present tense ("Add feature" not "Added feature")
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...")
- Limit the first line to 72 characters or less
- Reference issues and pull requests liberally after the first line

## Testing

- Write unit tests for all new functionality
- Ensure all tests pass before submitting PR
- Include integration tests for new providers
- Test with multiple Lidarr versions if possible

## Documentation

- Update README.md if needed
- Add XML comments to public methods
- Update provider guide for new providers
- Include examples in documentation

## Financial Contributions

We are not accepting financial contributions at this time. The best way to support the project is through code contributions, bug reports, and helping other users.

## Questions?

Feel free to open an issue with the "question" label if you need help!

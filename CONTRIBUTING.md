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
4. Add configuration fields to `BrainarrSettings.cs`
5. Add tests in `Brainarr.Tests/Services/Providers/`
6. Update the provider documentation

Example provider template:
```csharp
public class YourProvider : BaseAIProvider
{
    public override string ProviderName => "Your Provider";
    
    public override async Task<List<Recommendation>> GetRecommendationsAsync(
        LibraryProfile profile, 
        BrainarrSettings settings)
    {
        // Implementation
    }
}
```

### Pull Requests

1. Fork the repo and create your branch from `main`
2. If you've added code that should be tested, add tests
3. Ensure the test suite passes
4. Make sure your code follows the existing code style
5. Issue that pull request!

## Development Setup

### Prerequisites

- .NET 6.0 SDK or later
- Visual Studio 2022 or VS Code with C# extension
- A Lidarr instance for testing (optional but recommended)
- At least one AI provider configured (Ollama recommended for local testing)

### Setting Up Your Development Environment

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/Brainarr.git
   cd Brainarr
   ```

2. Copy the environment example:
   ```bash
   cp .env.example .env
   ```

3. Configure your `.env` file with your API keys and URLs

4. Build the project:
   ```bash
   dotnet build
   ```

5. Run tests:
   ```bash
   dotnet test
   ```

### Testing Your Changes

1. **Unit Tests**: Run `dotnet test` in the project root
2. **Integration Tests**: Configure test providers in `.env` and run `dotnet run --project IntegrationTest`
3. **Manual Testing**: Build the plugin and install in a test Lidarr instance

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
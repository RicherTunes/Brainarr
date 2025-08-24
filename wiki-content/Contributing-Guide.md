# Contributing Guide

Welcome to the Brainarr project! We appreciate your interest in contributing to AI-powered music discovery.

## Quick Start for Contributors

### 🚀 I want to contribute code
1. **Fork the repository** on GitHub
2. **Set up development environment** - [Development Setup](#development-setup)
3. **Find an issue** or create a feature proposal
4. **Create a branch** for your changes
5. **Submit a pull request** following our [PR guidelines](#pull-request-process)

### 🐛 I found a bug  
1. **Search existing issues** to avoid duplicates
2. **Create a detailed issue** with reproduction steps
3. **Include system info** (OS, Lidarr version, provider type)
4. **Attach relevant logs** (with API keys removed)

### 💡 I have a feature idea
1. **Check the roadmap** to see if it's already planned
2. **Create a feature request** with use case details
3. **Discuss with maintainers** before starting implementation
4. **Consider starting with a proof-of-concept**

### 📚 I want to improve documentation
1. **Identify documentation gaps** or errors
2. **Create an issue** or directly submit a PR
3. **Follow our [documentation standards](#documentation-standards)**
4. **Test examples** to ensure they work correctly

## Development Setup

### Prerequisites
- **.NET SDK 6.0+** - [Download from Microsoft](https://dotnet.microsoft.com/download)
- **Git** - For version control
- **IDE**: Visual Studio, VS Code, or JetBrains Rider
- **Lidarr Development Environment** (optional but recommended)

### Getting Started

#### 1. Fork and Clone
```bash
# Fork the repository on GitHub first
git clone https://github.com/YOUR-USERNAME/Brainarr.git
cd Brainarr

# Add upstream remote
git remote add upstream https://github.com/RicherTunes/Brainarr.git
```

#### 2. Set Up Development Dependencies
```bash
# Restore NuGet packages
dotnet restore

# Build the solution
dotnet build

# Run tests to verify setup
dotnet test
```

#### 3. Configure Development Environment

**Option A: Use Local Lidarr Installation**
```bash
# Set environment variable pointing to Lidarr installation
export LIDARR_PATH="/path/to/your/lidarr"
```

**Option B: Download Lidarr Assemblies**
```bash
# Run the setup script (creates ext/Lidarr/_output/net6.0/)
./scripts/setup-lidarr-assemblies.sh
```

**Option C: Use Docker Development Environment**
```bash
# Start development containers
docker-compose -f docker-compose.dev.yml up -d
```

### Development Workflow

#### Branch Naming Convention
```
feature/description       # New features
bugfix/description        # Bug fixes  
hotfix/critical-issue     # Critical production fixes
docs/improvement          # Documentation updates
refactor/component-name   # Code refactoring
test/test-description     # Test improvements
```

#### Example Development Flow
```bash
# Update your fork
git checkout main
git pull upstream main

# Create feature branch
git checkout -b feature/new-provider-support

# Make your changes
# ... code, test, commit ...

# Push to your fork
git push origin feature/new-provider-support

# Create pull request on GitHub
```

## Code Standards

### C# Coding Guidelines

#### 1. Follow Microsoft C# Guidelines
- Use PascalCase for public members
- Use camelCase for private fields (with underscore prefix)
- Use meaningful names that describe purpose
- Keep methods focused and small (< 50 lines preferred)

#### 2. Documentation Requirements
```csharp
/// <summary>
/// Gets music recommendations from the configured AI provider.
/// </summary>
/// <param name="libraryProfile">Analyzed library data for context</param>
/// <param name="cancellationToken">Cancellation token for async operations</param>
/// <returns>List of recommendation items formatted for Lidarr</returns>
/// <exception cref="ProviderException">Thrown when provider communication fails</exception>
public async Task<List<ImportListItemInfo>> GetRecommendationsAsync(
    LibraryProfile libraryProfile, 
    CancellationToken cancellationToken = default)
```

#### 3. Error Handling Patterns
```csharp
// Use specific exception types
public class ProviderConfigurationException : Exception
{
    public ProviderConfigurationException(string message, Exception innerException = null)
        : base(message, innerException) { }
}

// Always use structured logging
_logger.LogError(ex, "Failed to fetch recommendations from {Provider} after {AttemptCount} attempts", 
    provider.Name, attemptCount);

// Implement proper disposal
public class MyProvider : IAIProvider, IDisposable
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
            _rateLimiter?.Dispose();
        }
    }
}
```

#### 4. Async/Await Best Practices
```csharp
// Use ConfigureAwait(false) in library code
var result = await httpClient.GetAsync(url).ConfigureAwait(false);

// Handle cancellation properly
public async Task<string> ProcessAsync(CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    
    // Use cancellation token in async operations
    var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
    
    cancellationToken.ThrowIfCancellationRequested();
    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
}
```

### Testing Standards

#### 1. Test Organization
```csharp
[Trait("Category", "Unit")]
public class ProviderFactoryTests
{
    [Fact]
    public void CreateProvider_WithValidSettings_ReturnsCorrectProvider()
    {
        // Arrange
        var settings = new BrainarrSettings { Provider = AIProvider.Ollama };
        var factory = new ProviderFactory();
        
        // Act
        var provider = factory.CreateProvider(settings, _httpClient, _logger);
        
        // Assert
        Assert.IsType<OllamaProvider>(provider);
    }
    
    [Theory]
    [InlineData(AIProvider.OpenAI, typeof(OpenAIProvider))]
    [InlineData(AIProvider.Anthropic, typeof(AnthropicProvider))]
    public void CreateProvider_WithDifferentProviders_ReturnsCorrectType(
        AIProvider providerType, Type expectedType)
    {
        // Test implementation
    }
}
```

#### 2. Mock Usage Patterns
```csharp
public class AIServiceTests
{
    private readonly Mock<IAIProvider> _mockProvider;
    private readonly Mock<ILogger> _mockLogger;
    
    public AIServiceTests()
    {
        _mockProvider = new Mock<IAIProvider>();
        _mockLogger = new Mock<ILogger>();
    }
    
    [Fact]
    public async Task GetRecommendations_WhenProviderFails_ShouldRetryWithBackoff()
    {
        // Arrange
        _mockProvider.SetupSequence(p => p.GetRecommendationsAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Connection failed"))
            .ThrowsAsync(new HttpRequestException("Connection failed"))
            .ReturnsAsync(new List<Recommendation> { new() { Artist = "Test Artist" } });
        
        var service = new AIService(_mockProvider.Object, _mockLogger.Object);
        
        // Act
        var result = await service.GetRecommendationsAsync("test prompt");
        
        // Assert
        Assert.Single(result);
        _mockProvider.Verify(p => p.GetRecommendationsAsync(It.IsAny<string>()), Times.Exactly(3));
    }
}
```

#### 3. Integration Test Patterns
```csharp
[Trait("Category", "Integration")]
[Collection("RequiresLidarr")]
public class LibraryAnalyzerIntegrationTests
{
    [Fact]
    [SkipOnCI] // Skip on CI if requires specific environment
    public async Task AnalyzeLibrary_WithRealLibrary_GeneratesValidProfile()
    {
        // Requires real Lidarr instance with test data
        var analyzer = new LibraryAnalyzer(_artistService, _albumService, _logger);
        
        var profile = analyzer.AnalyzeLibrary();
        
        Assert.True(profile.TotalArtists > 0);
        Assert.NotEmpty(profile.GenreDistribution);
    }
}
```

## Contributing Types

### 1. Adding New AI Providers

#### Provider Implementation Checklist
- [ ] Implement `IAIProvider` interface
- [ ] Create provider-specific settings class
- [ ] Add provider to `AIProvider` enum
- [ ] Update `ProviderFactory` to create new provider
- [ ] Add configuration validation rules
- [ ] Create comprehensive unit tests
- [ ] Add integration tests (if possible)
- [ ] Update documentation

#### Example Provider Implementation
```csharp
public class NewProvider : IAIProvider
{
    private readonly BrainarrSettings _settings;
    private readonly IHttpClient _httpClient;
    private readonly Logger _logger;
    
    public string ProviderName => "NewProvider";
    
    public NewProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var request = new HttpRequest($"{_settings.BaseUrl}/api/health");
            request.Headers["Authorization"] = $"Bearer {_settings.ApiKey}";
            
            var response = await _httpClient.GetAsync(request);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Connection test failed for {Provider}", ProviderName);
            return false;
        }
    }
    
    public async Task<List<string>> GetAvailableModelsAsync()
    {
        // Implementation for model discovery
    }
    
    public async Task<List<ImportListItemInfo>> GetRecommendationsAsync(
        string prompt, 
        CancellationToken cancellationToken = default)
    {
        // Implementation for getting recommendations
    }
    
    public void UpdateModel(string model)
    {
        // Update model configuration
    }
    
    public void Dispose()
    {
        // Cleanup resources
    }
}
```

### 2. Core Feature Development

#### Adding New Configuration Options
1. **Update BrainarrSettings**:
   ```csharp
   [FieldDefinition(50, Label = "New Feature Enabled", Type = FieldType.Checkbox, HelpText = "Enable the new feature")]
   public bool NewFeatureEnabled { get; set; }
   ```

2. **Add Validation Rules**:
   ```csharp
   When(c => c.NewFeatureEnabled, () =>
   {
       RuleFor(c => c.NewFeatureParameter)
           .NotEmpty()
           .WithMessage("Parameter is required when new feature is enabled");
   });
   ```

3. **Update Constants**:
   ```csharp
   public static class BrainarrConstants
   {
       public const bool DefaultNewFeatureEnabled = false;
       public const string DefaultNewFeatureParameter = "default_value";
   }
   ```

#### Library Analysis Enhancements
```csharp
public class EnhancedLibraryAnalyzer : ILibraryAnalyzer
{
    public LibraryProfile AnalyzeLibrary(LibrarySamplingStrategy strategy = LibrarySamplingStrategy.Balanced)
    {
        var profile = base.AnalyzeLibrary(strategy);
        
        // Add new analysis dimensions
        profile.NewMetric = CalculateNewMetric(profile);
        profile.AdditionalInsights = GenerateInsights(profile);
        
        return profile;
    }
    
    private NewMetric CalculateNewMetric(LibraryProfile profile)
    {
        // New analysis logic
    }
}
```

### 3. Performance Improvements

#### Optimization Areas
1. **Caching Enhancements**
2. **Memory Usage Reduction**  
3. **Request Batching**
4. **Async Performance**
5. **Database Query Optimization**

#### Performance Testing
```csharp
[Fact]
public async Task ProcessLargeLibrary_ShouldCompleteWithinTimeLimit()
{
    // Arrange
    var largeLibrary = GenerateTestLibrary(artistCount: 1000);
    var analyzer = new LibraryAnalyzer();
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var profile = await analyzer.AnalyzeLibraryAsync(largeLibrary);
    stopwatch.Stop();
    
    // Assert
    Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Analysis took too long");
    Assert.NotNull(profile);
}
```

## Documentation Standards

### 1. Code Documentation
- **Public APIs**: Must have XML documentation
- **Complex algorithms**: Inline comments explaining logic
- **Configuration options**: Help text in field definitions
- **Error handling**: Document expected exceptions

### 2. Wiki Documentation
- **User-focused**: Write for end users, not developers
- **Step-by-step**: Provide clear, actionable instructions
- **Screenshots**: Include UI screenshots where helpful
- **Examples**: Provide concrete configuration examples
- **Troubleshooting**: Include common issues and solutions

### 3. README Updates
- **Keep current**: Update when adding features
- **Accurate counts**: Verify technical specifications
- **Working examples**: Test all code examples
- **Clear structure**: Maintain existing organization

## Pull Request Process

### 1. Before Submitting

#### Pre-submission Checklist
- [ ] **Code compiles** without warnings
- [ ] **All tests pass** locally
- [ ] **New features have tests** (unit + integration where possible)
- [ ] **Documentation updated** for user-facing changes
- [ ] **Breaking changes documented** in PR description
- [ ] **No sensitive data** (API keys, personal info) in commits

#### Code Quality Checks
```bash
# Run all tests
dotnet test

# Check code formatting
dotnet format --verify-no-changes

# Run static analysis (if configured)
dotnet build --verbosity normal
```

### 2. Pull Request Template

```markdown
## Description
Brief description of changes and motivation.

## Type of Change
- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to change)
- [ ] Documentation update
- [ ] Performance improvement
- [ ] Refactoring (no functional changes)

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated (if applicable)  
- [ ] Manual testing completed
- [ ] Performance testing completed (if relevant)

## Screenshots (if applicable)
Include screenshots for UI changes.

## Checklist
- [ ] My code follows the project's coding standards
- [ ] I have performed a self-review of my code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] I have made corresponding changes to the documentation
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] New and existing unit tests pass locally with my changes
```

### 3. Review Process

#### What Reviewers Look For
1. **Functionality**: Does it work as intended?
2. **Code Quality**: Readable, maintainable, follows standards
3. **Performance**: No significant performance regressions
4. **Security**: No security vulnerabilities introduced
5. **Testing**: Adequate test coverage
6. **Documentation**: User-facing changes documented

#### Responding to Feedback
- **Be responsive**: Address feedback promptly
- **Ask questions**: Clarify feedback if unclear
- **Make requested changes**: Update code based on feedback
- **Explain decisions**: Provide context for design choices
- **Be collaborative**: Work with reviewers to improve the code

## Issue Reporting Guidelines

### 1. Bug Reports

#### Bug Report Template
```markdown
**Bug Description**
Clear, concise description of the bug.

**Steps to Reproduce**
1. Go to '...'
2. Configure '...'
3. Click on '...'
4. See error

**Expected Behavior**
What you expected to happen.

**Actual Behavior**
What actually happened.

**Environment**
- OS: [e.g., Windows 10, Ubuntu 20.04]
- Lidarr Version: [e.g., 4.0.2.1183]
- Brainarr Version: [e.g., 1.0.0]
- Provider: [e.g., Ollama, OpenAI]
- Browser: [if UI issue]

**Configuration** (remove API keys)
```yaml
Provider: OpenAI
Model: gpt-4o
Discovery Mode: Adjacent
Max Recommendations: 10
```

**Logs**
```
Relevant log entries with timestamps
[Remove any API keys or sensitive information]
```

**Additional Context**
Any other context about the problem.
```

### 2. Feature Requests

#### Feature Request Template
```markdown
**Feature Description**
Clear description of the feature you'd like to see.

**Problem/Use Case**
What problem does this solve? What's your use case?

**Proposed Solution**
How you think this should work.

**Alternative Solutions**
Other ways you considered solving this.

**Additional Context**
Screenshots, mockups, examples from other applications, etc.
```

## Development Environment Tips

### 1. IDE Setup

#### Visual Studio Code Extensions
- **C# for Visual Studio Code** - Language support
- **NuGet Package Manager** - Package management
- **REST Client** - API testing
- **GitLens** - Git integration
- **TODO Highlight** - TODO comment highlighting

#### Visual Studio Extensions  
- **ReSharper** (paid) - Code analysis and refactoring
- **CodeMaid** - Code cleanup
- **Web Essentials** - Web development tools

### 2. Debugging Tips

#### Local Development Debugging
```csharp
// Add conditional compilation for debugging
#if DEBUG
_logger.Debug("Library analysis found {ArtistCount} artists", artists.Count);
foreach (var artist in artists.Take(5))
{
    _logger.Debug("Sample artist: {ArtistName} with {AlbumCount} albums", 
        artist.Name, artist.Albums.Count);
}
#endif
```

#### Provider Testing
```csharp
// Create test harness for provider development
public class ProviderTestHarness
{
    public static async Task TestProvider(IAIProvider provider)
    {
        Console.WriteLine($"Testing {provider.ProviderName}...");
        
        // Test connection
        var connectionOk = await provider.TestConnectionAsync();
        Console.WriteLine($"Connection: {(connectionOk ? "OK" : "FAILED")}");
        
        if (connectionOk)
        {
            // Test recommendations
            var recommendations = await provider.GetRecommendationsAsync("test prompt");
            Console.WriteLine($"Recommendations received: {recommendations?.Count ?? 0}");
        }
    }
}
```

### 3. Common Development Patterns

#### Provider Development Pattern
1. **Start with interface implementation** 
2. **Add basic connection testing**
3. **Implement model discovery** (if applicable)
4. **Add recommendation logic**
5. **Implement error handling**
6. **Add comprehensive tests**
7. **Update documentation**

#### Configuration Development Pattern
1. **Add setting property** to BrainarrSettings
2. **Add validation rules** in validator
3. **Update field definitions** for UI
4. **Add default constants**
5. **Update configuration examples**
6. **Test UI interaction**

## Community Guidelines

### 1. Code of Conduct
- **Be respectful** and inclusive
- **Help others** learn and contribute  
- **Focus on constructive feedback**
- **Assume positive intent**
- **Keep discussions technical** and on-topic

### 2. Communication Channels
- **GitHub Issues** - Bug reports, feature requests
- **Pull Requests** - Code review and discussion
- **Discussions** - General questions and ideas
- **Wiki** - Documentation collaboration

### 3. Recognition
- **Contributors list** - All contributors recognized in README
- **Changelog mentions** - Significant contributions noted in releases
- **Issue assignment** - Regular contributors can be assigned issues
- **Review privileges** - Trusted contributors invited to review PRs

## Getting Help

### 1. For Contributors
- **Development questions** - Create GitHub Discussion
- **Technical issues** - Search existing issues, create new issue
- **Process questions** - Ask in PR or issue comments
- **General guidance** - Check this guide, then ask

### 2. Useful Resources
- **[.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)** - Language and framework reference
- **[Lidarr API Docs](https://lidarr.audio/docs/api/)** - Integration reference  
- **[Architecture Overview](Architecture-Overview)** - System design details
- **[Testing Guide](Testing-Guide)** - Testing patterns and practices

## Next Steps

### New Contributors
1. **Read this entire guide**
2. **Set up development environment**
3. **Find a "good first issue" labeled issue**
4. **Join the community discussions**
5. **Make your first contribution!**

### Regular Contributors
1. **Consider taking on more complex issues**
2. **Help review other contributors' PRs**
3. **Mentor new contributors**  
4. **Propose new features and improvements**
5. **Help maintain documentation**

**Welcome to the Brainarr community! We're excited to have you contribute to the future of AI-powered music discovery.** 🎵🤖

---

*This guide is a living document. If you find areas that need improvement, please contribute updates!*
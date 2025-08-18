# Brainarr Testing Guide

## Overview

Brainarr includes a comprehensive test suite with over 30 test files covering unit tests, integration tests, and edge cases. This guide explains how to run tests, write new tests, and understand the testing architecture.

## Test Categories

### Unit Tests
Tests individual components in isolation with mocked dependencies.

**Location:** `Brainarr.Tests/Services/`, `Brainarr.Tests/Configuration/`

**Coverage:**
- Provider implementations
- Configuration validation
- Core services (AIService, LibraryAnalyzer, etc.)
- Support services (RateLimiter, Cache, RetryPolicy)

### Integration Tests
Tests interaction between multiple components with real dependencies.

**Location:** `Brainarr.Tests/Integration/`

**Coverage:**
- End-to-end provider testing
- Multi-provider failover scenarios
- Cache integration
- Health monitoring integration

### Edge Case Tests
Tests error handling and boundary conditions.

**Location:** `Brainarr.Tests/EdgeCases/`

**Coverage:**
- Network failures
- Invalid responses
- Rate limiting
- Timeout scenarios
- Malformed data handling

## Running Tests

### Prerequisites

```bash
# Install .NET SDK 6.0 or higher
dotnet --version

# Restore dependencies
dotnet restore
```

### Run All Tests

```bash
# Run all tests with detailed output
dotnet test

# Run with minimal output
dotnet test --verbosity quiet

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Run Specific Test Categories

```bash
# Run only unit tests
dotnet test --filter Category=Unit

# Run only integration tests (requires providers)
dotnet test --filter Category=Integration

# Run only edge case tests
dotnet test --filter Category=EdgeCase

# Run tests for specific provider
dotnet test --filter "FullyQualifiedName~OpenAIProvider"

# Run tests for specific component
dotnet test --filter "FullyQualifiedName~AIServiceTests"
```

### Run Tests by Namespace

```bash
# Test configuration components
dotnet test --filter "FullyQualifiedName~Configuration"

# Test core services
dotnet test --filter "FullyQualifiedName~Services.Core"

# Test providers
dotnet test --filter "FullyQualifiedName~Services.Providers"
```

### Continuous Testing

```bash
# Watch mode - reruns tests when files change
dotnet watch test

# Run specific tests in watch mode
dotnet watch test --filter Category=Unit
```

## Test Environment Setup

### Local Provider Testing

#### Ollama Setup
```bash
# Install Ollama
curl -fsSL https://ollama.ai/install.sh | sh

# Pull a model for testing
ollama pull llama3

# Verify Ollama is running
curl http://localhost:11434/api/tags
```

#### LM Studio Setup
1. Download from https://lmstudio.ai
2. Install and launch LM Studio
3. Download a model (e.g., Llama 3 7B)
4. Start the local server
5. Verify at http://localhost:1234/v1/models

### Cloud Provider Testing

Create a test configuration file:

**appsettings.test.json**
```json
{
  "TestSettings": {
    "Providers": {
      "OpenAI": {
        "ApiKey": "sk-test-...",
        "Model": "gpt-3.5-turbo",
        "Enabled": true
      },
      "Anthropic": {
        "ApiKey": "sk-ant-test-...",
        "Model": "claude-3-haiku-20240307",
        "Enabled": true
      },
      "Gemini": {
        "ApiKey": "AI...",
        "Model": "gemini-1.5-flash",
        "Enabled": true
      }
    },
    "TestMode": true,
    "UseMockResponses": false
  }
}
```

### Environment Variables

Set environment variables for sensitive data:

```bash
# Linux/Mac
export BRAINARR_TEST_OPENAI_KEY="sk-..."
export BRAINARR_TEST_ANTHROPIC_KEY="sk-ant-..."
export BRAINARR_TEST_MODE="true"

# Windows
set BRAINARR_TEST_OPENAI_KEY=sk-...
set BRAINARR_TEST_ANTHROPIC_KEY=sk-ant-...
set BRAINARR_TEST_MODE=true
```

## Writing Tests

### Test Structure

```csharp
using Xunit;
using FluentAssertions;
using Moq;

namespace Brainarr.Tests.Services
{
    [Category("Unit")]
    public class MyProviderTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Mock<Logger> _logger;
        private readonly MyProvider _sut; // System Under Test

        public MyProviderTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = new Mock<Logger>();
            _sut = new MyProvider(_httpClient.Object, _logger.Object, "test-key");
        }

        [Fact]
        public async Task GetRecommendations_ValidPrompt_ReturnsRecommendations()
        {
            // Arrange
            var prompt = "Test prompt";
            var expectedResponse = CreateMockResponse();
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                      .ReturnsAsync(expectedResponse);

            // Act
            var result = await _sut.GetRecommendationsAsync(prompt);

            // Assert
            result.Should().NotBeEmpty();
            result.First().Artist.Should().NotBeNullOrEmpty();
        }
    }
}
```

### Test Categories

Mark tests with appropriate categories:

```csharp
[Category("Unit")]          // Fast, isolated tests
[Category("Integration")]   // Tests with real dependencies
[Category("EdgeCase")]      // Error and boundary tests
[Category("Performance")]   // Performance benchmarks
[Category("LongRunning")]   // Tests that take > 5 seconds
```

### Mocking Best Practices

```csharp
// Mock HTTP responses
_httpClient.Setup(x => x.ExecuteAsync(It.Is<HttpRequest>(
    r => r.Url.Contains("/api/endpoint"))))
    .ReturnsAsync(new HttpResponse
    {
        StatusCode = HttpStatusCode.OK,
        Content = JsonConvert.SerializeObject(response)
    });

// Mock with delays for timeout testing
_httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
    .Returns(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        return new HttpResponse();
    });

// Verify calls were made
_httpClient.Verify(x => x.ExecuteAsync(
    It.Is<HttpRequest>(r => r.Method == HttpMethod.Post)), 
    Times.Once);
```

### Testing Async Code

```csharp
[Fact]
public async Task TestAsync_Method()
{
    // Always use async/await for async tests
    var result = await _sut.GetRecommendationsAsync("prompt");
    
    // Use FluentAssertions for better error messages
    result.Should().NotBeNull();
    
    // Test for exceptions
    await Assert.ThrowsAsync<ArgumentException>(
        async () => await _sut.GetRecommendationsAsync(null));
}
```

### Testing Error Scenarios

```csharp
[Theory]
[InlineData(HttpStatusCode.Unauthorized, typeof(UnauthorizedException))]
[InlineData(HttpStatusCode.TooManyRequests, typeof(RateLimitException))]
[InlineData(HttpStatusCode.InternalServerError, typeof(ProviderException))]
public async Task GetRecommendations_ErrorStatus_ThrowsAppropriateException(
    HttpStatusCode statusCode, Type exceptionType)
{
    // Arrange
    _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
              .ReturnsAsync(new HttpResponse { StatusCode = statusCode });

    // Act & Assert
    await Assert.ThrowsAsync(exceptionType, 
        async () => await _sut.GetRecommendationsAsync("prompt"));
}
```

## Test Data

### Test Fixtures

Create reusable test data:

```csharp
public static class TestData
{
    public static List<Artist> GetTestArtists()
    {
        return new List<Artist>
        {
            new Artist { Name = "Pink Floyd", Genre = "Progressive Rock" },
            new Artist { Name = "Led Zeppelin", Genre = "Hard Rock" },
            new Artist { Name = "The Beatles", Genre = "Rock" }
        };
    }

    public static string GetMockAIResponse()
    {
        return JsonConvert.SerializeObject(new[]
        {
            new { artist = "King Crimson", album = "In the Court", confidence = 0.9 },
            new { artist = "Yes", album = "Fragile", confidence = 0.85 }
        });
    }
}
```

### Builder Pattern for Complex Objects

```csharp
public class RecommendationBuilder
{
    private Recommendation _recommendation = new Recommendation();

    public RecommendationBuilder WithArtist(string artist)
    {
        _recommendation.Artist = artist;
        return this;
    }

    public RecommendationBuilder WithAlbum(string album)
    {
        _recommendation.Album = album;
        return this;
    }

    public RecommendationBuilder WithConfidence(double confidence)
    {
        _recommendation.Confidence = confidence;
        return this;
    }

    public Recommendation Build() => _recommendation;
}

// Usage
var recommendation = new RecommendationBuilder()
    .WithArtist("Pink Floyd")
    .WithAlbum("Dark Side of the Moon")
    .WithConfidence(0.95)
    .Build();
```

## Performance Testing

### Benchmark Tests

```csharp
[Category("Performance")]
public class PerformanceTests
{
    [Fact]
    public async Task Provider_ShouldRespondWithin5Seconds()
    {
        var stopwatch = Stopwatch.StartNew();
        
        var result = await _sut.GetRecommendationsAsync("prompt");
        
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    [Fact]
    public async Task Cache_ShouldImprovePerformance()
    {
        // First call - no cache
        var sw1 = Stopwatch.StartNew();
        await _service.GetRecommendationsAsync("prompt");
        sw1.Stop();
        var firstCallTime = sw1.ElapsedMilliseconds;

        // Second call - cached
        var sw2 = Stopwatch.StartNew();
        await _service.GetRecommendationsAsync("prompt");
        sw2.Stop();
        var secondCallTime = sw2.ElapsedMilliseconds;

        // Cache should be significantly faster
        secondCallTime.Should().BeLessThan(firstCallTime / 10);
    }
}
```

### Load Testing

```csharp
[Fact]
[Category("LongRunning")]
public async Task Provider_ShouldHandleConcurrentRequests()
{
    var tasks = Enumerable.Range(0, 10)
        .Select(i => _sut.GetRecommendationsAsync($"prompt {i}"));
    
    var results = await Task.WhenAll(tasks);
    
    results.Should().AllSatisfy(r => r.Should().NotBeEmpty());
}
```

## Test Coverage

### Generate Coverage Report

```bash
# Install coverage tools
dotnet tool install --global dotnet-reportgenerator-globaltool

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML report
reportgenerator \
  -reports:"**/coverage.cobertura.xml" \
  -targetdir:"coveragereport" \
  -reporttypes:Html

# Open report
open coveragereport/index.html  # Mac
xdg-open coveragereport/index.html  # Linux
start coveragereport/index.html  # Windows
```

### Coverage Goals

- **Overall:** > 80%
- **Core Services:** > 90%
- **Providers:** > 85%
- **Critical Paths:** 100%

## Debugging Tests

### Visual Studio / VS Code

1. Set breakpoints in test code
2. Right-click test → Debug Test
3. Step through code execution

### Command Line Debugging

```bash
# Run specific test with detailed logging
dotnet test --logger:"console;verbosity=detailed" \
  --filter "FullyQualifiedName~SpecificTestName"

# Enable diagnostic logging
export BRAINARR_LOG_LEVEL=Debug
dotnet test
```

### Test Output

```csharp
public class TestWithOutput
{
    private readonly ITestOutputHelper _output;

    public TestWithOutput(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestWithLogging()
    {
        _output.WriteLine("Starting test...");
        
        var result = await _sut.GetRecommendationsAsync("prompt");
        
        _output.WriteLine($"Got {result.Count} recommendations");
        foreach (var rec in result)
        {
            _output.WriteLine($"- {rec.Artist}: {rec.Album}");
        }
    }
}
```

## CI/CD Integration

### GitHub Actions

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v2
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: '6.0.x'
    
    - name: Restore
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore
    
    - name: Test
      run: dotnet test --no-build --verbosity normal \
        --collect:"XPlat Code Coverage" \
        --filter "Category!=LongRunning"
    
    - name: Upload coverage
      uses: codecov/codecov-action@v2
      with:
        files: '**/coverage.cobertura.xml'
```

## Troubleshooting

### Common Issues

#### Tests Fail Locally but Pass in CI
- Check environment variables
- Verify local provider availability
- Check for hardcoded paths
- Ensure consistent culture settings

#### Flaky Tests
- Add retries for network tests
- Increase timeouts for slow operations
- Use deterministic test data
- Avoid time-dependent assertions

#### Provider Tests Failing
```csharp
// Skip tests when provider unavailable
[SkippableFact]
public async Task RequiresOllama()
{
    Skip.IfNot(await IsOllamaAvailable(), "Ollama not available");
    // Test code
}
```

### Test Isolation

Ensure tests don't affect each other:

```csharp
public class IsolatedTests : IDisposable
{
    private readonly string _tempPath;

    public IsolatedTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
    }

    public void Dispose()
    {
        Directory.Delete(_tempPath, true);
    }
}
```

## Best Practices

1. **Fast Tests First**: Run unit tests before integration tests
2. **Descriptive Names**: Use clear, descriptive test names
3. **Single Assertion**: One logical assertion per test
4. **Arrange-Act-Assert**: Follow AAA pattern consistently
5. **Mock External Dependencies**: Don't make real API calls in unit tests
6. **Test Data Builders**: Use builders for complex test objects
7. **Parallel Execution**: Ensure tests can run in parallel
8. **Clean Up**: Always clean up resources in Dispose()
9. **Deterministic**: Tests should produce same results every run
10. **Documentation**: Document complex test scenarios

## Additional Resources

- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Guide](https://fluentassertions.com/)
- [Moq Quick Start](https://github.com/moq/moq4/wiki/Quickstart)
- [.NET Testing Best Practices](https://docs.microsoft.com/en-us/dotnet/core/testing/)
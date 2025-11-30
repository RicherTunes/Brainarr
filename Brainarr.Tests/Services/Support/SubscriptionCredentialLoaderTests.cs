using System;
using System.IO;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    public class SubscriptionCredentialLoaderTests : IDisposable
    {
        private readonly string _tempDir;

        public SubscriptionCredentialLoaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        #region ExpandPath Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void ExpandPath_WithNull_ReturnsNull()
        {
            var result = SubscriptionCredentialLoader.ExpandPath(null!);
            result.Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExpandPath_WithEmpty_ReturnsEmpty()
        {
            var result = SubscriptionCredentialLoader.ExpandPath(string.Empty);
            result.Should().BeEmpty();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExpandPath_WithTilde_ExpandsToUserProfile()
        {
            var result = SubscriptionCredentialLoader.ExpandPath("~/.claude/.credentials.json");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            result.Should().StartWith(home);
            result.Should().EndWith(".credentials.json");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ExpandPath_WithEnvironmentVariable_Expands()
        {
            // This test uses Windows-style environment variables
            // On non-Windows, %VAR% syntax is not expanded, so we check platform-specific behavior
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var result = SubscriptionCredentialLoader.ExpandPath("%TEMP%");
                result.Should().NotContain("%TEMP%");
            }
            else
            {
                // On Linux/macOS, %TEMP% is not expanded (it's a Windows syntax)
                // The ExpandEnvironmentVariables doesn't change it on non-Windows
                var result = SubscriptionCredentialLoader.ExpandPath("%TEMP%");
                // Just verify the method doesn't throw on non-Windows
                result.Should().NotBeNull();
            }
        }

        #endregion

        #region GetDefaultPath Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void GetDefaultClaudeCodePath_ReturnsValidPath()
        {
            var result = SubscriptionCredentialLoader.GetDefaultClaudeCodePath();
            result.Should().Contain(".claude");
            result.Should().EndWith(".credentials.json");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetDefaultCodexPath_ReturnsValidPath()
        {
            var result = SubscriptionCredentialLoader.GetDefaultCodexPath();
            result.Should().Contain(".codex");
            result.Should().EndWith("auth.json");
        }

        #endregion

        #region LoadClaudeCodeCredentials Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadClaudeCodeCredentials_FileNotFound_ReturnsFailure()
        {
            var nonExistentPath = Path.Combine(_tempDir, "nonexistent.json");
            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(nonExistentPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("not found");
            result.Token.Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadClaudeCodeCredentials_ValidCredentials_ReturnsSuccess()
        {
            var credentialsPath = Path.Combine(_tempDir, ".credentials.json");
            var futureExpiry = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
            var json = $@"{{
                ""claudeAiOauth"": {{
                    ""accessToken"": ""test-token-12345"",
                    ""expiresAt"": {futureExpiry},
                    ""refreshToken"": ""refresh-token-abc"",
                    ""subscriptionType"": ""max""
                }}
            }}";
            File.WriteAllText(credentialsPath, json);

            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(credentialsPath);

            result.IsSuccess.Should().BeTrue();
            result.Token.Should().Be("test-token-12345");
            result.RefreshToken.Should().Be("refresh-token-abc");
            result.ExpiresAt.Should().NotBeNull();
            result.ErrorMessage.Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadClaudeCodeCredentials_ExpiredToken_ReturnsFailure()
        {
            var credentialsPath = Path.Combine(_tempDir, ".credentials.json");
            var pastExpiry = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
            var json = $@"{{
                ""claudeAiOauth"": {{
                    ""accessToken"": ""expired-token"",
                    ""expiresAt"": {pastExpiry}
                }}
            }}";
            File.WriteAllText(credentialsPath, json);

            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(credentialsPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("expired");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadClaudeCodeCredentials_EmptyAccessToken_ReturnsFailure()
        {
            var credentialsPath = Path.Combine(_tempDir, ".credentials.json");
            var json = @"{
                ""claudeAiOauth"": {
                    ""accessToken"": """",
                    ""expiresAt"": 9999999999999
                }
            }";
            File.WriteAllText(credentialsPath, json);

            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(credentialsPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("empty");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadClaudeCodeCredentials_MissingOAuthSection_ReturnsFailure()
        {
            var credentialsPath = Path.Combine(_tempDir, ".credentials.json");
            var json = @"{ ""someOtherKey"": ""value"" }";
            File.WriteAllText(credentialsPath, json);

            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(credentialsPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("claudeAiOauth.accessToken");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadClaudeCodeCredentials_InvalidJson_ReturnsFailure()
        {
            var credentialsPath = Path.Combine(_tempDir, ".credentials.json");
            File.WriteAllText(credentialsPath, "not valid json {{{");

            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(credentialsPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Invalid JSON");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadClaudeCodeCredentials_NoExpiresAt_StillSucceeds()
        {
            var credentialsPath = Path.Combine(_tempDir, ".credentials.json");
            var json = @"{
                ""claudeAiOauth"": {
                    ""accessToken"": ""token-no-expiry""
                }
            }";
            File.WriteAllText(credentialsPath, json);

            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(credentialsPath);

            result.IsSuccess.Should().BeTrue();
            result.Token.Should().Be("token-no-expiry");
            result.ExpiresAt.Should().BeNull();
        }

        #endregion

        #region LoadCodexCredentials Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadCodexCredentials_FileNotFound_ReturnsFailure()
        {
            var nonExistentPath = Path.Combine(_tempDir, "nonexistent.json");
            var result = SubscriptionCredentialLoader.LoadCodexCredentials(nonExistentPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("not found");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadCodexCredentials_ValidTokensFormat_ReturnsSuccess()
        {
            var authPath = Path.Combine(_tempDir, "auth.json");
            var futureExpiry = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
            var json = $@"{{
                ""tokens"": {{
                    ""access_token"": ""openai-token-xyz"",
                    ""expires_at"": {futureExpiry},
                    ""refresh_token"": ""openai-refresh-token""
                }}
            }}";
            File.WriteAllText(authPath, json);

            var result = SubscriptionCredentialLoader.LoadCodexCredentials(authPath);

            result.IsSuccess.Should().BeTrue();
            result.Token.Should().Be("openai-token-xyz");
            result.RefreshToken.Should().Be("openai-refresh-token");
            result.ExpiresAt.Should().NotBeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadCodexCredentials_DirectApiKey_ReturnsSuccess()
        {
            var authPath = Path.Combine(_tempDir, "auth.json");
            var json = @"{
                ""OPENAI_API_KEY"": ""sk-direct-api-key""
            }";
            File.WriteAllText(authPath, json);

            var result = SubscriptionCredentialLoader.LoadCodexCredentials(authPath);

            result.IsSuccess.Should().BeTrue();
            result.Token.Should().Be("sk-direct-api-key");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadCodexCredentials_EmptyAccessToken_ReturnsFailure()
        {
            var authPath = Path.Combine(_tempDir, "auth.json");
            var json = @"{
                ""tokens"": {
                    ""access_token"": """"
                }
            }";
            File.WriteAllText(authPath, json);

            var result = SubscriptionCredentialLoader.LoadCodexCredentials(authPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("empty");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadCodexCredentials_MissingTokensSection_ReturnsFailure()
        {
            var authPath = Path.Combine(_tempDir, "auth.json");
            var json = @"{ ""someOtherKey"": ""value"" }";
            File.WriteAllText(authPath, json);

            var result = SubscriptionCredentialLoader.LoadCodexCredentials(authPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("tokens.access_token");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LoadCodexCredentials_InvalidJson_ReturnsFailure()
        {
            var authPath = Path.Combine(_tempDir, "auth.json");
            File.WriteAllText(authPath, "invalid json");

            var result = SubscriptionCredentialLoader.LoadCodexCredentials(authPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Invalid JSON");
        }

        #endregion

        #region CredentialResult Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void CredentialResult_TimeUntilExpiry_CalculatesCorrectly()
        {
            var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
            var result = CredentialResult.Success("token", expiresAt);

            result.TimeUntilExpiry.Should().NotBeNull();
            result.TimeUntilExpiry!.Value.TotalHours.Should().BeApproximately(2, 0.1);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CredentialResult_TimeUntilExpiry_WithNoExpiry_ReturnsNull()
        {
            var result = CredentialResult.Success("token");

            result.TimeUntilExpiry.Should().BeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CredentialResult_IsExpiringSoon_WithinThreshold_ReturnsTrue()
        {
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
            var result = CredentialResult.Success("token", expiresAt);

            result.IsExpiringSoon(TimeSpan.FromHours(1)).Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CredentialResult_IsExpiringSoon_OutsideThreshold_ReturnsFalse()
        {
            var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
            var result = CredentialResult.Success("token", expiresAt);

            result.IsExpiringSoon(TimeSpan.FromHours(1)).Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CredentialResult_CanAutoRefresh_WithRefreshToken_ReturnsTrue()
        {
            var result = CredentialResult.Success("token", DateTimeOffset.UtcNow.AddDays(1), "refresh-token");

            result.CanAutoRefresh.Should().BeTrue();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CredentialResult_CanAutoRefresh_WithoutRefreshToken_ReturnsFalse()
        {
            var result = CredentialResult.Success("token");

            result.CanAutoRefresh.Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void CredentialResult_Failure_HasErrorMessage()
        {
            var result = CredentialResult.Failure("Something went wrong");

            result.IsSuccess.Should().BeFalse();
            result.Token.Should().BeNull();
            result.ErrorMessage.Should().Be("Something went wrong");
        }

        #endregion
    }
}

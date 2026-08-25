using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    public class CodexTokenRefresherTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _authPath;

        public CodexTokenRefresherTests()
        {
            // IsPathSafe requires the file under the user home dir.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _tempDir = Path.Combine(home, $".brainarr-refresh-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _authPath = Path.Combine(_tempDir, "auth.json");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { /* best-effort */ }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            public string? RequestBody { get; private set; }
            public Uri? RequestUri { get; private set; }

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri;
                if (request.Content != null)
                {
                    RequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
            }
        }

        private void WriteAuth(string refreshToken = "old-refresh")
        {
            var json = JsonSerializer.Serialize(new
            {
                auth_mode = "chatgpt",
                OPENAI_API_KEY = (string?)null,
                tokens = new
                {
                    id_token = "old-id",
                    access_token = "old-access",
                    refresh_token = refreshToken,
                    account_id = "acct-keep",
                },
                last_refresh = "2026-01-01T00:00:00Z",
            });
            File.WriteAllText(_authPath, json);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task RefreshAsync_Success_RotatesTokensAndPreservesOtherFields()
        {
            WriteAuth();
            var handler = new StubHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                access_token = "new-access",
                id_token = "new-id",
                refresh_token = "new-refresh",
                expires_in = 3600,
            }));

            var result = await CodexTokenRefresher.RefreshAsync(_authPath, handler);

            result.IsSuccess.Should().BeTrue();
            result.AccessToken.Should().Be("new-access");

            // Correct OAuth2 grant sent.
            handler.RequestUri!.ToString().Should().Be(NzbDrone.Core.ImportLists.Brainarr.Configuration.BrainarrConstants.OpenAIOAuthTokenUrl);
            handler.RequestBody.Should().Contain("refresh_token");
            handler.RequestBody.Should().Contain("old-refresh");

            // File rewritten with rotated tokens; unrelated fields preserved.
            using var doc = JsonDocument.Parse(File.ReadAllText(_authPath));
            var root = doc.RootElement;
            var tokens = root.GetProperty("tokens");
            tokens.GetProperty("access_token").GetString().Should().Be("new-access");
            tokens.GetProperty("id_token").GetString().Should().Be("new-id");
            tokens.GetProperty("refresh_token").GetString().Should().Be("new-refresh");
            tokens.GetProperty("account_id").GetString().Should().Be("acct-keep");
            root.GetProperty("auth_mode").GetString().Should().Be("chatgpt");
            root.GetProperty("last_refresh").GetString().Should().NotBe("2026-01-01T00:00:00Z");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task RefreshAsync_NoRefreshToken_ReturnsFailure()
        {
            var json = JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens = new { access_token = "a" } });
            File.WriteAllText(_authPath, json);

            var result = await CodexTokenRefresher.RefreshAsync(_authPath);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("refresh_token");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task RefreshAsync_HttpError_ReturnsFailure_AndLeavesFileUntouched()
        {
            WriteAuth("keep-me");
            var handler = new StubHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");

            var result = await CodexTokenRefresher.RefreshAsync(_authPath, handler);

            result.IsSuccess.Should().BeFalse();
            // Original file must be intact so the CLI still works.
            using var doc = JsonDocument.Parse(File.ReadAllText(_authPath));
            doc.RootElement.GetProperty("tokens").GetProperty("refresh_token").GetString().Should().Be("keep-me");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task RefreshAsync_MissingFile_ReturnsFailure()
        {
            var result = await CodexTokenRefresher.RefreshAsync(_authPath);
            result.IsSuccess.Should().BeFalse();
        }
    }
}

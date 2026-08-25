using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Observability;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Rotates the OpenAI Codex ChatGPT-subscription access token using the OAuth2 refresh-token
    /// grant, then writes the fresh tokens back into <c>~/.codex/auth.json</c> in place — the same
    /// file the Codex CLI reads, so a single source of truth is preserved.
    ///
    /// <para>
    /// The refresh hits <see cref="BrainarrConstants.OpenAIOAuthTokenUrl"/> with the Codex CLI's
    /// public client id (<see cref="BrainarrConstants.OpenAICodexOAuthClientId"/>). The response's
    /// rotated <c>refresh_token</c>/<c>access_token</c>/<c>id_token</c> are merged into the existing
    /// JSON with a mutable DOM so every other field (<c>auth_mode</c>, <c>OPENAI_API_KEY</c>,
    /// <c>tokens.account_id</c>) survives untouched, and the file is replaced atomically (temp +
    /// File.Move) so a crash mid-write can never leave the CLI with a truncated auth file.
    /// </para>
    ///
    /// <para>
    /// A raw <see cref="HttpClient"/> is used (not Lidarr's IHttpClient) so the request isn't
    /// subject to the host dispatcher's User-Agent restriction, mirroring the Codex provider.
    /// Refreshes for a given path are serialized so two concurrent syncs can't both rotate the
    /// (single-use) refresh token and race each other into an invalid state.
    /// </para>
    /// </summary>
    public static class CodexTokenRefresher
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly Lazy<HttpClient> SharedClient = new(static () =>
            new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            })
            {
                Timeout = TimeSpan.FromSeconds(30),
            });

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new();

        /// <summary>
        /// Attempts to refresh the Codex access token stored at <paramref name="authFilePath"/>.
        /// On success the file is rewritten with the rotated tokens and the new access token is
        /// returned. Never throws for expected failures (missing file, no refresh token, HTTP
        /// error) — those come back as <see cref="CodexRefreshResult.Failure"/>.
        /// </summary>
        public static async Task<CodexRefreshResult> RefreshAsync(
            string? authFilePath = null,
            HttpMessageHandler? testHandler = null,
            CancellationToken cancellationToken = default)
        {
            var path = SubscriptionCredentialLoader.ExpandPath(
                authFilePath ?? SubscriptionCredentialLoader.GetDefaultCodexPath());

            if (!SubscriptionCredentialLoader.IsPathSafe(path))
            {
                return CodexRefreshResult.Failure("Codex credentials path is not allowed (UNC, traversal, or outside home directory).");
            }
            if (!File.Exists(path))
            {
                return CodexRefreshResult.Failure($"Codex auth file not found at {LogRedactor.Redact(path)}.");
            }

            var gate = PathLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RefreshLockedAsync(path, testHandler, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task<CodexRefreshResult> RefreshLockedAsync(
            string path, HttpMessageHandler? testHandler, CancellationToken cancellationToken)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                return CodexRefreshResult.Failure($"Could not read Codex auth file: {ex.Message}");
            }

            if (root is not JsonObject rootObj || rootObj["tokens"] is not JsonObject tokensObj)
            {
                return CodexRefreshResult.Failure("Codex auth file has no 'tokens' object to refresh.");
            }

            var refreshToken = tokensObj["refresh_token"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return CodexRefreshResult.Failure("Codex auth file has no refresh_token. Run 'codex auth login' to re-authenticate.");
            }

            OAuthTokenResponse tokenResponse;
            try
            {
                tokenResponse = await RequestNewTokensAsync(refreshToken!, testHandler, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Codex token refresh request failed: {ex.Message}");
                return CodexRefreshResult.Failure($"Token refresh request failed: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                return CodexRefreshResult.Failure("Token endpoint returned no access_token.");
            }

            // Merge rotated tokens back in, preserving every other field.
            tokensObj["access_token"] = tokenResponse.AccessToken;
            if (!string.IsNullOrWhiteSpace(tokenResponse.IdToken))
            {
                tokensObj["id_token"] = tokenResponse.IdToken;
            }
            // The refresh token is single-use and rotates; persist the new one so the next
            // refresh works. If the server omitted it, keep the existing value.
            if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            {
                tokensObj["refresh_token"] = tokenResponse.RefreshToken;
            }
            rootObj["last_refresh"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

            try
            {
                WriteAtomic(path, rootObj);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to persist refreshed Codex tokens to {LogRedactor.Redact(path)}");
                return CodexRefreshResult.Failure($"Refreshed token could not be written to disk: {ex.Message}");
            }

            var expiresAt = CodexJwt.GetExpiry(tokenResponse.AccessToken)
                            ?? (tokenResponse.ExpiresInSeconds > 0
                                ? DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresInSeconds)
                                : (DateTimeOffset?)null);

            Logger.Info("OpenAI Codex token refreshed and persisted.");
            return CodexRefreshResult.Success(tokenResponse.AccessToken!, expiresAt);
        }

        private static async Task<OAuthTokenResponse> RequestNewTokensAsync(
            string refreshToken, HttpMessageHandler? testHandler, CancellationToken cancellationToken)
        {
            var body = new
            {
                client_id = BrainarrConstants.OpenAICodexOAuthClientId,
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                scope = "openid profile email",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, BrainarrConstants.OpenAIOAuthTokenUrl);
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Content = content;

            var client = testHandler != null
                ? new HttpClient(testHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) }
                : SharedClient.Value;

            using var response = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            var payload = response.Content != null
                ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : string.Empty;

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode} from token endpoint.");
            }

            using var doc = JsonDocument.Parse(payload);
            var el = doc.RootElement;
            return new OAuthTokenResponse
            {
                AccessToken = el.TryGetProperty("access_token", out var a) ? a.GetString() : null,
                IdToken = el.TryGetProperty("id_token", out var i) ? i.GetString() : null,
                RefreshToken = el.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
                ExpiresInSeconds = el.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 0,
            };
        }

        private static void WriteAtomic(string path, JsonObject root)
        {
            var dir = Path.GetDirectoryName(path) ?? ".";
            var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.brainarr-{Guid.NewGuid():N}.tmp");
            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Match the CLI's private-file permissions before the file becomes visible under the
            // real name, so the rotated auth.json isn't briefly world-readable on Linux/Synology.
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                catch (Exception ex) { Logger.Debug(ex, "Could not set unix file mode on temp auth file"); }
            }

            File.Move(tmp, path, overwrite: true);
        }

        private sealed class OAuthTokenResponse
        {
            public string? AccessToken { get; init; }
            public string? IdToken { get; init; }
            public string? RefreshToken { get; init; }
            public int ExpiresInSeconds { get; init; }
        }
    }

    /// <summary>Outcome of a <see cref="CodexTokenRefresher.RefreshAsync"/> call.</summary>
    public sealed class CodexRefreshResult
    {
        public bool IsSuccess { get; }
        public string? AccessToken { get; }
        public DateTimeOffset? ExpiresAt { get; }
        public string? ErrorMessage { get; }

        private CodexRefreshResult(bool ok, string? token, DateTimeOffset? expiresAt, string? error)
        {
            IsSuccess = ok;
            AccessToken = token;
            ExpiresAt = expiresAt;
            ErrorMessage = error;
        }

        public static CodexRefreshResult Success(string token, DateTimeOffset? expiresAt)
            => new(true, token, expiresAt, null);
        public static CodexRefreshResult Failure(string error) => new(false, null, null, error);
    }
}

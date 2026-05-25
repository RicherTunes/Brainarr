using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Services.Bridge;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Per-(provider, API-key) authentication failure circuit breaker.
    ///
    /// <para>
    /// Thin facade over Common's <see cref="AuthFailureGate"/> +
    /// <see cref="SlidingWindowAuthFailureHandler"/>. Tracks consecutive 401-class
    /// failures per <c>(providerId, hashed-key)</c> tuple. Once N consecutive auth
    /// failures occur within a sliding window, the circuit opens for
    /// <see cref="OpenDuration"/> (default 30 min). After that period a single
    /// probe is allowed through; success closes the circuit, failure re-opens.
    /// </para>
    ///
    /// <para>
    /// API keys are never stored in plaintext. The dict key is
    /// <c>{providerId}::{SHA256(apiKey)[0..16]}</c> (base-64 first 16 chars).
    /// </para>
    ///
    /// <para>
    /// Wave-22 convergence: prior brainarr-internal phase/state implementation
    /// (Closed/Open/HalfOpen with custom timers) replaced by Common's
    /// <see cref="AuthFailureGate"/> + <see cref="SlidingWindowAuthFailureHandler"/>
    /// (Common v1.16.0+). Same documented behavior, shared ecosystem code — closes
    /// the ecosystem-parity divergence row recorded in
    /// <c>Lidarr.Plugin.Common/docs/ECOSYSTEM_PARITY_MATRIX.md</c>. The public API
    /// surface (<see cref="IsOpen"/>, <see cref="RecordAuthFailure"/>,
    /// <see cref="RecordSuccess"/>, the test-only <see cref="MakeKey"/>) is
    /// unchanged so call sites + tests don't need to migrate.
    /// </para>
    /// </summary>
    public sealed class LlmAuthCircuit
    {
        // ── Thresholds ──────────────────────────────────────────────────────────
        // N consecutive 401s required to open the circuit.
        // 3 chosen because providers can hiccup with a single 401 during token-
        // refresh races (Anthropic OAuth rotation, OpenAI key propagation delays).
        // Requiring 3 consecutive within the window avoids false positives while
        // still catching a genuinely dead key quickly.
        private const int DefaultConsecutiveFailureThreshold = 3;

        // Sliding window in which consecutive failures are tracked. Failures older
        // than this are forgotten — a stale run of 401s shouldn't count against a
        // newly rotated key.
        private static readonly TimeSpan DefaultFailureWindow = TimeSpan.FromMinutes(5);

        // Duration the circuit stays Open before the next probe is allowed. Maps
        // to Common's AuthFailureGate.probeInterval — the gate grants one probe
        // slot per probeInterval while latched, so a 30-min openDuration produces
        // the brainarr "open for 30 min, then HalfOpen one probe" semantics.
        private static readonly TimeSpan DefaultOpenDuration = TimeSpan.FromMinutes(30);

        // ── State ────────────────────────────────────────────────────────────────
        // Per-key gate map. Each entry has its own SlidingWindowAuthFailureHandler
        // + AuthFailureGate so state for (provider-A, key-X) is isolated from
        // (provider-B, key-Y).
        private readonly ConcurrentDictionary<string, GateEntry> _gates = new(StringComparer.Ordinal);
        private readonly Logger _logger;
        private readonly int _failureThreshold;
        private readonly TimeSpan _failureWindow;
        private readonly TimeSpan _openDuration;
        private readonly TimeProvider _time;

        // ── CTORs ────────────────────────────────────────────────────────────────
        public LlmAuthCircuit(Logger? logger = null)
            : this(logger, DefaultConsecutiveFailureThreshold, DefaultFailureWindow, DefaultOpenDuration, TimeProvider.System)
        { }

        /// <summary>Test-only constructor that accepts a fake <see cref="TimeProvider"/>.</summary>
        internal LlmAuthCircuit(
            Logger? logger,
            int failureThreshold,
            TimeSpan failureWindow,
            TimeSpan openDuration,
            TimeProvider time)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _failureThreshold = failureThreshold;
            _failureWindow = failureWindow;
            _openDuration = openDuration;
            _time = time ?? throw new ArgumentNullException(nameof(time));
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns <see langword="true"/> when the circuit is open for this (provider, key)
        /// tuple and the caller should NOT send the request.
        /// <paramref name="reason"/> is non-null when the method returns <see langword="true"/>.
        /// </summary>
        public bool IsOpen(string providerId, string apiKey, out string? reason)
        {
            var entry = GetOrCreateGate(providerId, apiKey);
            if (entry.Gate.IsHealthy)
            {
                reason = null;
                return false;
            }

            // Latched. Enforce the "stay Open for openDuration before any probe"
            // contract by checking LatchedAt locally. Common's AuthFailureGate
            // grants the FIRST probe slot immediately on first call (its
            // probeInterval rate-limits subsequent calls, not the first), but
            // brainarr's documented LlmAuthCircuit behavior is that the circuit
            // stays Open for openDuration before the HalfOpen probe is allowed.
            // LatchedAt is updated on every failure while latched (see
            // RecordAuthFailure) so a HalfOpen probe failure resets the timer.
            var now = _time.GetUtcNow();
            DateTimeOffset? latchedAt;
            lock (entry.SyncRoot)
            {
                latchedAt = entry.LatchedAt;
            }

            if (latchedAt.HasValue && (now - latchedAt.Value) < _openDuration)
            {
                var remaining = _openDuration - (now - latchedAt.Value);
                reason = $"Auth circuit open for {providerId} (resets in ~{remaining.TotalMinutes:0.0}m). Check API key.";
                return true;
            }

            // openDuration has elapsed — allow ONE probe through (HalfOpen).
            // Common's gate.TryAcquireProbeSlot handles the "at most one per
            // probeInterval" rate-limiting; with probeInterval = openDuration,
            // any subsequent IsOpen call within openDuration would be rejected.
            if (entry.Gate.TryAcquireProbeSlot())
            {
                _logger.Debug($"[LlmAuthCircuit] {providerId}: HalfOpen probe slot acquired");
                reason = null;
                return false;
            }

            reason = $"Auth circuit open for {providerId} (Open duration {_openDuration.TotalMinutes:0}m). Check API key.";
            return true;
        }

        /// <summary>
        /// Records an authentication failure for (provider, key).
        /// Opens the circuit once <see cref="_failureThreshold"/> consecutive failures are
        /// observed within the failure window. A failure observed during HalfOpen (i.e.,
        /// after a successful probe-slot grant) re-opens the circuit immediately because
        /// the handler's counter already sits at <c>threshold</c>; one more increment
        /// keeps status latched, and the gate's <c>_lastProbeAt</c> was just refreshed by
        /// the probe acquisition, so the next probe slot is granted only after a fresh
        /// <c>openDuration</c> elapses.
        /// </summary>
        public void RecordAuthFailure(string providerId, string apiKey, Exception? ex = null)
        {
            var entry = GetOrCreateGate(providerId, apiKey);
            var failure = new AuthFailure
            {
                ErrorCode = (ex as HttpRequestException)?.StatusCode?.ToString(),
                Message = ex?.Message ?? "Authentication failure",
            };

            // SYNC-OVER-ASYNC (Category A): HandleFailureAsync is synchronous internally
            // (returns ValueTask.CompletedTask) but routed through async to match the
            // bridge contract. Calling .AsTask().GetAwaiter().GetResult() is safe here
            // because no awaiter is involved on the success path.
            entry.Handler.HandleFailureAsync(failure).AsTask().GetAwaiter().GetResult();

            var count = entry.Handler.ConsecutiveFailureCount;
            var isLatched = !entry.Gate.IsHealthy;
            var justLatched = false;
            if (isLatched)
            {
                lock (entry.SyncRoot)
                {
                    justLatched = entry.LatchedAt is null;
                    // Update LatchedAt on EVERY failure while latched — a HalfOpen
                    // probe failure resets the 30-min open-duration timer, matching
                    // the original LlmAuthCircuit contract ("probe fail → reopen").
                    entry.LatchedAt = _time.GetUtcNow();
                }
            }

            _logger.Debug($"[LlmAuthCircuit] {providerId}: consecutive auth failures = {count}/{_failureThreshold}");
            if (justLatched)
            {
                _logger.Warn($"[LlmAuthCircuit] {providerId}: {count} consecutive auth failures — circuit OPENED for {_openDuration.TotalMinutes:0}m.");
            }
            else if (isLatched)
            {
                // Probe failure (or another failure while already open) resets the timer.
                _logger.Debug($"[LlmAuthCircuit] {providerId}: failure while open — open-duration timer reset.");
            }
        }

        /// <summary>
        /// Records a successful request for (provider, key).
        /// Resets the circuit to Closed for this key regardless of current state.
        /// </summary>
        public void RecordSuccess(string providerId, string apiKey)
        {
            var entry = GetOrCreateGate(providerId, apiKey);
            var wasLatched = !entry.Gate.IsHealthy;

            // SYNC-OVER-ASYNC (Category A): HandleSuccessAsync is synchronous internally.
            entry.Handler.HandleSuccessAsync().AsTask().GetAwaiter().GetResult();

            lock (entry.SyncRoot)
            {
                entry.LatchedAt = null;
            }

            if (wasLatched)
            {
                _logger.Info($"[LlmAuthCircuit] {providerId}: success — circuit CLOSED (reset).");
            }
        }

        /// <summary>Resets state for a given (provider, key) tuple. For test/debug use.</summary>
        internal void Reset(string providerId, string apiKey)
        {
            _gates.TryRemove(MakeKey(providerId, apiKey), out _);
        }

        // ── Key derivation ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns a dict key that encodes the provider id and a truncated SHA-256 of the API key.
        /// The raw key is never stored.
        /// </summary>
        /// <remarks>
        /// Rejects null/empty <paramref name="apiKey"/> — silently coercing to "" would
        /// produce identical keys for every provider that hasn't been configured yet,
        /// causing cross-account collisions in <see cref="_gates"/>. Callers that don't
        /// have a real API key (local providers, CLI providers) must not consult the
        /// circuit at all rather than passing a placeholder.
        /// </remarks>
        internal static string MakeKey(string providerId, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("providerId must be non-empty.", nameof(providerId));
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentException("apiKey must be non-empty — circuit keys cannot collide on the empty string.", nameof(apiKey));
            }

            // Hash the API key bytes with SHA-256 and take the first 12 bytes (16 base-64 chars).
            // This is sufficient for collision resistance across any realistic number of keys.
            var keyBytes = Encoding.UTF8.GetBytes(apiKey);
            var hash = SHA256.HashData(keyBytes);
            var hashB64 = Convert.ToBase64String(hash, 0, 12); // 12 bytes → 16 base-64 chars
            return $"{providerId}::{hashB64}";
        }

        // ── Internal state ───────────────────────────────────────────────────────

        private GateEntry GetOrCreateGate(string providerId, string apiKey)
        {
            var key = MakeKey(providerId, apiKey);
            return _gates.GetOrAdd(key, _ =>
            {
                var handler = new SlidingWindowAuthFailureHandler(
                    NullLogger<SlidingWindowAuthFailureHandler>.Instance,
                    _failureThreshold,
                    _failureWindow,
                    _time);
                var gate = new AuthFailureGate(
                    handler,
                    _time,
                    probeInterval: _openDuration,
                    NullLogger<AuthFailureGate>.Instance);
                return new GateEntry(gate, handler);
            });
        }

        // Encapsulates the (gate, handler) pair so we can read both during IsOpen /
        // RecordAuthFailure / RecordSuccess without two dict lookups. LatchedAt
        // tracks when the circuit transitioned to Open and is refreshed on every
        // failure while latched — this is what makes the "stay Open for D, then
        // probe" semantic work on top of Common's grant-first-probe-immediately
        // AuthFailureGate.TryAcquireProbeSlot behavior.
        private sealed class GateEntry
        {
            public GateEntry(AuthFailureGate gate, SlidingWindowAuthFailureHandler handler)
            {
                Gate = gate;
                Handler = handler;
            }

            public AuthFailureGate Gate { get; }
            public SlidingWindowAuthFailureHandler Handler { get; }
            public DateTimeOffset? LatchedAt { get; set; }
            public object SyncRoot { get; } = new();
        }
    }
}

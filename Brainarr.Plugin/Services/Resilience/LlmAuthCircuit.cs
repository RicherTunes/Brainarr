using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Lidarr.Plugin.Common.Errors;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Per-(provider, API-key) authentication failure circuit breaker.
    ///
    /// <para>
    /// Tracks consecutive 401-class failures per (providerId, hashed-key) tuple. Once N
    /// consecutive auth failures occur within a sliding window, the circuit opens for
    /// <see cref="OpenDuration"/> (default 30 min). After that period the circuit
    /// transitions to HalfOpen — the next request is allowed through as a probe; a
    /// success resets to Closed, a failure returns to Open.
    /// </para>
    ///
    /// <para>
    /// API keys are never stored in plaintext. The dict key is
    /// <c>{providerId}::{SHA256(apiKey)[0..16]}</c> (base-64 first 16 chars).
    /// </para>
    ///
    /// <para>
    /// Brainarr-internal by design. The streaming-plugin ecosystem (applemusicarr,
    /// tidalarr, qobuzarr) uses <c>Lidarr.Plugin.Common.Services.Bridge.AuthFailureGate</c>
    /// + <c>AuthFailureGateRegistry</c> for the equivalent role. The two patterns
    /// diverge intentionally on three axes:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Key hashing</b>: this class stores <c>SHA256(apiKey)[0..16]</c> so an
    ///     LLM API key never appears as a dict key in heap/debugger views. Common's
    ///     <c>AuthFailureGateRegistry</c> uses raw keys — fine for short-lived
    ///     streaming session tokens, too leaky for LLM keys with broad permissions.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Sliding window</b>: 3 consecutive failures within a 5-min window opens
    ///     the circuit; stale runs are forgotten. Common's <c>DefaultAuthFailureHandler</c>
    ///     is K-consecutive-without-reset and rate-limits the K-1 sub-threshold path,
    ///     which is the streaming pattern but pre-latches the LLM token-refresh-race case.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Open duration</b>: 30-min Open → HalfOpen single probe matches the
    ///     human-recoverable LLM-key-rotation cadence. Common's gate uses a 60s
    ///     continuous probe — correct for streaming session refresh, too tight for
    ///     LLM key rotation.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Convergence is possible by extending Common's <c>DefaultAuthFailureHandler</c>
    /// (or adding a <c>SlidingWindowAuthFailureHandler</c> sibling) to support
    /// <c>(failureThreshold, failureWindow, openDuration)</c>. Tracked as a Common
    /// extension opportunity; the parity-mission contract explicitly classifies the
    /// 11-provider LLM matrix as "different by design" so this stays as a documented
    /// architectural divergence until the convergence is done.
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

        // Duration the circuit stays Open before transitioning to HalfOpen.
        // 30 min mirrors the audit recommendation and gives a user time to fix the
        // key and restart (or for a subscription token to auto-refresh).
        private static readonly TimeSpan DefaultOpenDuration = TimeSpan.FromMinutes(30);

        // ── State ────────────────────────────────────────────────────────────────
        private readonly ConcurrentDictionary<string, KeyState> _states = new(StringComparer.Ordinal);
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
            var slot = _states.GetOrAdd(MakeKey(providerId, apiKey), _ => new KeyState());

            lock (slot)
            {
                var now = _time.GetUtcNow().UtcDateTime;

                switch (slot.Phase)
                {
                    case CircuitPhase.Open:
                        if (now - slot.OpenedAt >= _openDuration)
                        {
                            // Transition to HalfOpen — allow one probe through.
                            slot.Phase = CircuitPhase.HalfOpen;
                            _logger.Debug($"[LlmAuthCircuit] {providerId}: transitioning Open→HalfOpen after {_openDuration.TotalMinutes:0}m");
                        }
                        else
                        {
                            var remaining = _openDuration - (now - slot.OpenedAt);
                            reason = $"Auth circuit open for {providerId} (resets in ~{remaining.TotalMinutes:0.0}m). Check API key.";
                            return true;
                        }
                        break;

                    case CircuitPhase.HalfOpen:
                        // One probe allowed — leave state as HalfOpen; outcome recorded later.
                        break;
                }

                reason = null;
                return false;
            }
        }

        /// <summary>
        /// Records an authentication failure for (provider, key).
        /// Opens the circuit once <see cref="_failureThreshold"/> consecutive failures are
        /// observed within the failure window.
        /// </summary>
        public void RecordAuthFailure(string providerId, string apiKey, Exception? ex = null)
        {
            var slot = _states.GetOrAdd(MakeKey(providerId, apiKey), _ => new KeyState());

            lock (slot)
            {
                var now = _time.GetUtcNow().UtcDateTime;

                // If the previous failure was outside the window, reset the run count.
                if (slot.ConsecutiveFailures > 0 && now - slot.FirstFailureInRun > _failureWindow)
                {
                    slot.ConsecutiveFailures = 0;
                    slot.FirstFailureInRun = now;
                }

                if (slot.ConsecutiveFailures == 0)
                {
                    slot.FirstFailureInRun = now;
                }

                slot.ConsecutiveFailures++;
                slot.LastFailureAt = now;

                _logger.Debug($"[LlmAuthCircuit] {providerId}: consecutive auth failures = {slot.ConsecutiveFailures}/{_failureThreshold}");

                // HalfOpen probe failed → reopen immediately, no threshold required.
                if (slot.Phase == CircuitPhase.HalfOpen)
                {
                    slot.Phase = CircuitPhase.Open;
                    slot.OpenedAt = now;
                    _logger.Warn($"[LlmAuthCircuit] {providerId}: HalfOpen probe failed — circuit re-OPENED.");
                    return;
                }

                if (slot.ConsecutiveFailures >= _failureThreshold && slot.Phase == CircuitPhase.Closed)
                {
                    slot.Phase = CircuitPhase.Open;
                    slot.OpenedAt = now;
                    _logger.Warn($"[LlmAuthCircuit] {providerId}: {slot.ConsecutiveFailures} consecutive auth failures — circuit OPENED for {_openDuration.TotalMinutes:0}m.");
                }
            }
        }

        /// <summary>
        /// Records a successful request for (provider, key).
        /// Resets the circuit to Closed for this key regardless of current state.
        /// </summary>
        public void RecordSuccess(string providerId, string apiKey)
        {
            var slot = _states.GetOrAdd(MakeKey(providerId, apiKey), _ => new KeyState());

            lock (slot)
            {
                var wasOpen = slot.Phase != CircuitPhase.Closed;
                slot.Phase = CircuitPhase.Closed;
                slot.ConsecutiveFailures = 0;
                slot.FirstFailureInRun = DateTime.MinValue;

                if (wasOpen)
                {
                    _logger.Info($"[LlmAuthCircuit] {providerId}: success — circuit CLOSED (reset).");
                }
            }
        }

        /// <summary>Resets state for a given (provider, key) tuple. For test/debug use.</summary>
        internal void Reset(string providerId, string apiKey)
        {
            _states.TryRemove(MakeKey(providerId, apiKey), out _);
        }

        // ── Key derivation ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns a dict key that encodes the provider id and a truncated SHA-256 of the API key.
        /// The raw key is never stored.
        /// </summary>
        internal static string MakeKey(string providerId, string apiKey)
        {
            // Hash the API key bytes with SHA-256 and take the first 12 bytes (16 base-64 chars).
            // This is sufficient for collision resistance across any realistic number of keys.
            var keyBytes = Encoding.UTF8.GetBytes(apiKey ?? string.Empty);
            var hash = SHA256.HashData(keyBytes);
            var hashB64 = Convert.ToBase64String(hash, 0, 12); // 12 bytes → 16 base-64 chars
            return $"{providerId}::{hashB64}";
        }

        // ── Internal state ───────────────────────────────────────────────────────

        private enum CircuitPhase { Closed, Open, HalfOpen }

        private sealed class KeyState
        {
            public CircuitPhase Phase = CircuitPhase.Closed;
            public int ConsecutiveFailures;
            public DateTime FirstFailureInRun;
            public DateTime LastFailureAt;
            public DateTime OpenedAt;
        }
    }
}

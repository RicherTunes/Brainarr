using System;
using System.Threading;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Async-scoped, per-run accumulation of safety-gate drop reasons, used to attribute an
    /// under-target run to a specific cause (e.g. the confidence floor) without a process-wide
    /// cumulative counter — which would race across concurrent fetches sharing one metrics singleton.
    /// </summary>
    /// <remarks>
    /// The scope stores a mutable holder behind an <see cref="AsyncLocal{T}"/>. AsyncLocal flows the
    /// holder REFERENCE down into awaited child work (the pipeline → safety gate), so the gate mutates
    /// the holder's fields (NOT the AsyncLocal slot) and those writes are visible to the parent
    /// (orchestrator) after the awaited work completes. Concurrent runs each call
    /// <see cref="BeginScope"/> in their own async context and therefore get an isolated holder, so
    /// one run's gate drops can never leak into another run's count.
    /// </remarks>
    internal static class GateMetricsContext
    {
        private sealed class Counters
        {
            public int ConfidenceFloorDrops;
        }

        private static readonly AsyncLocal<Counters?> _current = new AsyncLocal<Counters?>();

        /// <summary>Begins a per-run accumulation scope. Dispose restores the previous scope.</summary>
        public static IDisposable BeginScope()
        {
            var prev = _current.Value;
            _current.Value = new Counters();
            return new Scope(() => _current.Value = prev);
        }

        /// <summary>
        /// Adds to the count of items the safety gate dropped because the model EXPLICITLY scored
        /// them below the Minimum Confidence floor (score-less items bypass the floor and are not
        /// counted). No-op outside a scope.
        /// </summary>
        public static void AddConfidenceFloorDrops(int count)
        {
            if (count <= 0) return;
            var c = _current.Value;
            if (c != null) Interlocked.Add(ref c.ConfidenceFloorDrops, count);
        }

        /// <summary>Confidence-floor drops recorded in the current scope (0 if none / no scope).</summary>
        public static int ConfidenceFloorDrops => _current.Value?.ConfidenceFloorDrops ?? 0;

        private sealed class Scope : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;

            public Scope(Action onDispose) => _onDispose = onDispose;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose();
            }
        }
    }
}

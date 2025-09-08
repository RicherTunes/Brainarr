using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Utility to temporarily change a setting/property and automatically restore
    /// the previous value when disposed. Intended to reduce try/finally noise
    /// when adjusting request-scoped configuration like MaxRecommendations.
    /// </summary>
    internal static class SettingScope
    {
        public static IDisposable Apply<T>(Func<T> getter, Action<T> setter, T newValue)
        {
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));

            var previous = getter();
            try { setter(newValue); } catch { /* best-effort */ }

            return new RevertScope<T>(previous, setter);
        }

        private sealed class RevertScope<T> : IDisposable
        {
            private readonly T _previous;
            private readonly Action<T> _setter;
            private bool _disposed;

            public RevertScope(T previous, Action<T> setter)
            {
                _previous = previous;
                _setter = setter;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _setter(_previous); } catch { /* best-effort */ }
            }
        }
    }
}

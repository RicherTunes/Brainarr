using System;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    // Global debug switches for Brainarr internals.
    internal static class DebugFlags
    {
        // When true (in current async context), providers print sanitized request payloads and endpoints at info level.
        private static readonly AsyncLocal<bool> _providerPayload = new AsyncLocal<bool>();

        public static bool ProviderPayload
        {
            get => _providerPayload.Value;
            set => _providerPayload.Value = value;
        }

        public static IDisposable PushFromSettings(BrainarrSettings settings)
        {
            var prev = ProviderPayload;
            ProviderPayload = settings?.EnableDebugLogging == true;
            return new Scope(() => ProviderPayload = prev);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;
            public Scope(Action onDispose) { _onDispose = onDispose; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose?.Invoke();
            }
        }
    }
}

using System;
using Microsoft.Extensions.DependencyInjection;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    // Minimal, internal service locator to allow helpers outside DI graphs
    // to optionally resolve services when available (e.g., IHttpResilience).
    internal static class ServiceLocator
    {
        private static IServiceProvider _provider;

        public static void Initialize(IServiceProvider provider)
        {
            _provider = provider;
        }

        public static T TryGet<T>() where T : class
        {
            return _provider?.GetService<T>();
        }
    }
}

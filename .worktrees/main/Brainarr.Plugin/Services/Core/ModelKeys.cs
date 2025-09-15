using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Stable keys for identifying a provider/model pair across subsystems
    /// (limiting, circuit breakers, metrics, etc.).
    /// </summary>
    public readonly record struct ModelKey(string Provider, string ModelId)
    {
        public override string ToString() => $"{Provider}:{ModelId}";

        public static ModelKey From(string provider, string modelId)
            => new ModelKey((provider ?? string.Empty).Trim().ToLowerInvariant(), (modelId ?? string.Empty).Trim());
    }

    /// <summary>
    /// Simple provider key (without model).
    /// </summary>
    public readonly record struct ProviderKey(string Provider)
    {
        public override string ToString() => Provider ?? string.Empty;
    }
}

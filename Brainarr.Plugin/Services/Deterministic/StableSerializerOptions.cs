using System.Text.Json;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Deterministic
{
    public static class StableSerializerOptions
    {
        // Single source of truth for deterministic JSON serialization used in key building and fingerprints.
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            // Keep defaults; we don't ignore nulls to ensure key material stays stable when values are missing
            // and we don't allow case-insensitive reads since we only serialize.
        };
    }
}

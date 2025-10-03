using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Deterministic
{
    public static class KeyBuilder
    {
        // Serialize the supplied payload with stable options and return a stage-prefixed url-safe SHA256 key.
        public static string Build(string stagePrefix, object payload, int version = 1, int take = 24)
        {
            if (string.IsNullOrWhiteSpace(stagePrefix)) stagePrefix = "key";
            var json = JsonSerializer.Serialize(payload, StableSerializerOptions.Options);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            var b64 = Convert.ToBase64String(hash).Replace('/', '_').Replace('+', '-');
            if (take > 0 && take < b64.Length) b64 = b64.Substring(0, take);
            return $"{stagePrefix}_{version}_{b64}";
        }
    }
}

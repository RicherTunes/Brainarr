using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Lidarr.Plugin.Common.Services.Caching;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IRecommendationCache
    {
        bool TryGet(string cacheKey, out List<ImportListItemInfo> recommendations);
        void Set(string cacheKey, List<ImportListItemInfo> recommendations, TimeSpan? duration = null);
        void Clear();
        string GenerateCacheKey(string provider, int maxRecommendations, string libraryFingerprint);
    }

    /// <summary>
    /// Recommendation cache backed by Lidarr.Plugin.Common's <see cref="SmartCache{TKey,TValue}"/>.
    /// </summary>
    /// <remarks>
    /// This is a thin Brainarr-specific adapter that preserves the existing
    /// <see cref="IRecommendationCache"/> contract — including defensive copy
    /// semantics on read — while delegating commodity multi-tier mechanics
    /// (size bounding, TTL, LFU/LRU hybrid eviction) to common.
    ///
    /// Sizing matches <see cref="BrainarrConstants.MaxCacheEntries"/>; default TTL
    /// matches <see cref="BrainarrConstants.CacheDurationMinutes"/>. Per-call TTL
    /// overrides are honored via the explicit-TTL <c>Set</c> overload.
    /// </remarks>
    public class RecommendationCache : IRecommendationCache
    {
        private readonly Logger _logger;
        private readonly TimeSpan _defaultCacheDuration;
        private readonly SmartCache<string, List<ImportListItemInfo>> _cache;

        public RecommendationCache(Logger logger, TimeSpan? defaultDuration = null)
        {
            // NOTE: Original behavior accepts null logger silently — preserve for compatibility.
            // Calls to `_logger.Debug/Info/etc` will then NRE; callers that pass null
            // historically rely on never-hit log paths.
            _logger = logger;
            _defaultCacheDuration = defaultDuration ?? TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes);

            var options = new SmartCacheOptions
            {
                MaxCacheSize = BrainarrConstants.MaxCacheEntries,
                EvictionBatchSize = Math.Max(1, BrainarrConstants.MaxCacheEntries / 10),
                DefaultExpiry = _defaultCacheDuration,
                NormalPriorityExpiry = _defaultCacheDuration,
                LowPriorityExpiry = _defaultCacheDuration,
                HighPriorityExpiry = _defaultCacheDuration,
                PopularItemExpiry = _defaultCacheDuration,
            };

            _cache = new SmartCache<string, List<ImportListItemInfo>>(
                keySerializer: k => k,
                options: options,
                logger: null);
        }

        public bool TryGet(string cacheKey, out List<ImportListItemInfo> recommendations)
        {
            if (_cache.TryGet(cacheKey, out var cached) && cached != null)
            {
                // CRITICAL: Return a defensive copy to prevent shared reference modifications.
                // This prevents the duplication bug where multiple callers modify the same list.
                recommendations = cached.Select(item => new ImportListItemInfo
                {
                    Artist = item.Artist,
                    Album = item.Album,
                    ReleaseDate = item.ReleaseDate,
                    ArtistMusicBrainzId = item.ArtistMusicBrainzId,
                    AlbumMusicBrainzId = item.AlbumMusicBrainzId
                }).ToList();

                _logger.Debug($"Cache hit for key: {cacheKey} ({cached.Count} recommendations)");
                return true;
            }

            recommendations = null;
            _logger.Debug($"Cache miss for key: {cacheKey}");
            return false;
        }

        public void Set(string cacheKey, List<ImportListItemInfo> recommendations, TimeSpan? duration = null)
        {
            // Don't store null data - this prevents cache pollution
            if (recommendations == null)
            {
                _logger.Debug($"Not caching null data for key: {cacheKey}");
                return;
            }

            var expiration = duration ?? _defaultCacheDuration;
            _cache.Set(cacheKey, recommendations, expiration);

            var count = recommendations.Count;
            _logger.Info($"Cached {count} recommendations with key: {cacheKey} (expires in {expiration.TotalMinutes} minutes)");
        }

        public void Clear()
        {
            _cache.Clear();
            _logger.Info("Recommendation cache cleared");
        }

        public string GenerateCacheKey(string provider, int maxRecommendations, string libraryFingerprint)
        {
            // Create a unique cache key based on provider settings and library state
            var keyData = $"{provider}|{maxRecommendations}|{libraryFingerprint}";

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyData));
                var shortHash = Convert.ToBase64String(hash).Substring(0, 8);
                return $"brainarr_recs_{provider}_{maxRecommendations}_{shortHash}";
            }
        }
    }
}

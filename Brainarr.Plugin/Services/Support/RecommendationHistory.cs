using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Tracks recommendation history to prevent redundant suggestions
    /// and reduce token usage by excluding already-handled items
    /// </summary>
    public class RecommendationHistory
    {
        private readonly Logger _logger;
        private readonly string _historyPath;
        private HistoryData _history;
        private readonly object _lock = new object();

        public RecommendationHistory(Logger logger, string? dataPath = null)
        {
            _logger = logger;
            _historyPath = Path.Combine(
                dataPath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Brainarr",
                "recommendation_history.json"
            );
            LoadHistory();
        }

        /// <summary>
        /// Record that recommendations were suggested to the user
        /// </summary>
        public void RecordSuggestions(List<Recommendation> recommendations)
        {
            lock (_lock)
            {
                foreach (var rec in recommendations)
                {
                    var key = GetKey(rec.Artist, rec.Album);
                    
                    if (!_history.Suggestions.ContainsKey(key))
                    {
                        _history.Suggestions[key] = new SuggestionRecord
                        {
                            Artist = rec.Artist,
                            Album = rec.Album,
                            FirstSuggested = DateTime.UtcNow,
                            LastSuggested = DateTime.UtcNow,
                            SuggestionCount = 1,
                            Confidence = rec.Confidence
                        };
                    }
                    else
                    {
                        var record = _history.Suggestions[key];
                        record.LastSuggested = DateTime.UtcNow;
                        record.SuggestionCount++;
                        record.Confidence = Math.Max(record.Confidence, rec.Confidence);
                    }
                }
                
                SaveHistory();
                _logger.Debug($"Recorded {recommendations.Count} suggestions");
            }
        }

        /// <summary>
        /// Mark items as accepted (user added to library)
        /// </summary>
        public void MarkAsAccepted(string artist, string? album = null)
        {
            lock (_lock)
            {
                var key = GetKey(artist, album);
                
                // Move from suggestions to accepted
                if (_history.Suggestions.ContainsKey(key))
                {
                    var suggestion = _history.Suggestions[key];
                    _history.Accepted[key] = new AcceptedRecord
                    {
                        Artist = suggestion.Artist,
                        Album = suggestion.Album,
                        AcceptedDate = DateTime.UtcNow,
                        SuggestionCount = suggestion.SuggestionCount
                    };
                    _history.Suggestions.Remove(key);
                }
                else
                {
                    // Directly added without suggestion
                    _history.Accepted[key] = new AcceptedRecord
                    {
                        Artist = artist,
                        Album = album,
                        AcceptedDate = DateTime.UtcNow,
                        SuggestionCount = 0
                    };
                }
                
                SaveHistory();
                _logger.Info($"Marked as accepted: {artist} - {album ?? "All albums"}");
            }
        }

        /// <summary>
        /// Mark items as rejected (suggested but not added after timeout)
        /// </summary>
        public void MarkAsRejected(string artist, string? album = null, string? reason = null)
        {
            lock (_lock)
            {
                var key = GetKey(artist, album);
                
                if (_history.Suggestions.ContainsKey(key))
                {
                    var suggestion = _history.Suggestions[key];
                    
                    // Don't mark as rejected if suggested very recently (< 1 day)
                    if (DateTime.UtcNow - suggestion.LastSuggested < TimeSpan.FromDays(1))
                    {
                        return;
                    }
                    
                    _history.Rejected[key] = new RejectedRecord
                    {
                        Artist = suggestion.Artist,
                        Album = suggestion.Album,
                        RejectedDate = DateTime.UtcNow,
                        SuggestionCount = suggestion.SuggestionCount,
                        DaysSinceSuggestion = (DateTime.UtcNow - suggestion.FirstSuggested).Days,
                        RejectionReason = reason
                    };
                    _history.Suggestions.Remove(key);
                    
                    SaveHistory();
                    _logger.Debug($"Marked as rejected: {artist} - {album ?? "All albums"}" + 
                                 (reason != null ? $" (Reason: {reason})" : ""));
                }
            }
        }
        
        /// <summary>
        /// Explicitly mark items as disliked by user (negative constraint)
        /// </summary>
        public void MarkAsDisliked(string artist, string? album = null, DislikeLevel level = DislikeLevel.Normal)
        {
            lock (_lock)
            {
                var key = GetKey(artist, album);
                
                // Add to disliked list with strength level
                _history.Disliked[key] = new DislikedRecord
                {
                    Artist = artist,
                    Album = album,
                    DislikedDate = DateTime.UtcNow,
                    Level = level,
                    IsActive = true
                };
                
                // Also remove from suggestions if present
                _history.Suggestions.Remove(key);
                
                // Track dislike patterns for genre/style analysis
                if (!string.IsNullOrEmpty(artist))
                {
                    TrackDislikePattern(artist, album, level);
                }
                
                SaveHistory();
                _logger.Info($"User disliked: {artist} - {album ?? "All albums"} (Level: {level})");
            }
        }
        
        /// <summary>
        /// Remove a dislike constraint (user changed their mind)
        /// </summary>
        public void RemoveDislike(string artist, string? album = null)
        {
            lock (_lock)
            {
                var key = GetKey(artist, album);
                
                if (_history.Disliked.ContainsKey(key))
                {
                    _history.Disliked[key].IsActive = false;
                    SaveHistory();
                    _logger.Info($"Removed dislike for: {artist} - {album ?? "All albums"}");
                }
            }
        }
        
        /// <summary>
        /// Track patterns in user dislikes to identify genres/styles to avoid
        /// </summary>
        private void TrackDislikePattern(string artist, string album, DislikeLevel level)
        {
            // Initialize pattern tracking if needed
            if (_history.DislikePatterns == null)
            {
                _history.DislikePatterns = new Dictionary<string, DislikePattern>();
            }
            
            // For now, track by artist - could be enhanced with genre data
            var patternKey = artist.ToLowerInvariant();
            
            if (!_history.DislikePatterns.ContainsKey(patternKey))
            {
                _history.DislikePatterns[patternKey] = new DislikePattern
                {
                    Pattern = artist,
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow,
                    Count = 1,
                    AverageLevel = (int)level
                };
            }
            else
            {
                var pattern = _history.DislikePatterns[patternKey];
                pattern.LastSeen = DateTime.UtcNow;
                pattern.Count++;
                pattern.AverageLevel = ((pattern.AverageLevel * (pattern.Count - 1)) + (int)level) / pattern.Count;
            }
        }

        /// <summary>
        /// Get items to exclude from new recommendations
        /// </summary>
        public ExclusionList GetExclusions()
        {
            lock (_lock)
            {
                var exclusions = new ExclusionList();
                
                // Exclude all accepted items (already in library)
                foreach (var accepted in _history.Accepted.Values)
                {
                    exclusions.InLibrary.Add(GetKey(accepted.Artist, accepted.Album));
                }
                
                // Exclude recently rejected items (within 30 days)
                var recentCutoff = DateTime.UtcNow.AddDays(-30);
                foreach (var rejected in _history.Rejected.Values)
                {
                    if (rejected.RejectedDate > recentCutoff)
                    {
                        exclusions.RecentlyRejected.Add(GetKey(rejected.Artist, rejected.Album));
                    }
                }
                
                // Exclude items suggested multiple times without acceptance
                foreach (var suggestion in _history.Suggestions.Values)
                {
                    if (suggestion.SuggestionCount >= 3)
                    {
                        exclusions.OverSuggested.Add(GetKey(suggestion.Artist, suggestion.Album));
                    }
                }
                
                // Exclude explicitly disliked items (negative constraints)
                if (_history.Disliked != null)
                {
                    foreach (var disliked in _history.Disliked.Values)
                    {
                        if (disliked.IsActive)
                        {
                            var key = GetKey(disliked.Artist, disliked.Album);
                            
                            // Add to appropriate category based on dislike level
                            if (disliked.Level == DislikeLevel.Strong || disliked.Level == DislikeLevel.NeverAgain)
                            {
                                exclusions.StronglyDisliked.Add(key);
                            }
                            else
                            {
                                exclusions.Disliked.Add(key);
                            }
                        }
                    }
                }
                
                _logger.Debug($"Exclusions: {exclusions.InLibrary.Count} in library, " +
                             $"{exclusions.RecentlyRejected.Count} rejected, " +
                             $"{exclusions.OverSuggested.Count} over-suggested, " +
                             $"{exclusions.Disliked.Count} disliked, " +
                             $"{exclusions.StronglyDisliked.Count} strongly disliked");
                
                return exclusions;
            }
        }

        /// <summary>
        /// Create a compact exclusion prompt segment
        /// </summary>
        public string GetExclusionPrompt()
        {
            var exclusions = GetExclusions();
            
            if (!exclusions.HasExclusions)
            {
                return string.Empty;
            }
            
            var prompt = new List<string>();
            
            // Ultra-compact format for token efficiency
            if (exclusions.InLibrary.Count > 0)
            {
                // Just send artist names for in-library items
                var artists = exclusions.InLibrary
                    .Select(k => k.Split('|')[0])
                    .Distinct()
                    .Take(50); // Limit to top 50
                    
                prompt.Add($"EXCLUDE:{string.Join(",", artists)}");
            }
            
            if (exclusions.RecentlyRejected.Count > 0)
            {
                // Send a few rejected items as negative examples
                var rejected = exclusions.RecentlyRejected
                    .Take(10)
                    .Select(k => k.Split('|')[0]);
                    
                prompt.Add($"AVOID:{string.Join(",", rejected)}");
            }
            
            // Add negative constraints for disliked items
            if (exclusions.StronglyDisliked.Count > 0)
            {
                var strongDisliked = exclusions.StronglyDisliked
                    .Take(20)
                    .Select(k => k.Split('|')[0]);
                    
                prompt.Add($"NEVER_RECOMMEND:{string.Join(",", strongDisliked)}");
            }
            
            if (exclusions.Disliked.Count > 0)
            {
                var disliked = exclusions.Disliked
                    .Take(15)
                    .Select(k => k.Split('|')[0]);
                    
                prompt.Add($"DO_NOT_SUGGEST:{string.Join(",", disliked)}");
            }
            
            // Add dislike pattern information if available
            if (_history.DislikePatterns != null && _history.DislikePatterns.Count > 0)
            {
                var patterns = _history.DislikePatterns
                    .Where(p => p.Value.Count >= 2)
                    .OrderByDescending(p => p.Value.AverageLevel)
                    .Take(5)
                    .Select(p => p.Value.Pattern);
                
                if (patterns.Any())
                {
                    prompt.Add($"USER_DISLIKES_STYLE:{string.Join(",", patterns)}");
                }
            }
            
            return string.Join("\n", prompt);
        }

        /// <summary>
        /// Cleanup old history entries to prevent unlimited growth
        /// </summary>
        public void CleanupOldEntries()
        {
            lock (_lock)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-180); // 6 months
                
                // Remove old rejected items
                var oldRejected = _history.Rejected
                    .Where(kvp => kvp.Value.RejectedDate < cutoffDate)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in oldRejected)
                {
                    _history.Rejected.Remove(key);
                }
                
                // Remove old suggestions that haven't been acted on
                var oldSuggestions = _history.Suggestions
                    .Where(kvp => kvp.Value.LastSuggested < cutoffDate)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in oldSuggestions)
                {
                    _history.Suggestions.Remove(key);
                }
                
                if (oldRejected.Any() || oldSuggestions.Any())
                {
                    SaveHistory();
                    _logger.Info($"Cleaned up {oldRejected.Count + oldSuggestions.Count} old history entries");
                }
            }
        }

        /// <summary>
        /// Sync with current Lidarr library to update accepted items
        /// </summary>
        public void SyncWithLibrary(List<string> currentArtists, List<(string Artist, string Album)> currentAlbums)
        {
            lock (_lock)
            {
                var changesMade = false;
                
                // Add artists from library that aren't in accepted list
                foreach (var artist in currentArtists)
                {
                    var key = GetKey(artist, null);
                    if (!_history.Accepted.ContainsKey(key))
                    {
                        _history.Accepted[key] = new AcceptedRecord
                        {
                            Artist = artist,
                            Album = null,
                            AcceptedDate = DateTime.UtcNow,
                            SuggestionCount = 0
                        };
                        changesMade = true;
                    }
                }
                
                // Add albums from library
                foreach (var (artist, album) in currentAlbums)
                {
                    var key = GetKey(artist, album);
                    if (!_history.Accepted.ContainsKey(key))
                    {
                        _history.Accepted[key] = new AcceptedRecord
                        {
                            Artist = artist,
                            Album = album,
                            AcceptedDate = DateTime.UtcNow,
                            SuggestionCount = 0
                        };
                        changesMade = true;
                    }
                }
                
                if (changesMade)
                {
                    SaveHistory();
                    _logger.Info($"Synced with library: {_history.Accepted.Count} items in library");
                }
            }
        }

        /// <summary>
        /// Get statistics about recommendation history
        /// </summary>
        public HistoryStats GetStats()
        {
            lock (_lock)
            {
                return new HistoryStats
                {
                    TotalSuggested = _history.Suggestions.Count,
                    TotalAccepted = _history.Accepted.Count,
                    TotalRejected = _history.Rejected.Count,
                    AcceptanceRate = _history.Suggestions.Count > 0 
                        ? (double)_history.Accepted.Count / (_history.Accepted.Count + _history.Rejected.Count + _history.Suggestions.Count)
                        : 0,
                    MostSuggestedArtist = _history.Suggestions.Values
                        .OrderByDescending(s => s.SuggestionCount)
                        .FirstOrDefault()?.Artist,
                    LastSuggestionDate = _history.Suggestions.Values
                        .OrderByDescending(s => s.LastSuggested)
                        .FirstOrDefault()?.LastSuggested
                };
            }
        }

        private string GetKey(string artist, string album)
        {
            return string.IsNullOrEmpty(album) 
                ? artist.ToLowerInvariant()
                : $"{artist.ToLowerInvariant()}|{album.ToLowerInvariant()}";
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyPath))
                {
                    var json = File.ReadAllText(_historyPath);
                    _history = JsonSerializer.Deserialize<HistoryData>(json) ?? new HistoryData();
                    _logger.Debug($"Loaded history: {_history.Suggestions.Count} suggestions, " +
                                 $"{_history.Accepted.Count} accepted, {_history.Rejected.Count} rejected");
                }
                else
                {
                    _history = new HistoryData();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load history, starting fresh");
                _history = new HistoryData();
            }
        }

        private void SaveHistory()
        {
            try
            {
                var directory = Path.GetDirectoryName(_historyPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_historyPath, json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save history");
            }
        }

        // Data models
        private class HistoryData
        {
            public Dictionary<string, SuggestionRecord> Suggestions { get; set; } = new();
            public Dictionary<string, AcceptedRecord> Accepted { get; set; } = new();
            public Dictionary<string, RejectedRecord> Rejected { get; set; } = new();
            public Dictionary<string, DislikedRecord> Disliked { get; set; } = new();
            public Dictionary<string, DislikePattern> DislikePatterns { get; set; } = new();
        }

        private class SuggestionRecord
        {
            public string Artist { get; set; }
            public string Album { get; set; }
            public DateTime FirstSuggested { get; set; }
            public DateTime LastSuggested { get; set; }
            public int SuggestionCount { get; set; }
            public double Confidence { get; set; }
        }

        private class AcceptedRecord
        {
            public string Artist { get; set; }
            public string Album { get; set; }
            public DateTime AcceptedDate { get; set; }
            public int SuggestionCount { get; set; }
        }

        private class RejectedRecord
        {
            public string Artist { get; set; }
            public string Album { get; set; }
            public DateTime RejectedDate { get; set; }
            public int SuggestionCount { get; set; }
            public int DaysSinceSuggestion { get; set; }
            public string RejectionReason { get; set; }
        }
        
        private class DislikedRecord
        {
            public string Artist { get; set; }
            public string Album { get; set; }
            public DateTime DislikedDate { get; set; }
            public DislikeLevel Level { get; set; }
            public bool IsActive { get; set; }
        }
        
        private class DislikePattern
        {
            public string Pattern { get; set; }
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public int Count { get; set; }
            public int AverageLevel { get; set; }
        }

        public class ExclusionList
        {
            public HashSet<string> InLibrary { get; set; } = new();
            public HashSet<string> RecentlyRejected { get; set; } = new();
            public HashSet<string> OverSuggested { get; set; } = new();
            public HashSet<string> Disliked { get; set; } = new();
            public HashSet<string> StronglyDisliked { get; set; } = new();
            
            public bool HasExclusions => InLibrary.Any() || RecentlyRejected.Any() || OverSuggested.Any() || 
                                         Disliked.Any() || StronglyDisliked.Any();
            public int TotalExclusions => InLibrary.Count + RecentlyRejected.Count + OverSuggested.Count + 
                                          Disliked.Count + StronglyDisliked.Count;
        }
        
        public enum DislikeLevel
        {
            Normal = 0,
            Strong = 1,
            NeverAgain = 2
        }

        public class HistoryStats
        {
            public int TotalSuggested { get; set; }
            public int TotalAccepted { get; set; }
            public int TotalRejected { get; set; }
            public double AcceptanceRate { get; set; }
            public string MostSuggestedArtist { get; set; }
            public DateTime? LastSuggestionDate { get; set; }
        }
    }
}
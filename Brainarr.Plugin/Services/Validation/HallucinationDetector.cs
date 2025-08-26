using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Interface for detecting AI hallucinations in music recommendations.
    /// </summary>
    public interface IHallucinationDetector
    {
        Task<HallucinationDetectionResult> DetectHallucinationAsync(Recommendation recommendation);
    }

    /// <summary>
    /// Advanced AI hallucination detector that identifies patterns indicating fake or hallucinated recommendations.
    /// </summary>
    public class HallucinationDetector : IHallucinationDetector
    {
        private readonly Logger _logger;
        
        // Known problematic patterns that indicate AI hallucinations
        private readonly Dictionary<HallucinationPatternType, List<HallucinationPattern>> _patterns;

        public HallucinationDetector(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _patterns = InitializeHallucinationPatterns();
        }

        public async Task<HallucinationDetectionResult> DetectHallucinationAsync(Recommendation recommendation)
        {
            if (recommendation == null)
                throw new ArgumentException("Recommendation cannot be null", nameof(recommendation));
                
            var result = new HallucinationDetectionResult();
            
            try
            {
                _logger.Debug($"Analyzing recommendation for hallucinations: {recommendation.Artist} - {recommendation.Album}");

                // Run all hallucination detection algorithms
                await DetectNonExistentArtistPatterns(recommendation, result);
                await DetectNonExistentAlbumPatterns(recommendation, result);
                await DetectImpossibleReleaseDates(recommendation, result);
                await DetectNamePatternAnomalies(recommendation, result);
                await DetectRepetitiveElements(recommendation, result);
                await DetectSuspiciousCombinations(recommendation, result);
                await DetectTemporalInconsistencies(recommendation, result);
                await DetectFormatAnomalies(recommendation, result);
                await DetectLanguagePatterns(recommendation, result);

                // Calculate overall hallucination confidence
                CalculateOverallConfidence(result);

                _logger.Debug($"Hallucination analysis complete: {recommendation.Artist} - {recommendation.Album} " +
                            $"(Confidence: {result.HallucinationConfidence:P2}, Patterns: {result.DetectedPatterns.Count})");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error detecting hallucinations for: {recommendation.Artist} - {recommendation.Album}");
                
                // Return safe result on error
                result.HallucinationConfidence = 0.5; // Neutral confidence
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.FormatAnomalies,
                    Description = $"Analysis failed: {ex.Message}",
                    Confidence = 0.5
                });
                
                return result;
            }
        }

        private async Task DetectNonExistentArtistPatterns(Recommendation recommendation, HallucinationDetectionResult result)
        {
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            if (string.IsNullOrWhiteSpace(recommendation.Artist))
            {
                suspiciousPatterns.Add("Empty artist name");
                confidence += 0.9;
            }
            else
            {
                var artist = recommendation.Artist;

                // Check for AI-generated sounding names
                if (HasAIGeneratedArtistNamePattern(artist))
                {
                    suspiciousPatterns.Add("Artist name follows AI generation pattern");
                    confidence += 0.6;
                }

                // Check for impossible character combinations
                if (HasImpossibleCharacterCombinations(artist))
                {
                    suspiciousPatterns.Add("Artist name has impossible character combinations");
                    confidence += 0.8;
                }

                // Check for overly generic patterns
                if (HasOverlyGenericPattern(artist))
                {
                    suspiciousPatterns.Add("Artist name is overly generic");
                    confidence += 0.4;
                }

                // Check for trademark/copyright symbols (unusual in artist names)
                if (Regex.IsMatch(artist, @"[™®©]"))
                {
                    suspiciousPatterns.Add("Artist name contains trademark/copyright symbols");
                    confidence += 0.7;
                }
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.NonExistentArtist,
                    Description = $"Suspicious artist name patterns detected",
                    Confidence = Math.Min(1.0, confidence / suspiciousPatterns.Count),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private async Task DetectNonExistentAlbumPatterns(Recommendation recommendation, HallucinationDetectionResult result)
        {
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            if (string.IsNullOrWhiteSpace(recommendation.Album))
            {
                suspiciousPatterns.Add("Empty album name");
                confidence += 0.9;
            }
            else
            {
                var album = recommendation.Album;

                // Check for AI hallucination patterns in album names
                if (HasAIHallucinationPatterns(album))
                {
                    suspiciousPatterns.Add("Album name contains AI hallucination patterns");
                    confidence += 0.7;
                }

                // Check for impossible album naming patterns
                if (HasImpossibleAlbumNaming(album))
                {
                    suspiciousPatterns.Add("Album name follows impossible naming pattern");
                    confidence += 0.8;
                }

                // Check for overly complex or nonsensical titles
                if (IsOverlyComplexTitle(album))
                {
                    suspiciousPatterns.Add("Album title is overly complex or nonsensical");
                    confidence += 0.6;
                }

                // Check for repetitive remaster patterns
                if (HasSuspiciousRemasterPattern(album))
                {
                    suspiciousPatterns.Add("Album has suspicious remaster/edition pattern");
                    confidence += 0.5;
                }
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.NonExistentAlbum,
                    Description = "Suspicious album name patterns detected",
                    Confidence = Math.Min(1.0, confidence / suspiciousPatterns.Count),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private async Task DetectImpossibleReleaseDates(Recommendation recommendation, HallucinationDetectionResult result)
        {
            if (!recommendation.Year.HasValue) return;

            var year = recommendation.Year.Value;
            var currentYear = DateTime.UtcNow.Year;
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            // Check for impossible years
            if (year < 1877) // Before Edison's phonograph
            {
                suspiciousPatterns.Add($"Release year {year} is before recorded music was invented");
                confidence = 0.95;
            }
            else if (year > currentYear + 3) // More than 3 years in future
            {
                suspiciousPatterns.Add($"Release year {year} is too far in the future");
                confidence = 0.8;
            }
            else if (year == 0 || year == 1 || year == 1900 || year == 2000)
            {
                suspiciousPatterns.Add($"Release year {year} appears to be a default/placeholder value");
                confidence = 0.7;
            }

            // Check for suspicious round numbers
            if (year % 100 == 0 && year != currentYear)
            {
                suspiciousPatterns.Add($"Release year {year} is suspiciously round");
                confidence += 0.3;
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.ImpossibleReleaseDate,
                    Description = "Impossible or suspicious release date detected",
                    Confidence = Math.Min(1.0, confidence),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private async Task DetectNamePatternAnomalies(Recommendation recommendation, HallucinationDetectionResult result)
        {
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            // Check artist name patterns
            if (!string.IsNullOrEmpty(recommendation.Artist))
            {
                var artist = recommendation.Artist;

                // Check for AI-typical patterns
                if (Regex.IsMatch(artist, @"^(The\s+)?[A-Z][a-z]+\s+(and\s+the\s+)?[A-Z][a-z]+s?$") && 
                    artist.Length > 20)
                {
                    suspiciousPatterns.Add("Artist name follows AI generation pattern");
                    confidence += 0.4;
                }

                // Check for impossible character sequences
                if (Regex.IsMatch(artist, @"[qwrtypsdfghjklzxcvbnm]{5,}", RegexOptions.IgnoreCase))
                {
                    suspiciousPatterns.Add("Artist name contains impossible character sequences");
                    confidence += 0.7;
                }
            }

            // Check album name patterns
            if (!string.IsNullOrEmpty(recommendation.Album))
            {
                var album = recommendation.Album;

                // Check for AI-typical album naming
                if (Regex.IsMatch(album, @"^(The\s+)?(Ultimate|Complete|Essential|Greatest|Best\s+of)\s+", RegexOptions.IgnoreCase))
                {
                    suspiciousPatterns.Add("Album name uses AI-typical superlatives");
                    confidence += 0.3;
                }

                // Check for nonsensical word combinations
                if (HasNonsensicalWordCombinations(album))
                {
                    suspiciousPatterns.Add("Album name contains nonsensical word combinations");
                    confidence += 0.6;
                }
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.NamePatternAnomalies,
                    Description = "Name pattern anomalies detected",
                    Confidence = Math.Min(1.0, confidence / suspiciousPatterns.Count),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private async Task DetectRepetitiveElements(Recommendation recommendation, HallucinationDetectionResult result)
        {
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            var combinedText = $"{recommendation.Artist} {recommendation.Album} {recommendation.Genre} {recommendation.Reason}";

            // Check for repeated words
            var words = combinedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var wordCounts = words.GroupBy(w => w.ToLower()).Where(g => g.Count() > 2).ToList();

            if (wordCounts.Any())
            {
                suspiciousPatterns.Add($"Repeated words found: {string.Join(", ", wordCounts.Select(g => g.Key))}");
                confidence += 0.4;
            }

            // Check for repeated character patterns
            if (Regex.IsMatch(combinedText, @"(.{3,})\1{2,}"))
            {
                suspiciousPatterns.Add("Repeated character patterns detected");
                confidence += 0.6;
            }

            // Check for self-referential loops (artist name in album name or vice versa)
            if (!string.IsNullOrEmpty(recommendation.Artist) && !string.IsNullOrEmpty(recommendation.Album))
            {
                if (recommendation.Album.Contains(recommendation.Artist, StringComparison.OrdinalIgnoreCase) &&
                    recommendation.Artist.Contains(recommendation.Album, StringComparison.OrdinalIgnoreCase))
                {
                    suspiciousPatterns.Add("Self-referential naming loop detected");
                    confidence += 0.8;
                }
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.RepetitiveElements,
                    Description = "Repetitive elements detected",
                    Confidence = Math.Min(1.0, confidence),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private async Task DetectSuspiciousCombinations(Recommendation recommendation, HallucinationDetectionResult result)
        {
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            // Check for suspicious album type combinations
            if (!string.IsNullOrEmpty(recommendation.Album))
            {
                var album = recommendation.Album.ToLower();

                // Contradictory album types
                var contradictions = new[]
                {
                    new[] { "live", "studio" },
                    new[] { "acoustic", "electric" },
                    new[] { "demo", "deluxe" },
                    new[] { "unplugged", "amplified" },
                    new[] { "greatest hits", "debut" }
                };

                foreach (var contradiction in contradictions)
                {
                    if (contradiction.All(term => album.Contains(term)))
                    {
                        suspiciousPatterns.Add($"Contradictory terms: {string.Join(" and ", contradiction)}");
                        confidence += 0.7;
                    }
                }

                // Impossible combinations
                if (Regex.IsMatch(album, @"\b(live)\b.*\b(studio recording)\b", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(album, @"\b(demo)\b.*\b(deluxe edition)\b", RegexOptions.IgnoreCase))
                {
                    suspiciousPatterns.Add("Impossible album type combination");
                    confidence += 0.8;
                }
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.SuspiciousCombinations,
                    Description = "Suspicious element combinations detected",
                    Confidence = Math.Min(1.0, confidence),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private async Task DetectTemporalInconsistencies(Recommendation recommendation, HallucinationDetectionResult result)
        {
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            if (!string.IsNullOrEmpty(recommendation.Album) && recommendation.Year.HasValue)
            {
                var album = recommendation.Album.ToLower();
                var year = recommendation.Year.Value;

                // Check for anachronistic terms
                var anachronisms = new Dictionary<string, int>
                {
                    {"digital", 1980},
                    {"cd", 1980},
                    {"remastered", 1970},
                    {"hdcd", 1990},
                    {"surround", 1970},
                    {"blu-ray", 2000},
                    {"vinyl", 1940},
                    {"cassette", 1960}
                };

                foreach (var anachronism in anachronisms)
                {
                    if (album.Contains(anachronism.Key) && year < anachronism.Value)
                    {
                        suspiciousPatterns.Add($"Album mentions '{anachronism.Key}' but year {year} predates technology");
                        confidence += 0.8;
                    }
                }

                // Check for future remaster dates
                if (Regex.IsMatch(album, @"\b(\d{4})\s*(remaster|edition)\b", RegexOptions.IgnoreCase))
                {
                    var match = Regex.Match(album, @"\b(\d{4})\s*(remaster|edition)\b", RegexOptions.IgnoreCase);
                    if (int.TryParse(match.Groups[1].Value, out var remasterYear))
                    {
                        if (remasterYear > DateTime.UtcNow.Year + 1)
                        {
                            suspiciousPatterns.Add($"Remaster year {remasterYear} is in the future");
                            confidence += 0.9;
                        }
                        else if (remasterYear < year)
                        {
                            suspiciousPatterns.Add($"Remaster year {remasterYear} predates original release {year}");
                            confidence += 0.8;
                        }
                    }
                }
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.TemporalInconsistencies,
                    Description = "Temporal inconsistencies detected",
                    Confidence = Math.Min(1.0, confidence / suspiciousPatterns.Count),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private async Task DetectFormatAnomalies(Recommendation recommendation, HallucinationDetectionResult result)
        {
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            // Check for unusual formatting
            var fields = new[] { recommendation.Artist, recommendation.Album, recommendation.Genre };
            
            foreach (var field in fields.Where(f => !string.IsNullOrEmpty(f)))
            {
                // Check for excessive punctuation
                if (Regex.Matches(field, @"[!@#$%^&*()+=\[\]{};:'""|\\<>?/~`]").Count > field.Length * 0.2)
                {
                    suspiciousPatterns.Add($"Excessive punctuation in: {field.Substring(0, Math.Min(20, field.Length))}...");
                    confidence += 0.6;
                }

                // Check for unusual capitalization
                if (field.All(char.IsUpper) && field.Length > 10)
                {
                    suspiciousPatterns.Add("All caps text (unusual for music metadata)");
                    confidence += 0.4;
                }

                // Check for encoding issues
                if (field.Contains("�") || Regex.IsMatch(field, @"[\x00-\x1F\x7F-\x9F]"))
                {
                    suspiciousPatterns.Add("Text encoding issues detected");
                    confidence += 0.8;
                }
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.FormatAnomalies,
                    Description = "Format anomalies detected",
                    Confidence = Math.Min(1.0, confidence / suspiciousPatterns.Count),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private async Task DetectLanguagePatterns(Recommendation recommendation, HallucinationDetectionResult result)
        {
            var suspiciousPatterns = new List<string>();
            var confidence = 0.0;

            var allText = $"{recommendation.Artist} {recommendation.Album} {recommendation.Reason}";

            // Check for AI-typical language patterns
            var aiPhrases = new[]
            {
                "as an ai", "i don't have", "i cannot", "i'm sorry", "unfortunately",
                "based on my", "in my opinion", "i think", "i believe", "it seems"
            };

            foreach (var phrase in aiPhrases)
            {
                if (allText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    suspiciousPatterns.Add($"AI language pattern detected: '{phrase}'");
                    confidence += 0.9;
                }
            }

            // Check for overly formal language in casual contexts
            var formalPatterns = new[]
            {
                @"\bthus\b", @"\btherefore\b", @"\bfurthermore\b", @"\bmoreover\b", 
                @"\bconsequently\b", @"\bnevertheless\b"
            };

            foreach (var pattern in formalPatterns)
            {
                if (Regex.IsMatch(allText, pattern, RegexOptions.IgnoreCase))
                {
                    suspiciousPatterns.Add("Overly formal language detected");
                    confidence += 0.3;
                    break; // Only count once
                }
            }

            if (suspiciousPatterns.Any())
            {
                result.DetectedPatterns.Add(new HallucinationPattern
                {
                    PatternType = HallucinationPatternType.LanguagePatterns,
                    Description = "Suspicious language patterns detected",
                    Confidence = Math.Min(1.0, confidence / suspiciousPatterns.Count),
                    Evidence = suspiciousPatterns
                });
            }
        }

        private void CalculateOverallConfidence(HallucinationDetectionResult result)
        {
            if (!result.DetectedPatterns.Any())
            {
                result.HallucinationConfidence = 0.0;
                return;
            }

            // Weighted average of pattern confidences
            var weights = new Dictionary<HallucinationPatternType, double>
            {
                [HallucinationPatternType.NonExistentArtist] = 1.0,
                [HallucinationPatternType.NonExistentAlbum] = 1.0,
                [HallucinationPatternType.ImpossibleReleaseDate] = 0.9,
                [HallucinationPatternType.TemporalInconsistencies] = 0.8,
                [HallucinationPatternType.SuspiciousCombinations] = 0.7,
                [HallucinationPatternType.NamePatternAnomalies] = 0.6,
                [HallucinationPatternType.FormatAnomalies] = 0.5,
                [HallucinationPatternType.RepetitiveElements] = 0.4,
                [HallucinationPatternType.LanguagePatterns] = 0.3
            };

            var weightedSum = result.DetectedPatterns.Sum(pattern => 
                pattern.Confidence * (weights.ContainsKey(pattern.PatternType) ? weights[pattern.PatternType] : 0.5));
            var totalWeight = result.DetectedPatterns.Sum(pattern => 
                weights.ContainsKey(pattern.PatternType) ? weights[pattern.PatternType] : 0.5);

            result.HallucinationConfidence = Math.Min(1.0, weightedSum / totalWeight);
        }

        // Helper methods for pattern detection
        private bool HasAIGeneratedArtistNamePattern(string name)
        {
            // Patterns common in AI-generated artist names
            return Regex.IsMatch(name, @"^(The\s+)?[A-Z][a-z]+(ing|ed|er|ly)\s+[A-Z][a-z]+s?$") ||
                   Regex.IsMatch(name, @"^[A-Z][a-z]+\s+(of|and|the)\s+(the\s+)?[A-Z][a-z]+s?$") ||
                   name.Contains("Band") && name.Contains("The");
        }

        private bool HasImpossibleCharacterCombinations(string text)
        {
            // Check for impossible consonant clusters
            return Regex.IsMatch(text, @"[bcdfghjklmnpqrstvwxz]{5,}", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(text, @"[aeiou]{4,}", RegexOptions.IgnoreCase);
        }

        private bool HasOverlyGenericPattern(string name)
        {
            var genericWords = new[] { "music", "band", "group", "artist", "singer", "player" };
            return genericWords.Count(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)) >= 2;
        }

        private bool HasAIHallucinationPatterns(string album)
        {
            // Common AI hallucination patterns in album names
            var patterns = new[]
            {
                @"\b(Album|Record|Music)\s+(Number|Vol\.?|#)\s*\d+\b",
                @"\b(The\s+)?(Best|Greatest|Ultimate|Complete)\s+(of|Collection|Hits)\b",
                @"\b(Untitled|Unknown|Various)\s+(Album|Record)\b",
                @"\b(Live\s+at\s+)?(The\s+)?[A-Z][a-z]+\s+(Arena|Stadium|Center|Hall)\b.*\d{4}\b"
            };

            return patterns.Any(pattern => Regex.IsMatch(album, pattern, RegexOptions.IgnoreCase));
        }

        private bool HasImpossibleAlbumNaming(string album)
        {
            // Check for patterns that don't make sense in real album names
            return album.Count(c => c == '(') != album.Count(c => c == ')') ||
                   album.Count(c => c == '[') != album.Count(c => c == ']') ||
                   Regex.IsMatch(album, @"\(\s*\)|\[\s*\]") || // Empty parentheses/brackets
                   album.Contains("  "); // Double spaces
        }

        private bool IsOverlyComplexTitle(string title)
        {
            // Check for overly complex titles that might be AI-generated
            var wordCount = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var punctuationCount = title.Count(c => !char.IsLetterOrDigit(c) && c != ' ');
            
            return wordCount > 15 || punctuationCount > wordCount * 0.3;
        }

        private bool HasSuspiciousRemasterPattern(string album)
        {
            // Multiple remaster indicators (suspicious)
            var remasterCount = Regex.Matches(album, @"\b(remaster|edition|version|mix)\b", RegexOptions.IgnoreCase).Count;
            return remasterCount > 2;
        }

        private bool HasNonsensicalWordCombinations(string text)
        {
            // Check for word combinations that don't make linguistic sense
            var nonsensicalPatterns = new[]
            {
                @"\b(Silent|Quiet)\s+(Noise|Sound|Music)\b",
                @"\b(Invisible|Hidden)\s+(Light|Color)\b",
                @"\b(Frozen|Cold)\s+(Fire|Heat)\b"
            };

            return nonsensicalPatterns.Any(pattern => 
                Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
        }

        private Dictionary<HallucinationPatternType, List<HallucinationPattern>> InitializeHallucinationPatterns()
        {
            // Initialize known hallucination patterns (can be expanded)
            return new Dictionary<HallucinationPatternType, List<HallucinationPattern>>();
        }
    }
}
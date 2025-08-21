using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Rules;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Engines
{
    /// <summary>
    /// Calculates confidence scores for AI recommendations based on multiple factors
    /// </summary>
    public interface IConfidenceCalculator
    {
        Task<ConfidenceResult> CalculateConfidenceAsync(
            Recommendation recommendation,
            PatternMatchResults patternResults,
            ValidationRuleResults ruleResults);
    }

    public class ConfidenceCalculator : IConfidenceCalculator
    {
        private readonly Logger _logger;
        private readonly IConfidenceWeightConfiguration _weightConfig;

        public ConfidenceCalculator(Logger logger, IConfidenceWeightConfiguration weightConfig)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _weightConfig = weightConfig ?? throw new ArgumentNullException(nameof(weightConfig));
        }

        public async Task<ConfidenceResult> CalculateConfidenceAsync(
            Recommendation recommendation,
            PatternMatchResults patternResults,
            ValidationRuleResults ruleResults)
        {
            var result = new ConfidenceResult();
            var factors = new List<ConfidenceFactor>();

            // Factor 1: Pattern matching score
            var patternFactor = CalculatePatternFactor(patternResults);
            factors.Add(patternFactor);

            // Factor 2: Validation rule compliance
            var ruleFactor = CalculateRuleFactor(ruleResults);
            factors.Add(ruleFactor);

            // Factor 3: Data completeness
            var completenessFactor = CalculateCompletenessFactor(recommendation);
            factors.Add(completenessFactor);

            // Factor 4: Metadata consistency
            var consistencyFactor = await CalculateConsistencyFactor(recommendation);
            factors.Add(consistencyFactor);

            // Factor 5: Provider confidence (if available)
            var providerFactor = CalculateProviderConfidenceFactor(recommendation);
            if (providerFactor != null)
            {
                factors.Add(providerFactor);
            }

            // Calculate weighted average
            result.Factors = factors;
            result.Score = CalculateWeightedScore(factors);
            result.Category = CategorizeConfidence(result.Score);
            result.Recommendation = GenerateRecommendation(result);

            _logger.Debug($"Confidence calculation complete: Score={result.Score:P}, Category={result.Category}");

            return result;
        }

        private ConfidenceFactor CalculatePatternFactor(PatternMatchResults patternResults)
        {
            return new ConfidenceFactor
            {
                Name = "PatternMatching",
                Score = patternResults?.MatchScore ?? 0.5,
                Weight = _weightConfig.PatternWeight,
                Description = $"Pattern analysis score based on {patternResults?.TotalPatternsChecked ?? 0} patterns"
            };
        }

        private ConfidenceFactor CalculateRuleFactor(ValidationRuleResults ruleResults)
        {
            if (ruleResults == null || ruleResults.TotalRules == 0)
            {
                return new ConfidenceFactor
                {
                    Name = "ValidationRules",
                    Score = 0.5,
                    Weight = _weightConfig.RuleWeight,
                    Description = "No validation rules applied"
                };
            }

            var passRate = (double)ruleResults.PassedRules / ruleResults.TotalRules;
            
            return new ConfidenceFactor
            {
                Name = "ValidationRules",
                Score = passRate,
                Weight = _weightConfig.RuleWeight,
                Description = $"Passed {ruleResults.PassedRules}/{ruleResults.TotalRules} validation rules"
            };
        }

        private ConfidenceFactor CalculateCompletenessFactor(Recommendation recommendation)
        {
            var completenessScore = 0.0;
            var fieldCount = 0;

            // Required fields
            if (!string.IsNullOrWhiteSpace(recommendation.Artist))
            {
                completenessScore += 0.35;
                fieldCount++;
            }

            if (!string.IsNullOrWhiteSpace(recommendation.Album))
            {
                completenessScore += 0.35;
                fieldCount++;
            }

            // Optional but important fields
            if (!string.IsNullOrWhiteSpace(recommendation.Genre))
            {
                completenessScore += 0.1;
                fieldCount++;
            }

            if (recommendation.Year.HasValue && recommendation.Year > 0)
            {
                completenessScore += 0.1;
                fieldCount++;
            }

            if (!string.IsNullOrWhiteSpace(recommendation.Reason))
            {
                completenessScore += 0.1;
                fieldCount++;
            }

            return new ConfidenceFactor
            {
                Name = "DataCompleteness",
                Score = Math.Min(1.0, completenessScore),
                Weight = _weightConfig.CompletenessWeight,
                Description = $"{fieldCount}/5 fields populated"
            };
        }

        private async Task<ConfidenceFactor> CalculateConsistencyFactor(Recommendation recommendation)
        {
            var consistencyScore = 1.0;
            var issues = new List<string>();

            // Check year consistency
            if (recommendation.Year.HasValue)
            {
                var currentYear = DateTime.UtcNow.Year;
                if (recommendation.Year > currentYear + 1)
                {
                    consistencyScore -= 0.3;
                    issues.Add("Future release date");
                }
                else if (recommendation.Year < 1900)
                {
                    consistencyScore -= 0.2;
                    issues.Add("Very old release date");
                }
            }

            // Check for mixed languages or scripts
            if (ContainsMixedScripts(recommendation.Artist) || ContainsMixedScripts(recommendation.Album))
            {
                consistencyScore -= 0.1;
                issues.Add("Mixed character scripts");
            }

            // Check genre consistency
            if (!string.IsNullOrWhiteSpace(recommendation.Genre))
            {
                if (IsGenreInconsistent(recommendation.Genre))
                {
                    consistencyScore -= 0.2;
                    issues.Add("Inconsistent genre");
                }
            }

            return new ConfidenceFactor
            {
                Name = "MetadataConsistency",
                Score = Math.Max(0, consistencyScore),
                Weight = _weightConfig.ConsistencyWeight,
                Description = issues.Any() ? string.Join(", ", issues) : "All metadata consistent"
            };
        }

        private ConfidenceFactor CalculateProviderConfidenceFactor(Recommendation recommendation)
        {
            if (!recommendation.Confidence.HasValue)
                return null;

            return new ConfidenceFactor
            {
                Name = "ProviderConfidence",
                Score = recommendation.Confidence.Value,
                Weight = _weightConfig.ProviderConfidenceWeight,
                Description = $"AI provider confidence: {recommendation.Confidence.Value:P}"
            };
        }

        private double CalculateWeightedScore(List<ConfidenceFactor> factors)
        {
            var totalWeight = factors.Sum(f => f.Weight);
            if (totalWeight == 0)
                return 0.5;

            return factors.Sum(f => f.Score * f.Weight) / totalWeight;
        }

        private ConfidenceCategory CategorizeConfidence(double score)
        {
            return score switch
            {
                >= 0.9 => ConfidenceCategory.VeryHigh,
                >= 0.75 => ConfidenceCategory.High,
                >= 0.5 => ConfidenceCategory.Medium,
                >= 0.25 => ConfidenceCategory.Low,
                _ => ConfidenceCategory.VeryLow
            };
        }

        private string GenerateRecommendation(ConfidenceResult result)
        {
            return result.Category switch
            {
                ConfidenceCategory.VeryHigh => "Highly recommended - very likely to be accurate",
                ConfidenceCategory.High => "Recommended - likely to be accurate",
                ConfidenceCategory.Medium => "Review recommended - moderate confidence",
                ConfidenceCategory.Low => "Manual verification required - low confidence",
                ConfidenceCategory.VeryLow => "Not recommended - likely hallucination",
                _ => "Unable to determine confidence"
            };
        }

        private bool ContainsMixedScripts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var hasLatin = text.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
            var hasCyrillic = text.Any(c => (c >= '\u0400' && c <= '\u04FF'));
            var hasArabic = text.Any(c => (c >= '\u0600' && c <= '\u06FF'));
            var hasCJK = text.Any(c => (c >= '\u4E00' && c <= '\u9FFF') || 
                                      (c >= '\u3040' && c <= '\u309F') || 
                                      (c >= '\u30A0' && c <= '\u30FF'));

            var scriptCount = new[] { hasLatin, hasCyrillic, hasArabic, hasCJK }.Count(x => x);
            return scriptCount > 1;
        }

        private bool IsGenreInconsistent(string genre)
        {
            var suspiciousGenres = new[]
            {
                "test", "unknown", "various", "mixed", "undefined", "null", "none"
            };

            return suspiciousGenres.Any(s => genre.ToLowerInvariant().Contains(s));
        }
    }

    public class ConfidenceResult
    {
        public double Score { get; set; }
        public ConfidenceCategory Category { get; set; }
        public List<ConfidenceFactor> Factors { get; set; }
        public string Recommendation { get; set; }
    }

    public class ConfidenceFactor
    {
        public string Name { get; set; }
        public double Score { get; set; }
        public double Weight { get; set; }
        public string Description { get; set; }
    }

    public enum ConfidenceCategory
    {
        VeryLow,
        Low,
        Medium,
        High,
        VeryHigh
    }

    public interface IConfidenceWeightConfiguration
    {
        double PatternWeight { get; }
        double RuleWeight { get; }
        double CompletenessWeight { get; }
        double ConsistencyWeight { get; }
        double ProviderConfidenceWeight { get; }
    }

    public class DefaultConfidenceWeightConfiguration : IConfidenceWeightConfiguration
    {
        public double PatternWeight => 0.3;
        public double RuleWeight => 0.25;
        public double CompletenessWeight => 0.2;
        public double ConsistencyWeight => 0.15;
        public double ProviderConfidenceWeight => 0.1;
    }
}
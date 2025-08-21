using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Rules
{
    /// <summary>
    /// Manages and executes validation rules for recommendations
    /// </summary>
    public interface IValidationRuleSet
    {
        Task<ValidationRuleResults> ValidateAsync(Recommendation recommendation);
        void RegisterRule(IValidationRule rule);
    }

    public class ValidationRuleSet : IValidationRuleSet
    {
        private readonly Logger _logger;
        private readonly List<IValidationRule> _rules;

        public ValidationRuleSet(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rules = new List<IValidationRule>();
            InitializeDefaultRules();
        }

        public async Task<ValidationRuleResults> ValidateAsync(Recommendation recommendation)
        {
            var results = new ValidationRuleResults
            {
                Recommendation = recommendation,
                ValidationTime = DateTime.UtcNow
            };

            foreach (var rule in _rules)
            {
                try
                {
                    var ruleResult = await rule.ValidateAsync(recommendation);
                    results.RuleResults.Add(ruleResult);

                    if (ruleResult.Passed)
                    {
                        results.PassedRules++;
                    }
                    else
                    {
                        results.FailedRules++;
                        results.Violations.Add(new RuleViolation
                        {
                            RuleName = rule.Name,
                            Severity = rule.Severity,
                            Message = ruleResult.Message,
                            Field = ruleResult.Field
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error executing validation rule {rule.Name}");
                    results.Errors.Add($"{rule.Name}: {ex.Message}");
                }
            }

            results.TotalRules = _rules.Count;
            results.IsValid = results.FailedRules == 0;
            results.ValidationScore = results.TotalRules > 0 
                ? (double)results.PassedRules / results.TotalRules 
                : 0;

            return results;
        }

        public void RegisterRule(IValidationRule rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            _rules.Add(rule);
            _logger.Debug($"Registered validation rule: {rule.Name}");
        }

        private void InitializeDefaultRules()
        {
            // Required field rules
            RegisterRule(new RequiredFieldRule("ArtistRequired", "Artist", RuleSeverity.Critical));
            RegisterRule(new RequiredFieldRule("AlbumRequired", "Album", RuleSeverity.Critical));

            // Format validation rules
            RegisterRule(new StringLengthRule("ArtistLength", "Artist", 1, 200, RuleSeverity.High));
            RegisterRule(new StringLengthRule("AlbumLength", "Album", 1, 300, RuleSeverity.High));

            // Year validation rules
            RegisterRule(new YearRangeRule("ValidYear", 1877, DateTime.UtcNow.Year + 1, RuleSeverity.Medium));

            // Character validation rules
            RegisterRule(new CharacterValidationRule("ArtistCharacters", "Artist", RuleSeverity.Medium));
            RegisterRule(new CharacterValidationRule("AlbumCharacters", "Album", RuleSeverity.Medium));

            // Duplicate detection rule
            RegisterRule(new DuplicateDetectionRule("NoDuplicates", RuleSeverity.Low));

            // Genre validation rule
            RegisterRule(new GenreValidationRule("ValidGenre", RuleSeverity.Low));
        }
    }

    /// <summary>
    /// Base interface for all validation rules
    /// </summary>
    public interface IValidationRule
    {
        string Name { get; }
        RuleSeverity Severity { get; }
        Task<RuleResult> ValidateAsync(Recommendation recommendation);
    }

    /// <summary>
    /// Validates required fields are present
    /// </summary>
    public class RequiredFieldRule : IValidationRule
    {
        public string Name { get; }
        public RuleSeverity Severity { get; }
        private readonly string _fieldName;

        public RequiredFieldRule(string name, string fieldName, RuleSeverity severity)
        {
            Name = name;
            _fieldName = fieldName;
            Severity = severity;
        }

        public Task<RuleResult> ValidateAsync(Recommendation recommendation)
        {
            var value = _fieldName switch
            {
                "Artist" => recommendation.Artist,
                "Album" => recommendation.Album,
                "Genre" => recommendation.Genre,
                _ => null
            };

            var passed = !string.IsNullOrWhiteSpace(value);

            return Task.FromResult(new RuleResult
            {
                RuleName = Name,
                Passed = passed,
                Field = _fieldName,
                Message = passed ? null : $"{_fieldName} is required"
            });
        }
    }

    /// <summary>
    /// Validates string length constraints
    /// </summary>
    public class StringLengthRule : IValidationRule
    {
        public string Name { get; }
        public RuleSeverity Severity { get; }
        private readonly string _fieldName;
        private readonly int _minLength;
        private readonly int _maxLength;

        public StringLengthRule(string name, string fieldName, int minLength, int maxLength, RuleSeverity severity)
        {
            Name = name;
            _fieldName = fieldName;
            _minLength = minLength;
            _maxLength = maxLength;
            Severity = severity;
        }

        public Task<RuleResult> ValidateAsync(Recommendation recommendation)
        {
            var value = _fieldName switch
            {
                "Artist" => recommendation.Artist,
                "Album" => recommendation.Album,
                _ => null
            };

            if (string.IsNullOrEmpty(value))
            {
                return Task.FromResult(new RuleResult
                {
                    RuleName = Name,
                    Passed = true,
                    Field = _fieldName
                });
            }

            var length = value.Length;
            var passed = length >= _minLength && length <= _maxLength;

            return Task.FromResult(new RuleResult
            {
                RuleName = Name,
                Passed = passed,
                Field = _fieldName,
                Message = passed ? null : $"{_fieldName} length must be between {_minLength} and {_maxLength} characters"
            });
        }
    }

    /// <summary>
    /// Validates year is within acceptable range
    /// </summary>
    public class YearRangeRule : IValidationRule
    {
        public string Name { get; }
        public RuleSeverity Severity { get; }
        private readonly int _minYear;
        private readonly int _maxYear;

        public YearRangeRule(string name, int minYear, int maxYear, RuleSeverity severity)
        {
            Name = name;
            _minYear = minYear;
            _maxYear = maxYear;
            Severity = severity;
        }

        public Task<RuleResult> ValidateAsync(Recommendation recommendation)
        {
            if (!recommendation.Year.HasValue)
            {
                return Task.FromResult(new RuleResult
                {
                    RuleName = Name,
                    Passed = true,
                    Field = "Year"
                });
            }

            var passed = recommendation.Year >= _minYear && recommendation.Year <= _maxYear;

            return Task.FromResult(new RuleResult
            {
                RuleName = Name,
                Passed = passed,
                Field = "Year",
                Message = passed ? null : $"Year {recommendation.Year} is outside valid range ({_minYear}-{_maxYear})"
            });
        }
    }

    /// <summary>
    /// Validates characters in text fields
    /// </summary>
    public class CharacterValidationRule : IValidationRule
    {
        public string Name { get; }
        public RuleSeverity Severity { get; }
        private readonly string _fieldName;

        public CharacterValidationRule(string name, string fieldName, RuleSeverity severity)
        {
            Name = name;
            _fieldName = fieldName;
            Severity = severity;
        }

        public Task<RuleResult> ValidateAsync(Recommendation recommendation)
        {
            var value = _fieldName switch
            {
                "Artist" => recommendation.Artist,
                "Album" => recommendation.Album,
                _ => null
            };

            if (string.IsNullOrEmpty(value))
            {
                return Task.FromResult(new RuleResult
                {
                    RuleName = Name,
                    Passed = true,
                    Field = _fieldName
                });
            }

            // Check for suspicious characters
            var suspiciousChars = new[] { '\0', '\x01', '\x02', '\x03', '\x04' };
            var passed = !value.Any(c => suspiciousChars.Contains(c));

            return Task.FromResult(new RuleResult
            {
                RuleName = Name,
                Passed = passed,
                Field = _fieldName,
                Message = passed ? null : $"{_fieldName} contains invalid characters"
            });
        }
    }

    /// <summary>
    /// Detects duplicate artist/album combinations
    /// </summary>
    public class DuplicateDetectionRule : IValidationRule
    {
        public string Name { get; }
        public RuleSeverity Severity { get; }
        private static readonly HashSet<string> _seenCombinations = new HashSet<string>();

        public DuplicateDetectionRule(string name, RuleSeverity severity)
        {
            Name = name;
            Severity = severity;
        }

        public Task<RuleResult> ValidateAsync(Recommendation recommendation)
        {
            var key = $"{recommendation.Artist?.ToLowerInvariant()}|{recommendation.Album?.ToLowerInvariant()}";
            var isDuplicate = _seenCombinations.Contains(key);
            
            if (!isDuplicate)
            {
                _seenCombinations.Add(key);
            }

            return Task.FromResult(new RuleResult
            {
                RuleName = Name,
                Passed = !isDuplicate,
                Field = "Artist/Album",
                Message = isDuplicate ? "Duplicate recommendation detected" : null
            });
        }
    }

    /// <summary>
    /// Validates genre values
    /// </summary>
    public class GenreValidationRule : IValidationRule
    {
        public string Name { get; }
        public RuleSeverity Severity { get; }
        private static readonly HashSet<string> _invalidGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "test", "unknown", "null", "undefined", "none", "n/a", "tbd"
        };

        public GenreValidationRule(string name, RuleSeverity severity)
        {
            Name = name;
            Severity = severity;
        }

        public Task<RuleResult> ValidateAsync(Recommendation recommendation)
        {
            if (string.IsNullOrWhiteSpace(recommendation.Genre))
            {
                return Task.FromResult(new RuleResult
                {
                    RuleName = Name,
                    Passed = true,
                    Field = "Genre"
                });
            }

            var passed = !_invalidGenres.Contains(recommendation.Genre);

            return Task.FromResult(new RuleResult
            {
                RuleName = Name,
                Passed = passed,
                Field = "Genre",
                Message = passed ? null : $"Invalid genre: {recommendation.Genre}"
            });
        }
    }

    public class ValidationRuleResults
    {
        public Recommendation Recommendation { get; set; }
        public List<RuleResult> RuleResults { get; set; } = new List<RuleResult>();
        public List<RuleViolation> Violations { get; set; } = new List<RuleViolation>();
        public List<string> Errors { get; set; } = new List<string>();
        public int TotalRules { get; set; }
        public int PassedRules { get; set; }
        public int FailedRules { get; set; }
        public bool IsValid { get; set; }
        public double ValidationScore { get; set; }
        public DateTime ValidationTime { get; set; }
    }

    public class RuleResult
    {
        public string RuleName { get; set; }
        public bool Passed { get; set; }
        public string Field { get; set; }
        public string Message { get; set; }
    }

    public class RuleViolation
    {
        public string RuleName { get; set; }
        public RuleSeverity Severity { get; set; }
        public string Message { get; set; }
        public string Field { get; set; }
    }

    public enum RuleSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Engines;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation.Patterns
{
    /// <summary>
    /// Repository for hallucination detection patterns
    /// </summary>
    public interface IHallucinationPatternRepository
    {
        Task<IEnumerable<HallucinationPattern>> GetArtistPatternsAsync();
        Task<IEnumerable<HallucinationPattern>> GetAlbumPatternsAsync();
        Task<IEnumerable<HallucinationPattern>> GetAllPatternsAsync();
    }

    public class HallucinationPatternRepository : IHallucinationPatternRepository
    {
        private readonly Logger _logger;
        private readonly List<HallucinationPattern> _artistPatterns;
        private readonly List<HallucinationPattern> _albumPatterns;

        public HallucinationPatternRepository(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _artistPatterns = InitializeArtistPatterns();
            _albumPatterns = InitializeAlbumPatterns();
        }

        public Task<IEnumerable<HallucinationPattern>> GetArtistPatternsAsync()
        {
            return Task.FromResult(_artistPatterns.AsEnumerable());
        }

        public Task<IEnumerable<HallucinationPattern>> GetAlbumPatternsAsync()
        {
            return Task.FromResult(_albumPatterns.AsEnumerable());
        }

        public Task<IEnumerable<HallucinationPattern>> GetAllPatternsAsync()
        {
            var allPatterns = _artistPatterns.Concat(_albumPatterns);
            return Task.FromResult(allPatterns);
        }

        private List<HallucinationPattern> InitializeArtistPatterns()
        {
            return new List<HallucinationPattern>
            {
                // Generic placeholder patterns
                new HallucinationPattern
                {
                    Name = "GenericArtist",
                    Expression = @"^(Artist\s*\d*|Band\s*\d*|Group\s*\d*|Unknown\s*Artist)$",
                    Description = "Generic artist placeholder",
                    Severity = PatternSeverity.High
                },

                // Numbered patterns (AI often generates these)
                new HallucinationPattern
                {
                    Name = "NumberedArtist",
                    Expression = @"^(Artist|Band|Group|Singer)\s+#?\d+$",
                    Description = "Numbered artist name",
                    Severity = PatternSeverity.High
                },

                // Test/Example patterns
                new HallucinationPattern
                {
                    Name = "TestArtist",
                    Expression = @"(?i)(test|example|sample|demo|temp|dummy)",
                    Description = "Test or example artist name",
                    Severity = PatternSeverity.Critical
                },

                // Random character sequences
                new HallucinationPattern
                {
                    Name = "RandomCharacters",
                    Expression = @"^[A-Z]{8,}$|^[a-z]{15,}$",
                    Description = "Random character sequence",
                    Severity = PatternSeverity.Medium
                },

                // Repeated words
                new HallucinationPattern
                {
                    Name = "RepeatedWords",
                    Expression = @"^(\w+)\s+\1(\s+\1)+$",
                    Description = "Repeated word pattern",
                    Severity = PatternSeverity.Medium
                },

                // UUID/GUID patterns
                new HallucinationPattern
                {
                    Name = "UUIDPattern",
                    Expression = @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
                    Description = "Contains UUID/GUID",
                    Severity = PatternSeverity.High
                },

                // Lorem ipsum patterns
                new HallucinationPattern
                {
                    Name = "LoremIpsum",
                    Expression = @"(?i)(lorem|ipsum|dolor|sit|amet)",
                    Description = "Lorem ipsum placeholder text",
                    Severity = PatternSeverity.Critical
                },

                // Single letter/number patterns
                new HallucinationPattern
                {
                    Name = "SingleCharacter",
                    Expression = @"^[A-Z]$|^\d$",
                    Description = "Single character artist name",
                    Severity = PatternSeverity.High
                }
            };
        }

        private List<HallucinationPattern> InitializeAlbumPatterns()
        {
            return new List<HallucinationPattern>
            {
                // Generic album patterns
                new HallucinationPattern
                {
                    Name = "GenericAlbum",
                    Expression = @"^(Album\s*\d*|Record\s*\d*|Release\s*\d*|Untitled\s*\d*)$",
                    Description = "Generic album placeholder",
                    Severity = PatternSeverity.High
                },

                // Self-titled pattern (when exactly matches artist)
                new HallucinationPattern
                {
                    Name = "SelfTitledDuplicate",
                    Expression = @"^Self[\s-]?Titled$",
                    Description = "Generic self-titled album",
                    Severity = PatternSeverity.Low
                },

                // Volume/Part patterns without proper names
                new HallucinationPattern
                {
                    Name = "GenericVolume",
                    Expression = @"^(Volume|Vol\.?|Part|Pt\.?)\s+\d+$",
                    Description = "Generic volume/part naming",
                    Severity = PatternSeverity.Medium
                },

                // Date-only albums
                new HallucinationPattern
                {
                    Name = "DateOnlyAlbum",
                    Expression = @"^\d{4}(-\d{2}){0,2}$",
                    Description = "Date-only album name",
                    Severity = PatternSeverity.Medium
                },

                // Compilation placeholders
                new HallucinationPattern
                {
                    Name = "GenericCompilation",
                    Expression = @"^(Various|Compilation|Collection|Best\s+Of|Greatest\s+Hits)$",
                    Description = "Generic compilation name",
                    Severity = PatternSeverity.Low
                },

                // Track listing patterns
                new HallucinationPattern
                {
                    Name = "TrackListing",
                    Expression = @"^Track\s+\d+(-\d+)?$",
                    Description = "Track number as album name",
                    Severity = PatternSeverity.High
                },

                // Placeholder brackets
                new HallucinationPattern
                {
                    Name = "PlaceholderBrackets",
                    Expression = @"\[(TBD|TBA|Unknown|Insert|Name|Title)\]",
                    Description = "Placeholder text in brackets",
                    Severity = PatternSeverity.Critical
                },

                // Encoding artifacts
                new HallucinationPattern
                {
                    Name = "EncodingArtifact",
                    Expression = @"(&#\d+;|\\u[0-9a-f]{4}|%[0-9a-f]{2})",
                    Description = "Contains encoding artifacts",
                    Severity = PatternSeverity.High
                }
            };
        }
    }

    /// <summary>
    /// Represents a pattern used to detect hallucinations
    /// </summary>
    public class HallucinationPattern
    {
        private Regex _regex;

        public string Name { get; set; }
        public string Expression { get; set; }
        public string Description { get; set; }
        public PatternSeverity Severity { get; set; }

        public bool Matches(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (_regex == null)
            {
                _regex = new Regex(Expression, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }

            return _regex.IsMatch(input);
        }
    }
}
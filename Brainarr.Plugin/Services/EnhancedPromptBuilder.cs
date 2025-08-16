using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Enhanced prompt builder with music DJ expert persona and advanced techniques for 95%+ accuracy
    /// </summary>
    public class EnhancedPromptBuilder
    {
        private readonly Logger _logger;

        public EnhancedPromptBuilder(Logger logger)
        {
            _logger = logger;
        }

        public string BuildDJExpertPrompt(
            LibraryProfile profile,
            LibrarySample sample,
            BrainarrSettings settings)
        {
            var promptBuilder = new StringBuilder();

            // EXPERT PERSONA - Critical for context setting
            promptBuilder.AppendLine("🎧 **EXPERT MUSIC DJ & MUSIC DISCOVERY SPECIALIST**");
            promptBuilder.AppendLine("You are a world-renowned music DJ and discovery expert with:");
            promptBuilder.AppendLine("• 25+ years of professional DJ experience across all genres");
            promptBuilder.AppendLine("• Deep knowledge of music history, subgenres, and artist connections");
            promptBuilder.AppendLine("• Expertise in reading musical DNA and finding perfect recommendations");
            promptBuilder.AppendLine("• Ability to understand the subtle connections between artists and sounds");
            promptBuilder.AppendLine("• Master of discovering hidden gems and overlooked masterpieces");
            promptBuilder.AppendLine();

            // MISSION STATEMENT
            promptBuilder.AppendLine("🎯 **YOUR MISSION:**");
            promptBuilder.AppendLine($"Analyze this music collection and recommend EXACTLY {settings.MaxRecommendations} perfect matches.");
            promptBuilder.AppendLine("Your recommendations must be:");
            promptBuilder.AppendLine("✓ Absolutely NOT already in the collection");
            promptBuilder.AppendLine("✓ Perfect fits for the listener's established taste");
            promptBuilder.AppendLine("✓ Diverse enough to expand their musical horizons");
            promptBuilder.AppendLine("✓ High-quality artists/albums (no obscure demos or poor quality)");
            promptBuilder.AppendLine();

            // ENHANCED LIBRARY ANALYSIS
            AddDetailedLibraryAnalysis(promptBuilder, profile, sample, settings);

            // MUSICAL DNA PROFILING
            AddMusicalDNAProfile(promptBuilder, profile, settings);

            // USER PREFERENCES WITH CONTEXT
            AddUserPreferencesWithContext(promptBuilder, settings, profile);

            // EXISTING COLLECTION (BLACKLIST)
            AddExistingCollectionBlacklist(promptBuilder, sample);

            // DISCOVERY STRATEGY BASED ON MODE
            AddDiscoveryStrategy(promptBuilder, settings, profile);

            // OUTPUT FORMAT WITH EXAMPLES
            AddOutputFormatWithExamples(promptBuilder, settings);

            // FINAL VALIDATION CHECKLIST
            AddValidationChecklist(promptBuilder, settings);

            return promptBuilder.ToString();
        }

        private void AddDetailedLibraryAnalysis(StringBuilder prompt, LibraryProfile profile, LibrarySample sample, BrainarrSettings settings)
        {
            prompt.AppendLine("📊 **DETAILED LIBRARY ANALYSIS:**");
            prompt.AppendLine($"Collection Size: {profile.TotalArtists} artists, {profile.TotalAlbums} albums");

            // Genre Distribution with Percentages
            var totalGenreCount = profile.TopGenres.Sum(g => g.Value);
            prompt.AppendLine("🎵 Genre Distribution:");
            foreach (var genre in profile.TopGenres.Take(8))
            {
                var percentage = (double)genre.Value / totalGenreCount * 100;
                prompt.AppendLine($"  • {genre.Key}: {percentage:F1}% ({genre.Value} albums)");
            }

            // Collection Characteristics
            prompt.AppendLine();
            prompt.AppendLine("🔍 Collection Characteristics:");

            if (profile.TotalArtists < 50)
            {
                prompt.AppendLine("  • SMALL COLLECTION: Focus on foundational artists and essential albums");
            }
            else if (profile.TotalArtists < 200)
            {
                prompt.AppendLine("  • GROWING COLLECTION: Balance popular and lesser-known gems");
            }
            else
            {
                prompt.AppendLine("  • MATURE COLLECTION: Seek rare gems and deep cuts from the musical ecosystem");
            }

            // Diversity Analysis
            var genreDiversity = profile.TopGenres.Count;
            if (genreDiversity <= 3)
            {
                prompt.AppendLine("  • FOCUSED TASTE: Deep dive into specific subgenres and related artists");
            }
            else if (genreDiversity <= 6)
            {
                prompt.AppendLine("  • BALANCED TASTE: Mix core and adjacent genres thoughtfully");
            }
            else
            {
                prompt.AppendLine("  • ECLECTIC TASTE: Explore connections across diverse musical landscapes");
            }

            prompt.AppendLine();
        }

        private void AddMusicalDNAProfile(StringBuilder prompt, LibraryProfile profile, BrainarrSettings settings)
        {
            prompt.AppendLine("🧬 **MUSICAL DNA PROFILE:**");
            prompt.AppendLine("Based on the collection, this listener enjoys:");

            // Dominant Genre Characteristics
            var topGenres = profile.TopGenres.Take(5).ToList();
            prompt.AppendLine("Genre Preferences:");
            foreach (var genre in topGenres)
            {
                var characteristics = GetGenreCharacteristics(genre.Key);
                prompt.AppendLine($"  • {genre.Key}: {characteristics}");
            }

            // Inferred Musical Preferences
            prompt.AppendLine();
            prompt.AppendLine("Inferred Preferences:");
            var preferences = InferMusicalPreferences(topGenres.Select(g => g.Key).ToList());
            foreach (var pref in preferences)
            {
                prompt.AppendLine($"  • {pref}");
            }

            prompt.AppendLine();
        }

        private void AddUserPreferencesWithContext(StringBuilder prompt, BrainarrSettings settings, LibraryProfile profile)
        {
            prompt.AppendLine("🎪 **USER-SPECIFIED PREFERENCES:**");

            // Target Genres with Context
            if (!string.IsNullOrWhiteSpace(settings.TargetGenres))
            {
                prompt.AppendLine($"🎵 Target Genres: {settings.TargetGenres}");
                prompt.AppendLine("   → Use this as PRIMARY filter - user wants to explore these specifically");
            }

            // Mood Analysis with Musical Context
            var selectedMoods = GetSelectedMoodsDetailed(settings.MusicMood);
            if (selectedMoods.Any())
            {
                prompt.AppendLine($"😊 Target Moods: {string.Join(", ", selectedMoods)}");
                prompt.AppendLine("   → Find artists/albums that naturally embody these emotional qualities");
            }

            // Era Analysis with Cultural Context
            var selectedEras = GetSelectedErasDetailed(settings.MusicEra);
            if (selectedEras.Any())
            {
                prompt.AppendLine($"📅 Target Eras: {string.Join(", ", selectedEras)}");
                prompt.AppendLine("   → Consider the musical innovations and cultural movements of these periods");
            }

            // Discovery Mode Strategy
            prompt.AppendLine($"🔍 Discovery Mode: {GetDetailedDiscoveryMode(settings.DiscoveryMode)}");

            prompt.AppendLine();
        }

        private void AddExistingCollectionBlacklist(StringBuilder prompt, LibrarySample sample)
        {
            if (sample.Artists.Any())
            {
                prompt.AppendLine("🚫 **EXISTING ARTISTS IN COLLECTION (DO NOT RECOMMEND):**");
                prompt.AppendLine("These artists are ALREADY in the library - find NEW artists instead:");

                // Chunk artists for readability
                var artistChunks = sample.Artists
                    .Select((artist, index) => new { artist, index })
                    .GroupBy(x => x.index / 8)
                    .Select(g => string.Join(" • ", g.Select(x => x.artist)));

                foreach (var chunk in artistChunks)
                {
                    prompt.AppendLine($"  {chunk}");
                }

                prompt.AppendLine();
                prompt.AppendLine("⚠️ CRITICAL: Your mission is to find NEW artists that complement this collection!");
                prompt.AppendLine();
            }

            if (sample.Albums.Any())
            {
                prompt.AppendLine("📀 **EXISTING ALBUMS IN COLLECTION (DO NOT RECOMMEND):**");
                prompt.AppendLine($"These {sample.Albums.Count} albums are already owned:");

                // Show a representative sample to save tokens
                var albumSample = sample.Albums.Take(20).ToList();
                foreach (var album in albumSample)
                {
                    prompt.AppendLine($"  • {album}");
                }

                if (sample.Albums.Count > 20)
                {
                    prompt.AppendLine($"  ... and {sample.Albums.Count - 20} more albums");
                }

                prompt.AppendLine();
            }
        }

        private void AddDiscoveryStrategy(StringBuilder prompt, BrainarrSettings settings, LibraryProfile profile)
        {
            prompt.AppendLine("🎯 **DISCOVERY STRATEGY:**");

            switch (settings.DiscoveryMode)
            {
                case DiscoveryMode.Similar:
                    prompt.AppendLine("SIMILAR MODE - Find artists that sound very close to the existing collection:");
                    prompt.AppendLine("• Same subgenres but different artists");
                    prompt.AppendLine("• Artists influenced by or influencing those in the collection");
                    prompt.AppendLine("• Contemporary releases in the same style");
                    break;

                case DiscoveryMode.Adjacent:
                    prompt.AppendLine("ADJACENT MODE - Find artists in related but distinct styles:");
                    prompt.AppendLine("• Subgenres that blend with existing taste");
                    prompt.AppendLine("• Artists who evolved from or into these styles");
                    prompt.AppendLine("• Cross-genre pollination and fusion styles");
                    break;

                case DiscoveryMode.Exploratory:
                    prompt.AppendLine("EXPLORATORY MODE - Expand horizons while respecting core taste:");
                    prompt.AppendLine("• Artists from different genres but similar energy/quality");
                    prompt.AppendLine("• Unexpected connections and crossover artists");
                    prompt.AppendLine("• Gateway artists to new musical territories");
                    break;
            }

            prompt.AppendLine();
        }

        private void AddOutputFormatWithExamples(StringBuilder prompt, BrainarrSettings settings)
        {
            prompt.AppendLine("📋 **OUTPUT FORMAT REQUIREMENTS:**");
            prompt.AppendLine("Return EXACTLY this JSON format:");
            prompt.AppendLine();
            prompt.AppendLine("```json");
            prompt.AppendLine("[");
            prompt.AppendLine("  {");
            prompt.AppendLine("    \"artist\": \"Artist Name (no compilation/various artists)\",");
            prompt.AppendLine("    \"album\": \"Specific Album Title\",");
            prompt.AppendLine("    \"genre\": \"Specific subgenre\",");
            prompt.AppendLine("    \"year\": 2020,");
            prompt.AppendLine("    \"confidence\": 0.92,");
            prompt.AppendLine("    \"reason\": \"Detailed explanation connecting to user's taste\"");
            prompt.AppendLine("  }");
            prompt.AppendLine("]");
            prompt.AppendLine("```");
            prompt.AppendLine();

            // Type-specific guidance
            if (settings.RecommendationType == RecommendationType.Artists)
            {
                prompt.AppendLine("🎤 ARTIST MODE: Pick ANY representative album from each recommended artist");
                prompt.AppendLine("   • Choose their most acclaimed or accessible album");
                prompt.AppendLine("   • Each artist should only appear ONCE");
            }
            else
            {
                prompt.AppendLine("💿 ALBUM MODE: Recommend specific standout albums");
                prompt.AppendLine("   • Each album should be from a DIFFERENT artist");
                prompt.AppendLine("   • Focus on the best/most fitting album from each artist");
            }

            prompt.AppendLine();
        }

        private void AddValidationChecklist(StringBuilder prompt, BrainarrSettings settings)
        {
            prompt.AppendLine("✅ **FINAL VALIDATION CHECKLIST:**");
            prompt.AppendLine("Before submitting, verify EACH recommendation:");
            prompt.AppendLine($"1. Returns EXACTLY {settings.MaxRecommendations} unique recommendations");
            prompt.AppendLine("2. NO artist from the existing collection appears");
            prompt.AppendLine("3. NO 'Various Artists', 'Soundtrack', or compilation albums");
            prompt.AppendLine("4. Each artist appears only ONCE in your recommendations");
            prompt.AppendLine("5. All recommendations are real, released albums (not demos/bootlegs)");
            prompt.AppendLine("6. Confidence scores reflect genuine fit (0.7-1.0 for good matches)");
            prompt.AppendLine("7. Reasons explain the connection to the user's musical taste");
            prompt.AppendLine("8. Genres are specific and accurate");
            prompt.AppendLine("9. Years are correct (don't guess if unsure)");
            prompt.AppendLine("10. Valid JSON format with all required fields");
            prompt.AppendLine();

            prompt.AppendLine("🎵 **MAKE YOUR RECOMMENDATIONS COUNT!**");
            prompt.AppendLine("Each suggestion should be something you'd personally play for this listener.");
        }

        // Helper methods for detailed analysis
        private string GetGenreCharacteristics(string genre)
        {
            var characteristics = genre.ToLower() switch
            {
                var g when g.Contains("rock") => "Power, energy, guitar-driven, rebellious spirit",
                var g when g.Contains("electronic") => "Innovation, texture, atmospheric, rhythmic complexity",
                var g when g.Contains("jazz") => "Improvisation, sophistication, musical complexity, spontaneity",
                var g when g.Contains("pop") => "Melody-focused, accessibility, contemporary production, hooks",
                var g when g.Contains("metal") => "Intensity, technical skill, powerful dynamics, emotional release",
                var g when g.Contains("folk") => "Storytelling, acoustic textures, cultural heritage, intimacy",
                var g when g.Contains("hip hop") || g.Contains("rap") => "Lyrical prowess, rhythm, cultural commentary, innovation",
                var g when g.Contains("classical") => "Compositional complexity, emotional depth, historical significance",
                var g when g.Contains("blues") => "Emotional expression, musical tradition, soul, authenticity",
                var g when g.Contains("country") => "Narrative tradition, American roots, heartfelt emotion",
                _ => "Rich musical tradition and distinctive sonic characteristics"
            };
            return characteristics;
        }

        private List<string> InferMusicalPreferences(List<string> topGenres)
        {
            var preferences = new List<string>();

            if (topGenres.Any(g => g.ToLower().Contains("rock") || g.ToLower().Contains("metal")))
                preferences.Add("Appreciates strong rhythm sections and guitar work");

            if (topGenres.Any(g => g.ToLower().Contains("electronic") || g.ToLower().Contains("ambient")))
                preferences.Add("Enjoys sonic experimentation and atmospheric textures");

            if (topGenres.Any(g => g.ToLower().Contains("jazz") || g.ToLower().Contains("blues")))
                preferences.Add("Values musical virtuosity and improvisational elements");

            if (topGenres.Any(g => g.ToLower().Contains("folk") || g.ToLower().Contains("indie")))
                preferences.Add("Drawn to authentic expression and artistic integrity");

            return preferences;
        }

        private List<string> GetSelectedMoodsDetailed(IEnumerable<int> moodIds)
        {
            var moodMap = new Dictionary<int, string>
            {
                { 1, "High-energy and driving" },
                { 2, "Relaxed and laid-back" },
                { 3, "Dark and atmospheric" },
                { 4, "Emotional and passionate" },
                { 5, "Experimental and avant-garde" },
                { 6, "Danceable and rhythmic" },
                { 7, "Aggressive and intense" },
                { 8, "Peaceful and meditative" },
                { 9, "Melancholic and introspective" },
                { 10, "Uplifting and inspiring" },
                { 11, "Mysterious and ethereal" },
                { 12, "Playful and whimsical" },
                { 13, "Epic and cinematic" },
                { 14, "Intimate and personal" },
                { 15, "Groovy and funky" },
                { 16, "Nostalgic and evocative" }
            };

            return moodIds?.Where(id => id > 0)
                .Select(id => moodMap.TryGetValue(id, out var mood) ? mood : null)
                .Where(mood => mood != null)
                .ToList() ?? new List<string>();
        }

        private List<string> GetSelectedErasDetailed(IEnumerable<int> eraIds)
        {
            var eraMap = new Dictionary<int, string>
            {
                { 1, "Pre-Rock Era (1900s-1940s): Early jazz, blues, big band" },
                { 2, "Early Rock Era (1950s): Birth of rock, early R&B" },
                { 3, "60s Pop Revolution: British invasion, Motown, folk rock" },
                { 4, "70s Classic Rock: Progressive, hard rock, singer-songwriter" },
                { 5, "80s New Wave: Synth-pop, post-punk, MTV era" },
                { 6, "90s Alternative: Grunge, indie rock, electronic emergence" },
                { 7, "2000s Millennium: Nu-metal, pop-punk, digital revolution" },
                { 8, "2010s Social Media: Indie mainstream, streaming culture" },
                { 9, "Modern Era (2020s+): Current trends, genre blending" },
                { 10, "General Vintage (50s-70s): Classic foundation years" },
                { 11, "General Retro (80s-90s): Nostalgic modern classics" },
                { 12, "Contemporary (Recent 3-5 years): Current releases" }
            };

            return eraIds?.Where(id => id > 0)
                .Select(id => eraMap.TryGetValue(id, out var era) ? era : null)
                .Where(era => era != null)
                .ToList() ?? new List<string>();
        }

        private string GetDetailedDiscoveryMode(DiscoveryMode mode)
        {
            return mode switch
            {
                DiscoveryMode.Similar => "SIMILAR - Stay close to established taste, find sonic cousins",
                DiscoveryMode.Adjacent => "ADJACENT - Explore related genres and evolutionary paths",
                DiscoveryMode.Exploratory => "EXPLORATORY - Expand horizons while maintaining quality standards",
                _ => "BALANCED - Mix familiar and adventurous recommendations"
            };
        }
    }
}
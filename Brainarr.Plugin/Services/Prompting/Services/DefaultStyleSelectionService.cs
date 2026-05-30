using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;

public sealed class DefaultStyleSelectionService : IStyleSelectionService
{
    private const int SparseStyleArtistThreshold = 5;
    private const double RelaxedMatchThreshold = 0.70;

    private readonly Logger _logger;
    private readonly IStyleCatalogService _catalog;

    public DefaultStyleSelectionService(Logger logger, IStyleCatalogService catalog)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public StylePlanContext Build(
        LibraryProfile profile,
        BrainarrSettings settings,
        LibraryStyleContext styleContext,
        ICompressionPolicy compressionPolicy,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        settings ??= new BrainarrSettings();
        styleContext ??= new LibraryStyleContext();
        compressionPolicy ??= new DefaultCompressionPolicy();

        var coverage = styleContext.StyleCoverage ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var normalized = _catalog.Normalize(settings.StyleFilters ?? Array.Empty<string>());

        // Freestyle passthrough: styles the user typed that AREN'T in the catalog (e.g. "vaporwave").
        // Normalize drops them, so without this they never reach the prompt. We carry them as synthetic
        // seed anchors (a "freestyle:" slug prefix keeps them from colliding with catalog slugs or the
        // post-validation matcher). Their presence also means the user DID make a selection, so the
        // library-inference fallbacks below must NOT kick in and overwrite the user's intent.
        var freestyleTerms = (settings.StyleFilters ?? Array.Empty<string>())
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0 && string.IsNullOrEmpty(_catalog.ResolveSlug(s)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var trimmed = new List<string>();
        var inferred = new List<string>();

        if (normalized.Count == 0 && freestyleTerms.Count == 0 && coverage.Count > 0)
        {
            normalized = coverage.Keys
                .OrderByDescending(key => coverage.TryGetValue(key, out var count) ? count : 0)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                .Take(settings.MaxSelectedStyles)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (normalized.Count > settings.MaxSelectedStyles)
        {
            var ordered = normalized
                .OrderByDescending(slug => coverage.TryGetValue(slug, out var count) ? count : 0)
                .ThenBy(slug => slug, StringComparer.OrdinalIgnoreCase)
                .ToList();

            trimmed = ordered.Skip(settings.MaxSelectedStyles).ToList();
            normalized = ordered.Take(settings.MaxSelectedStyles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (normalized.Count == 0 && freestyleTerms.Count == 0 && settings.DiscoveryMode == DiscoveryMode.Similar)
        {
            var dominant = styleContext.DominantStyles ?? Array.Empty<string>();
            foreach (var slug in dominant)
            {
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                if (_catalog.ResolveSlug(slug) is { Length: > 0 } resolved)
                {
                    inferred.Add(resolved);
                }
            }

            if (inferred.Count > 0)
            {
                normalized = inferred
                    .Take(settings.MaxSelectedStyles)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        if (normalized.Count == 0)
        {
            _logger.Debug("No style filters selected; falling back to discovery defaults");
        }

        var entries = normalized
            .Select(slug => _catalog.GetBySlug(slug) ?? new StyleEntry { Name = slug, Slug = slug })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Add freestyle terms as synthetic seed anchors (within the overall MaxSelectedStyles budget),
        // preserving the user's original text for the prompt. They have no library coverage, so they
        // drive the renderer's genre-first (style-seeded) path and are never dropped by the matcher.
        var freestyleBudget = Math.Max(0, settings.MaxSelectedStyles - normalized.Count);
        if (freestyleBudget > 0 && freestyleTerms.Count > 0)
        {
            foreach (var term in freestyleTerms)
            {
                if (freestyleBudget <= 0) break;
                var pseudoSlug = "freestyle:" + term.ToLowerInvariant();
                if (!normalized.Add(pseudoSlug)) continue;
                entries.Add(new StyleEntry { Name = term, Slug = pseudoSlug });
                freestyleBudget--;
            }

            entries = entries
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Slug, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var relaxed = settings.RelaxStyleMatching;
        var threshold = relaxed ? RelaxedMatchThreshold : 1.0;

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var adjacent = new List<StyleEntry>();

        if (relaxed)
        {
            foreach (var slug in normalized)
            {
                foreach (var similar in _catalog.GetSimilarSlugs(slug))
                {
                    if (similar.Score < threshold)
                    {
                        continue;
                    }

                    if (normalized.Contains(similar.Slug))
                    {
                        continue;
                    }

                    if (expanded.Add(similar.Slug))
                    {
                        var entry = _catalog.GetBySlug(similar.Slug);
                        if (entry != null)
                        {
                            adjacent.Add(entry);
                        }
                    }
                }
            }
        }

        adjacent = adjacent
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var context = new StylePlanContext(
            normalized,
            expanded,
            entries,
            adjacent,
            new Dictionary<string, int>(coverage, StringComparer.OrdinalIgnoreCase),
            relaxed,
            threshold,
            trimmed,
            inferred);

        if (context.HasStyles)
        {
            var totalCoverage = context.SelectedSlugs.Sum(slug => context.Coverage.TryGetValue(slug, out var count) ? count : 0);
            if (totalCoverage < SparseStyleArtistThreshold)
            {
                context.Sparse = true;
            }
        }

        if (context.ShouldUseRelaxedMatches)
        {
            var strictCount = context.SelectedSlugs.Count;
            var maxByFactor = (int)Math.Ceiling(strictCount * compressionPolicy.MaxRelaxedInflation);
            var allowed = Math.Min(Math.Max(maxByFactor, strictCount), compressionPolicy.AbsoluteRelaxedCap);
            var allowedExtras = Math.Max(0, allowed - strictCount);

            var extraSlugs = context.ExpandedSlugs
                .Where(slug => !context.SelectedSlugs.Contains(slug))
                .OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (extraSlugs.Count > allowedExtras)
            {
                var keepExtras = extraSlugs.Take(allowedExtras).ToList();

                context.ExpandedSlugs.Clear();
                foreach (var slug in context.SelectedSlugs)
                {
                    context.ExpandedSlugs.Add(slug);
                }

                foreach (var slug in keepExtras)
                {
                    context.ExpandedSlugs.Add(slug);
                }
            }
        }

        return context;
    }
}

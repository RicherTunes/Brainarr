using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;

// Explicitly reference Services namespace types to avoid ambiguity with Services.Validation
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface ITopUpPlanner
    {
        Task<List<ImportListItemInfo>> TopUpAsync(
            BrainarrSettings settings,
            IAIProvider provider,
            ILibraryAnalyzer libraryAnalyzer,
            ILibraryAwarePromptBuilder promptBuilder,
            IDuplicationPrevention duplicationPrevention,
            LibraryProfile libraryProfile,
            int needed,
            NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult? initialValidation,
            CancellationToken cancellationToken,
            // T1: recommendations already delivered this run (avoid re-suggesting them in top-up).
            IReadOnlyList<ImportListItemInfo>? alreadyAccepted = null,
            // T2: resolver used to MBID-enrich top-up recs before the require-MBID filter (artist mode).
            IArtistMbidResolver? artistResolver = null);
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;

// Explicitly reference Services namespace types to avoid ambiguity with Services.Validation
using NzbDrone.Core.ImportLists.Brainarr.Services;

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
            NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult? initialValidation);
    }
}

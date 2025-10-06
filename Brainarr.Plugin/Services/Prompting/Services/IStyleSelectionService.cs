using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;

public interface IStyleSelectionService
{
    StylePlanContext Build(
        LibraryProfile profile,
        BrainarrSettings settings,
        LibraryStyleContext styleContext,
        ICompressionPolicy compressionPolicy,
        CancellationToken token);
}

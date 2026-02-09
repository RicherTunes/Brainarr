using System;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public interface IPromptPlanner
{
    PromptPlan Plan(LibraryProfile profile, RecommendationRequest request, CancellationToken cancellationToken);

    void ConfigureCacheTtl(TimeSpan ttl);
}

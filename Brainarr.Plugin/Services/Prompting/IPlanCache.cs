using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public interface IPlanCache
{
    bool TryGet(string key, out PromptPlan plan);

    void Set(string key, PromptPlan plan, TimeSpan ttl);

    void InvalidateByFingerprint(string libraryFingerprint);
}

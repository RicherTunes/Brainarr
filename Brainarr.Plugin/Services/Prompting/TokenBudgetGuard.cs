using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public static class TokenBudgetGuard
{
    public static int ClampTargetTokens(int targetTokens, int contextWindow, int headroomTokens)
    {
        if (targetTokens < 0)
        {
            return 0;
        }

        var maxAllowed = Math.Max(0, contextWindow - headroomTokens);
        return Math.Min(targetTokens, maxAllowed);
    }
}

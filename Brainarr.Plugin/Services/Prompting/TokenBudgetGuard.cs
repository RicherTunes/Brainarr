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

    public static int Enforce(int estimatedTokens, int contextWindow, int headroomTokens, int targetTokens, Action? onTrim = null)
    {
        if (estimatedTokens <= 0)
        {
            return 0;
        }

        var clampedTarget = ClampTargetTokens(targetTokens, contextWindow, headroomTokens);
        if (clampedTarget <= 0)
        {
            onTrim?.Invoke();
            return 0;
        }

        if (estimatedTokens <= clampedTarget)
        {
            return estimatedTokens;
        }

        onTrim?.Invoke();
        return clampedTarget;
    }
}

using System;
using System.Globalization;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;

public sealed class DefaultTokenBudgetPolicy : ITokenBudgetPolicy
{
    public int SystemReserveTokens(string modelKey)
    {
        return 1200;
    }

    public double CompletionReserveRatio(string modelKey)
    {
        return IsLocal(modelKey) ? 0.15 : 0.20;
    }

    public double SafetyMarginRatio(string modelKey)
    {
        return IsLocal(modelKey) ? 0.05 : 0.10;
    }

    public int HeadroomTokens(string modelKey)
    {
        return IsLocal(modelKey) ? 512 : 1024;
    }

    private static bool IsLocal(string modelKey)
    {
        var normalized = modelKey?.ToLower(CultureInfo.InvariantCulture) ?? string.Empty;
        return normalized.Contains("ollama", StringComparison.Ordinal) ||
               normalized.Contains("lmstudio", StringComparison.Ordinal) ||
               normalized.Contains("lm-studio", StringComparison.Ordinal);
    }
}

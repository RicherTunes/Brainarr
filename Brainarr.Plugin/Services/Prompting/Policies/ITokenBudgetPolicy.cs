using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;

public interface ITokenBudgetPolicy
{
    int SystemReserveTokens(string modelKey);
    double CompletionReserveRatio(string modelKey);
    double SafetyMarginRatio(string modelKey);
    int HeadroomTokens(string modelKey);
}

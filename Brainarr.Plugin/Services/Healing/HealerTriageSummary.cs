namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record HealerTriageSummary(
    int Total,
    IReadOnlyDictionary<string, int> ByWorkflow,
    IReadOnlyDictionary<string, int> ByRisk,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ByWorkflowByRisk,
    IReadOnlyDictionary<string, int> Authorization,
    IReadOnlyDictionary<string, int> BlockedReasons)
{
    public static HealerTriageSummary Create(IReadOnlyList<HealerTreatmentPlan> plans)
    {
        var byWorkflow = CountBy(plans, plan => plan.CandidateWorkflow);
        var byRisk = CountBy(plans, plan => plan.Risk);
        var byWorkflowByRisk = plans
            .GroupBy(plan => plan.CandidateWorkflow, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, int>)CountBy(group.ToArray(), plan => plan.Risk),
                StringComparer.Ordinal);
        var authorization = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["authorized"] = plans.Count(plan => plan.ExecutionAuthorization.Authorized),
            ["unauthorized"] = plans.Count(plan => !plan.ExecutionAuthorization.Authorized),
        };
        var blockedReasons = plans
            .SelectMany(plan => plan.BlockedReasons)
            .Where(reason => reason != HealerTreatmentVocab.BlockedReason.None)
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new HealerTriageSummary(
            plans.Count,
            byWorkflow,
            byRisk,
            byWorkflowByRisk,
            authorization,
            blockedReasons);
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IReadOnlyList<HealerTreatmentPlan> plans,
        Func<HealerTreatmentPlan, string> keySelector)
    {
        return plans
            .GroupBy(keySelector, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }
}

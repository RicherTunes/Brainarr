# Review Queue

> Compatibility
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

The Review Queue is Brainarr’s safety net for borderline results. Keep the heavy guidance in sync by updating `docs/TROUBLESHOOTING.md` first—this page highlights only the key workflow.

## When items land here

- Safety Gates (Minimum Confidence, Require MBIDs) flag items as risky.
- **Queue Borderline Items** is enabled in [Advanced Settings](Advanced-Settings), so the plugin queues them instead of discarding.

## Workflow

1. Enable **Queue Borderline Items** under Advanced Settings (see README quick start for navigation).
2. Run Brainarr manually or on schedule; flagged items appear in the Review Queue UI.
3. Approve or reject in batches. Approved items apply on the next sync or immediately if you trigger the review/apply action.
4. Clear completed items; Brainarr preserves selections where the host supports it.

## Tips

- Tune **Minimum Confidence** and **Require MBIDs** to balance quality vs. throughput. Keep changes recorded in `docs/TROUBLESHOOTING.md` so future releases stay aligned.
- Pair **Guarantee Exact Target** (Advanced Settings) with the Review Queue when you need fixed batch sizes without sacrificing safety.
- For deeper diagnostics (logs, metrics), jump to [Troubleshooting](Troubleshooting) and the Observability preview.

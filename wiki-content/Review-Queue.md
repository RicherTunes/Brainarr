# Review Queue

> Compatibility
> Requires Lidarr 2.14.1.4716+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

Use the Review Queue to approve borderline items before they’re added to your library.

## Overview`r`n`r`n
- Items that do not meet Safety Gates (e.g., below Minimum Confidence or missing MBIDs) can be queued instead of dropped when Queue Borderline Items is enabled.
- Approve items in batches from the Review Queue UI.

## Workflow`r`n`r`n
1. Enable Queue Borderline Items in Advanced Settings.
2. Run Brainarr (manual or scheduled). Items failing gates are sent to the queue.
3. In the Review Queue section, choose “Approve Suggestions” and pick items to accept.
4. Save to apply on the next sync, or trigger the review/apply action to apply immediately.
5. After apply, selections auto‑clear and persist when supported by the host.

## Tips`r`n`r`n
- Tune Minimum Confidence and Require MBIDs to balance quality vs. volume.
- Use Guarantee Exact Target when you need consistent counts but pair it with Safety Gates and the Review Queue to keep quality high.

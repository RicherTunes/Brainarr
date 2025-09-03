# ✅ Review Queue

A lightweight queue to review borderline recommendations before they are added. Borderline items are those that fail safety gates (low confidence or missing MBIDs) when enabled.

## What gets queued
- Confidence below Min Confidence (defaults to 0.7)
- Missing MusicBrainz IDs when Require MBIDs is enabled (defaults to on)

Queued items are persisted to `review_queue.json` under the Brainarr data folder.

## Actions (UI/API)

Use Import List “Request Action” with these actions and query params:
- `review/getqueue`
  - Returns: `{ items: [{ artist, album, genre, year, confidence, artistMusicBrainzId, albumMusicBrainzId, createdAt }] }`
- `review/accept?artist=...&album=...`
  - Marks the item as Accepted. Accepted items will be included in the next sync.
- `review/reject?artist=...&album=...&notes=...`
  - Rejects the item and records soft negative feedback.
- `review/never?artist=...&album=...&notes=...`
  - Never suggest again: strong negative constraint.
- `review/getoptions`
  - Returns options list suitable for UI pickers: `{ options: [{ value: "Artist|Album", name: "Artist — Album" }] }`
- `review/getsummaryoptions`
  - Returns summary counts as options: pending/accepted/rejected/never
- `review/apply?keys=Artist|Album,Another Artist|Album`
  - Batch-accepts specific items and releases them into the next sync

## Using From UI

- Approve via Settings:
  1) Open the Brainarr Import List settings
  2) In the "Review Queue" section, use "Approve Suggestions" to pick items (searchable multi‑select chips)
  3) Click Save
  4) On the next sync, those items are released
     - After a successful apply, the selections are auto‑cleared and persisted (when supported by the host)
     - If your host cannot auto‑persist, the selections are still cleared in memory; click Save once to persist the cleared state

- Apply immediately (without waiting for sync):
  - Most Lidarr builds expose provider actions on the Import List configuration page (buttons/menu)
  - If your UI doesn’t expose them, call the actions above via the Lidarr API

## Quick UI Test Recipe

1) Prepare a couple of queued items
- Turn on “Queue Borderline Items” and (optionally) “Require MusicBrainz IDs” in Brainarr settings
- Run a sync to populate the Review Queue (borderline items will be queued)

2) Approve via Settings (deferred apply)
- Open Brainarr Import List settings
- In “Review Queue” → “Approve Suggestions”, search and select a few chips
- Click Save
- On the next sync, those items should be released and added
- After apply, reopen settings and confirm the selection chips auto‑cleared

3) Apply immediately (no wait)
- Use the provider action `review/apply` (UI action button or API):
  - POST `/api/v1/importlist/action/review/apply?id=<importListId>&keys=Artist|Album,Artist2|Album2`
- Confirm items are released right away
- Reopen settings and verify Approve Suggestions is now empty (auto‑cleared)

4) Verify persistence
- If your host supports automatic persistence, no extra save is needed after apply
- Otherwise, click Save once more to persist the cleared selection state

Tips
- Use the “Review Summary” chips to quickly gauge pending/accepted/rejected/never counts
- If options look stale, close/reopen the settings modal to reload the options list

## Batch Operations

- Apply approvals now:
  - POST `/api/v1/importlist/action/review/apply?id=<importListId>&keys=Artist|Album,Artist2|Album2`
  - Selections are auto‑cleared; if your host supports automatic persistence, no further action is required
  - If not supported, follow with a settings save (can be an empty save) to persist the cleared selections
- Reject or never are per-item via `review/reject` and `review/never`.

## Where to find buttons in UI

- In most Lidarr builds, provider actions appear on the Import List configuration page as action buttons or in an actions menu. If your UI doesn’t expose them, call the endpoints above via API.

## Safety Gates (Settings)
- `Minimum Confidence`: Drop or queue items below this threshold.
- `Require MusicBrainz IDs`: Enforce MBID presence before auto-adding; non‑MBID items go to the queue.
- `Queue Borderline Items`: When on, sends borderline items to the Review Queue (instead of dropping).

## Flow
1. Generate → Validate → Resolve MBIDs
2. Apply Safety Gates
   - Pass → added immediately
   - Borderline → queued (if enabled)
3. Accepted queue items are released on the next sync

## Tips
- Keep Require MBIDs on for deterministic adds.
- If your model confidence is conservative, lower `Minimum Confidence` a bit (e.g., 0.6) and keep queueing enabled.
- Use `review/never` for artists/albums you don’t want to see again.

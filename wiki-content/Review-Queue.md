# ✅ Review Queue

A lightweight queue to review borderline recommendations before they are added. Borderline items are those that fail safety gates (low confidence or missing MBIDs) when enabled.

## What gets queued
- Confidence below Min Confidence (defaults to 0.7)
- Missing MusicBrainz IDs when Require MBIDs is enabled (defaults to on)

Queued items are persisted to `review_queue.json` under the Brainarr data folder.

## Actions (UI/API)

Use Import List “Request Action” with these actions and query params:
- `review/getQueue`
  - Returns: `{ items: [{ artist, album, genre, year, confidence, artistMusicBrainzId, albumMusicBrainzId, createdAt }] }`
- `review/accept?artist=...&album=...`
  - Marks the item as Accepted. Accepted items will be included in the next sync.
- `review/reject?artist=...&album=...&notes=...`
  - Rejects the item and records soft negative feedback.
- `review/never?artist=...&album=...&notes=...`
  - Never suggest again: strong negative constraint. Uses RecommendationHistory to exclude future suggestions.
- `metrics/get`
  - Returns a basic snapshot including review queue counts and provider status.

## Settings Field: Approve Suggestions

- In the Brainarr import list settings, use the "Approve Suggestions" field to select pending review items (auto-complete list).
- Click Save. On the next sync, those selected items are approved and imported.
- This is the easiest way to batch-approve items without manual actions.

## Settings Field: Review Summary (Read-only)

- Shows counts of items in the Review Queue as tags (read-only).
- Automatically populated via `review/getSummaryOptions`.

## Apply Approvals Immediately (Button/API)

- If your UI shows provider action buttons, use the custom action to apply approvals immediately.
- API: POST `/api/v1/importlist/action/review/apply?id=<your-list-id>`
  - Optional query: `keys=Artist|Album,Another Artist|Album`
  - If `keys` is omitted, the action applies current selections from the “Approve Suggestions” field.
  - Response includes `cleared: true` and a note; click Save to persist the cleared selection in settings.

### Clear Approvals (Reset Selection)

- API: POST `/api/v1/importlist/action/review/clear?id=<your-list-id>`
  - Clears the current “Approve Suggestions” selection in memory.
  - Click Save in the UI to persist the cleared selection in settings.

### Reject or Never Selected (Batch)

- Reject selected (use Approve Suggestions selection or pass keys):
  - POST `/api/v1/importlist/action/review/rejectSelected?id=<your-list-id>`
  - Optional: `keys=Artist|Album,Artist 2|Album 2`
- Never suggest selected (strong negative):
  - POST `/api/v1/importlist/action/review/neverSelected?id=<your-list-id>`
  - Optional: `keys=Artist|Album,Artist 2|Album 2`
- Both actions clear the current selection in memory; click Save to persist clearing.

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

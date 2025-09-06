# Advanced Settings

> Compatibility
> Requires Lidarr 2.14.1.4716+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

This page documents advanced options referenced by Brainarr’s UI help links.

## Model Selection

- Click Test first to auto-detect available models (local providers) and validate keys (cloud).
- UI model dropdowns use provider-specific enums; at runtime they map to model IDs.

## Manual Model Override

- Advanced users can provide an exact model route via Manual Model Id.
- Example (OpenRouter): `anthropic/claude-3.5-sonnet` or `anthropic/claude-3.5-sonnet:thinking`
- When set, this overrides the dropdown.

## API Keys

- Required for cloud providers. Paste in the corresponding field.
- Keep keys secret. Remove them from logs before sharing.

## Auto-Detect Model

- When enabled, Brainarr auto-selects a suitable model after Test.
- Local providers rely on live `/api/tags` (Ollama) or `/v1/models` (LM Studio).

## Recommendations

- Target number of items per run (1–50). Brainarr treats this as a target and uses top‑up iterations to fill gaps caused by duplicates or existing library items.
- Start small (5–10) and adjust after you review results.

## Discovery Mode

- Similar: very close to your known artists/genres.
- Adjacent: related genres and neighboring styles.
- Exploratory: broader discovery across genres.

## Library Sampling

- Minimal: small sample of your library for speed.
- Balanced: default; good mix of context and speed.
- Comprehensive: broad sampling; slower but more thorough.

## Recommendation Type

- Specific Albums: default album recommendations.
- Artists: artist-only mode (helpful for local models or fast exploration).

## Backfill Strategy

- Standard: conservative fill behavior.
- Aggressive: raises caps and expand-avoid lists to reach the target sooner.

## Iterative Top-Up

- When under target, Brainarr requests additional recommendations with feedback about rejected items to improve uniqueness.
- Local providers (Ollama, LM Studio): enabled by default.
- Cloud providers: toggle here.

## Hysteresis Controls

- Top‑Up Max Iterations: hard cap on how many extra attempts.
- Zero‑Success Stop After: stop when an iteration yields no unique items N times.
- Low‑Success Stop After: stop when unique ratio stays low N times (mode‑adjusted).
- Top‑Up Cooldown (ms): small pause after early stops to reduce churn.
- Top‑Up Stop Sensitivity: Strict (stop early), Balanced, Lenient (allow more attempts). Threshold fields act as minimums.

## Timeouts

- AI Request Timeout (s): request timeout per provider call.
- Notes: Local providers may effectively use a higher ceiling to accommodate slower inference. Retries use jittered backoff.

## Safety Gates

- Minimum Confidence: discard items below this confidence, or queue them when Queue Borderline Items is enabled.
- Require MBIDs: only add items with MusicBrainz IDs (artist mode accepts artist MBID). Others go to Review Queue.
- Queue Borderline Items: send low-confidence or missing-MBID items to Review Queue instead of dropping them.

## Guarantee Exact Target

- When enabled, Brainarr aggressively iterates (within safe bounds) to reach the exact target count. Useful for scheduled runs when consistent counts are desired.

## Structured Output Behavior

- Providers request structured JSON when supported (OpenAI, OpenRouter, Perplexity, LM Studio, Groq, DeepSeek). If `response_format` is unsupported, Brainarr retries without it and falls back to robust parsing.
- Accepted shapes: `{ "recommendations": [...] }`, array root `[ ... ]`, or single object.

## Logging and Diagnostics

- Enable Debug Logging: more verbose logs and token estimates.
- Per-Item Decisions: logs accepted/rejected decisions (compact).
- See Troubleshooting for log locations and how to capture correlation IDs.



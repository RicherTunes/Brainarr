
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Troubleshooting

Start with the full playbook in [docs/troubleshooting.md](https://github.com/RicherTunes/Brainarr/blob/main/docs/troubleshooting.md). Below is a quick triage you can follow inside Lidarr.

## Quick triage

- Provider test fails or hangs:
  - Click “Test” in the Brainarr settings pane. If it times out, verify the base URL (Ollama `http://localhost:11434`, LM Studio `http://localhost:1234`) or that cloud API keys are set. Brainarr enforces per‑operation timeouts and logs failures without blocking the UI.
  - Check `System → Logs` for provider errors and hints. Fix the reported issue and re‑try.

- Recommendations look stale or unchanged:
  - The plan cache uses a sliding TTL (default 5 minutes). Toggle the import list off/on to flush immediately, then run again.

- “Prompt was trimmed for headroom” appears:
  - Switch to a model with a larger context window or reduce styles; see the playbook section “Prompt was trimmed”.

- Styles matching looks off:
  - Brainarr ships an embedded styles catalog and refreshes from a canonical JSON in this repo. If the remote fetch fails, the embedded catalog remains authoritative.

If the quick steps don’t resolve it, read the detailed sections in [docs/troubleshooting.md](https://github.com/RicherTunes/Brainarr/blob/main/docs/troubleshooting.md).

## Reading Brainarr logs

Brainarr logs under the `Brainarr` logger in **System → Logs** (raise Lidarr's log level to `Debug` or
`Trace` to see the per-run detail). Every run is tagged with a run id so you can follow one sync end to end.

The lines worth finding first:

- **Run summary** — `items`, `target`, `attainment`, `providerSuccess`, and whether the result came from
  cache. `attainment` is how much of your target was delivered; `providerSuccess` is whether the provider
  answered at all. They are independent: a provider can be 100% healthy while attainment is 0%.
- **Under target** — printed when a run delivered less than the target, naming the likely cause (provider
  truncation or timeout, duplicates against your library, or a confidence/MBID gate). If it names
  *Minimum Confidence*, the [safety gates](Advanced-Settings.md#safety-gates) trimmed the run.
- **Safety gate** — how many items passed, how many fell below the confidence floor, and how many lacked
  a MusicBrainz id.
- **Provider errors** — the provider id, the mapped error kind, and a hint. A timeout here usually means
  the model needs a longer [AI Request Timeout](Advanced-Settings.md#timeouts), not a broken endpoint.

**Log Per-Item Decisions** (advanced) adds one line per recommendation explaining why it was kept or
dropped. It is the fastest way to find out why a specific album never appeared, and the noisiest — turn it
on for a run or two, then off again.

Secrets are redacted: API keys and credential contents are never written to the log. If you are pasting a
log into an issue, still skim it for anything environment-specific first.

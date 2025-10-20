
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Troubleshooting

Start with the full playbook in [docs/troubleshooting.md](../docs/troubleshooting.md). Below is a quick triage you can follow inside Lidarr.

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

If the quick steps don’t resolve it, read the detailed sections in [docs/troubleshooting.md](../docs/troubleshooting.md).

# Brainarr Troubleshooting Guide

Use this page as an index to the canonical troubleshooting material rather than a duplicate playbook.

## Quick diagnostics

Run the helpers below when Brainarr misbehaves; copy the output into issues or discussions.

```bash
# Confirm Brainarr loads (Linux/Docker paths shown; adjust for your install)
grep -i brainarr /var/log/lidarr/lidarr.txt | tail -40

# Inspect plugin payload
tree /var/lib/lidarr/plugins/RicherTunes/Brainarr | head

# Basic provider probes
curl -s http://localhost:11434/api/tags | jq '.[].name'   # Ollama
curl -s http://localhost:1234/v1/models | jq '.models[].id'  # LM Studio
```

## Canonical troubleshooting resources

| Topic | Where to look |
|-------|---------------|
| Plugin fails to load / version mismatch | README “Compatibility notice” and wiki [Installation FAQ](https://github.com/RicherTunes/Brainarr/wiki/Installation-FAQ) |
| Provider connectivity, auth errors, rate limits | Wiki [Local Providers](https://github.com/RicherTunes/Brainarr/wiki/Local-Providers) / [Cloud Providers](https://github.com/RicherTunes/Brainarr/wiki/Cloud-Providers) |
| Prompt/token issues, cache drift, determinism | Wiki [Observability & Metrics](https://github.com/RicherTunes/Brainarr/wiki/Observability-&-Metrics) and `docs/Advanced-Settings.md` |
| CI/build failures | `docs/DEPLOYMENT.md` (linking to `BUILD.md`) and `ci-stability-guide.md` |
| Security/sanitisation findings | `docs/SECURITY.md` and `docs/TROUBLESHOOTING.md` (this file) section below |

## Common issues (pointers only)

- **Brainarr missing in Import Lists** → Verify the nightly Lidarr requirement (README). If satisfied, follow the wiki’s Installation FAQ for platform-specific plugin directories.
- **Provider “Test” button fails** → Use the provider-specific wiki article to validate credentials, required scopes, and rate limits. Update `docs/providers.yaml` after confirming fixes.
- **Prompt exceeds context window** → Ensure the new headroom guard is enabled (`LibraryAwarePromptBuilder` logs `headroom_guard`). If you see trims, revisit Advanced Settings → Token Budgets in the wiki.
- **Plan cache returning stale results** → Clear cache via the UI (Operations → Cache Reset) and monitor the `plan_cache` metrics documented in the Observability wiki page.
- **API keys exposed in logs** → Double-check redaction rules in `docs/SECURITY.md` and file an issue with anonymised snippets if anything leaks.

## When reporting issues

1. Include the quick diagnostic output and Lidarr version/branch.
2. Attach the relevant wiki section you followed; note any missing documentation so we can patch the source of truth.
3. Reference the Brainarr commit/tag you are running.

If a troubleshooting step is missing in the wiki or these docs, update the wiki first, then add a pointer here so every surface stays aligned.

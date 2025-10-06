# Brainarr FAQ

## Brainarr doesn’t show up in Lidarr
- Confirm Lidarr is on the `nightly` branch (Settings → General → Updates).
- If the plugin was copied manually, ensure files live under `plugins/RicherTunes/Brainarr/` (no extra nested folder).
- Restart Lidarr and check **System → Logs** for `Brainarr: minVersion` entries.

## The Test button fails
- Double-check provider URLs/API keys (see [Provider Basics](https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics)).
- For local providers, ensure the service is running: `curl http://localhost:11434/api/tags` (Ollama), `curl http://localhost:1234/v1/models` (LM Studio).
- For cloud providers, verify quota limits and that the key has the required scope.

## Recommendations are empty or duplicates
- Make sure your library has at least 10 artists/albums.
- Review Safety Gates (Minimum Confidence / Require MBIDs) and the Review Queue.
- Enable iterative refinement for local providers or increase Max Recommendations.

## Prompts exceed the model context window
- Brainarr clamps output to `context - headroom`; if you still see trims, adjust Token Headroom and Sampling Strategy in Advanced Settings.
- Large trims may indicate an aggressive sampling shape override; revert to defaults or tune ratios.

## How do I upgrade safely?
1. Pull the latest tag or release package.
2. Regenerate provider matrices if `docs/providers.yaml` changed (`pwsh ./scripts/sync-provider-matrix.ps1`).
3. Copy new plugin files over the old ones (or reinstall via the UI) and restart Lidarr.
4. Review the [Operations Playbook](https://github.com/RicherTunes/Brainarr/wiki/Operations) for post-upgrade validation.
5. Keep the previous ZIP handy; if you need to roll back, restore the prior DLL/manifest pair.

## Where can I ask questions?
- GitHub Issues for bugs/feature requests.
- GitHub Discussions (if enabled) or the Arr community channels for general Q&A.
- Check the [Operations Playbook](https://github.com/RicherTunes/Brainarr/wiki/Operations) and [Troubleshooting](troubleshooting.md) before filing a ticket.

See also: [README](../README.md), [Start Here](https://github.com/RicherTunes/Brainarr/wiki/Start-Here), and the wiki playbooks for deeper coverage.

# Troubleshooting

> Compatibility
> Requires Lidarr 2.14.1.4716+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

## Common Issues`r`n`r`n
- Ollama: `curl http://localhost:11434/api/tags`
- LM Studio: `curl http://localhost:1234/v1/models`

- Ensure library has at least 10 artists.
- Click Test to validate provider and populate models.
- Verify API keys for cloud providers.
- Try a simpler Sampling Strategy (Minimal) for slower local models.
- Brainarr requests structured JSON when available; if you see parsing errors, enable Debug Logging and try again.

- Check provider URL and service status.
- Validate API key format/permissions (cloud).
- Review firewall and rate limiting.

## Reading Brainarr Logs

Logs help diagnose issues. Capture around the time of a run and include correlation IDs when available.

- Docker: `docker logs <lidarr-container> | grep -i "brainarr\|plugin"`
- Windows: `C:\\ProgramData\\Lidarr\\logs\\lidarr.txt`
- Linux (systemd): `journalctl -u lidarr -e --no-pager | grep -i brainarr`

Enable debug logging in Brainarr settings to get more context (token estimates, parser diagnostics, per-item decisions).

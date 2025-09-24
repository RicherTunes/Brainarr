# Troubleshooting

> Compatibility
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

## Common Issues

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

## Diagnostics Checklist (Performance / 429)

1. Open Observability (Preview) under Advanced in the Brainarr list settings.
   - Identify which `provider:model` shows elevated p95/p99 or rising errors/429.
2. If 429s are frequent:
   - Enable `EnableAdaptiveThrottling` (Advanced → Hidden), set CloudCap=2, LocalCap=8, TTL=60s.
   - Optionally reduce `MaxConcurrentPerModelCloud` for the affected model to 1–2.
3. If one model throws most errors:
   - Switch to a more stable model/route; consider disabling structured JSON for that provider.
4. Verify external causes (network, provider status) and retry off-peak.
5. Export Prometheus via `metrics/prometheus` and inspect trends in PromQL/Grafana.

## Reading Brainarr Logs

Logs help diagnose issues. Capture around the time of a run and include correlation IDs when available.

- Docker: `docker logs <lidarr-container> | grep -i "brainarr\|plugin"`
- Windows: `C:\\ProgramData\\Lidarr\\logs\\lidarr.txt`
- Linux (systemd): `journalctl -u lidarr -e --no-pager | grep -i brainarr`

Enable debug logging in Brainarr settings to get more context (token estimates, parser diagnostics, per-item decisions).

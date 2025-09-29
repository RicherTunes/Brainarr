# Troubleshooting

> Compatibility
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

For the canonical playbook, see [`docs/TROUBLESHOOTING.md`](../docs/TROUBLESHOOTING.md). Update that document first, then refresh this page with summary pointers.

## Quick checks

- **Provider test button**: Run from the Brainarr list UI; fixes most credential or URL issues.
- **Local diagnostics**: `curl http://localhost:11434/api/tags` (Ollama), `curl http://localhost:1234/v1/models` (LM Studio).
- **Library size**: Ensure ≥10 artists so the planner has context.
- **Sampling strategy**: Try `Minimal` if local models are slow.

## Observability & rate limits

1. Open the Observability preview (Advanced → Observability) and inspect the `provider:model` series for rising p95 or 429 counts.
2. If 429s spike, enable adaptive throttling (Advanced → Hidden) with modest caps (Cloud=2, Local=8, TTL=60s) or lower `MaxConcurrentPerModelCloud` for the affected model.
3. Persistent errors on a single model? Switch routes or disable structured JSON temporarily.
4. Export `metrics/prometheus` for dashboards—examples live in `dashboards/README.md`.

## Logs

Gather logs around a failing run and include correlation IDs when filing issues:

- Docker: `docker logs <lidarr-container> | grep -i "brainarr\|plugin"`
- Windows: `C:\ProgramData\Lidarr\logs\lidarr.txt`
- Linux (systemd): `journalctl -u lidarr -e --no-pager | grep -i brainarr`

Enable Debug logging temporarily to capture token estimates and parser diagnostics; remember to revert to Info afterwards.

Need more? Follow the in-depth flow in [`docs/TROUBLESHOOTING.md`](../docs/TROUBLESHOOTING.md) or the wiki [Observability & Metrics](Observability-and-Metrics) page.

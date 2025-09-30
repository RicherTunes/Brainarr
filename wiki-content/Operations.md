# Operations Playbook

> Use this page as your runbook from first install through ongoing operations. Every workflow links back to the canonical docs/tests so the information stays in sync.

## Day 0 – Confirm prerequisites

- Lidarr nightly ≥ 2.14.2.4786 (README compatibility notice).
- Brainarr built via `./setup.ps1` / `./setup.sh`; provider matrix regenerated if `docs/providers.yaml` changed.
- Follow the README quick start and [docs/USER_SETUP_GUIDE.md](../docs/USER_SETUP_GUIDE.md) before proceeding.

## Day 1 – First run validation

1. **Connectivity**: Use the Brainarr list UI → **Test** button (watch for green toast). If it fails, jump to [Troubleshooting](Troubleshooting) → Quick checks.
2. **Manual fetch**: Trigger Manual Import; expect the log sequence documented in the First Run Guide.
3. **Quality review**: Approve/reject the first batch. If Safety Gates are active, use the [Review Queue](Review-Queue) workflow.
4. **Metrics snapshot**: Open the Observability preview (Advanced tab) and record baseline p50/p95 latency, error rate, and 429 counts per provider:model.

## Day N – Scheduled operations

- **Import cadence**: Set Brainarr’s refresh interval (Advanced → Refresh Interval) and enable Guarantee Exact Target when you need a fixed batch size.
- **Review cadence**: Reserve a weekly check on the Review Queue for borderline items and the verification ledger (`docs/VERIFICATION-RESULTS.md`).
- **Provider health**: Monitor the dashboards below; review provider quotas/announcements monthly and update `docs/providers.yaml`/README if anything changes.

## Incident response quick reference

1. **Symptoms**: Identify whether the issue is latency, empty recommendations, or provider errors.
2. **Logs**: Capture Brainarr logs (README → Observability & Troubleshooting section); include the correlation ID if present.
3. **Metrics**: Hit `metrics/prometheus` and your Grafana dashboard to confirm which provider/model is misbehaving.
4. **Mitigation**:
   - Latency spike → reduce concurrency (Advanced Settings) or switch provider to fallback.
   - Empty batches → verify library size, Safety Gate thresholds, and Review Queue.
   - 429 storms → enable adaptive throttling or lower per-model caps; confirm free-tier limits.
5. **Document**: Note the root cause and remediation in `docs/VERIFICATION-RESULTS.md`; update `docs/providers.yaml` if the provider status changes.

## Dashboards & alerts

- Import `dashboards/grafana-brainarr-observability.json` as a starting panel set (p95 latency, error rate, 429 ratio).
- Suggested PromQL (from `dashboards/README.md`):
  - `provider_latency_seconds_p95{provider="openai"}` for tails.
  - `increase(provider_requests_total{status="error"}[5m])` for spikes.
- Alert hints:
  - Trigger an alert if p95 latency doubles baseline for >10 minutes.
  - Alert when 429 counts exceed 5% of traffic for any provider:model over 15 minutes.

## Change log integration

- Every operational change (new provider status, advanced setting override, incident) should append an entry to `docs/VERIFICATION-RESULTS.md` and mention the version in `CHANGELOG.md`.
- If workflows change materially, update [docs/DOCS_STRATEGY.md](../docs/DOCS_STRATEGY.md) and this playbook together.

## Reference links

- [docs/USER_SETUP_GUIDE.md](../docs/USER_SETUP_GUIDE.md)
- [docs/TROUBLESHOOTING.md](../docs/TROUBLESHOOTING.md)
- [wiki Observability & Metrics](Observability-and-Metrics)
- [wiki Advanced Settings](Advanced-Settings)
- [wiki First Run Guide](First-Run-Guide)
- [wiki Review Queue](Review-Queue)

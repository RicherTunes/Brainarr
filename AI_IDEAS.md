# AI_IDEAS

Living backlog for post-roadmap AI features.

## Principles

- Keep `Lidarr.Plugin.Common` thin: contracts, test harnesses, shared guardrails.
- Keep provider/runtime policy in plugins.
- Ship each feature with hermetic tests + explicit error codes.
- Prefer additive endpoints/actions behind feature flags first.

## Next 90 Days (Proposed)

1. **AI-assisted queue triage** (Brainarr first)
2. **AI library gap planner** (Brainarr first)
3. **Confidence calibration + explainability contracts** (Common contracts, plugin logic local)
4. **Provider cost/latency policy engine** (Brainarr local)

---

## Priority Features

### 1) AI-Assisted Queue Triage

**Goal:** Explain why queued candidates are low-confidence, duplicate-prone, or policy-blocked and recommend one-click actions.

#### Scope

- Input: queued recommendation candidates + match metadata + dedup/safety decisions.
- Output: `reason_codes[]`, confidence score, suggested actions (`approve`, `drop`, `retry_with_provider`, `defer`).
- Add lightweight endpoint/action pair (read-only explain + explicit action call).

#### Keep in plugin vs Common

- Plugin: scoring logic, feature extraction, recommendation rationale text.
- Common: only reusable reason-code contract + redaction-safe logging assertions.

#### Acceptance

- 95%+ deterministic reason-code coverage in hermetic tests.
- No secrets in rationales/log output (existing redaction contract extended).
- p95 triage explanation latency < 300 ms for 100 items.

### 2) AI Library Gap Planner

**Goal:** Prioritize underrepresented eras/styles with explicit rationale and confidence.

#### Scope

- Compute library distribution baseline (era/style/region if available).
- Produce ranked "gap plan" with expected diversity lift and confidence.
- Output includes `why_now`, `expected_lift`, `confidence`, `evidence[]`.

#### Keep in plugin vs Common

- Plugin: distribution analysis, planning heuristic, recommendation policy.
- Common: optional DTO contract for gap-plan response shape.

#### Acceptance

- Snapshot tests for deterministic plans on golden fixtures.
- Monotonicity checks (bigger gap => non-lower priority unless policy-excluded).
- User-facing rationale always includes at least one concrete evidence item.

### 3) Confidence Calibration Layer

**Goal:** Convert provider-specific confidence into calibrated scores for fair cross-provider ranking.

#### Scope

- Add calibration adapters per provider.
- Track calibration drift over time (nightly).
- Emit confidence bucket metrics for observability.

#### Keep in plugin vs Common

- Plugin: calibration curves + ranking usage.
- Common: metric/event naming helpers only.

### 4) Cost/Latency-Aware Provider Routing

**Goal:** Route generation requests to providers based on budget, latency SLO, and recent health.

#### Scope

- Add policy inputs: max cost/run, latency budget, retry budget.
- Add deterministic fallback chain with explicit failure reasons.
- Expose routing decision trace in debug mode.

#### Keep in plugin vs Common

- Plugin: policy engine and routing decisions.
- Common: none (except generic diagnostics contracts already present).

### 5) Recommendation Diff + Explain

**Goal:** Explain why today’s list differs from yesterday’s and which constraints changed the output.

#### Scope

- Produce semantic diff categories: `new_due_to_gap`, `dropped_duplicate`, `policy_filtered`, `provider_unavailable`.
- Add short user-facing explanation block.

### 6) Safe Auto-Actions (Opt-in)

**Goal:** Auto-approve only high-confidence low-risk items using strict policy gates.

#### Scope

- Feature flag + dry-run mode.
- Hard cap per run and mandatory audit trail.
- Rollback endpoint for last auto-action batch.

---

## Additional Brainarr Ideas

- **Prompt budget optimizer:** adaptive prompt compression based on model context/cost and target confidence.
- **Cold-start mode:** bootstrap recommendations for near-empty libraries using canonical diversity seeds.
- **Session memory safety rail:** retain useful short-term context while enforcing strict redaction and TTL.
- **"Why not this artist?" explainer:** reverse query that explains exclusion causes and required changes.
- **Model A/B sandbox:** compare provider/model outputs on frozen fixtures before enabling in production.

## Additional AI Feature Concepts (New)

- **AI-assisted queue triage simulation mode:** run triage on the pending queue and show "what would happen" without applying any action.
- **Library gap planner with budget constraints:** prioritize underrepresented styles/eras while respecting user caps (max artists/month, discovery aggressiveness).
- **Provider disagreement detector:** highlight candidates where providers disagree strongly and explain the conflict signals.
- **Novelty vs familiarity dial:** let users tune exploration/exploitation and emit transparent rationale for where each recommendation landed.
- **Collection continuity planner:** propose follow-up artists/albums that complete partially explored scenes, labels, or eras already present in library.
- **Seasonal/contextual mode packs:** optional policy presets (festival season, mellow evenings, new-release sprint) that only alter scoring weights, not provider contracts.
- **Recommendation confidence drift monitor:** detect when confidence distributions shift over time and suggest recalibration or provider fallback updates.
- **Human-feedback replay harness:** replay historical approve/reject actions against new scoring heuristics to estimate precision/recall before rollout.

## Tech-Debt Preconditions Before Large New Features

- Finish parity cleanup for stale Common pins and submodule drift across all plugins.
- Keep stress tests quarantined to nightly/stress lanes only; CI default must remain deterministic.
- Continue reducing large files with extraction + characterization tests before behavioral changes.

## Candidate Execution Order

1. Queue triage MVP (read-only explanations)
2. Queue triage actions + audit trail
3. Gap planner MVP + golden fixtures
4. Confidence calibration
5. Cost/latency routing
6. Safe auto-actions

## Definition of Done for Any New AI Feature

- Hermetic tests, golden fixtures, and explicit error codes.
- Redaction tests for exception + structured logs.
- No new plugin/host boundary leaks.
- Clear plugin-vs-Common ownership documented in PR.

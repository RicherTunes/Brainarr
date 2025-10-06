# Brainarr Orchestrator Decomposition Blueprint (Draft)

Last updated: 2025-09-25

## 1. Current-State Snapshot

- `Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs` is 1,426 LOC with 30 injected dependencies and multiple optional constructor parameters.
- Orchestrator owns provider selection, pipeline orchestration, cache coordination, sanitization, validation, telemetry, retry logic, settings persistence, and request/response DTO shaping.
- `LibraryAnalyzer` (1,412 LOC) and `LibraryPromptPlanner` (967 LOC) act as tightly coupled collaborators; orchestrator calls into their internals instead of high-level contracts.
- Static singletons (`LimiterRegistry`, `BreakerRegistry`) are constructed inside orchestrator, preventing test overrides and cross-plugin reuse.
- Provider-specific branching lives in orchestrator (`_currentProviderType` switches), increasing maintenance as providers expand.

## 2. Pain Points

1. **Testability**: constructor sprawl + static registries make it hard to isolate orchestrator behavior; integration tests rely on full DI graphs.
2. **Responsibility overload**: orchestrator handles decision making, stateful caching, workflow coordination, and service wiring.
3. **Provider coupling**: provider lifecycle (load, health checks, failover) is embedded, blocking re-use in other import list plugins.
4. **Pipeline rigidity**: pipeline steps (analysis, top-up, sanitization, dedupe, validation) are coded inline rather than declarative.
5. **Parallelism constraints**: attempts to parallelize provider calls/caching risk contention because of shared static registries.

## 3. Target-State Components

```    ext
+------------------+      +---------------------------+
| Recommendation   |      | Provider Session Manager  |
| Coordinator      |<---->| (new) handles provider    |
| (workflow)       |      | lifecycle, retries,       |
+------------------+      | capability discovery       |
         |                +-------------^-------------+
         |                              |
         v                              |
+------------------+       +---------------------------+
| Pipeline Engine  |       | Resilience Services (DI) |
| (Strategy chain) |       | LimiterRegistry,         |
+------------------+       | BreakerRegistry, Retry   |
         |                 +---------------------------+
         v
+------------------+
| Stage Contracts  |
| (analysis, top-up,
| sanitization, etc.)
+------------------+
```

### Proposed Assemblies / Namespaces

- `NzbDrone.Core.ImportLists.Brainarr.Orchestration`
  - `IRecommendationCoordinator` (thin facade)
  - `RecommendationCoordinator` (composes pipeline + provider session)
- `NzbDrone.Core.ImportLists.Brainarr.Providers`
  - `IProviderSessionManager`
  - `ProviderSessionManager` (health, failover, discovery)
- `NzbDrone.Core.ImportLists.Brainarr.Pipeline`
  - `IPipelineStage`
  - Concrete stages: `LibraryProfilingStage`, `PromptPlanningStage`, `RecommendationGenerationStage`, `SanitizationStage`, `ValidationStage`, `PersistenceStage`
- `NzbDrone.Core.ImportLists.Brainarr.Resilience`
  - `ILimiterRegistry`, `IBreakerRegistry`, `IResilienceFactory` supplied via DI (backed by `lidarr.plugin.common` submodule, future package option)

## 4. Interface Adjustments (v1)

- Slim `IBrainarrOrchestrator` to `Task<ImportListSyncResult> ExecuteAsync(BrainarrSettings settings, CancellationToken token)` returning aggregate result instead of multiple side effects.
- Introduce `ProviderExecutionContext` that contains selected provider, capabilities, and rate-limit tokens.
- Replace optional ctor parameters with required dependencies + small option objects; surface `ISettingsPersistence` abstraction.
- Promote `LibraryAnalyzer` into stateless strategies invoked via stage pipeline.

## 5. Migration Plan

### Slice 0 – Setup

- Introduce new namespaces + interfaces behind the scenes without removing existing orchestrator entry points.
- Lift `LimiterRegistry` and `BreakerRegistry` into DI registrations (use existing implementations).

### Slice 1 – Provider Session Extraction

- Create `ProviderSessionManager` that encapsulates `_providerFactory`, `_providerHealth`, `_providerInvoker`, `_coordinator` logic.
- Orchestrator delegates provider acquisition + health fallback to the new manager; unit tests cover failover paths.

### Slice 2 – Pipeline Engine

- Define `IPipelineStage` contract; wrap current inline steps into stage adapters.
- Implement `PipelineEngine` executing registered stages sequentially (later parallelizable).

### Slice 3 – Coordinator Facade

- Replace orchestrator call sites with `RecommendationCoordinator` facade exposing task-based API.
- Deprecate old orchestrator methods and keep shim until all consumers updated.

### Slice 4 – Cleanup

- Remove redundant state (`_currentProvider`, `_currentProviderType`), ensure metrics + history injection moves to appropriate stages.
- Update docs + diagrams.

## 6. Open Questions

- Should stage registration remain static or be configuration-driven?
- How to version interfaces consumed by other plugins as we evolve the submodule (and potential future package)?
- Do we model pipeline stages as sync or async by default (some steps purely CPU-bound)?

## 7. Next Steps

- Draft DI changes in `Brainarr.Plugin/Services/ServiceCollectionExtensions` (or equivalent) to wire new abstractions.
- Prototype `ProviderSessionManager` unit tests using Moq to validate failover behavior.
- Sync with `lidarr.plugin.common` maintainers about hosting resilience interfaces for cross-plugin reuse.

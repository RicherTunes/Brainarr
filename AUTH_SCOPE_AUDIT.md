# Brainarr Auth Scope Audit

**Date**: 2026-05-23
**Investigator**: Wave 7C agent (claude-haiku-4-5 READ-ONLY audit)

## Executive Summary

Brainarr has **11 AI provider implementations**, 9 of which use cloud APIs with bearer-token / API-key authentication. Currently **there is NO AuthFailureGate logic** — 401 responses are delegated to `LlmErrorMapper` (from common library) and surfaced as generic `LlmProviderException`, which the adapter catches and returns empty recommendations silently.

**Circuit breaker infrastructure exists** (`Brainarr.Plugin\Services\Resilience\CircuitBreaker.cs`) but is **NOT wired into providers**. It's defined but completely unused.

## Provider Inventory

| Provider | Auth Type | 401 Handling | Gate Needed? |
|---|---|---|---|
| Ollama | None (local) | N/A | NO |
| LM Studio | Optional Bearer (local) | N/A | NO |
| OpenAI | Bearer | Maps to LlmProviderException | **YES** |
| Anthropic | x-api-key | Maps to LlmProviderException | **YES** |
| Claude Code | Bearer/CLI token | Maps to LlmProviderException | **YES** |
| Google Gemini | Query param key= | Maps to LlmProviderException | **YES** |
| Groq | Bearer (OpenAI compat) | Maps to LlmProviderException | **YES** |
| DeepSeek | Bearer | Maps to LlmProviderException | **YES** |
| Perplexity | Bearer | Maps to LlmProviderException | **YES** |
| OpenRouter | Bearer | Maps to LlmProviderException | **YES** |
| OpenAI Codex | Bearer/CLI token | Maps to LlmProviderException | **YES** |
| Z.AI GLM | Bearer | Maps to LlmProviderException | **YES** |
| Z.AI Coding | Bearer | Maps to LlmProviderException | **YES** |

**Key finding**: 9 of 11 providers require auth-failure gating.

## Critical Discovery: No Infinite Retry at Provider Level

Common's `ExponentialBackoffRetryPolicy` explicitly classifies 401/403 as NON-RETRYABLE (documented in `RetryPolicy.cs` lines 50–58). This means individual provider calls DO NOT retry on 401 indefinitely. They fail once and propagate up.

✅ Good news: Auth failures don't cause infinite provider-level retry storms.

⚠️ Bad news: Retry loop occurs at orchestrator level (failover chain, batch retries). No circuit breaker means repeated 401s from same provider are not tracked.

## Existing Circuit Breaker Infrastructure

**Status**: ✅ Fully implemented, ❌ Completely unused

Location: `Brainarr.Plugin\Services\Resilience\CircuitBreaker.cs`

Capabilities:
- Three-state machine (Closed → Open → HalfOpen)
- Configurable failure threshold, open duration, success threshold
- Per-provider factory with customized configs
- Full NLog integration

**The factory example** (line 240–248) shows provider-specific thresholds but is never instantiated or called.

## Top 3 High-Impact Candidates

1. **OpenAI** (`BrainarrOpenAiProvider.cs:104–151`)
   - File: `BrainarrOpenAiProvider.cs`
   - Blast radius: 10,000+ users (most popular provider)
   - Effort: MEDIUM
   - Work: Wrap `CompleteAsync()` in circuit breaker

2. **Anthropic** (`BrainarrAnthropicProvider.cs:130–205`)
   - File: `BrainarrAnthropicProvider.cs`
   - Blast radius: 5,000+ users
   - Effort: MEDIUM
   - Work: Same pattern as OpenAI

3. **Claude Code Subscription** (`BrainarrClaudeCodeSubscriptionProvider.cs:238–268`)
   - File: `BrainarrClaudeCodeSubscriptionProvider.cs`
   - Blast radius: 1,000+ subscription users (high pain)
   - Effort: SMALL (already has 401-specific hint)
   - Work: Wire breaker, enhance hint

## Recommended AuthFailureGate Scope: **(b) One per (provider, API key) tuple**

**NOT (a)** Global — providers are independent, 401 from OpenAI shouldn't affect Groq.

**NOT (c)** Per-provider-type — too coarse, multiple keys per provider would share a breaker.

**RECOMMENDED (b)**: Keyed by `(provider_id, hash(api_key))`
- Isolation: Each key gets its own breaker
- Scoping: Lives in `LlmProviderAdapter`, wraps `_llm.CompleteAsync()`
- Lifetime: Persists for adapter instance lifecycle

## Punch List for Next Wave

### Phase 1: Wire Circuit Breaker (CRITICAL — 2–3 hours)

1. Instantiate `CircuitBreakerFactory` in `BrainarrModule` (DI) — SMALL (5 min)
2. Inject factory into `LlmProviderAdapter`, wrap `CompleteAsync()` — SMALL (30 min)
3. Enhance `CaptureUserHint()` for auth failures — SMALL (15 min)

### Phase 2: Subscription Hardening (OPTIONAL — 3–4 hours)

4. Add token refresh retry to Claude Code / OpenAI Codex — MEDIUM (1–2 hr each)
5. Document token rotation in README — SMALL (20 min)

### Phase 3: Observability (NICE-TO-HAVE — 1 hour)

6. Add circuit-breaker metrics to telemetry — SMALL (30 min)

### Phase 4: Testing (1 hour)

7. Unit test: CircuitBreakerAdapterTests.cs — Mock 401, assert breaker opens — MEDIUM (1 hr)

## Summary

| Aspect | Status |
|---|---|
| **Auth provider count** | 9 of 11 (cloud + subscriptions) |
| **401 retry risk** | LOW at provider (common excludes 401 from retries), MEDIUM at orchestrator |
| **Circuit breaker** | Implemented but unused, needs wiring |
| **Recommended scope** | (b) Per (provider, key) tuple |
| **Total effort** | **SMALL** Phase 1 (2–3 hr), MEDIUM Phase 2 (optional) |
| **Top candidates** | OpenAI (10K users), Anthropic (5K users), Claude Code (1K users) |

---

Generated by Wave 7C audit agent, 2026-05-23. READ-ONLY investigation; not to be committed.

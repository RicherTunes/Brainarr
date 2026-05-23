# Handoff: Auth-failure gate registry (paused mid-work)

**Status:** WIP code preserved on branch `recovered/wip-auth-gate-and-zai-2026-05-23`.
The Z.AI Coding Plan work that was interleaved with it has been split out and
landed separately as commit `01b8de9` on `cleanup/delete-dead-enhanced-rate-limiter`.

This document is the prompt to send to another AI to finish the auth-gate work.

---

## Prompt to send

```
You are picking up paused work on the Lidarr Brainarr plugin
(github.com/RicherTunes/Brainarr). Repo path: C:\r\Alex\github\brainarr.

WHAT YOU INHERIT

Branch `recovered/wip-auth-gate-and-zai-2026-05-23` contains a single
commit (86a151d) that interleaves two unrelated workstreams. Your work
is to extract ONLY the auth-failure-gate registry workstream, drop the
Z.AI Coding Plan additions (those landed separately as commit 01b8de9
on `cleanup/delete-dead-enhanced-rate-limiter`), and land the auth-gate
work on its own clean branch.

THE AUTH-GATE WORK YOU OWN

New scaffolding (your code, untracked at time of pause):
- Brainarr.Plugin/Services/Support/AuthGateLatchedException.cs
- Brainarr.Plugin/Services/Support/AuthGatedSend.cs

Test coverage (your code, untracked at time of pause):
- Brainarr.Tests/Services/Support/AuthGatedSendTests.cs
- Brainarr.Tests/Services/Support/SubscriptionCredentialLoaderRedactionTests.cs
- Brainarr.Tests/Services/Core/BrainarrAuthFailureGateRegistryTests.cs
- Brainarr.Tests/Services/Core/ProviderRegistryAuthGateEndToEndTests.cs
- Brainarr.Tests/Providers/Llm/StreamingHttpExecutorAuthGateTests.cs
- Brainarr.Tests/Providers/Llm/SubscriptionProviderAuthGateTests.cs
- Brainarr.Tests/Services/RateLimiterPerProviderTests.cs
- Brainarr.Tests/Services/Resilience/ResiliencePolicyTests.cs (your edits)

Provider plumbing (your edits to existing files — pass IAuthFailureGateRegistry
through every constructor and use AuthGatedSend.ExecuteAsync at the HTTP
boundary):
- Brainarr.Plugin/Services/Providers/Llm/BrainarrAnthropicProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrClaudeCodeSubscriptionProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrDeepSeekProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrGeminiProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrGroqProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrOpenAiCodexSubscriptionProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrOpenAiProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrOpenRouterProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrPerplexityProvider.cs
- Brainarr.Plugin/Services/Providers/Llm/BrainarrZaiGlmProvider.cs (auth-gate ctor only — the 1113-mapping logic on the same file is NOT yours, it landed in 01b8de9; do not revert it)
- Brainarr.Plugin/Services/Providers/Llm/StreamingHttpExecutor.cs
- Brainarr.Plugin/Services/Support/SubscriptionCredentialLoader.cs

Registry / hosting (your edits):
- Brainarr.Plugin/Services/Core/ProviderRegistry.cs (the gateRegistry: _authGateRegistry passes through every factory — but the ZaiCoding factory registration on this file is NOT yours; preserve it)
- Brainarr.Plugin/Hosting/BrainarrModule.cs
- Brainarr.Plugin/Services/Core/BrainarrOrchestratorFactory.cs

Docs:
- docs/configuration.md (your additions about the gate registry)

WHAT IS NOT YOURS — DO NOT TOUCH

The following files were touched in the same recovered commit but are
the Z.AI Coding Plan work that landed in 01b8de9. Their post-01b8de9
state is canonical; if you find your old WIP for them on the recovery
branch, discard it:

- Brainarr.Plugin/Configuration/Enums.cs (ZaiCoding enum + expanded GLM catalog)
- Brainarr.Plugin/Configuration/Constants.cs (ZaiCoding constants)
- Brainarr.Plugin/Configuration/BrainarrSettingsValidator.cs (ZaiCoding validator)
- Brainarr.Plugin/Configuration/ModelIdMapper.cs (zaicoding case)
- Brainarr.Plugin/Configuration/ModelIdMappingValidator.cs
- Brainarr.Plugin/Configuration/Providers/CloudProviderSettings.cs (ZaiCodingProviderSettings)
- Brainarr.Plugin/Services/Core/AIServiceResourceKeys.cs (new — rate-limit key fix)
- Brainarr.Plugin/Services/RateLimiter.cs (canonical bucket names)
- Brainarr.Plugin/Services/Core/AIService.cs (ToCanonicalKey usage)
- Brainarr.Plugin/Services/Core/ProviderCalibrationProfile.cs (ZaiCoding profile)
- Brainarr.Plugin/Services/Core/BrainarrActionHandler.cs (getZaiCodingModels)
- Brainarr.Plugin/Services/Core/ImportListActionHandler.cs (ZaiCoding case)
- Brainarr.Plugin/Services/Core/ModelActionHandler.cs (ZaiCoding case)
- Brainarr.Plugin/Services/Core/CanonicalModelMapper.cs (ZaiCoding mapping)
- Brainarr.Plugin/Services/Registry/ProviderSlugs.cs (zaicoding slug)
- Brainarr.Plugin/BrainarrSettings.Providers.cs (ZaiCoding API key routing)
- Brainarr.Plugin/Services/Providers/Llm/BrainarrZaiCodingProvider.cs (new — Z.AI Coding provider)
- Brainarr.Tests/Providers/Llm/BrainarrZaiCodingProviderTests.cs (new)
- Brainarr.Tests/Services/RateLimitKeyResolutionTests.cs (new)
- Brainarr.Tests/ProviderRegistryCovTests.cs (expectedCount=14)
- Brainarr.Tests/Services/Core/ProviderGoldenFixtureTests.cs (snapshot count 14)
- Brainarr.Tests/Services/Registry/ProviderSlugsTests.cs (ZaiCoding mapping)

THE THREE FILES THAT NEED CO-EVOLUTION (auth-gate AND ZaiCoding both touch)

ProviderRegistry.cs and BrainarrZaiGlmProvider.cs both saw edits from
each workstream. The Z.AI-only versions are on cleanup/delete-dead-
enhanced-rate-limiter HEAD. You need to re-apply your auth-gate edits
ON TOP of those, not replace them. Specifically:

ProviderRegistry.cs (HEAD has ZaiCoding registration + expanded MapZaiGlmModel
+ new MapZaiCodingModel):
- Add IAuthFailureGateRegistry? _authGateRegistry field
- Add overloaded constructors that accept authGateRegistry
- Modify EVERY existing Register(...) factory body to pass
  authGateRegistry: _authGateRegistry into the provider constructor
- Including the BrainarrZaiCodingProvider call — that provider currently
  has no authGateRegistry parameter; you'll need to add one to it (mirror
  the pattern from BrainarrZaiGlmProvider)

BrainarrZaiGlmProvider.cs (HEAD has the 1113 mapping + MapZaiHttpError +
TryParseZaiErrorCode + ZaiErrorEnvelope DTOs):
- Add IAuthFailureGateRegistry? _authGateRegistry field
- Add overloaded constructor that accepts authGateRegistry
- In SendAsync, wrap the _httpClient.ExecuteAsync(request) call with
  AuthGatedSend.ExecuteAsync(_authGateRegistry, ProviderIdConst, () => ...)
- Do NOT touch MapZaiHttpError, TryParseZaiErrorCode, or the
  ZaiErrorEnvelope DTOs — those are the 1113 fix and already correct

BrainarrZaiCodingProvider.cs (NEW file on HEAD, currently has no auth-gate):
- Add same auth-gate plumbing as BrainarrZaiGlmProvider gets
- Constructor overload taking IAuthFailureGateRegistry?
- Wrap the _httpClient.ExecuteAsync call in AuthGatedSend.ExecuteAsync
- Re-add the using statements:
    using Lidarr.Plugin.Common.Services.Bridge;
    using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

SUGGESTED WORKFLOW

1. Read this whole prompt + git log of branches `recovered/wip-auth-gate-and-zai-2026-05-23`
   and `cleanup/delete-dead-enhanced-rate-limiter` to confirm the split.

2. `git checkout cleanup/delete-dead-enhanced-rate-limiter`
   `git pull`
   `git checkout -b feature/auth-failure-gate-registry`

3. Extract your auth-gate files from the recovery branch one at a time using
   `git checkout recovered/wip-auth-gate-and-zai-2026-05-23 -- <file>` for the
   AuthGate*, AuthGated*, BrainarrModule, BrainarrOrchestratorFactory, and
   the provider files (Anthropic, OpenAI, etc.) that have your auth-gate
   refactor with NO ZaiCoding bleed-through.

4. For ProviderRegistry.cs, BrainarrZaiGlmProvider.cs, and the new
   BrainarrZaiCodingProvider.cs, do NOT `git checkout` from the recovery
   branch — re-apply your auth-gate hunks by hand on top of the current
   HEAD versions (which now have the Z.AI bits).

5. Verify with:
   `dotnet build Brainarr.Plugin/Brainarr.Plugin.csproj -m:1`
   `dotnet test Brainarr.Tests/Brainarr.Tests.csproj --filter "State!=Quarantined&FullyQualifiedName!~DockerE2E" --blame-hang-timeout 30s`

6. All 67 ZaiCoding/RateLimitKeyResolution tests MUST still pass after your
   merge. If they don't, your auth-gate refactor regressed something.

7. Open a PR against the same `cleanup/delete-dead-enhanced-rate-limiter`
   branch (this is the team's working integration branch).

CONSTRAINTS

- Multi-AI workspace: another AI is also pushing to
  cleanup/delete-dead-enhanced-rate-limiter. Rebase, don't merge, before pushing.
- Do not bump the submodule (ext/Lidarr.Plugin.Common) unless you know what
  you're doing — it's pinned by ext-common-sha.txt.
- The submodule's IUniversalAdaptiveRateLimiter exposes RecordAuthFailure;
  your gate registry should record auth failures into that signal so the
  rate limiter can react.
- ResiliencePolicyTests.cs in HEAD has a stub `RecordAuthFailure` on its
  FakeLimiter (added by the Z.AI commit to make the test project compile).
  Your real auth-gate work should reuse that stub or replace it with
  meaningful assertions about gate behavior.

VERIFICATION CHECKLIST BEFORE OPENING PR

- [ ] `dotnet build` clean (0 errors)
- [ ] Full suite passes (2929+ tests, 0 fail)
- [ ] All 67 Z.AI / RateLimitKeyResolution tests still green
- [ ] No conflict markers in any file
- [ ] No changes to any of the "NOT YOURS" files listed above
  (`git diff cleanup/delete-dead-enhanced-rate-limiter --name-only` should
  only list AuthGate-related and provider-constructor-edit files)
- [ ] Commit message explains WHY (incident or class of bug that motivated
  the registry), not just WHAT changed

Start by exploring the codebase to confirm the current state matches this
prompt, then proceed. If anything in this prompt doesn't match what you
see, stop and ask before acting.
```

---

## Quick reference for the originating session

- **Recovery branch** with everything pre-split: `recovered/wip-auth-gate-and-zai-2026-05-23` (commit `86a151d`)
- **Z.AI-only commit** that landed first: `01b8de9` on `cleanup/delete-dead-enhanced-rate-limiter`
- **Common shared touchpoints** that needed re-application by hand (not `git checkout`): `ProviderRegistry.cs`, `BrainarrZaiGlmProvider.cs`, `BrainarrZaiCodingProvider.cs`

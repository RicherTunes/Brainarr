# ADR-002: Subscription Authentication for LLM Providers

**Status:** Accepted
**Date:** 2026-01-31
**Decision Makers:** Project maintainers

## Context

Brainarr supports multiple LLM providers. Users have requested subscription-based authentication (similar to Claude Code CLI's credential file approach) as an alternative to API key billing for OpenAI and Gemini providers.

### Research Objective

Determine feasibility of implementing subscription-based authentication for:
- OpenAI (ChatGPT Plus subscription)
- Google Gemini (Gemini Pro/Ultra subscription)

### Decision Criteria

| Criterion | Threshold |
|-----------|-----------|
| Technical Feasibility | Authentication method exists and is stable |
| Official Support | Documented or explicitly permitted by provider |
| ToS Compliance | Does not violate Terms of Service |
| Reliability | Production-ready, not experimental/buggy |
| Data Protection | Paid tier data privacy guarantees transfer to API |

## Research Findings

### OpenAI Subscription Authentication

**Finding: NO SUBSCRIPTION API EXISTS**

| Aspect | ChatGPT Plus | OpenAI API |
|--------|--------------|------------|
| Payment Model | Flat monthly fee ($20 USD) | Pay-as-you-go (per-token) |
| Interface | Web browser only | Programmatic HTTP |
| Credential Type | Session token (web auth) | API key |
| API Access Included | **NO** | Yes |

**Critical Points:**
- ChatGPT Plus does NOT provide programmatic API access
- API access requires separate pay-as-you-go billing
- No hybrid model exists (unlike Claude Code)

**ToS Implications:**
- "You may not make account access credentials available to third parties"
- Using session tokens programmatically violates ToS
- Account suspension/permanent ban risk

**Verdict: NO-GO**

### Google Gemini Subscription Authentication

**Finding: NO SUBSCRIPTION API EXISTS**

| Aspect | Gemini Subscription | Gemini API |
|--------|---------------------|-----------|
| Payment Model | Monthly fee ($20-80 USD) | Pay-as-you-go |
| Interface | Web/Mobile app | Programmatic HTTP |
| Credential Type | OAuth session | API key |
| API Access Included | **NO** | Yes |

**Critical Points:**
- Gemini subscriptions do NOT automatically grant API access
- Users must separately generate API key in Google AI Studio
- Subscription payment is separate from API usage billing
- OAuth with subscription credentials has known bugs (2025)

**ToS Implications:**
- Personal OAuth + automation = data training risks (free tier policies apply)
- Data privacy protections unclear with subscription OAuth
- No official documentation for subscription-as-auth pattern

**Verdict: NO-GO**

### Comparison: Claude Code Reference Model

| Provider | Subscription = API Access | Credential File Support | Official Documentation |
|----------|--------------------------|-------------------------|------------------------|
| **Claude** | YES | YES (`~/.config/claude-code/auth.json`) | YES |
| **OpenAI** | NO | NO | N/A |
| **Gemini** | NO | NO | N/A |

Claude's model is architecturally unique - OpenAI and Gemini do not offer equivalent functionality.

## Decision

**API Key Authentication Only for OpenAI/Gemini Providers**

Subscription-based authentication is not feasible for OpenAI or Gemini. These providers require separate API key billing regardless of consumer subscription status.

### Rationale

1. **No Technical Path** - Neither provider offers subscription-based API access
2. **ToS Violation Risk** - Extracting session tokens for programmatic use is prohibited
3. **Reliability Concerns** - Session tokens expire, OAuth has bugs, high maintenance burden
4. **No Cost Benefit** - Users would pay subscription AND API usage separately

## Consequences

### Positive

- Clear architecture: API key authentication for all HTTP providers
- ToS compliance: No risk of account bans or legal liability
- Reliability: API keys are stable, well-documented, production-ready
- Simplicity: Single authentication pattern for all HTTP providers

### Negative

- No "subscription mode" for users who prefer flat-rate billing
- Users must manage API keys and monitor usage costs
- Different UX from Claude Code CLI (which supports subscription auth)

### Neutral

- Future re-evaluation if providers add official subscription API access
- Document this decision clearly for users asking about subscription auth

## Implementation

### Current State (Maintained)

**HTTP Providers (OpenAI, Gemini, OpenRouter, DeepSeek, Groq, Perplexity):**
- API key authentication only
- Store credentials via settings UI
- No credential file approach

**CLI Providers (Claude Code CLI):**
- Supports both subscription and API key authentication
- Uses `~/.config/claude-code/auth.json` when available
- Unique to Anthropic's intentional design

### Provider Authentication Matrix

| Provider | Auth Method | Credential Storage | Subscription Supported |
|----------|-------------|-------------------|----------------------|
| Claude Code CLI | API Key or Subscription | Settings UI or credential file | YES |
| OpenAI | API Key | Settings UI | NO |
| Gemini | API Key | Settings UI | NO |
| OpenRouter | API Key | Settings UI | NO |
| DeepSeek | API Key | Settings UI | NO |
| Groq | API Key | Settings UI | NO |
| Perplexity | API Key | Settings UI | NO |
| Ollama | None (local) | N/A | N/A |
| LM Studio | None (local) | N/A | N/A |

### Security Recommendations

- Store API keys in Lidarr's encrypted settings storage
- Implement API key rotation guidance (60-90 day policy recommended)
- Log key prefixes only (never full keys) for debugging
- Validate key format before API calls

## Future Work

### Re-evaluation Triggers

Monitor for changes that would warrant revisiting this decision:

1. **OpenAI** announces subscription-based API access tier
2. **Google** fixes Gemini CLI subscription authentication bugs
3. **Either provider** documents credential file approach for tools

### Re-evaluation Timeline

- Next review: 2026 Q3
- Monitor GitHub issues for Gemini CLI auth improvements
- Monitor OpenAI announcements for API tier changes

## References

- OpenAI API Authentication: https://platform.openai.com/docs/api-reference/authentication
- Google Gemini API Authentication: https://ai.google.dev/gemini-api/docs/api-key
- ADR-001: Streaming Architecture (related decision on HTTP vs CLI capabilities)

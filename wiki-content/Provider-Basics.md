# 🧩 Provider Basics

Quick reference for core provider settings used across Brainarr.

## 🧩 Choosing a Provider

- Local (Privacy-First): Ollama, LM Studio — best for zero cost and full control.
- Gateway: OpenRouter — one key for 200+ models, great for flexibility.
- Budget: DeepSeek, Gemini — low cost or generous free tiers.
- Speed: Groq — ultra-fast inference.
- Premium: OpenAI, Anthropic — highest quality.

Tip: Start local if possible; switch to cloud for larger context or premium quality.

## 🔗 Configuration URL

- Local providers:
  - Ollama: `http://localhost:11434` (or LAN/host address inside Docker)
  - LM Studio: `http://localhost:1234`
- Cloud providers: URL is managed by the SDK; enter your API key instead.

If running in Docker and targeting a host service, prefer `host.docker.internal` (Mac/Windows) or the host LAN IP.

## 🔐 API Keys

- Keep keys secret; never share in logs.
- Paste the key for your chosen cloud provider; no key needed for local providers.
- If validation fails on Test, re-issue a key and try again.


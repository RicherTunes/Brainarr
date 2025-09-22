# Provider Testing Checklist (v1.2.4)

Use this checklist to verify Brainarr provider integrations before marking them as “Tested” in docs. Run through the General steps for every provider, then the provider‑specific checks.

## General

- Environment: Lidarr 2.14.1.4716+ on `nightly` (plugins branch)
- Plugin: Brainarr v1.2.4 installed and enabled
- Health: No errors on Lidarr startup about plugin loading
- Configure provider in Brainarr settings and save without validation errors
- Model discovery works (if applicable) and the selected default model exists
- Test run: Trigger an Import List refresh and confirm recommendations are produced
- Rate limiting: Typical refresh succeeds without hitting provider limits
- Error handling: Temporary provider failure surfaces a clear message, and retries work

## LM Studio (Local)

- LM Studio Local Server running on `http://localhost:1234`
- Load a model in LM Studio (e.g., Qwen 3 or Llama 3 8B)
- Brainarr detects available models via `/v1/models`
- Test run produces recommendations; no network/API keys required
- Optional: Validate long contexts (e.g., Qwen 3 at ~40–50K tokens across GPU+CPU)

## Google Gemini (Cloud)

- API key created at https://aistudio.google.com/apikey
- Free tier rate limits understood (15 RPM, 1.5K RPD)
- Default model `gemini-1.5-flash` selected (or Pro if desired)
- Test run produces recommendations consistently
- Note any provider‑side rate limiting or timeouts during heavier usage

## Perplexity (Cloud)

- API key created at https://perplexity.ai/settings/api
- Default model `llama-3.1-sonar-large-128k-online` (Sonar)
- Test run produces web‑enhanced results
- If using Pro subscription, note $5/month API credit availability in docs

## Recording Results

- Update docs/PROVIDER_SUPPORT_MATRIX.md with “Tested” and Last Verified date
- Update docs/PROVIDER_GUIDE.md “Last Verified” for the provider section
- Update README Provider Status snippet
- Add a note in CHANGELOG and release notes for the version

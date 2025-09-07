# 🎯 Reducing Hallucinations (Local Models)

Practical settings and prompts to keep LM Studio and Ollama recommendations grounded in real, existing albums.

## Core Principles

- Prefer models strong at instruction-following: Qwen, Llama, Mistral, Phi.
- Lower temperature for precision: 0.2–0.6 works best for curation tasks.
- Force strict JSON shape and forbid prose/markdown.
- Explicitly require “only real, existing albums” and exclude speculative editions.
- Post-validate with MusicBrainz in Brainarr (now enabled) to filter any stragglers.

---

## LM Studio

### Recommended Models
- qwen/Qwen2.5 7B–14B instruct variants
- mistralai/Mistral-7B-Instruct-v0.x
- meta-llama/Llama-3.x Instruct (Q4_K_M quantized if memory limited)
- microsoft/Phi-3-Medium Instruct

### Server Settings
- Temperature: 0.3–0.5
- Top P: 0.9 (default is fine)
- Max Tokens: 800–1200 (5–10 items)
- Streaming: off

### System Prompt Template
Use this pattern for precise outputs (Brainarr ships with a variant):

"You are a music recommendation engine. Return ONLY a valid JSON array with 5–10 items. Each item: artist (string), album (string), genre (string), year (int), confidence (0..1), reason (string). Only include real, existing studio albums that can be found on MusicBrainz or Qobuz. Do NOT invent special editions, remasters, or speculative releases. If uncertain an album exists, exclude it. No prose, no markdown, no extra keys."

### Operational Tips
- Keep one high‑quality model loaded; avoid frequent model switches.
- Ensure the Local Server URL in Brainarr matches LM Studio (default http://localhost:1234).
- If JSON gets messy, lower temperature and reduce max tokens.

---

## Ollama

### Recommended Models
- qwen2.5:latest (best balance)
- llama3.2:latest or :8b (balanced)
- mistral:latest (lightweight)
- phi3:medium (high quality, slower)

### Run Hints
- Keep server alive to avoid model cold starts (`OLLAMA_KEEP_ALIVE=30m`).
- GPU acceleration improves quality/speed; ensure drivers installed.

### Prompting
Apply the same system prompt principles. For CLI testing:

```bash
ollama run qwen2.5:latest "You are a music recommendation engine. Return ONLY a valid JSON array with 5–10 items..."
```

If you template prompts, add strict instructions about JSON only + real albums.

### Parameters
- Temperature: 0.3–0.6
- Num Predict (max tokens): 800–1200
- Top P: 0.9

---

## Brainarr Safeguards

- MusicBrainz pre‑resolution: Brainarr resolves recommendations to MusicBrainz IDs (artist and/or release‑group MBIDs) where possible, improving matching.
- Library duplicate filter: Removes albums already in your library.
- Caching: Prevents repeat calls for the same profile/settings.

Safety Gates (see [[Advanced Settings#safety-gates]]):
- Minimum Confidence: drop or queue items below the threshold.
- Require MusicBrainz IDs: only add when MBIDs are resolved.
- Queue Borderline Items: send borderline items to the Review Queue instead of discarding.

---

## Troubleshooting Hallucinations

- Too many imaginary editions:
  - Lower temperature; add “No special editions/remasters” to the system prompt.
  - Use Qwen or Phi models (stronger instruction‑following).

- Valid JSON still fails to add in Lidarr:
  - The MBID pre‑resolution step may be filtering them; verify the album exists on musicbrainz.org.
  - Try less niche targets (wider known discography) to test pipeline.

- Output drifts into prose/markdown:
  - Reduce max tokens; reiterate “ONLY JSON array, no prose” in the system prompt.
  - Ensure “Streaming” is disabled.

---

## Import Race/Lock Guidance

If you see “file being used by another process” during tag reads:
- Downloader: write as `.partial` (or in a staging folder), then atomically rename/move when complete.
- Lower concurrent downloads for large albums (e.g., from 10 → 3–5).
- Add a short post‑download delay before import (Lidarr Completed Download Handling).

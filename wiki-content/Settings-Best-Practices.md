# ⚙️ Settings Best Practices

Opinionated defaults that work well in practice, tuned for quality and simplicity. Use this as a quick reference when configuring Brainarr.

## Quick Recommendations

- Library Sampling: Balanced (default). Use Comprehensive for powerful local/cloud models.
- Backfill Strategy: Aggressive (default). Use Off/Standard to reduce iterations.
- Discovery Mode: Adjacent (default). Switch to Similar if results are too random; Exploratory if you want more adventurous picks.
- Recommendation Mode: Specific Albums for tighter curation. Artists mode when you want to build out discographies.

## By Provider Type

### Local (Ollama / LM Studio)
- Library Sampling: Comprehensive for large-context models (Qwen3, Llama) to maximize personalization.
- Backfill Strategy: Standard to start; Aggressive for large libraries where duplicates are common.
- Tip: Brainarr scales token budgets for local providers; Comprehensive can reach ~40k tokens.

### Cloud (OpenAI, Anthropic, Gemini, DeepSeek, Groq, OpenRouter, Perplexity)
- Library Sampling: Balanced for cost/latency; Comprehensive when using premium large‑context models.
- Backfill Strategy: Standard (default). Aggressive if you want guaranteed counts.

## By Library Size

### Small (< 500 albums)
- Max Recs: 10–15
- Sampling: Balanced
- Backfill: Standard

### Medium (500–2000 albums)
- Max Recs: 15–25
- Sampling: Balanced → Comprehensive if quality feels generic
- Backfill: Standard; Aggressive if you need exactly N

### Large (2000+ albums)
- Max Recs: 20–40
- Sampling: Comprehensive (more context reduces duplicates)
- Backfill: Aggressive for better fill; expect top-ups due to dedupe

## Advanced Tips

- Initial Oversampling: Enabled automatically with Standard/Aggressive Backfill to reduce iterations and increase unique hits on the first pass.
- Confidence/MBIDs: Keep min confidence at 0.7; require MBIDs in Albums mode; optional in Artists mode.
- Recently Added Bias: Comprehensive sampling includes recently added artists to reflect current taste.

## Known Good Combos

- “Efficient Local”: Ollama + Qwen2.5, Sampling=Balanced, Backfill=Standard, Recs=10–15
- “Deep Local”: LM Studio + Qwen3 32k, Sampling=Comprehensive, Backfill=Aggressive, Recs=20–30
- “Premium Cloud”: Claude Sonnet, Sampling=Comprehensive, Backfill=Standard, Recs=15–25

---

If in doubt, start simple: Balanced + Standard, Adjacent, Specific Albums, 10–15 recs. Iterate from there.

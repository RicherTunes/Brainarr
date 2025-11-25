# Prompt Strategy — Comprehensive Sampling & Styles

This proposal was implemented across the Brainarr 1.3.0 planning refactor. For current behaviour consult:

- [`CHANGELOG.md`](../CHANGELOG.md) — entries under 1.3.0 documenting deterministic planning, sampling shapes, and style-aware prompts.
- Wiki [Advanced Settings ▸ Sampling](https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#sampling) — how to tune `sampling_shape`, style filters, and relaxed expansion.
- `Brainarr.Plugin/Services/Planning/` — source of truth for planner, renderer, and style matching logic.

Historical design rationale remains in git history (`git show b963...:docs/PLAN_Prompt_Comprehensive_Styles.md`). Add new design notes to `docs/architecture/` or the wiki Design Notes section instead of reviving this file.

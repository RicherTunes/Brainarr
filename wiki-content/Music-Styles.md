# Music Styles — Selection, Strict Matching, and Preview

## Overview
This page explains how Brainarr’s Music Styles feature works: selecting styles, strict matching, dynamic catalog updates, and using the preview to understand coverage before running recommendations.

## Key Concepts
- Dynamic Catalog: A curated JSON catalog of styles (with aliases and parent relationships) is embedded and auto‑updates from GitHub when available.
- Strict Matching (default): When styles are selected, results must belong to those styles only. No widening beyond the chosen set.
- Relax Matching (advanced): Maintainers can enable `RelaxStyleMatching` to allow parent/adjacent widening. Default is OFF.
- Selection Cap: A soft cap (default 10) ensures focus. If exceeded, the most prevalent styles in your library are applied; the rest are ignored with a log note.

## Selecting Styles (TagSelect)
- Field: `Music Styles` on the Brainarr settings page.
- Typeahead: Start typing to search the catalog by name or alias (e.g., "Prog Rock").
- No query: The list shows top‑in‑library styles with coverage counts (e.g., "Progressive Rock — 27").
- Saved selection: Brainarr normalizes selected values to canonical style slugs.

## Strict Enforcement
When one or more styles are selected:
- Sampling: Artists/albums are prefiltered to match the selected styles (alias/parent aware). In strict mode, no widening is performed.
- Prompt: A dedicated `STYLE FILTERS` block and rule are included to guide the model.
- Guardrail: The pipeline drops any out‑of‑style results if the model drifts.

When no styles are selected:
- Recommendations remain library‑centric and avoid generic content. No style block is added.

## Preview Coverage
- Endpoint: `styles/preview` (used by the UI to show counts before running).
- Response: Coverage counts for the current selection (artists and albums) relative to your library.
- Use cases:
  - Fine‑tune selections to achieve enough coverage.
  - Remove sparse styles that lead to small candidate sets.

## Token Budgets and “Comprehensive” Mode
- Model‑aware token ceilings are applied in Comprehensive mode so larger prompts stay within limits.
- Compression: Albums are grouped by artist and elided with “+ N more …” to save tokens.
- Override: Maintainers can provide an advanced `ComprehensiveTokenBudgetOverride` if needed.

## Tips
- Start narrow (1–3 styles) for focused results. Add more if you need variety.
- If coverage is too low, try a parent style or enable relax mode (advanced) temporarily.
- Leverage the preview to balance focus and coverage before running.

## Troubleshooting
- No options appear: Ensure the embedded catalog is loaded; type at least 2 characters. If the remote source is down, the embedded catalog is still used.
- Empty results with strict matching: Reduce the number of selected styles, choose broader parents, or visit relax mode (advanced).
- Large libraries: Expect more aggressive list compression in the prompt; this is normal to stay under model limits.
# Music Styles — Selection, Strict Matching, and Preview

## Overview
This page explains how Brainarr’s Music Styles feature works: selecting styles, strict matching, dynamic catalog updates, and using the preview to understand coverage before running recommendations.

## Key Concepts

- Dynamic Catalog: A curated JSON catalog of styles (with aliases and parent relationships) is embedded and auto‑updates from GitHub when available.
- Strict Matching (default): When styles are selected, results must belong to those styles only. No widening beyond the chosen set.
- Relax Matching (advanced): Maintainers can enable `RelaxStyleMatching` to allow parent/adjacent widening. Default is OFF.
- Selection Cap: A soft cap (default 10) ensures focus. If exceeded, the most prevalent styles in your library are applied; the rest are ignored with a log note.

## Selecting Styles (TagSelect)

- Field: `Music Styles` on the Brainarr settings page.
- Typeahead: Start typing to search the catalog by name or alias (e.g., "Prog Rock").
- No query: The list shows top‑in‑library styles with coverage counts (e.g., "Progressive Rock — 27").
- Saved selection: Brainarr normalizes selected values to canonical style slugs.

## Strict Enforcement

When one or more styles are selected:

- Sampling: Artists/albums are prefiltered to match the selected styles (alias/parent aware). In strict mode, no widening is performed.
- Prompt: A dedicated `STYLE FILTERS` block and rule are included to guide the model.
- Guardrail: The pipeline drops any out‑of‑style results if the model drifts.

When no styles are selected:

- Recommendations remain library‑centric and avoid generic content. No style block is added.

## Preview Coverage

- Endpoint: `styles/preview` (used by the UI to show counts before running).
- Response: Coverage counts for the current selection (artists and albums) relative to your library.
- Use cases:
  - Fine‑tune selections to achieve enough coverage.
  - Remove sparse styles that lead to small candidate sets.

## Token Budgets and “Comprehensive” Mode

- Model‑aware token ceilings are applied in Comprehensive mode so larger prompts stay within limits.
- Compression: Albums are grouped by artist and elided with “+ N more …” to save tokens.
- Override: Maintainers can provide an advanced `ComprehensiveTokenBudgetOverride` if needed.

## Tips

- Start narrow (1–3 styles) for focused results. Add more if you need variety.
- If coverage is too low, try a parent style or enable relax mode (advanced) temporarily.
- Leverage the preview to balance focus and coverage before running.

## Troubleshooting

- No options appear: Ensure the embedded catalog is loaded; type at least 2 characters. If the remote source is down, the embedded catalog is still used.
- Empty results with strict matching: Reduce the number of selected styles, choose broader parents, or visit relax mode (advanced).
- Large libraries: Expect more aggressive list compression in the prompt; this is normal to stay under model limits.

## Catalog Maintenance

- Source of truth: `Brainarr.Plugin/Resources/music_styles.json` (embedded) with automatic refresh from `BrainarrConstants.StylesCatalogUrl`.
- Slugs: lowercase, hyphen-separated; unique across the catalog (e.g., `progressive-rock`).
- Aliases: common alternate names mapped to a single canonical slug; avoid duplicates and keep them concise.
- Parents: use human-readable names that exist in the catalog; prefer broad categories (e.g., Rock, Electronic). Multiple parents allowed.
- Validation: run unit tests to ensure unique slugs and sane alias mappings.
- Contribution tips: group related additions in one PR; include a short note describing new styles and reasoning.


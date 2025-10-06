# Recommendation Modes

Recommendation modes describe how Brainarr balances artist versus album sampling. The canonical explanations and UI screenshots now live in the wiki’s [Advanced Settings ▸ Recommendation Modes](https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#recommendation-modes) section.

Key pointers:

- The enum values (`SpecificAlbums`, `Artists`, etc.) and defaults are documented in `Brainarr.Plugin/Configuration/RecommendationMode.cs`.
- Behavioural differences (discography imports, token budgets, sampling shape) are covered in the wiki alongside other advanced tuning knobs.
- Release notes summarise changes when new modes or defaults ship (see `CHANGELOG.md`).

If you extend recommendation behaviour, update the wiki page and `CHANGELOG.md` first. Then, if a deep-dive doc is still required, link it here rather than duplicating the tables or step-by-step examples.

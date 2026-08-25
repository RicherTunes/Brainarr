
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Music Styles

The **Music Styles** field narrows recommendations to the genres and styles you care about. Brainarr ships
an embedded styles catalog that normalises what you type, so "Prog Rock", "progressive rock" and
"Progressive-Rock" all resolve to the same canonical style.

Canonical detail: [docs/configuration.md ▸ Styles catalog](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#styles-catalog).

## How selections are used

Selected styles are added to the prompt as anchors and are also used when filtering results, so they shape
both what the model is asked for and what survives validation.

Two modes fall out of that, depending on your library:

- **Styles your library already covers** — recommendations stay grounded in your existing collection and
  are steered toward those styles.
- **Styles your library has no coverage of at all** — Brainarr switches to genre-first discovery and
  recommends artists *of* those styles instead of neighbours of what you already own. This is the way to
  deliberately open a new corner of a collection.

## Free-text styles

You can type a style that is not in the catalog. It is carried through as a free-form anchor rather than
being dropped, so niche or very new genre names still work — they just do not get catalog normalisation or
similarity matching.

## Notes

- The catalog refreshes periodically from a canonical JSON in this repository. If that fetch fails the
  embedded catalog stays authoritative, so style matching never depends on network access.
- Selecting a large number of styles dilutes the prompt; a handful of specific styles steers results more
  effectively than a long list.
- If matching looks wrong, confirm the style resolved to the slug you expected — see
  [Troubleshooting](Troubleshooting.md#quick-triage), and turn on
  [Log Per-Item Decisions](Troubleshooting.md#reading-brainarr-logs) to see why individual items were
  kept or dropped.

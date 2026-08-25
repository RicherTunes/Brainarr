
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Confidence Calibration

Every recommendation carries a confidence score, but providers do not agree on what a number means: one
model's `0.8` is another's `0.6`. Calibration rescales those raw scores onto a common range so that a single
**Minimum Confidence** threshold behaves consistently no matter which provider produced the item.

Canonical implementation: `Brainarr.Plugin/Services/Core/ProviderCalibrationProfile.cs`.

## What the setting does

**Enable Provider Calibration** (advanced, on by default) applies a per-provider adjustment before the
score is compared against your [safety gates](Advanced-Settings.md#safety-gates) and before triage decides
whether an item is accepted, reviewed, or rejected. Turn it off to see raw, uncalibrated scores exactly as
the provider reported them — useful when you are diagnosing a provider's scoring behaviour, and rarely what
you want in normal operation.

Each provider has a profile with a scale factor, a bias, and a quality tier; the calibrated value is
clamped to the 0–1 range. Providers with no profile are left untouched.

## How it interacts with the confidence floor

- With calibration **on**, **Minimum Confidence** is effectively provider-neutral: raising it tightens
  results by a comparable amount across providers.
- With calibration **off**, the same threshold is stricter for providers that score conservatively and
  looser for optimistic ones, so you may need a different threshold per provider.

## Items with no score

Not every model returns a confidence value. Brainarr tracks whether a score was actually provided, and
score-less items **bypass the confidence floor entirely** rather than being judged against an invented
default — otherwise a provider that never scores would be filtered to nothing. Those items are still
subject to the confidence-independent checks (MusicBrainz id, duplicates), and triage marks them as
unscored instead of guessing a band for them.

## When to change it

Leave it on unless you are investigating scoring itself. If results feel uniformly too strict or too loose,
adjust [Minimum Confidence](Advanced-Settings.md#safety-gates) first — that is the intended control. Use
the [Review Queue](Review-Queue.md) to see which items a threshold change would have kept or dropped
before you commit to it.

# User Setup Guide (Clean)

> Compatibility\n> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly). For the canonical notice, see the README compatibility section.

## How to use this guide

1. Finish the [README quick start](../README.md#quick-start) so Brainarr is built, installed, and visible inside Lidarr.
2. Consult the [Brainarr AI Provider Guide](PROVIDER_GUIDE.md) and wiki provider pages to pick your primary and fallback models.
3. Use the steps below inside Lidarr to wire everything together and validate the installation.

## Step 1 — Prepare the environment

- Run `./setup.ps1` (Windows) or `./setup.sh` (macOS/Linux) from the repository root to fetch Lidarr assemblies and restore the Brainarr solution.
- Optional: `pwsh ./build.ps1 --test` to run the full validation suite before deploying to production.
- Keep `ext/Lidarr/_output/net6.0` intact—this is where the setup scripts place the assemblies Lidarr expects.

## Step 2 — Choose and configure a provider

- Reference the [Provider status matrix](PROVIDER_MATRIX.md) for current verification notes.
- Follow the matching wiki article for authentication details and rate-limit guidance: [Local Providers](https://github.com/RicherTunes/Brainarr/wiki/Local-Providers) or [Cloud Providers](https://github.com/RicherTunes/Brainarr/wiki/Cloud-Providers).
- Record API keys using the secure storage that matches your operating system (Keychain, Credential Manager, libsecret, etc.).

## Step 3 — Add the Brainarr import list in Lidarr

1. In Lidarr, go to **Settings → Import Lists → Add (+)**.
2. Select **Brainarr** and choose a descriptive name (e.g., `AI Music Recommendations`).
3. Set **Enable Automatic Add** to *Yes* unless you plan to gate every recommendation manually.
4. Pick the **Quality Profile**, **Metadata Profile**, and **Root Folder** that match your library.
5. Add any tags (for example `ai-recommendations`) so you can filter lists later.
6. Save to create the list—the plugin will render provider-specific settings underneath.

## Step 4 — Wire up providers and test connectivity

1. In the provider panel, choose your **Primary Provider** and optional **Fallback Providers**.
2. Supply required credentials or base URLs. Use local endpoints (`http://localhost:11434`, `http://localhost:1234`, etc.) for Ollama/LM Studio.
3. Click **Test**. A green toast confirms Brainarr can authenticate and query the provider. Resolve failures using the troubleshooting links in the UI or the wiki.
4. (Optional) Enable additional providers and set their priorities if you want automatic failover—Brainarr will advance through enabled providers in ascending priority when the primary errors or exceeds quotas.

## Step 5 — Request the first recommendations

1. Open **Import Lists → Brainarr → Manual** and trigger **Fetch**.
2. Inspect the generated recommendations; approve the albums you want to monitor.
3. Schedule automatic refreshes in **Import Lists → Options → Interval** once you are satisfied with the output.
4. Track provider usage and headroom via Lidarr **System → Logs** (`Brainarr:` entries) and your provider dashboards.

## Ongoing operations

- **Review queue workflow:** Follow the “Operations” section in the wiki to triage recommendations, invalidate cache entries, and monitor prompt metrics.
- **Observability:** Metrics and log field definitions are centralised in the wiki’s **Observability & Metrics** page.
- **Upgrades:** When updating Brainarr, rerun the setup script and review the [CHANGELOG](../CHANGELOG.md) for migrations or new settings.

## Troubleshooting

- Start with `docs/troubleshooting.md` for common failure modes (authentication, token budgets, cache states).
- Provider-specific issues (429/401 responses, model discovery failures) are linked from the wiki provider pages.
- If Brainarr fails to load, confirm your Lidarr branch matches the README requirement and check **System → Logs** for `Brainarr: minVersion` messages.

Keep this guide focused on the user-facing workflow. Any time a step changes (new settings, provider parameter, etc.), update `docs/providers.yaml` or the relevant wiki page so every surface stays aligned.

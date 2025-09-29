# Installation

> Compatibility
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

Follow the README [Quick start](../README.md#quick-start) first—these steps assume Brainarr is already cloned/built and Lidarr is on the nightly branch. Keep installation details in sync with the README and `docs/USER_SETUP_GUIDE.md`.

## Install via Lidarr UI (recommended)

1. In Lidarr, set **Settings → General → Updates → Branch = nightly**.
2. Go to **Settings → Plugins → Add Plugin**.
3. Enter repository URL `https://github.com/RicherTunes/Brainarr` and install.
4. Restart Lidarr when prompted.
5. Confirm **Settings → Import Lists → Add → Brainarr** shows the plugin.

## Manual installation

1. Download the latest `Brainarr-*.zip` from GitHub Releases.
2. Extract into the plugin directory (`owner/name` layout):
   - Linux: `/var/lib/lidarr/plugins/RicherTunes/Brainarr/`
   - Windows: `C:\ProgramData\Lidarr\plugins\RicherTunes\Brainarr\`
   - Docker: `/config/plugins/RicherTunes/Brainarr/`
3. Ensure `Lidarr.Plugin.Brainarr.dll` and `plugin.json` sit in that folder.
4. Restart Lidarr/container.

## Verification

- **Settings → Plugins** lists Brainarr.
- **Settings → Import Lists → Add** includes Brainarr.
- If not, revisit the README compatibility notice and consult [Troubleshooting](Troubleshooting).

Need help after install? Continue with the [First Run Guide](First-Run-Guide) and provider walkthroughs.

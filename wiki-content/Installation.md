# Installation

> Compatibility
> Requires Lidarr 2.14.1.4716+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

## Install via Lidarr UI (Recommended)

1. In Lidarr, go to Settings > General > Updates and set Branch = nightly.
2. Go to Settings > Plugins > Add Plugin.
3. Enter repository URL: `https://github.com/RicherTunes/Brainarr` and Install.
4. Restart Lidarr when prompted.
5. Go to Settings > Import Lists > Add New > Brainarr.

Benefits:
- Automatic updates and built-in plugin management.
- Works across Docker/Windows/Linux.

## Manual Installation

1. Download the latest release from GitHub Releases.
2. Extract files to your plugin directory (owner/name layout):
   - Linux: `/var/lib/lidarr/plugins/RicherTunes/Brainarr/`
   - Windows: `C:\\ProgramData\\Lidarr\\plugins\\RicherTunes\\Brainarr\\`
   - Docker: `/config/plugins/RicherTunes/Brainarr/`
3. Ensure `Lidarr.Plugin.Brainarr.dll` and `plugin.json` are in the same folder.
4. Restart Lidarr (or the container).

## Verification

After restart:
1. Settings > Plugins: confirm Brainarr is listed.
2. Settings > Import Lists > Add New: Brainarr should be available.
3. If missing, check logs and file layout. See Troubleshooting.

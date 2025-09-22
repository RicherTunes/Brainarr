# Deployment Guide (Brainarr v1.2.4)

This guide complements the quick-start instructions in the [README](../README.md) with detailed deployment workflows.

> **Compatibility**
> Lidarr 2.14.1.4716+ on the nightly/plugins branch is required. Set Settings ➜ General ➜ Updates ➜ Branch = `nightly` before installing the plugin.

## Recommended: Install via Lidarr Plugin Gallery

1. Open Lidarr ➜ **Settings** ➜ **Plugins**.
2. Select **Add Plugin**.
3. Enter the GitHub URL: `https://github.com/RicherTunes/Brainarr`.
4. Confirm the install and restart Lidarr when prompted.
5. Configure the import list under **Settings** ➜ **Import Lists** ➜ **Add** ➜ **Brainarr**.

Benefits:

- Automatic updates through Lidarr’s plugin manager.
- Works identically on Docker, Windows, and Linux.
- No manual file copies or service restarts beyond the initial reboot.

## Manual Installation (Advanced / Offline)

Use this when installing from a release asset or if the plugin gallery cannot reach GitHub.

### 1. Download Artifacts

- Grab the latest release zip from <https://github.com/RicherTunes/Brainarr/releases> **or** build from source (`dotnet build -c Release` after running the repository setup script).
- Required files: `Lidarr.Plugin.Brainarr.dll`, `plugin.json`, and the `docs/models.example.json` + `docs/models.schema.json` assets.

### 2. Copy Files to the Lidarr Plugin Directory

| Platform | Path |
|----------|------|
| Docker   | `/config/plugins/RicherTunes/Brainarr/` |
| Linux    | `/var/lib/lidarr/plugins/RicherTunes/Brainarr/` |
| Windows  | `C:\ProgramData\Lidarr\plugins\RicherTunes\Brainarr\` |
| macOS    | `~/Library/Application Support/Lidarr/plugins/RicherTunes/Brainarr/` |

Create the directory if it does not exist, then copy the files and ensure the Lidarr service account can read them.

### 3. Restart Lidarr

- Docker: `docker restart lidarr`
- systemd: `sudo systemctl restart lidarr`
- Windows Service: restart from Services.msc or PowerShell (`Restart-Service Lidarr`)

### 4. Verify

- Check the Lidarr logs for `Loaded plugin: Brainarr`.
- Confirm **Settings ➜ Import Lists ➜ Add ➜ Brainarr** is available.

## Optional: External Model Registry

Brainarr 1.2.4 adds an opt-in JSON registry for provider metadata.

1. Set `BRAINARR_USE_EXTERNAL_MODEL_REGISTRY=true` before starting Lidarr.
2. (Optional) Set `BRAINARR_MODEL_REGISTRY_URL=https://…/models.json` to point at your hosted registry. When unset, Brainarr uses the embedded `docs/models.example.json` and caches downloads under `%TEMP%/Brainarr/ModelRegistry/`.
3. Restart Lidarr. Watch the logs for `Brainarr: External model registry enabled …` to confirm activation.
4. To force a refresh, delete the cached file and restart Lidarr.

## Build from Source (for Contributors)

1. Clone the repository and run `./setup.sh` (Linux/macOS) or `./setup.ps1` (Windows). The script pulls Lidarr assemblies into `ext/` and restores the solution.
2. Build the plugin: `dotnet build -c Release` (or use `./build.sh --package` / `./build.ps1 -Package`).
3. The output assembly and manifest are under `Brainarr.Plugin/bin/Release/net6.0/`.
4. Follow the manual installation steps above to copy the artifacts into Lidarr’s plugin directory.

## Troubleshooting Deployment

- **Plugin missing from list**: ensure you are on Lidarr nightly, restart the service, and confirm files reside under `RicherTunes/Brainarr`.
- **Version mismatch**: verify `plugin.json` `version` and `minimumVersion` match the release you installed (1.2.4 and 2.14.1.4716 respectively).
- **Permission errors**: the Lidarr service account must have read access to the plugin folder. On Linux, `chown -R lidarr:lidarr /var/lib/lidarr/plugins/RicherTunes/Brainarr`.
- **Registry issues**: if `BRAINARR_USE_EXTERNAL_MODEL_REGISTRY` is true but the registry fails to load, Brainarr logs the error and falls back to static defaults. Fix the remote JSON or clear the cache to retry.

For additional support see [docs/TROUBLESHOOTING.md](TROUBLESHOOTING.md) and the wiki’s [Troubleshooting](../wiki-content/Troubleshooting.md) page.

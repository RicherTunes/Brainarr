# Brainarr Migration Guide

This guide helps you upgrade between Brainarr versions and migrate your configuration.

## Version Compatibility

| Brainarr Version | Lidarr Version | .NET Version | Breaking Changes |
|-----------------|----------------|--------------|------------------|
| 1.0.0           | 4.0.0+         | 6.0+         | Initial release  |

## Upgrading from Pre-1.0 Versions

If you're using a development or beta version of Brainarr, follow these steps:

### 1. Backup Your Configuration

Before upgrading, backup your Lidarr configuration:

```bash
# Linux/Docker
cp -r /var/lib/lidarr /var/lib/lidarr.backup

# Windows
xcopy "C:\ProgramData\Lidarr" "C:\ProgramData\Lidarr.backup" /E /I
```

### 2. Export Import List Settings

1. Navigate to Settings > Import Lists in Lidarr
2. Click on your Brainarr import list
3. Note down all configured settings:
   - AI Provider selection
   - API Keys (store securely)
   - Model names
   - Discovery mode
   - Custom URLs for local providers

### 3. Remove Old Version

```bash
# Stop Lidarr
systemctl stop lidarr

# Remove old plugin
rm -rf /var/lib/lidarr/plugins/RicherTunes/Brainarr

# For Windows
# Delete C:\\ProgramData\\Lidarr\\plugins\\RicherTunes\\Brainarr folder
```

### 4. Install New Version

Follow the standard installation instructions in the README.

### 5. Reconfigure Settings

After installation, reconfigure your Brainarr import list with the settings you exported in step 2.

## Configuration Migration

### Provider Settings Migration

#### Ollama Provider

If you were using a custom Ollama model that's no longer default:

**Old Default**: `llama3`
**New Default**: `qwen2.5:latest`

To keep using your old model:

1. Go to Settings > Import Lists > Brainarr
2. Set Model Name to `llama3` (or your preferred model)
3. Save settings

#### LM Studio Provider

LM Studio now uses a dedicated implementation:

**Old**: Shared implementation with Ollama
**New**: Dedicated LMStudioProvider class

No action required - the provider will automatically use the correct implementation.

### API Key Migration

API keys are stored in Lidarr's secure configuration. If you need to update them:

1. Navigate to Settings > Import Lists > Brainarr
2. Re-enter your API keys
3. Test connection to verify

### Discovery Mode Settings

Discovery modes remain unchanged:

- **Similar**: Very similar to existing library
- **Adjacent**: Related genres and styles
- **Exploratory**: New genres and territories

## Common Migration Issues

### Issue: Provider Not Found

**Symptom**: "Provider initialization failed" error
**Solution**:

1. Verify the provider is still supported (all 9 providers from v1.0.0+ are maintained)
2. Check if provider URLs have changed in your configuration
3. For local providers, ensure they're running on expected ports

### Issue: Invalid Model Name

**Symptom**: "Model not found" error
**Solution**:

1. Run model detection: Test Connection button in settings
2. Update to a valid model name from the list
3. For Ollama: `ollama list` to see available models
4. For LM Studio: Check loaded model in the GUI

### Issue: Cache Conflicts

**Symptom**: Getting old recommendations repeatedly
**Solution**:

1. Clear the recommendation cache
2. Restart Lidarr
3. The cache will rebuild automatically

### Issue: Rate Limiting Errors

**Symptom**: "Rate limit exceeded" after upgrade
**Solution**:

1. Check if rate limits have changed for your provider
2. Adjust request frequency in advanced settings
3. Consider using provider failover chain

## Database Schema Changes

Brainarr uses Lidarr's built-in database schema. No manual database migrations are required.

## Rollback Procedure

If you need to rollback to a previous version:

1. Stop Lidarr
2. Restore your backup from step 1
3. Install the previous version of Brainarr
4. Start Lidarr
5. Verify your import lists are working

## Breaking Changes by Version

### v1.0.0

- Initial release, no breaking changes

### Future Versions

Breaking changes will be documented here with migration instructions.

## Configuration File Locations

Configuration files are stored in Lidarr's config directory:

- **Linux**: `/var/lib/lidarr/config.xml`
- **Windows**: `C:\ProgramData\Lidarr\config.xml`
- **Docker**: `/config/config.xml`
- **MacOS**: `~/Library/Application Support/Lidarr/config.xml`

## Provider-Specific Migration Notes

### OpenAI

- Model names may change (e.g., `gpt-4` → `gpt-4o`)
- Verify your selected model is still available

### Anthropic

- Model naming convention changed to include dates
- Old: `claude-3-sonnet`
- New: `claude-3-5-sonnet-latest`

### Google Gemini

- Free tier limits may have changed
- Verify at aistudio.google.com

### Local Providers

- Ensure local services are running before upgrading
- Update model names if you've downloaded new versions

## Getting Help

If you encounter issues during migration:

1. Check the [Troubleshooting Guide](TROUBLESHOOTING.md)
2. Review Lidarr logs: `/var/lib/lidarr/logs/`
3. Enable debug logging for detailed information
4. Open an issue on GitHub with migration details

## Version History

### v1.0.0 (Current)

- Initial production release
- 8 AI providers
- Multi-provider failover
- Health monitoring
- Intelligent caching
- Library analysis

---

Last Updated: 2024-12-20

# Brainarr Learning Paths

## Structured Documentation Guides for Every User Level

---

## Quick Path Finder

| Your Goal | Path | Time Commitment |
|-----------|------|-----------------|
| **Just installed, want to get started** | [5-Minute Setup](#5-minute-setup-path) | 5 minutes |
| **Know basics, want to configure properly** | [Power User](#power-user-path) | 15 minutes |
| **Having issues, need troubleshooting** | [Troubleshooting Path](#troubleshooting-path) | 10-30 minutes |
| **Want to contribute or extend** | [Developer Path](#developer-path) | 30-60 minutes |

---

## 5-Minute Setup Path

**Best for**: New users who just installed Brainarr

### Step 1: Verify Installation (1 minute)

1. Go to **Settings > Plugins** in Lidarr
2. Confirm Brainarr appears in the installed plugins list
3. Check these files exist in your plugins directory:
   - `plugin.json`
   - `manifest.json`
   - `Lidarr.Plugin.Brainarr.dll`

### Step 2: Choose an AI Provider (2 minutes)

**For local AI (no API keys needed)**:
- Select **Ollama** or **LM Studio**
- Ensure the provider is running locally
- Model defaults: `qwen2.5:latest` for Ollama

**For cloud AI**:
- Select your preferred provider (OpenAI, Anthropic, etc.)
- Have your API key ready
- See [Configuration Guide](configuration.md) for provider-specific setup

### Step 3: Configure and Test (2 minutes)

1. Go to **Settings > Import Lists > Add > Brainarr AI Music Discovery**
2. Select your **AI Provider**
3. Enter **Model Name** (or accept default)
4. Click **Test** to verify connectivity
5. Click **Save** when test succeeds

### Next Steps

- Run your first import list: **Run Now** in Import Lists
- Check results in **Activity > Import Lists**
- See [Power User Path](#power-user-path) for advanced configuration

---

## Power User Path

**Best for**: Users who want optimal configuration and advanced features

### Step 1: Provider Selection (5 minutes)

**Local Providers** (Recommended for privacy):
- **Ollama**: `qwen2.5:latest` (balanced) or `llama3.2:8b` (smaller footprint)
- **LM Studio**: Any model you have downloaded

**Cloud Providers** (For best quality):
- **OpenAI**: `gpt-4o` (best overall)
- **Anthropic**: `claude-3-5-sonnet-20241022` (great reasoning)
- **Gemini**: `gemini-1.5-flash` (fast, cost-effective)

**Subscription Providers** (No API keys):
- **Claude Code**: Uses your existing Claude Code CLI credentials
- **OpenAI Codex**: Uses your existing Codex CLI credentials

See [Provider Matrix](PROVIDER_MATRIX.md) for full comparison.

### Step 2: Discovery Mode Configuration (3 minutes)

| Mode | Best For | Behavior |
|------|----------|----------|
| **Adjacent** | Finding similar artists | Explores nearby styles in your library |
| **Diverse** | Exploring new genres | Broad exploration across styles |

### Step 3: Sampling Strategy (3 minutes)

| Strategy | Speed | Coverage | Best For |
|----------|-------|----------|----------|
| **Minimal** | Fastest | Basic | Quick discovery, small libraries |
| **Balanced** | Moderate | Even | **Recommended for most users** |
| **Comprehensive** | Slower | Deep | Large libraries, maximum discovery |

### Step 4: Cache Tuning (3 minutes)

Default settings work well, but you can optimize:

- **Plan Cache Capacity**: Increase for large libraries (default: 256)
- **Plan Cache TTL (minutes)**: Increase for repeated runs (default: 5)
- **Enable Iterative Refinement**: Keep enabled for sparse results

### Step 5: Monitoring (1 minute)

Check these sources for healthy operation:

1. **System > Logs**: Look for `[Brainarr]` entries
2. **Activity > Import Lists**: See recommendations appearing
3. **Prometheus metrics** (if configured): `prompt.plan_cache_*`

### Advanced: Timeout Configuration

For slow local models or high-latency connections:

- Local providers (Ollama/LM Studio): Automatically use 360-second timeout
- Cloud providers: Adjust per-provider timeout in settings
- Maximum: 600 seconds (10 minutes) for any single request

See [Configuration Guide](configuration.md) for all options.

---

## Troubleshooting Path

**Best for**: Users experiencing issues

### Step 1: Quick Diagnostics (2 minutes)

1. **Check provider connectivity**: Click **Test** in settings
2. **Check logs**: Go to **System > Logs**, filter for `Brainarr`
3. **Check settings**: Verify provider and model name are correct

### Step 2: Common Issues (5-15 minutes)

#### Issue: Prompt shows headroom_guard or trims frequently

**Symptoms**: Recommendations are truncated, logs show headroom violations

**Solutions**:
1. Increase the provider context window in settings
2. Loosen style filters to reduce prompt size
3. Switch to a larger local model with bigger context
4. After adjusting, run the list once to warm the cache
5. Watch `prompt.plan_cache_*` metrics stabilize

#### Issue: Token counts look inaccurate

**Symptoms**: `tokenizer.fallback` warnings, actual vs. estimated tokens differ significantly

**Solutions**:
1. This is expected on first run (fallback happens once per model)
2. Accept the basic estimator if drift is acceptable (±25% guardrail)
3. Track `prompt.actual_tokens` vs `prompt.tokens_pre` to confirm drift
4. Monitor that `tokenizer.fallback` fires only once per model

#### Issue: Provider test fails

**Symptoms**: Test button shows error, provider connection times out

**Solutions**:
1. **Ollama/LM Studio**: Verify URL is correct and service is running
   - Ollama default: `http://localhost:11434`
   - Run `ollama list` to verify models
2. **Cloud providers**: Verify API key is valid and has quota
3. Check **System → Logs** for detailed error messages
4. For subscription providers, verify CLI credentials exist

### Step 3: Debug Logging (5 minutes)

Enable detailed logging:

1. Go to **Settings > General**
2. Set **Log Level** to **Debug**
3. Restart Lidarr
4. Reproduce the issue
5. Check **System > Logs** for detailed output

### Step 4: Get Help (5 minutes)

If issues persist:

1. Check existing [GitHub Issues](https://github.com/RicherTunes/Brainarr/issues)
2. Check existing [GitHub Discussions](https://github.com/RicherTunes/Brainarr/discussions)
3. Gather this information before asking:
   - Lidarr version
   - Plugin version
   - AI provider and model
   - Error messages from logs
   - Steps to reproduce

See [Troubleshooting Guide](troubleshooting.md) for detailed troubleshooting.

---

## Developer Path

**Best for**: Contributors and those extending Brainarr

### Step 1: Development Setup (10 minutes)

```bash
# Clone the repository
git clone https://github.com/RicherTunes/Brainarr.git
cd Brainarr

# Run setup script
chmod +x setup.sh && ./setup.sh  # Linux/macOS
# OR
.\setup.ps1  # Windows PowerShell
```

Setup scripts handle:
- Cloning Lidarr dependencies
- Restoring NuGet packages
- Building the solution
- Running tests

### Step 2: Understanding the Architecture (10 minutes)

**Key Components**:

- **BrainarrImportList**: Main Lidarr integration point
- **AIService**: Orchestrates provider calls and recommendation generation
- **LibraryAnalyzer**: Profiles your collection for styles and artists
- **AIProviderFactory**: Manages provider instantiation and failover
- **RecommendationCache**: Fingerprinted LRU cache for plan reuse

**Directory Structure**:
```
src/Brainarr.Plugin/
├── Configuration/        # Provider settings and validation
├── Services/
│   ├── Core/            # Orchestration services
│   ├── Providers/       # AI provider implementations (11 providers)
│   └── Support/         # Supporting services
└── BrainarrImportList.cs # Main Lidarr integration
```

See [Architecture Documentation](ARCHITECTURE.md) for full details.

### Step 3: Running Tests (5 minutes)

```bash
# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter Category=Integration
dotnet test --filter Category=Unit
dotnet test --filter Category=EdgeCase

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Step 4: Adding a New Provider (15 minutes)

To add a new AI provider:

1. Create provider class in `Services/Providers/`
2. Implement `IAIProvider` interface
3. Add configuration in `Configuration/Providers/`
4. Register in `ProviderRegistry`
5. Add to `BrainarrSettings`
6. Update [Provider Matrix](PROVIDER_MATRIX.md)

See [Developer Guide](../CONTRIBUTING.md) for guidelines.

### Step 5: Documentation (10 minutes)

Before submitting:

1. Update inline documentation for new features
2. Update [Provider Matrix](PROVIDER_MATRIX.md) if adding/changing providers
3. Run documentation consistency checks:
   ```bash
   bash ./scripts/check-docs-consistency.sh
   ```
4. Update CHANGELOG with significant changes

### Step 6: Running Local CI (5 minutes)

Test your changes with the same checks as CI:

```bash
# PowerShell
pwsh ./test-local-ci.ps1 -ExcludeHeavy

# POSIX
bash ./test-local-ci.sh --exclude-heavy
```

---

## Additional Resources

### Documentation Index

- [Configuration Guide](configuration.md) - Complete settings reference
- [Planner & Cache Deep Dive](planner-and-cache.md) - Cache behavior and fingerprints
- [Tokenization & Estimates](tokenization-and-estimates.md) - Token accuracy
- [Troubleshooting Playbook](troubleshooting.md) - Step-by-step issue resolution
- [Upgrade Notes 1.3.0](upgrade-notes-1.3.0.md) - Migration from 1.2.x
- [Metrics Reference](METRICS_REFERENCE.md) - Comprehensive metrics documentation
- [Provider Matrix](PROVIDER_MATRIX.md) - Provider availability and notes

### External Resources

- [Lidarr Documentation](https://wiki.lidarr.audio/)
- [Lidarr Plugin System](https://lidarr.audio/docs/plugins)
- [Ollama Documentation](https://ollama.com/docs)
- [LM Studio Documentation](https://lmstudio.ai/)

---

## Need Help?

- **Issues**: [GitHub Issues](https://github.com/RicherTunes/Brainarr/issues)
- **Discussions**: [GitHub Discussions](https://github.com/RicherTunes/Brainarr/discussions)
- **Wiki**: [Project Wiki](https://github.com/RicherTunes/Brainarr/wiki)

---

**Last Updated**: January 2025 | **Plugin Version**: 1.3.2

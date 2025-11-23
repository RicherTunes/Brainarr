# AI Assistant Prompt for .NET 8 Multi-Targeting Fix

Copy and paste this prompt to your AI assistant (like Claude, ChatGPT, etc.) to get help adding .NET 8.0 support to your Lidarr plugin.

---

## 🤖 PROMPT START

I need help adding .NET 8.0 multi-targeting support to my Lidarr plugin to fix a TypeLoadException issue that occurs when users run the plugin on Lidarr instances using .NET 8.0 runtime.

### Problem Context

My plugin currently only targets .NET 6.0, but modern Lidarr instances (v3.0.0.4856+) are transitioning to .NET 8.0. When users install my plugin on these newer Lidarr versions, they get this error:

```
Method 'Test' in type 'NzbDrone.Core.ImportLists.MyPlugin.MyPlugin' from assembly
'Lidarr.Plugin.MyPlugin' does not have an implementation.
```

This happens because the IImportList interface signatures changed between .NET versions, causing a TypeLoadException.

### Solution

I need to add multi-targeting support so my plugin compiles for both .NET 6.0 and .NET 8.0. When deployed, Lidarr will automatically load the correct version based on its runtime.

### Reference Implementation

Here's a successfully implemented fix from the Brainarr plugin: https://github.com/RicherTunes/Brainarr/pull/269

The fix involves:
1. Updating the plugin .csproj to target both frameworks
2. Adding framework-specific package versions
3. Updating CI/CD workflows to build and test both targets
4. Adding plugin loading tests to prevent future regressions

### My Repository Structure

```
MyPlugin/
├── MyPlugin.Plugin/
│   ├── MyPlugin.Plugin.csproj         # Main plugin project
│   └── ... (source files)
├── MyPlugin.Tests/
│   ├── MyPlugin.Tests.csproj
│   └── ... (test files)
├── ext/
│   └── lidarr.plugin.common/          # Submodule
│       └── src/
│           └── Lidarr.Plugin.Common.csproj
├── Directory.Packages.props           # Central package management
├── .github/
│   └── workflows/
│       ├── ci.yml                     # CI workflow
│       └── release.yml                # Release workflow
└── plugin.json
```

### What I Need Help With

Please help me:

1. **Update MyPlugin.Plugin.csproj**:
   - Change from `<TargetFramework>net6.0</TargetFramework>` to `<TargetFrameworks>net6.0;net8.0</TargetFrameworks>`
   - Replace all hardcoded `net6.0` paths with `$(TargetFramework)` in LidarrPath conditions
   - Example: `..\ext\Lidarr-docker\_output\net6.0` → `..\ext\Lidarr-docker\_output\$(TargetFramework)`

2. **Update Directory.Packages.props**:
   - Reorganize packages into:
     - Framework-agnostic packages (core dependencies, test dependencies)
     - .NET 6-specific packages (`Condition="'$(TargetFramework)' == 'net6.0'"`)
     - .NET 8-specific packages (`Condition="'$(TargetFramework)' == 'net8.0'"`)
   - Use .NET 6.0.x versions for net6.0 target
   - Use .NET 8.0.x versions for net8.0 target

3. **Update .github/workflows/ci.yml**:
   - Extract Lidarr assemblies for both net6.0 and net8.0
   - Upload separate artifacts: `lidarr-assemblies-net6.0` and `lidarr-assemblies-net8.0`
   - Download and verify both artifact sets in test jobs
   - Update all assembly sanity checks to run for both frameworks

4. **Update .github/workflows/release.yml**:
   - Install both .NET 6.0.x and 8.0.x SDKs
   - Extract assemblies for both frameworks
   - Package plugin with dual folder structure: `release/net6.0/` and `release/net8.0/`

5. **Update ext/lidarr.plugin.common submodule**:
   - Update to latest version (v1.2.2 or later) that supports both frameworks
   - Commands: `cd ext/lidarr.plugin.common && git checkout main && git pull`

6. **Add plugin loading tests**:
   - Create `MyPlugin.Tests/PluginLoadingTests.cs`
   - Include tests that verify:
     - Assembly is compiled for correct target framework
     - Plugin instantiates without TypeLoadException
     - Test method is accessible (this was the failing method)
     - Dependencies load without version conflicts

### Current File Contents

[Paste your current file contents here if needed, or I can provide them upon request]

### Package Versions I'm Currently Using

Microsoft.Extensions packages: [Specify your current versions or say "using default NuGet versions"]

### Expected Output

Please provide:
1. The exact changes needed for each file
2. Complete updated file contents where appropriate
3. The correct .NET 8 package versions to use
4. Any additional steps I need to take

### Testing Requirements

The fix should:
- ✅ Build successfully for both net6.0 and net8.0
- ✅ Pass all existing tests on both frameworks
- ✅ Not break compatibility with .NET 6 Lidarr instances
- ✅ Fix the TypeLoadException on .NET 8 Lidarr instances
- ✅ Include tests to prevent future regressions

### Additional Context

- My plugin extends: `ImportListBase<MyPluginSettings>`
- Lidarr plugin framework version: [Specify if known, or say "latest"]
- Current lidarr.plugin.common version: [Check with `cd ext/lidarr.plugin.common && git log -1 --oneline`]

Please analyze my repository structure and provide step-by-step instructions with the exact code changes needed.

## 🤖 PROMPT END

---

## Usage Instructions for Repository Owners

1. **Copy the prompt above** (everything between "PROMPT START" and "PROMPT END")

2. **Customize the sections marked with**:
   - `MyPlugin` → Your actual plugin name (e.g., Tidalarr, Qobuzarr)
   - Add your current file contents if the AI asks for them
   - Specify your package versions if known

3. **Paste to your AI assistant**:
   - Claude Code: Use in your workspace
   - Claude.ai: Start a new conversation
   - ChatGPT: Start a new chat
   - GitHub Copilot: Use in chat mode

4. **Follow the AI's instructions** to apply the changes

5. **Test the changes**:
   ```bash
   # Build for both frameworks
   dotnet build -c Release

   # Verify both outputs exist
   ls -la MyPlugin.Plugin/bin/Release/net6.0/
   ls -la MyPlugin.Plugin/bin/Release/net8.0/

   # Run tests
   dotnet test
   ```

6. **Commit and push**:
   ```bash
   git checkout -b feat/add-net8-multitargeting
   git add -A
   git commit -m "feat: add .NET 8.0 multi-targeting support"
   git push -u origin feat/add-net8-multitargeting
   ```

7. **Create a PR** and verify CI passes

## Alternative: Quick Fix Script

If your repository follows the standard structure, you can also use the automated migration script from Brainarr:

```bash
# Download the migration guide
curl -O https://raw.githubusercontent.com/RicherTunes/Brainarr/main/docs/NET8_MIGRATION_GUIDE.md

# Follow the automated script section
# Or apply changes manually following the guide
```

## Example Session

Here's what a typical AI conversation might look like:

**You**: [Paste the prompt above]

**AI**: I'll help you add .NET 8.0 multi-targeting support. Let me start by updating your MyPlugin.Plugin.csproj file...

[AI provides step-by-step changes]

**You**: Here's my current Directory.Packages.props content: [paste content]

**AI**: I see you're using Microsoft.Extensions.* version 7.0.0. Here's the updated version with framework-specific packages...

[Continues through all files]

## Tips for Best Results

1. **Provide context**: Share your current file contents when asked
2. **Ask for verification**: Request the AI to verify the changes won't break existing functionality
3. **Request tests**: Ask the AI to help you create plugin loading tests
4. **One file at a time**: It's easier to review changes file-by-file
5. **Test after each major change**: Don't apply all changes at once without testing

## Common Issues and Solutions

### Issue: "Could not find Lidarr.Core.dll"
**Solution**: The LidarrPath conditions need `$(TargetFramework)` instead of hardcoded `net6.0`

### Issue: "Package version not found for net8.0"
**Solution**: Add framework-specific package versions in Directory.Packages.props

### Issue: Tests fail on one framework but not the other
**Solution**: Check for framework-specific code paths; may need conditional compilation

### Issue: CI can't find assemblies
**Solution**: Update workflows to extract and upload assemblies for both frameworks

## Success Criteria

Your fix is complete when:
- ✅ `grep "TargetFrameworks" MyPlugin.Plugin/MyPlugin.Plugin.csproj` shows both frameworks
- ✅ Both `bin/Release/net6.0/` and `bin/Release/net8.0/` directories exist after build
- ✅ CI passes on all platforms and frameworks
- ✅ Plugin loading tests pass
- ✅ No regressions in existing functionality

## Support

If you encounter issues:
1. Check the [Brainarr fix PR](https://github.com/RicherTunes/Brainarr/pull/269) for reference
2. Review the [detailed migration guide](https://github.com/RicherTunes/Brainarr/blob/main/docs/NET8_MIGRATION_GUIDE.md)
3. Ask the AI assistant to troubleshoot specific errors

---

**Note**: This prompt is designed to work with any AI coding assistant. The more context you provide about your specific repository structure and current configuration, the more accurate and helpful the AI's response will be.

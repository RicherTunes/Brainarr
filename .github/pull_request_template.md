# Pull Request

## Description
Brief description of what this PR does.

## Type of Change
- [ ] 🐛 Bug fix (non-breaking change which fixes an issue)
- [ ] ✨ New feature (non-breaking change which adds functionality)
- [ ] 💥 Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] 📚 Documentation update
- [ ] 🔧 Refactoring (no functional changes)
- [ ] ⚡ Performance improvement
- [ ] 🤖 New AI provider support
- [ ] 🧪 Test improvements

## Related Issues
Fixes #(issue_number)
Closes #(issue_number)
Related to #(issue_number)

## Changes Made
-
-
-

## Testing
- [ ] All existing tests pass
- [ ] New tests added for new functionality
- [ ] Manual testing completed
- [ ] Tested with multiple AI providers
- [ ] No API keys or sensitive data in code

## Provider Testing (if applicable)
- [ ] Ollama
- [ ] LM Studio
- [ ] OpenAI
- [ ] Anthropic
- [ ] Other: ___________

## Screenshots/Examples
<!-- Add screenshots or examples if applicable -->

## Checklist
- [ ] My code follows the project's coding standards
- [ ] I have performed a self-review of my code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] I have made corresponding changes to the documentation
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] New and existing unit tests pass locally with my changes
- [ ] Any dependent changes have been merged and published

## Breaking Changes
<!-- If this is a breaking change, describe what users need to do to upgrade -->

## Additional Notes
<!-- Any additional information that reviewers should know -->

---

## Pre-Merge Verification (CI billing blocked — manual verification required)

This section matches the cross-plugin convention used by applemusicarr/qobuzarr/tidalarr. While Actions billing is blocked, PR authors must attach build/test evidence (or explain the skip) before merge.

### Required (attach evidence or explain skip)
- [ ] `dotnet build Brainarr.Plugin/Brainarr.Plugin.csproj -m:1` succeeds (0 errors)
- [ ] `dotnet test --blame-hang-timeout 30s` — test count and failures noted below
- [ ] Packaging tests pass (`--filter "Category=Packaging"` with `REQUIRE_PACKAGE_TESTS=true` and `PLUGIN_PACKAGE_PATH` set)
- [ ] Version contract tests pass (`--filter "FullyQualifiedName~VersionContractTests"`)
- [ ] No new `net6.0` references introduced
- [ ] Merged DLL is `≥2MB` (ILRepack includes internalized Common + Abstractions — see `Plugin_Dll_Should_Be_Merged_Size`)

### If Common submodule changed
- [ ] Common SHA matches a tagged release (e.g., v1.7.1 / v1.8.0)
- [ ] Promotion checklist items verified per `ext/Lidarr.Plugin.Common/docs/ECOSYSTEM_PROMOTION_CHECKLIST.md`

### If touching release.yml / plugin-package.yml
- [ ] Asset filename still contains literal `net8.0.zip` (see Lidarr `PluginService.GetRemotePlugin`)
- [ ] Forbidden host DLLs guard intact (FluentValidation, NLog, Microsoft.Extensions.*.Abstractions, Lidarr.Plugin.{Common,Abstractions})

### Test Results
- Total: ___ passed, ___ failed, ___ skipped
- Packaging: ___ passed
- Version contract: ___ passed
- Docker E2E (if run): ___ passed


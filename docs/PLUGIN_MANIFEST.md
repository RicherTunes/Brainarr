# Plugin Manifest Documentation

## Overview

The `plugin.json` file is the manifest that defines your Lidarr plugin's metadata and requirements. This file must be present in the root of your plugin directory for Lidarr to recognize and load the plugin.

## Current Manifest

```json
{
  "id": "brainarr",
  "apiVersion": "1.x",
  "name": "Brainarr",
  "version": "1.6.1",
  "description": "AI-powered music discovery with 14 providers including local, cloud, subscription, and CLI options",
  "author": "Brainarr Team",
  "commonVersion": "1.18.0-dev",
  "minHostVersion": "3.0.0.4855",
  "main": "Lidarr.Plugin.Brainarr.dll",
  "homepage": "https://github.com/RicherTunes/Brainarr",
  "owner": "RicherTunes",
  "repository": "https://github.com/RicherTunes/Brainarr",
  "supportUri": "https://github.com/RicherTunes/Brainarr/issues",
  "changelogUri": "https://github.com/RicherTunes/Brainarr/blob/main/CHANGELOG.md",
  "license": "MIT",
  "tags": ["music", "ai", "recommendations", "import-list"],
  "targetFramework": "net8.0",
  "rootNamespace": "NzbDrone.Core.ImportLists.Brainarr"
}
```

## Field Descriptions

### name

**Type:** `string`
**Required:** Yes
**Description:** The display name of your plugin as it appears in Lidarr's UI.

**Example:**

```json
"name": "Brainarr"
```

**Guidelines:**

- Use a clear, descriptive name
- Avoid special characters that might cause display issues
- Keep it concise but meaningful

### version

**Type:** `string`
**Required:** Yes
**Format:** `major.minor.patch` (Semantic Versioning)
**Description:** The current version of your plugin.

**Example:**

```json
"version": "1.3.0"
```

**Versioning Guidelines:**

- **Major** (1.x.x): Breaking changes, major features
- **Minor** (x.1.x): New features, backward compatible
- **Patch** (x.x.1): Bug fixes, minor improvements

### description

**Type:** `string`
**Required:** Yes
**Description:** A brief description of what your plugin does.

**Example:**

```json
"description": "AI-powered music discovery with 14 providers including local, cloud, subscription, and CLI options"
```

**Guidelines:**

- Keep under 200 characters for best display
- Highlight key features and capabilities
- Be specific about the plugin's purpose

### author

**Type:** `string`
**Required:** Yes
**Description:** The name of the plugin author or team.

**Example:**

```json
"author": "Brainarr Team"
```

**Formats:**

- Individual: `"John Doe"`
- Team: `"Brainarr Team"`
- With email: `"John Doe <john@example.com>"`

### minHostVersion

**Type:** `string`
**Required:** Yes
**Format:** `major.minor.patch.build`
**Description:** The minimum version of Lidarr required to run this plugin.

**Example:**

```json
"minHostVersion": "3.0.0.4855"
```

**Important Versions:**

- `3.0.0.4855` - Lidarr nightly (plugins branch, required for .NET 8 plugins)

### main

**Type:** `string`
**Required:** Yes
**Description:** The main DLL file that contains the plugin's entry point.

**Example:**

```json
"main": "Lidarr.Plugin.Brainarr.dll"
```

**Naming Convention:**

- Format: `Lidarr.Plugin.{PluginName}.dll`
- Must match the actual compiled DLL name
- Case-sensitive on Linux systems

## Extended Manifest Options

The following additional fields are supported (and several are used by Brainarr):

### homepage

**Type:** `string`
**Required:** No
**Description:** URL to the plugin's homepage or documentation. (Renamed from
legacy `website` field in tech-debt wave 8 to align with sibling plugins.)

```json
"homepage": "https://github.com/RicherTunes/Brainarr"
```

### supportUri / changelogUri / repository

**Type:** `string`
**Required:** No
**Description:** Support page URL, changelog URL, and repository URL.

```json
"supportUri": "https://github.com/RicherTunes/Brainarr/issues",
"changelogUri": "https://github.com/RicherTunes/Brainarr/blob/main/CHANGELOG.md",
"repository": "https://github.com/RicherTunes/Brainarr"
```

### tags

**Type:** `string[]`
**Required:** No
**Description:** Tags to categorize the plugin.

```json
"tags": ["ai", "recommendations", "import-list", "discovery"]
```

### dependencies

**Type:** `object`
**Required:** No
**Description:** Other plugins or libraries this plugin depends on.

```json
"dependencies": {
  "SomeOtherPlugin": ">=1.0.0"
}
```

### permissions

**Type:** `string[]`
**Required:** No
**Description:** Special permissions the plugin requires.

```json
"permissions": ["network", "filesystem"]
```

## Complete Example

Here's a fully-featured manifest with all optional fields:

```json
{
  "id": "brainarr",
  "apiVersion": "1.x",
  "name": "Brainarr",
  "version": "1.6.1",
  "description": "AI-powered music discovery with 14 providers including local, cloud, subscription, and CLI options",
  "author": "Brainarr Team",
  "commonVersion": "1.18.0-dev",
  "minHostVersion": "3.0.0.4855",
  "main": "Lidarr.Plugin.Brainarr.dll",
  "owner": "RicherTunes",
  "homepage": "https://github.com/RicherTunes/Brainarr",
  "repository": "https://github.com/RicherTunes/Brainarr",
  "supportUri": "https://github.com/RicherTunes/Brainarr/issues",
  "changelogUri": "https://github.com/RicherTunes/Brainarr/blob/main/CHANGELOG.md",
  "license": "MIT",
  "tags": ["music", "ai", "recommendations", "import-list"],
  "targetFramework": "net8.0",
  "rootNamespace": "NzbDrone.Core.ImportLists.Brainarr"
}
```

## Validation

### Required Fields Checklist

- [ ] `name` - Plugin display name
- [ ] `version` - Current version (semantic)
- [ ] `description` - Brief description
- [ ] `author` - Author name/team
- [ ] `minHostVersion` - Minimum Lidarr version
- [ ] `entryPoint` - Main DLL file

### Common Issues

#### Plugin Not Loading

```json
// Wrong - incorrect entryPoint
{
  "entryPoint": "Brainarr.dll"  // Missing "Lidarr.Plugin." prefix
}

// Correct
{
  "entryPoint": "Lidarr.Plugin.Brainarr.dll"
}
```

#### Version Compatibility

```json
// Wrong - incompatible version format (example key renamed to avoid confusion)
{
  "minHostVersionExample": "3.0.0"  // Missing build number
}

// Correct
{
  "minHostVersion": "3.0.0.4855"
}
```

#### Invalid JSON

```json
// Wrong - trailing comma
{
  "name": "Brainarr",
  "version": "1.3.0",  // <- trailing comma causes error
}

// Correct - no trailing comma
{
  "name": "Brainarr",
  "version": "1.3.0"
}
```

## Deployment Structure

When deployed, your plugin directory should look like:

```text
/var/lib/lidarr/plugins/RicherTunes/Brainarr/
├── plugin.json                          # Manifest file
├── Lidarr.Plugin.Brainarr.dll          # Main plugin DLL
├── Lidarr.Plugin.Brainarr.pdb          # Debug symbols (optional)
├── Newtonsoft.Json.dll                 # Dependencies
└── [other dependency DLLs]
```

## Version Management

### Updating Version

When releasing a new version:

1. Update `plugin.json`:

```json
{
  "version": "1.6.1"  // Increment appropriately
}
```

1. Update assembly version in `.csproj`:

```xml
<PropertyGroup>
  <Version>1.1.0</Version>
  <AssemblyVersion>1.1.0.0</AssemblyVersion>
  <FileVersion>1.1.0.0</FileVersion>
</PropertyGroup>
```

1. Tag the release in git:

```bash
git tag v1.1.0
git push origin v1.1.0
```

### Version Compatibility Matrix

| Plugin Version | Minimum Lidarr | Maximum Lidarr | Notes |
|---------------|----------------|----------------|-------|
| 1.6.1 | 3.0.0.4855 | - | Current release |
| 1.5.x | 3.0.0.4855 | - | Previous releases |
| 1.3.0 | 3.0.0.4855 | - | Planning refactor |

## Best Practices

1. **Semantic Versioning**: Always follow semantic versioning rules
2. **Backward Compatibility**: Maintain compatibility when possible
3. **Clear Descriptions**: Write clear, concise descriptions
4. **Test Manifests**: Validate JSON before deployment
5. **Document Changes**: Update manifest when features change
6. **Version Alignment**: Keep manifest and assembly versions in sync

## Troubleshooting

### Plugin Not Appearing in Lidarr

1. Check JSON validity:

```bash
# Validate JSON syntax
python -m json.tool plugin.json
```

1. Verify file location:

```bash
# Correct location (owner/name layout)
ls -la /var/lib/lidarr/plugins/RicherTunes/Brainarr/plugin.json
```

1. Check Lidarr logs:

```bash
tail -f /var/log/lidarr/lidarr.txt | grep -i plugin
```

### Version Mismatch Errors

Ensure versions match across:

- `plugin.json` version field
- Assembly version in DLL
- Git tags
- Documentation

### Loading Errors

Common causes:

- Missing dependencies
- Incorrect entryPoint path
- Incompatible Lidarr version
- Corrupted DLL files

## Additional Resources

- [Lidarr Plugin Development](https://wiki.servarr.com/lidarr/plugins)
- [Semantic Versioning](https://semver.org/)
- [JSON Schema Validation](https://jsonschemavalidator.net/)
- [.NET Assembly Versioning](https://docs.microsoft.com/en-us/dotnet/standard/assembly/versioning)

<#
.SYNOPSIS
    Migrates Brainarr's LLM provider API keys from plaintext to encrypted-at-rest format.

.DESCRIPTION
    Reads the current Lidarr settings database, identifies any plaintext API key fields,
    and re-saves them through the BrainarrSettings encryption layer (which triggers
    IStringProtector.Protect on each key). A pre-migration snapshot is written to
    a .bak file before any mutation.

    Safe to run multiple times (idempotent). If all API keys are already encrypted
    (lpc:ps:v1: prefix), the script exits 0 with no changes.

    Because Brainarr stores settings in Lidarr's SQLite database rather than a separate
    JSON file, this script drives migration by touching each API key property via a small
    dotnet-run helper that loads and saves the settings object. The helper does not need
    a running Lidarr instance.

.PARAMETER LidarrDbPath
    Path to the Lidarr database file (nzbdrone.db). Defaults to platform-standard locations:
    - Windows : %APPDATA%\Lidarr\nzbdrone.db
    - Linux   : ~/.config/Lidarr/nzbdrone.db

.PARAMETER Force
    Overwrite an existing .bak file without prompting.

.EXAMPLE
    # Migrate using the default database location
    pwsh -File scripts/Migrate-BrainarrSettings.ps1

.EXAMPLE
    # Migrate a non-default path
    pwsh -File scripts/Migrate-BrainarrSettings.ps1 -LidarrDbPath /data/Lidarr/nzbdrone.db

.NOTES
    Requires .NET 8 SDK.
    Protector back-end is selected by LP_COMMON_PROTECTOR environment variable:
    - auto (default) : DPAPI (Windows), Keychain (macOS), DataProtection (Linux)
    - dpapi          : Windows DPAPI user-scope
    - dpapi-machine  : Windows DPAPI machine-scope (shared service accounts)
    - keychain       : macOS Keychain
    - dataprotection : ASP.NET DataProtection + AES (cross-platform; requires LP_COMMON_KEYS_PATH)

    The migration helper must be run on the same machine / user account as the Lidarr service
    so that the OS-level protector (DPAPI/Keychain) uses the correct key material.
    If you move the database to another machine, run the migration on the new machine so
    keys are re-protected with that machine's credentials.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $LidarrDbPath,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---- Resolve default settings path ----
if (-not $LidarrDbPath) {
    if ($IsWindows) {
        $LidarrDbPath = Join-Path $env:APPDATA 'Lidarr\nzbdrone.db'
    } else {
        $LidarrDbPath = Join-Path $HOME '.config/Lidarr/nzbdrone.db'
    }
}

$LidarrDbPath = [System.IO.Path]::GetFullPath($LidarrDbPath)

Write-Host "Migrate-BrainarrSettings: target database = $LidarrDbPath"

if (-not (Test-Path $LidarrDbPath)) {
    Write-Warning "Lidarr database not found at '$LidarrDbPath'. Nothing to migrate."
    Write-Warning "If Lidarr is installed in a non-standard location, pass -LidarrDbPath <path>."
    exit 0
}

# ---- Create .bak before any mutation ----
$bakPath = "$LidarrDbPath.brn001.bak"
if ((Test-Path $bakPath) -and -not $Force) {
    $answer = Read-Host "Backup '$bakPath' already exists. Overwrite? [y/N]"
    if ($answer -notin @('y', 'Y', 'yes', 'Yes', 'YES')) {
        Write-Warning "Migration aborted by user. Existing .bak preserved."
        exit 1
    }
}

Copy-Item -Path $LidarrDbPath -Destination $bakPath -Force
Write-Host "Backup created: $bakPath"

# ---- Build and run the migration driver ----
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Split-Path -Parent $scriptDir
$pluginCsproj = Join-Path $repoRoot 'Brainarr.Plugin\Brainarr.Plugin.csproj'

$driverDir  = Join-Path ([System.IO.Path]::GetTempPath()) "brainarr_migrate_$([Guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Path $driverDir -Force | Out-Null

$driverCsproj = Join-Path $driverDir 'MigrateBrainarrDriver.csproj'
$driverCs     = Join-Path $driverDir 'Program.cs'

# Project file — references the plugin to pick up BrainarrSettings + IStringProtector
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>CS0618;CS8618;CS8602</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$pluginCsproj">
      <AdditionalProperties>PluginPackagingDisable=true</AdditionalProperties>
    </ProjectReference>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $driverCsproj -Encoding UTF8

# Driver entry-point:
# 1. Opens the Lidarr SQLite database directly.
# 2. Reads ImportListSettings rows for the Brainarr import list.
# 3. Deserialises BrainarrSettings JSON.
# 4. Assigns each API key back through the property setter (which encrypts via IStringProtector).
# 5. Writes the updated JSON back to the database.
@'
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Security;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: MigrateBrainarrDriver <lidarr-db-path>");
    return 1;
}

var dbPath = args[0];
Console.WriteLine($"Opening Lidarr database: {dbPath}");

// API key property names to migrate
var apiKeyFields = new[]
{
    "PerplexityApiKey", "OpenAIApiKey", "AnthropicApiKey", "GeminiApiKey",
    "OpenRouterApiKey", "GroqApiKey", "DeepSeekApiKey", "ZaiGlmApiKey"
};

var protector = BrainarrApiKeyProtection.GetDefaultStringProtector();
var migratedCount = 0;
var alreadyEncryptedCount = 0;
var skippedEmptyCount = 0;

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

// Find all Brainarr import list settings rows
using var selectCmd = connection.CreateCommand();
selectCmd.CommandText = @"
    SELECT Id, Settings FROM ImportLists
    WHERE Implementation = 'BrainarrImportList' OR TypeName LIKE '%Brainarr%'";

var rows = new List<(long Id, string Settings)>();
using (var reader = selectCmd.ExecuteReader())
{
    while (reader.Read())
    {
        rows.Add((reader.GetInt64(0), reader.GetString(1)));
    }
}

if (rows.Count == 0)
{
    Console.WriteLine("No Brainarr import list entries found in the database. Nothing to migrate.");
    return 0;
}

Console.WriteLine($"Found {rows.Count} Brainarr import list row(s).");

foreach (var (rowId, settingsJson) in rows)
{
    Console.WriteLine($"  Processing row Id={rowId} ...");

    JsonObject? obj;
    try
    {
        obj = JsonNode.Parse(settingsJson)?.AsObject();
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"  ERROR: Failed to parse settings JSON: {ex.Message}");
        continue;
    }

    if (obj is null) continue;

    var changed = false;

    foreach (var fieldName in apiKeyFields)
    {
        // JSON property names are camelCase in Lidarr's serialisation
        var jsonKey = char.ToLower(fieldName[0]) + fieldName.Substring(1);

        if (!obj.TryGetPropertyValue(jsonKey, out var node) || node is null)
            continue;

        var rawValue = node.GetValue<string?>();

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            skippedEmptyCount++;
            continue;
        }

        if (protector.IsProtected(rawValue))
        {
            alreadyEncryptedCount++;
            continue;
        }

        // Encrypt the plaintext value
        var encrypted = protector.Protect(rawValue);
        obj[jsonKey] = encrypted;
        Console.WriteLine($"    Encrypted {fieldName}");
        migratedCount++;
        changed = true;
    }

    if (!changed)
    {
        Console.WriteLine($"  Row Id={rowId}: all keys already encrypted or empty.");
        continue;
    }

    var updatedJson = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    using var updateCmd = connection.CreateCommand();
    updateCmd.CommandText = "UPDATE ImportLists SET Settings = @settings WHERE Id = @id";
    updateCmd.Parameters.AddWithValue("@settings", updatedJson);
    updateCmd.Parameters.AddWithValue("@id", rowId);
    updateCmd.ExecuteNonQuery();

    Console.WriteLine($"  Row Id={rowId}: updated.");
}

Console.WriteLine();
Console.WriteLine($"Migration complete: {migratedCount} key(s) encrypted, {alreadyEncryptedCount} already encrypted, {skippedEmptyCount} empty/skipped.");
return 0;
'@ | Set-Content -Path $driverCs -Encoding UTF8

try {
    Write-Host "Building and running encryption migration driver..."
    $result = & dotnet run --project $driverCsproj -- $LidarrDbPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Migration driver failed (exit $LASTEXITCODE):`n$result"
        Write-Warning "Restoring from backup..."
        Copy-Item -Path $bakPath -Destination $LidarrDbPath -Force
        exit 1
    }
    Write-Host $result
    Write-Host ""
    Write-Host "Migration succeeded." -ForegroundColor Green
    Write-Host "Backup retained at: $bakPath"
    Write-Host "(Safe to delete after verifying Lidarr starts correctly with the migrated database.)"
} finally {
    Remove-Item -Recurse -Force $driverDir -ErrorAction SilentlyContinue
}

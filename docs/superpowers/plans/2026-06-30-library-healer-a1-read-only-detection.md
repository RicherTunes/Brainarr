# Library Healer A1 Read-Only Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Milestone A1 of Brainarr Library Healer: read-only detection and reporting for Lidarr-managed track files whose Lidarr tag reader reports missing or zero-duration evidence.

**Architecture:** Add a new `Services.Healing` subsystem that is independent from recommendation generation. The subsystem enumerates Lidarr artists and track files, reads file evidence through injected read-only adapters, classifies findings with a pure classifier, stores redacted finding summaries under Brainarr AppData, and exposes read-only UI actions through Brainarr's existing `RequestAction` / `BrainarrOrchestrator.HandleAction` path.

**Tech Stack:** C# / .NET 8, xUnit, FluentAssertions, Moq, Lidarr plugins branch assemblies, `IArtistService`, `IMediaFileService`, `IAudioTagService.ReadTags(string)`, Common `JsonFileStore<TKey,TValue>`, `PluginConfigRoots.Resolve("Brainarr")`.

## Global Constraints

- Work only in the isolated worktree `C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1` on branch `feature/import-brain-a1`.
- Before merging back, re-run verification from the primary checkout and merge only after the feature branch is green.
- TDD is mandatory: every production change must be preceded by a failing test, and each task must record the failing and passing command output in the worker notes.
- A1 is read-only with respect to media and Lidarr state. It may only write Brainarr-owned diagnostic/report files under `PluginConfigRoots.Resolve("Brainarr")`.
- A1 must not call `IAudioTagService.WriteTags`, `IAudioTagService.SyncTags`, `IAudioTagService.RemoveMusicBrainzTags`, `IMediaFileService.Update`, `IMediaFileService.Delete`, `IMediaFileService.DeleteMany`, `IMediaFileService.UpdateMediaInfo`, `IManageCommandQueue.Push`, `RefreshArtistCommand`, `BulkRefreshArtistCommand`, `RescanFoldersCommand`, or `AlbumSearchCommand`.
- A1 must not rename, move, delete, overwrite, copy over, chmod, chown, or timestamp-write media paths.
- A1 must not use AI providers. It is deterministic evidence gathering and classification only.
- A1 stored/action labels emitted by the default scan are `FalsePositive`, `TagReaderSymptom`, and `NeedsHumanReview`; `ProbeEvidence` remains a reserved classifier label for optional probe evidence and is not produced until a probe source is implemented.
- `HeaderRepairCandidate` may exist only as an internal reason code. It must not be exposed as an actionable UI label in A1.
- Persisted and shareable findings must redact paths as `basename + sha256(path)[0..12]`; A1 must not persist raw media paths.
- Hydrate `ext\Lidarr\_output\net8.0` inside the feature worktree before implementation; do not depend on the primary checkout for final A1 verification.
- Keep new production code under `Brainarr.Plugin/Services/Healing/` unless a task explicitly modifies an existing integration file.
- Keep tests under `Brainarr.Tests/Services/Healing/` except integration/action tests that naturally belong under `Brainarr.Tests/Services/Core/`.
- Every task ends with a focused test command. After all tasks, run full build and test commands listed in "Final Verification".

---

## Current Integration Facts

- `BrainarrImportList.RequestAction(string action, IDictionary<string,string> query)` delegates to `IBrainarrOrchestrator.HandleAction(action, query, Settings)`.
- `BrainarrOrchestrator.HandleAction` already routes action strings such as `review/getqueue`, `metrics/get`, and `planning/getgapplan`.
- `BrainarrOrchestratorFactory.ConfigureServices(IServiceCollection)` is the central DI registration point for Brainarr services.
- `ReviewQueueService` is a useful persistence pattern: it stores plugin-owned JSON under `PluginConfigRoots.Resolve("Brainarr")` with Common `JsonFileStore`.
- Lidarr's vendored plugins-branch source exposes:
  - `NzbDrone.Core.Music.IArtistService.GetAllArtists()`
  - `NzbDrone.Core.MediaFiles.IMediaFileService.GetFilesByArtist(int artistId)`
  - `NzbDrone.Core.MediaFiles.TrackFile.Path`, `Id`, `Size`, `Modified`, `AlbumId`
  - `NzbDrone.Core.MediaFiles.IAudioTagService.ReadTags(string file)`
  - `NzbDrone.Core.Parser.Model.ParsedTrackInfo.Duration`

## File Structure

- Create `Brainarr.Plugin/Services/Healing/LibraryHealerLabels.cs`: stable enums for labels, evidence kinds, and scan status.
- Create `Brainarr.Plugin/Services/Healing/LibraryHealerEvidence.cs`: immutable evidence DTOs for file identity, tag-reader results, optional probe results, and scan findings.
- Create `Brainarr.Plugin/Services/Healing/PathPrivacy.cs`: path redaction and stable path hashing helpers.
- Create `Brainarr.Plugin/Services/Healing/LibraryHealerClassifier.cs`: pure classification logic with no filesystem, Lidarr, network, or process dependencies.
- Create `Brainarr.Plugin/Services/Healing/IFileFingerprintService.cs`: safe file fingerprint interface.
- Create `Brainarr.Plugin/Services/Healing/FileFingerprintService.cs`: read-only file `Exists`, length, and mtime reader.
- Create `Brainarr.Plugin/Services/Healing/ITagLibSymptomReader.cs`: read-only wrapper interface around Lidarr tag evidence.
- Create `Brainarr.Plugin/Services/Healing/LidarrAudioTagSymptomReader.cs`: adapter over `IAudioTagService.ReadTags(string)`.
- Create `Brainarr.Plugin/Services/Healing/LibraryHealerFindingStore.cs`: plugin-owned finding persistence using `JsonFileStore`.
- Create `Brainarr.Plugin/Services/Healing/LibraryHealerScanRunner.cs`: `ILibraryHealerScanRunner` plus the implementation that enumerates artists/files and records findings.
- Create `Brainarr.Plugin/Services/Healing/LibraryHealerActionHandler.cs`: read-only action DTOs for UI calls.
- Modify `Brainarr.Plugin/Services/Core/BrainarrOrchestratorFactory.cs`: register A1 healing services when Lidarr dependencies are available.
- Modify `Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs`: accept optional `LibraryHealerActionHandler` and route `healer/*` actions.
- Modify `Brainarr.Plugin/BrainarrImportList.cs`: constructor injection and service-provider registration for Lidarr media/audio read-only services; no direct healing logic.
- Create `Brainarr.Tests/Services/Healing/LibraryHealerClassifierTests.cs`.
- Create `Brainarr.Tests/Services/Healing/PathPrivacyTests.cs`.
- Create `Brainarr.Tests/Services/Healing/LibraryHealerFindingStoreTests.cs`.
- Create `Brainarr.Tests/Services/Healing/LidarrAudioTagSymptomReaderTests.cs`.
- Create `Brainarr.Tests/Services/Healing/LibraryHealerScanRunnerTests.cs`.
- Create `Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs`.
- Create `Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs`.
- Create `docs/library-healer.md`.
- Modify `docs/README.md` to link the new read-only Library Healer doc.

---

### Task 1: API Spike and Branch Baseline

**Files:**
- Create: `docs/superpowers/spikes/2026-06-30-library-healer-a1-api-spike.md`
- Test: command-only compile smoke, no production files

**Interfaces:**
- Consumes: active Lidarr assemblies at `C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0`
- Produces: recorded API evidence for `IArtistService`, `IMediaFileService`, `IAudioTagService`, and `TrackFile`

- [ ] **Step 1: Hydrate submodules and host assemblies**

Run:

```powershell
git -c protocol.file.allow=always submodule update --init --recursive ext/Lidarr.Plugin.Common
git -c protocol.file.allow=always submodule update --init --recursive ext/Lidarr
if (-not (Test-Path ext\Lidarr\_output\net8.0\Lidarr.Core.dll)) {
    pwsh ./setup-lidarr.ps1 -Branch plugins
}
git submodule status
Test-Path C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0\Lidarr.Core.dll
Test-Path ext\Lidarr.Plugin.Common\src\Lidarr.Plugin.Common.csproj
```

Expected: Common submodule has commit `a43c2f15e64c9e24d13c8877fa629e55368a7ee9`; Lidarr host assemblies exist in this worktree; both `Test-Path` calls print `True`.

- [ ] **Step 2: Record exact API facts**

Create `docs/superpowers/spikes/2026-06-30-library-healer-a1-api-spike.md` with this content:

```markdown
# Library Healer A1 API Spike

Date: 2026-06-30
Branch: feature/import-brain-a1
Assembly source: C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
Source checkout for API verification: C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr

Verified APIs:
- NzbDrone.Core.Music.IArtistService.GetAllArtists(): List<Artist>
- NzbDrone.Core.MediaFiles.IMediaFileService.GetFilesByArtist(int artistId): List<TrackFile>
- NzbDrone.Core.MediaFiles.TrackFile.Path: string
- NzbDrone.Core.MediaFiles.TrackFile.Id: int inherited from ModelBase
- NzbDrone.Core.MediaFiles.TrackFile.Size: long
- NzbDrone.Core.MediaFiles.TrackFile.Modified: DateTime
- NzbDrone.Core.MediaFiles.TrackFile.AlbumId: int
- NzbDrone.Core.MediaFiles.IAudioTagService.ReadTags(string file): ParsedTrackInfo
- NzbDrone.Core.Parser.Model.ParsedTrackInfo.Duration: TimeSpan

A1 forbidden APIs:
- IAudioTagService.WriteTags
- IAudioTagService.SyncTags
- IAudioTagService.RemoveMusicBrainzTags
- IMediaFileService.Update
- IMediaFileService.Delete
- IMediaFileService.DeleteMany
- IMediaFileService.UpdateMediaInfo
- IManageCommandQueue.Push
- RefreshArtistCommand
- BulkRefreshArtistCommand
- RescanFoldersCommand
- AlbumSearchCommand

Baseline:
- dotnet build Brainarr.sln -c Release -m:1 -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
- dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --no-build -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

- [ ] **Step 3: Run baseline build**

Run:

```powershell
dotnet build Brainarr.sln -c Release -m:1 -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Run baseline tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --no-build -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: `Failed: 0`.

- [ ] **Step 5: Commit the spike doc**

Run:

```powershell
git add docs/superpowers/spikes/2026-06-30-library-healer-a1-api-spike.md
git commit -m "docs: record library healer a1 api spike"
```

Expected: commit created on `feature/import-brain-a1`.

---

### Task 2: Pure Labels, Evidence DTOs, and Path Privacy

**Files:**
- Create: `Brainarr.Plugin/Services/Healing/LibraryHealerLabels.cs`
- Create: `Brainarr.Plugin/Services/Healing/LibraryHealerEvidence.cs`
- Create: `Brainarr.Plugin/Services/Healing/PathPrivacy.cs`
- Test: `Brainarr.Tests/Services/Healing/PathPrivacyTests.cs`

**Interfaces:**
- Consumes: none from previous production tasks
- Produces:
  - `LibraryHealerLabel`
  - `LibraryHealerEvidenceKind`
  - `LibraryHealerScanStatus`
  - `LibraryHealerFileIdentity`
  - `TagReaderEvidence`
  - `ProbeEvidence`
  - `LibraryHealerFinding`
  - `PathPrivacy.Redact(string path)`
  - `PathPrivacy.HashPath(string path)`

- [ ] **Step 1: Write failing path privacy tests**

Create `Brainarr.Tests/Services/Healing/PathPrivacyTests.cs`:

```csharp
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class PathPrivacyTests
{
    [Fact]
    public void Redact_ShouldReturnBasenameAndStableShortHash()
    {
        var first = PathPrivacy.Redact(@"D:\Music\Artist\Album\track01.flac");
        var second = PathPrivacy.Redact(@"D:\Music\Artist\Album\track01.flac");

        first.Should().StartWith("track01.flac#");
        first.Should().HaveLength("track01.flac#".Length + 12);
        second.Should().Be(first);
        first.Should().NotContain("Artist");
        first.Should().NotContain("Album");
    }

    [Fact]
    public void Redact_ShouldTreatSlashAndBackslashAsPathSeparators()
    {
        PathPrivacy.Redact(@"D:\Music\Artist\Album\track01.flac")
            .Should()
            .StartWith("track01.flac#");
        PathPrivacy.Redact("/mnt/music/Artist/Album/track02.flac")
            .Should()
            .StartWith("track02.flac#");
    }

    [Fact]
    public void Redact_ShouldHandleBlankPathWithoutThrowing()
    {
        PathPrivacy.Redact(null).Should().Be("<missing>#000000000000");
        PathPrivacy.Redact("").Should().Be("<missing>#000000000000");
        PathPrivacy.Redact("   ").Should().Be("<missing>#000000000000");
    }

    [Fact]
    public void HashPath_ShouldPreserveCaseSensitivity()
    {
        PathPrivacy.HashPath(@"D:\Music\A.FLAC")
            .Should()
            .NotBe(PathPrivacy.HashPath(@"d:\music\a.flac"));
    }

    [Fact]
    public void RedactMessage_ShouldRemoveKnownAndLikelyPaths()
    {
        var message = @"Cannot read D:\Music\Private Artist\Album\track01.m4a from /mnt/music/Private Artist/file.flac";

        var redacted = PathPrivacy.RedactMessage(message, @"D:\Music\Private Artist\Album\track01.m4a");

        redacted.Should().NotContain("Private Artist");
        redacted.Should().NotContain("/mnt/music");
        redacted.Should().Contain("track01.m4a#");
        redacted.Should().Contain("<path>");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~PathPrivacyTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: build/test discovery fails because `PathPrivacy` and `Services.Healing` do not exist.

- [ ] **Step 3: Add production code**

Create `Brainarr.Plugin/Services/Healing/LibraryHealerLabels.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public enum LibraryHealerLabel
{
    FalsePositive = 0,
    TagReaderSymptom = 1,
    ProbeEvidence = 2,
    NeedsHumanReview = 3
}

public enum LibraryHealerEvidenceKind
{
    FileFingerprint = 0,
    TagReader = 1,
    Probe = 2,
    Classifier = 3
}

public enum LibraryHealerScanStatus
{
    NotStarted = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
```

Create `Brainarr.Plugin/Services/Healing/LibraryHealerEvidence.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record LibraryHealerFileIdentity(
    int TrackFileId,
    int ArtistId,
    int AlbumId,
    string RedactedPath,
    string PathHash,
    long? Size,
    DateTime? ModifiedUtc);

public sealed record TagReaderEvidence(
    bool ReadAttempted,
    bool ReadSucceeded,
    double? DurationSeconds,
    string? ErrorType,
    string? ErrorMessage);

public sealed record ProbeEvidence(
    bool ProbeAttempted,
    bool ProbeSucceeded,
    double? DurationSeconds,
    string? Container,
    string? AudioCodec,
    string? ErrorType,
    string? ErrorMessage);

public sealed record LibraryHealerFinding(
    string Id,
    LibraryHealerFileIdentity File,
    LibraryHealerLabel Label,
    IReadOnlyList<string> InternalReasonCodes,
    TagReaderEvidence TagReader,
    ProbeEvidence? Probe,
    DateTime ObservedAtUtc);
```

Create `Brainarr.Plugin/Services/Healing/PathPrivacy.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public static class PathPrivacy
{
    private const string MissingHash = "000000000000";

    public static string Redact(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "<missing>#" + MissingHash;
        }

        var trimmed = path.Trim();
        var separators = new[] { '/', '\\' };
        var withoutTrailingSeparators = trimmed.TrimEnd(separators);
        var lastSeparator = withoutTrailingSeparators.LastIndexOfAny(separators);
        var basename = lastSeparator >= 0
            ? withoutTrailingSeparators.Substring(lastSeparator + 1)
            : withoutTrailingSeparators;
        if (string.IsNullOrWhiteSpace(basename))
        {
            basename = "<missing>";
        }

        return basename + "#" + HashPath(path);
    }

    public static string HashPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MissingHash;
        }

        var normalized = path.Trim().Replace('\\', '/');
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 12);
    }

    public static string? RedactMessage(string? message, string? knownPath = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var redacted = message;
        if (!string.IsNullOrWhiteSpace(knownPath))
        {
            redacted = redacted.Replace(knownPath, Redact(knownPath), StringComparison.OrdinalIgnoreCase);
        }

        redacted = Regex.Replace(redacted, "[A-Za-z]:[\\\\/][^\\r\\n\\t\\\"<>|]+", "<path>");
        redacted = Regex.Replace(redacted, "/[^\\r\\n\\t\\\"<>|]+", "<path>");
        return redacted;
    }
}
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~PathPrivacyTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: all `PathPrivacyTests` pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add Brainarr.Plugin/Services/Healing/LibraryHealerLabels.cs Brainarr.Plugin/Services/Healing/LibraryHealerEvidence.cs Brainarr.Plugin/Services/Healing/PathPrivacy.cs Brainarr.Tests/Services/Healing/PathPrivacyTests.cs
git commit -m "feat: add library healer evidence primitives"
```

Expected: commit created.

---

### Task 3: Pure Classifier

**Files:**
- Create: `Brainarr.Plugin/Services/Healing/LibraryHealerClassifier.cs`
- Test: `Brainarr.Tests/Services/Healing/LibraryHealerClassifierTests.cs`

**Interfaces:**
- Consumes: `TagReaderEvidence`, `ProbeEvidence`, `LibraryHealerLabel`
- Produces: `LibraryHealerClassifier.Classify(TagReaderEvidence tagReader, ProbeEvidence? probe): LibraryHealerClassification`

- [ ] **Step 1: Write failing classifier tests**

Create `Brainarr.Tests/Services/Healing/LibraryHealerClassifierTests.cs`:

```csharp
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class LibraryHealerClassifierTests
{
    [Fact]
    public void Classify_ShouldReturnFalsePositive_WhenTagReaderHasPositiveDuration()
    {
        var result = LibraryHealerClassifier.Classify(
            new TagReaderEvidence(true, true, 245.2, null, null),
            null);

        result.Label.Should().Be(LibraryHealerLabel.FalsePositive);
        result.InternalReasonCodes.Should().Contain("TAG_READER_DURATION_POSITIVE");
    }

    [Fact]
    public void Classify_ShouldReturnTagReaderSymptom_WhenTagReaderReportsZeroAndNoProbeExists()
    {
        var result = LibraryHealerClassifier.Classify(
            new TagReaderEvidence(true, true, 0, null, null),
            null);

        result.Label.Should().Be(LibraryHealerLabel.TagReaderSymptom);
        result.InternalReasonCodes.Should().Contain("TAG_READER_ZERO_DURATION");
        result.InternalReasonCodes.Should().NotContain("HEADER_REPAIR_CANDIDATE");
    }

    [Fact]
    public void Classify_ShouldReturnProbeEvidence_WhenTagReaderZeroButProbeShowsAudioDuration()
    {
        var result = LibraryHealerClassifier.Classify(
            new TagReaderEvidence(true, true, 0, null, null),
            new ProbeEvidence(true, true, 245.1, "mov,mp4,m4a", "flac", null, null));

        result.Label.Should().Be(LibraryHealerLabel.ProbeEvidence);
        result.InternalReasonCodes.Should().Contain("TAG_READER_ZERO_DURATION");
        result.InternalReasonCodes.Should().Contain("PROBE_DURATION_POSITIVE");
        result.InternalReasonCodes.Should().Contain("HEADER_REPAIR_CANDIDATE");
    }

    [Fact]
    public void Classify_ShouldReturnNeedsHumanReview_WhenBothReadersFail()
    {
        var result = LibraryHealerClassifier.Classify(
            new TagReaderEvidence(true, false, null, "CorruptFileException", "bad header"),
            new ProbeEvidence(true, false, null, null, null, "InvalidDataException", "decode failed"));

        result.Label.Should().Be(LibraryHealerLabel.NeedsHumanReview);
        result.InternalReasonCodes.Should().Contain("TAG_READER_FAILED");
        result.InternalReasonCodes.Should().Contain("PROBE_FAILED");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerClassifierTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: fails because `LibraryHealerClassifier` is not defined.

- [ ] **Step 3: Add minimal classifier**

Create `Brainarr.Plugin/Services/Healing/LibraryHealerClassifier.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record LibraryHealerClassification(
    LibraryHealerLabel Label,
    IReadOnlyList<string> InternalReasonCodes);

public static class LibraryHealerClassifier
{
    public static LibraryHealerClassification Classify(TagReaderEvidence tagReader, ProbeEvidence? probe)
    {
        var reasons = new List<string>();

        if (!tagReader.ReadAttempted)
        {
            reasons.Add("TAG_READER_NOT_ATTEMPTED");
            return new LibraryHealerClassification(LibraryHealerLabel.NeedsHumanReview, reasons);
        }

        if (!tagReader.ReadSucceeded)
        {
            reasons.Add("TAG_READER_FAILED");
        }
        else if (tagReader.DurationSeconds.GetValueOrDefault() > 0)
        {
            reasons.Add("TAG_READER_DURATION_POSITIVE");
            return new LibraryHealerClassification(LibraryHealerLabel.FalsePositive, reasons);
        }
        else
        {
            reasons.Add("TAG_READER_ZERO_DURATION");
        }

        if (probe is null || !probe.ProbeAttempted)
        {
            return new LibraryHealerClassification(
                tagReader.ReadSucceeded ? LibraryHealerLabel.TagReaderSymptom : LibraryHealerLabel.NeedsHumanReview,
                reasons);
        }

        if (!probe.ProbeSucceeded)
        {
            reasons.Add("PROBE_FAILED");
            return new LibraryHealerClassification(LibraryHealerLabel.NeedsHumanReview, reasons);
        }

        if (probe.DurationSeconds.GetValueOrDefault() > 0)
        {
            reasons.Add("PROBE_DURATION_POSITIVE");
            if (IsMp4FlacSignature(probe))
            {
                reasons.Add("HEADER_REPAIR_CANDIDATE");
            }

            return new LibraryHealerClassification(LibraryHealerLabel.ProbeEvidence, reasons);
        }

        reasons.Add("PROBE_DURATION_ZERO");
        return new LibraryHealerClassification(LibraryHealerLabel.NeedsHumanReview, reasons);
    }

    private static bool IsMp4FlacSignature(ProbeEvidence probe)
    {
        return Contains(probe.Container, "mp4")
            || Contains(probe.Container, "m4a")
            || Contains(probe.Container, "mov")
            ? Contains(probe.AudioCodec, "flac")
            : false;
    }

    private static bool Contains(string? value, string token)
    {
        return value?.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerClassifierTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: all classifier tests pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add Brainarr.Plugin/Services/Healing/LibraryHealerClassifier.cs Brainarr.Tests/Services/Healing/LibraryHealerClassifierTests.cs
git commit -m "feat: classify library healer evidence"
```

Expected: commit created.

---

### Task 4: Read-Only Fingerprint and Tag Symptom Readers

**Files:**
- Create: `Brainarr.Plugin/Services/Healing/IFileFingerprintService.cs`
- Create: `Brainarr.Plugin/Services/Healing/FileFingerprintService.cs`
- Create: `Brainarr.Plugin/Services/Healing/ITagLibSymptomReader.cs`
- Create: `Brainarr.Plugin/Services/Healing/LidarrAudioTagSymptomReader.cs`
- Test: `Brainarr.Tests/Services/Healing/LidarrAudioTagSymptomReaderTests.cs`

**Interfaces:**
- Consumes: `NzbDrone.Core.MediaFiles.IAudioTagService`
- Produces:
  - `IFileFingerprintService.Read(string path): FileFingerprint`
  - `ITagLibSymptomReader.Read(string path, CancellationToken cancellationToken): TagReaderEvidence`

- [ ] **Step 1: Write failing tag symptom tests**

Create `Brainarr.Tests/Services/Healing/LidarrAudioTagSymptomReaderTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class LidarrAudioTagSymptomReaderTests
{
    [Fact]
    public void Read_ShouldReturnSuccessfulEvidence_WhenLidarrReaderReturnsDuration()
    {
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("song.flac"))
            .Returns(new ParsedTrackInfo
            {
                Duration = TimeSpan.FromSeconds(245)
            });

        var result = new LidarrAudioTagSymptomReader(audio.Object).Read("song.flac", CancellationToken.None);

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeTrue();
        result.DurationSeconds.Should().Be(245);
    }

    [Fact]
    public void Read_ShouldReturnFailureEvidence_WhenLidarrReaderThrows()
    {
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags(@"D:\Music\Private Artist\bad.m4a"))
            .Throws(new InvalidDataException(@"broken D:\Music\Private Artist\bad.m4a"));

        var result = new LidarrAudioTagSymptomReader(audio.Object).Read(@"D:\Music\Private Artist\bad.m4a", CancellationToken.None);

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeFalse();
        result.ErrorType.Should().Be(nameof(InvalidDataException));
        result.ErrorMessage.Should().NotContain("Private Artist");
    }

    [Fact]
    public void Read_ShouldReturnTimeoutEvidence_WhenLidarrReaderHangsPastTimeout()
    {
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("slow.flac"))
            .Returns(() =>
            {
                Thread.Sleep(250);
                return new ParsedTrackInfo { Duration = TimeSpan.FromSeconds(245) };
            });

        var result = new LidarrAudioTagSymptomReader(audio.Object, TimeSpan.FromMilliseconds(25))
            .Read("slow.flac", CancellationToken.None);

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Timed out");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LidarrAudioTagSymptomReaderTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: fails because reader classes are absent.

- [ ] **Step 3: Add reader and fingerprint code**

Create `Brainarr.Plugin/Services/Healing/IFileFingerprintService.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record FileFingerprint(bool Exists, long? Size, DateTime? ModifiedUtc);

public interface IFileFingerprintService
{
    FileFingerprint Read(string path);
}
```

Create `Brainarr.Plugin/Services/Healing/FileFingerprintService.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class FileFingerprintService : IFileFingerprintService
{
    public FileFingerprint Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new FileFingerprint(false, null, null);
        }

        var info = new FileInfo(path);
        return new FileFingerprint(true, info.Length, info.LastWriteTimeUtc);
    }
}
```

Create `Brainarr.Plugin/Services/Healing/ITagLibSymptomReader.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public interface ITagLibSymptomReader
{
    TagReaderEvidence Read(string path, CancellationToken cancellationToken);
}
```

Create `Brainarr.Plugin/Services/Healing/LidarrAudioTagSymptomReader.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class LidarrAudioTagSymptomReader : ITagLibSymptomReader
{
    private readonly IAudioTagService _audioTagService;
    private readonly TimeSpan _timeout;

    public LidarrAudioTagSymptomReader(IAudioTagService audioTagService, TimeSpan? timeout = null)
    {
        _audioTagService = audioTagService ?? throw new ArgumentNullException(nameof(audioTagService));
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public TagReaderEvidence Read(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            var parsed = Task.Run(() => _audioTagService.ReadTags(path), timeout.Token)
                .WaitAsync(_timeout, timeout.Token)
                .GetAwaiter()
                .GetResult();
            var seconds = parsed?.Duration.TotalSeconds;
            return new TagReaderEvidence(true, true, seconds, null, null);
        }
        catch (OperationCanceledException)
        {
            return new TagReaderEvidence(true, false, null, nameof(OperationCanceledException), "Timed out reading audio tags");
        }
        catch (TimeoutException)
        {
            return new TagReaderEvidence(true, false, null, nameof(TimeoutException), "Timed out reading audio tags");
        }
        catch (Exception ex)
        {
            return new TagReaderEvidence(true, false, null, ex.GetType().Name, PathPrivacy.RedactMessage(ex.Message, path));
        }
    }
}
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LidarrAudioTagSymptomReaderTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: all tag symptom reader tests pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add Brainarr.Plugin/Services/Healing/IFileFingerprintService.cs Brainarr.Plugin/Services/Healing/FileFingerprintService.cs Brainarr.Plugin/Services/Healing/ITagLibSymptomReader.cs Brainarr.Plugin/Services/Healing/LidarrAudioTagSymptomReader.cs Brainarr.Tests/Services/Healing/LidarrAudioTagSymptomReaderTests.cs
git commit -m "feat: read library healer file symptoms"
```

Expected: commit created.

---

### Task 5: Finding Store

**Files:**
- Create: `Brainarr.Plugin/Services/Healing/LibraryHealerFindingStore.cs`
- Test: `Brainarr.Tests/Services/Healing/LibraryHealerFindingStoreTests.cs`

**Interfaces:**
- Consumes: `LibraryHealerFinding`
- Produces:
  - `ILibraryHealerFindingStore.SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)`
  - `ILibraryHealerFindingStore.GetRecent(int limit)`
  - `ILibraryHealerFindingStore.Clear()`

- [ ] **Step 1: Write failing store tests**

Create `Brainarr.Tests/Services/Healing/LibraryHealerFindingStoreTests.cs`:

```csharp
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class LibraryHealerFindingStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveBatch_ShouldPersistOnlyRedactedPathAndHash()
    {
        var store = new LibraryHealerFindingStore(_dir);
        var finding = CreateFinding(@"D:\Music\Private Artist\Album\track01.m4a");

        store.SaveBatch(new[] { finding });

        var recent = store.GetRecent(10);
        recent.Should().ContainSingle();
        recent[0].File.RedactedPath.Should().NotContain("Private Artist");
        recent[0].File.PathHash.Should().Be(PathPrivacy.HashPath(@"D:\Music\Private Artist\Album\track01.m4a"));

        var persistedJson = File.ReadAllText(Path.Combine(_dir, "library_healer_findings.json"));
        persistedJson.Should().NotContain("Private Artist");
    }

    [Fact]
    public void Clear_ShouldRemovePersistedFindings()
    {
        var store = new LibraryHealerFindingStore(_dir);
        store.SaveBatch(new[] { CreateFinding("song.flac") });

        store.Clear();

        store.GetRecent(10).Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static LibraryHealerFinding CreateFinding(string path)
    {
        return new LibraryHealerFinding(
            "track-1-" + PathPrivacy.HashPath(path),
            new LibraryHealerFileIdentity(1, 2, 3, PathPrivacy.Redact(path), PathPrivacy.HashPath(path), 123, DateTime.UnixEpoch),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            new TagReaderEvidence(true, true, 0, null, null),
            null,
            DateTime.UnixEpoch);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerFindingStoreTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: fails because finding store is absent.

- [ ] **Step 3: Add finding store**

Create `Brainarr.Plugin/Services/Healing/LibraryHealerFindingStore.cs`:

```csharp
using Lidarr.Plugin.Common.Hosting;
using Lidarr.Plugin.Common.Services.Storage;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public interface ILibraryHealerFindingStore
{
    void SaveBatch(IReadOnlyList<LibraryHealerFinding> findings);
    IReadOnlyList<LibraryHealerFinding> GetRecent(int limit);
    void Clear();
}

public sealed class LibraryHealerFindingStore : ILibraryHealerFindingStore
{
    private readonly JsonFileStore<string, LibraryHealerFinding> _store;

    public LibraryHealerFindingStore(string? dataPath = null)
    {
        var root = dataPath ?? PluginConfigRoots.Resolve("Brainarr");
        Directory.CreateDirectory(root);
        _store = new JsonFileStore<string, LibraryHealerFinding>(
            Path.Combine(root, "library_healer_findings.json"),
            new JsonFileStoreOptions<string>
            {
                KeyNormalizer = static key => (key ?? string.Empty).ToLowerInvariant(),
                KeyComparer = StringComparer.OrdinalIgnoreCase,
                MaxEntries = 5000
            });
    }

    public void SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
    {
        if (findings == null || findings.Count == 0)
        {
            return;
        }

        Task.Run(async () =>
        {
            foreach (var finding in findings)
            {
                await _store.SetAsync(finding.Id, finding).ConfigureAwait(false);
            }
        }).GetAwaiter().GetResult();
    }

    public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
    {
        var bounded = Math.Clamp(limit, 1, 500);
        return Task.Run(async () =>
        {
            var all = new List<LibraryHealerFinding>();
            await foreach (var item in _store.EnumerateAsync().ConfigureAwait(false))
            {
                all.Add(item.Value);
            }

            return all
                .OrderByDescending(x => x.ObservedAtUtc)
                .Take(bounded)
                .ToList();
        }).GetAwaiter().GetResult();
    }

    public void Clear()
    {
        Task.Run(async () => await _store.ClearAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
    }
}
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerFindingStoreTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: all finding store tests pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add Brainarr.Plugin/Services/Healing/LibraryHealerFindingStore.cs Brainarr.Tests/Services/Healing/LibraryHealerFindingStoreTests.cs
git commit -m "feat: persist library healer findings"
```

Expected: commit created.

---

### Task 6: Scan Runner

**Files:**
- Create: `Brainarr.Plugin/Services/Healing/LibraryHealerScanRunner.cs`
- Test: `Brainarr.Tests/Services/Healing/LibraryHealerScanRunnerTests.cs`

**Interfaces:**
- Consumes: `IArtistService`, `IMediaFileService`, `ITagLibSymptomReader`, `IFileFingerprintService`, `ILibraryHealerFindingStore`
- Produces:
  - `ILibraryHealerScanRunner.Scan(LibraryHealerScanRequest? request = null, CancellationToken cancellationToken = default): LibraryHealerScanResult`

- [ ] **Step 1: Write failing scanner tests**

Create `Brainarr.Tests/Services/Healing/LibraryHealerScanRunnerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class LibraryHealerScanRunnerTests
{
    [Fact]
    public void Scan_ShouldEnumerateArtistsAndPersistOnlyNonFalsePositiveFindings()
    {
        var artists = new Mock<IArtistService>();
        var media = new Mock<IMediaFileService>();
        var reader = new Mock<ITagLibSymptomReader>();
        var fingerprint = new Mock<IFileFingerprintService>();
        var store = new InMemoryFindingStore();

        artists.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new Artist { Id = 10 } });
        media.Setup(x => x.GetFilesByArtist(10)).Returns(new List<TrackFile>
        {
            new TrackFile { Id = 1, Artist = null, AlbumId = 20, Path = @"D:\Music\a.flac" },
            new TrackFile { Id = 2, Artist = null, AlbumId = 21, Path = @"D:\Music\b.flac" }
        });
        fingerprint.Setup(x => x.Read(It.IsAny<string>()))
            .Returns(new FileFingerprint(true, 100, DateTime.UnixEpoch));
        reader.Setup(x => x.Read(@"D:\Music\a.flac", It.IsAny<CancellationToken>()))
            .Returns(new TagReaderEvidence(true, true, 0, null, null));
        reader.Setup(x => x.Read(@"D:\Music\b.flac", It.IsAny<CancellationToken>()))
            .Returns(new TagReaderEvidence(true, true, 250, null, null));

        var result = new LibraryHealerScanRunner(
            artists.Object,
            media.Object,
            reader.Object,
            fingerprint.Object,
            store,
            () => DateTime.UnixEpoch).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.ScannedTrackFiles.Should().Be(2);
        result.PersistedFindings.Should().Be(1);
        store.Items.Should().ContainSingle(x => x.File.TrackFileId == 1);
    }

    [Fact]
    public void Scan_ShouldRespectMaxFiles()
    {
        var artists = new Mock<IArtistService>();
        var media = new Mock<IMediaFileService>();
        artists.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new Artist { Id = 10 } });
        media.Setup(x => x.GetFilesByArtist(10)).Returns(new List<TrackFile>
        {
            new TrackFile { Id = 1, AlbumId = 20, Path = "a.flac" },
            new TrackFile { Id = 2, AlbumId = 20, Path = "b.flac" }
        });

        var result = new LibraryHealerScanRunner(
            artists.Object,
            media.Object,
            Mock.Of<ITagLibSymptomReader>(x => x.Read(It.IsAny<string>(), It.IsAny<CancellationToken>()) == new TagReaderEvidence(true, true, 0, null, null)),
            Mock.Of<IFileFingerprintService>(x => x.Read(It.IsAny<string>()) == new FileFingerprint(true, 1, DateTime.UnixEpoch)),
            new InMemoryFindingStore(),
            () => DateTime.UnixEpoch).Scan(new LibraryHealerScanRequest(MaxFiles: 1));

        result.ScannedTrackFiles.Should().Be(1);
        result.AvailableTrackFiles.Should().Be(2);
        result.Truncated.Should().BeTrue();
        result.NextAfterTrackFileId.Should().Be(1);
    }

    [Fact]
    public void Scan_ShouldResumeAfterTrackFileId()
    {
        var artists = new Mock<IArtistService>();
        var media = new Mock<IMediaFileService>();
        artists.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new Artist { Id = 10 } });
        media.Setup(x => x.GetFilesByArtist(10)).Returns(new List<TrackFile>
        {
            new TrackFile { Id = 1, AlbumId = 20, Path = "a.flac" },
            new TrackFile { Id = 2, AlbumId = 20, Path = "b.flac" }
        });
        var store = new InMemoryFindingStore();

        var result = new LibraryHealerScanRunner(
            artists.Object,
            media.Object,
            Mock.Of<ITagLibSymptomReader>(x => x.Read(It.IsAny<string>(), It.IsAny<CancellationToken>()) == new TagReaderEvidence(true, true, 0, null, null)),
            Mock.Of<IFileFingerprintService>(x => x.Read(It.IsAny<string>()) == new FileFingerprint(true, 1, DateTime.UnixEpoch)),
            store,
            () => DateTime.UnixEpoch).Scan(new LibraryHealerScanRequest(AfterTrackFileId: 1, MaxFiles: 10));

        result.ScannedTrackFiles.Should().Be(1);
        store.Items.Should().ContainSingle(x => x.File.TrackFileId == 2);
    }

    [Fact]
    public void Scan_ShouldNotMutateMediaFileCanary()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"), "canary.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(tempFile)!);
        File.WriteAllText(tempFile, "original");
        var originalBytes = File.ReadAllBytes(tempFile);
        var originalMtime = File.GetLastWriteTimeUtc(tempFile);

        var artists = new Mock<IArtistService>();
        var media = new Mock<IMediaFileService>();
        artists.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new Artist { Id = 10 } });
        media.Setup(x => x.GetFilesByArtist(10)).Returns(new List<TrackFile>
        {
            new TrackFile { Id = 1, AlbumId = 20, Path = tempFile }
        });

        new LibraryHealerScanRunner(
            artists.Object,
            media.Object,
            Mock.Of<ITagLibSymptomReader>(x => x.Read(tempFile, It.IsAny<CancellationToken>()) == new TagReaderEvidence(true, true, 0, null, null)),
            new FileFingerprintService(),
            new InMemoryFindingStore(),
            () => DateTime.UnixEpoch).Scan(new LibraryHealerScanRequest(MaxFiles: 1));

        File.ReadAllBytes(tempFile).Should().Equal(originalBytes);
        File.GetLastWriteTimeUtc(tempFile).Should().Be(originalMtime);
    }

    [Fact]
    public void Scan_ShouldNotThrow_WhenArtistEnumerationFails()
    {
        var artists = new Mock<IArtistService>();
        artists.Setup(x => x.GetAllArtists()).Throws(new InvalidOperationException("db unavailable"));

        var result = new LibraryHealerScanRunner(
            artists.Object,
            Mock.Of<IMediaFileService>(),
            Mock.Of<ITagLibSymptomReader>(),
            Mock.Of<IFileFingerprintService>(),
            new InMemoryFindingStore(),
            () => DateTime.UnixEpoch).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Failed);
        result.ErrorMessage.Should().Be("db unavailable");
    }

    private sealed class InMemoryFindingStore : ILibraryHealerFindingStore
    {
        public List<LibraryHealerFinding> Items { get; } = new();

        public void SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
        {
            Items.AddRange(findings);
        }

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
        {
            return Items.Take(limit).ToList();
        }

        public void Clear()
        {
            Items.Clear();
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerScanRunnerTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: fails because scan runner is absent.

- [ ] **Step 3: Add scan runner**

Create `Brainarr.Plugin/Services/Healing/LibraryHealerScanRunner.cs`:

```csharp
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record LibraryHealerScanResult(
    LibraryHealerScanStatus Status,
    int TotalArtists,
    int AvailableTrackFiles,
    int ScannedTrackFiles,
    int PersistedFindings,
    bool Truncated,
    int? NextAfterTrackFileId,
    string? ErrorMessage);

public sealed record LibraryHealerScanRequest(int? ArtistId = null, int? AfterTrackFileId = null, int MaxFiles = 100, int MaxSeconds = 10);

public interface ILibraryHealerScanRunner
{
    LibraryHealerScanResult Scan(LibraryHealerScanRequest? request = null, CancellationToken cancellationToken = default);
}

public sealed class LibraryHealerScanRunner : ILibraryHealerScanRunner
{
    private readonly IArtistService _artistService;
    private readonly IMediaFileService _mediaFileService;
    private readonly ITagLibSymptomReader _tagReader;
    private readonly IFileFingerprintService _fingerprintService;
    private readonly ILibraryHealerFindingStore _store;
    private readonly Func<DateTime> _utcNow;

    public LibraryHealerScanRunner(
        IArtistService artistService,
        IMediaFileService mediaFileService,
        ITagLibSymptomReader tagReader,
        IFileFingerprintService fingerprintService,
        ILibraryHealerFindingStore store,
        Func<DateTime>? utcNow = null)
    {
        _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
        _mediaFileService = mediaFileService ?? throw new ArgumentNullException(nameof(mediaFileService));
        _tagReader = tagReader ?? throw new ArgumentNullException(nameof(tagReader));
        _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public LibraryHealerScanResult Scan(LibraryHealerScanRequest? request = null, CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new LibraryHealerScanRequest();
            var maxFiles = Math.Clamp(request.MaxFiles, 1, 500);
            var afterTrackFileId = Math.Max(0, request.AfterTrackFileId.GetValueOrDefault());
            var artists = _artistService.GetAllArtists();
            if (request.ArtistId.HasValue)
            {
                artists = artists.Where(a => a.Id == request.ArtistId.Value).ToList();
            }

            var findings = new List<LibraryHealerFinding>();
            var scannedTrackFiles = 0;
            var truncated = false;
            int? nextAfterTrackFileId = null;
            var candidates = new List<(int ArtistId, TrackFile File)>();

            foreach (var artist in artists)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var files = _mediaFileService.GetFilesByArtist(artist.Id);
                foreach (var file in files.Where(f => f.Id > afterTrackFileId))
                {
                    candidates.Add((artist.Id, file));
                }
            }

            candidates = candidates.OrderBy(x => x.File.Id).ToList();
            foreach (var candidate in candidates)
            {
                if (scannedTrackFiles >= maxFiles)
                {
                    truncated = true;
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var finding = BuildFinding(candidate.ArtistId, candidate.File, cancellationToken);
                scannedTrackFiles++;
                nextAfterTrackFileId = candidate.File.Id;
                if (finding.Label != LibraryHealerLabel.FalsePositive)
                {
                    findings.Add(finding);
                }
            }

            _store.SaveBatch(findings);
            return new LibraryHealerScanResult(
                LibraryHealerScanStatus.Completed,
                artists.Count,
                candidates.Count,
                scannedTrackFiles,
                findings.Count,
                truncated,
                nextAfterTrackFileId,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LibraryHealerScanResult(LibraryHealerScanStatus.Failed, 0, 0, 0, 0, false, null, PathPrivacy.RedactMessage(ex.Message));
        }
    }

    private LibraryHealerFinding BuildFinding(int artistId, TrackFile file, CancellationToken cancellationToken)
    {
        var path = file.Path ?? string.Empty;
        var fingerprint = _fingerprintService.Read(path);
        var tagEvidence = _tagReader.Read(path, cancellationToken);
        var classification = LibraryHealerClassifier.Classify(tagEvidence, null);
        var identity = new LibraryHealerFileIdentity(
            file.Id,
            artistId,
            file.AlbumId,
            PathPrivacy.Redact(path),
            PathPrivacy.HashPath(path),
            fingerprint.Size ?? file.Size,
            fingerprint.ModifiedUtc ?? file.Modified);

        return new LibraryHealerFinding(
            "track-" + file.Id + "-" + PathPrivacy.HashPath(path),
            identity,
            classification.Label,
            classification.InternalReasonCodes,
            tagEvidence,
            null,
            _utcNow());
    }
}
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerScanRunnerTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: all scan runner tests pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add Brainarr.Plugin/Services/Healing/LibraryHealerScanRunner.cs Brainarr.Tests/Services/Healing/LibraryHealerScanRunnerTests.cs
git commit -m "feat: scan lidarr files for healer symptoms"
```

Expected: commit created.

---

### Task 7: Read-Only Action Handler and Orchestrator Routing

**Files:**
- Create: `Brainarr.Plugin/Services/Healing/LibraryHealerActionHandler.cs`
- Modify: `Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs`
- Test: `Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs`

**Interfaces:**
- Consumes: `ILibraryHealerScanRunner`, `ILibraryHealerFindingStore`
- Produces read-only UI actions:
  - `healer/scan`
  - `healer/getfindings`
  - `healer/clearfindings`

- [ ] **Step 1: Write failing action tests**

Create `Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs`:

```csharp
using System.Text.Json;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Core;

public class BrainarrOrchestratorHealerActionsTests
{
    [Fact]
    public void HandleAction_ShouldRouteHealerGetFindings()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var scanRunner = new Mock<ILibraryHealerScanRunner>();
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Returns(new LibraryHealerScanResult(LibraryHealerScanStatus.Completed, 0, 0, 0, 0, false, null, null));
        var handler = new LibraryHealerActionHandler(
            scanRunner.Object,
            new FakeFindingStore());
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction(
            "healer/getfindings",
            new Dictionary<string, string> { ["limit"] = "5" },
            new BrainarrSettings());

        result.Should().NotBeNull();
        JsonSerializer.Serialize(result).Should().Contain("\"items\"");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactHealerActionErrors()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new FakeFindingStore(new InvalidOperationException(@"failed D:\Music\Private Artist\secret.flac")));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("error");
        json.Should().NotContain("Private Artist");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    private static BrainarrOrchestrator CreateOrchestrator(
        LibraryHealerActionHandler healerActionHandler,
        Mock<IProviderFactory> providerFactory,
        Mock<IProviderInvoker> providerInvoker,
        Mock<ILibraryAwarePromptBuilder> promptBuilder)
    {
        var logger = TestLogger.CreateNullLogger();
        var libraryAnalyzer = new Mock<ILibraryAnalyzer>();
        libraryAnalyzer.Setup(x => x.AnalyzeLibrary()).Returns(new LibraryProfile());

        return new BrainarrOrchestrator(
            logger,
            providerFactory.Object,
            libraryAnalyzer.Object,
            Mock.Of<IRecommendationCache>(),
            Mock.Of<IProviderHealthMonitor>(),
            Mock.Of<IRecommendationValidator>(),
            Mock.Of<IModelDetectionService>(),
            Mock.Of<IHttpClient>(),
            duplicationPrevention: null,
            providerInvoker: providerInvoker.Object,
            promptBuilder: promptBuilder.Object,
            breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object,
            duplicateFilter: Mock.Of<IDuplicateFilterService>(),
            healerActionHandler: healerActionHandler);
    }

    private sealed class FakeFindingStore : ILibraryHealerFindingStore
    {
        private readonly Exception? _getRecentException;

        public FakeFindingStore(Exception? getRecentException = null)
        {
            _getRecentException = getRecentException;
        }

        public void SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
        {
        }

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
        {
            if (_getRecentException != null)
            {
                throw _getRecentException;
            }

            return Array.Empty<LibraryHealerFinding>();
        }

        public void Clear()
        {
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~BrainarrOrchestratorHealerActionsTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: fails because action handler and orchestrator injection are absent.

- [ ] **Step 3: Add action handler**

Create `Brainarr.Plugin/Services/Healing/LibraryHealerActionHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class LibraryHealerActionHandler
{
    private readonly ILibraryHealerScanRunner _scanRunner;
    private readonly ILibraryHealerFindingStore _store;
    private int _scanInProgress;

    public LibraryHealerActionHandler(ILibraryHealerScanRunner scanRunner, ILibraryHealerFindingStore store)
    {
        _scanRunner = scanRunner ?? throw new ArgumentNullException(nameof(scanRunner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public object Handle(string action, IDictionary<string, string> query)
    {
        return action.ToLowerInvariant() switch
        {
            "healer/scan" => Scan(query),
            "healer/getfindings" => GetFindings(query),
            "healer/clearfindings" => ClearFindings(),
            _ => throw new NotSupportedException($"Healer action '{action}' is not supported")
        };
    }

    private object Scan(IDictionary<string, string> query)
    {
        if (Interlocked.Exchange(ref _scanInProgress, 1) == 1)
        {
            return new { ok = false, status = "Running", error = "A Library Healer scan is already running" };
        }

        try
        {
            var request = ParseScanRequest(query);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(request.MaxSeconds, 1, 30)));
            var result = _scanRunner.Scan(request, cts.Token);
            return new
            {
                ok = result.Status == LibraryHealerScanStatus.Completed,
                status = result.Status.ToString(),
                totalArtists = result.TotalArtists,
                availableTrackFiles = result.AvailableTrackFiles,
                scannedTrackFiles = result.ScannedTrackFiles,
                persistedFindings = result.PersistedFindings,
                truncated = result.Truncated,
                nextAfterTrackFileId = result.NextAfterTrackFileId,
                error = result.ErrorMessage
            };
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, 0);
        }
    }

    private static LibraryHealerScanRequest ParseScanRequest(IDictionary<string, string> query)
    {
        int? artistId = null;
        if (query != null && query.TryGetValue("artistId", out var rawArtistId) && int.TryParse(rawArtistId, out var parsedArtistId))
        {
            artistId = parsedArtistId;
        }

        var maxFiles = 100;
        if (query != null && query.TryGetValue("maxFiles", out var rawMaxFiles) && int.TryParse(rawMaxFiles, out var parsedMaxFiles))
        {
            maxFiles = parsedMaxFiles;
        }

        int? afterTrackFileId = null;
        if (query != null && query.TryGetValue("afterTrackFileId", out var rawAfterTrackFileId) && int.TryParse(rawAfterTrackFileId, out var parsedAfterTrackFileId))
        {
            afterTrackFileId = parsedAfterTrackFileId;
        }

        var maxSeconds = 10;
        if (query != null && query.TryGetValue("maxSeconds", out var rawMaxSeconds) && int.TryParse(rawMaxSeconds, out var parsedMaxSeconds))
        {
            maxSeconds = parsedMaxSeconds;
        }

        return new LibraryHealerScanRequest(artistId, afterTrackFileId, Math.Clamp(maxFiles, 1, 500), Math.Clamp(maxSeconds, 1, 30));
    }

    private object GetFindings(IDictionary<string, string> query)
    {
        var limit = 100;
        if (query != null && query.TryGetValue("limit", out var raw) && int.TryParse(raw, out var parsed))
        {
            limit = parsed;
        }

        var items = _store.GetRecent(limit).Select(f => new
        {
            id = f.Id,
            trackFileId = f.File.TrackFileId,
            albumId = f.File.AlbumId,
            path = f.File.RedactedPath,
            label = f.Label.ToString(),
            reasons = f.InternalReasonCodes,
            observedAtUtc = f.ObservedAtUtc,
            tagReader = f.TagReader,
            probe = f.Probe
        }).ToList();

        return new { items };
    }

    private object ClearFindings()
    {
        _store.Clear();
        return new { ok = true };
    }
}
```

- [ ] **Step 4: Wire orchestrator routing**

Modify `Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs`:

```csharp
// Add using:
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

// Add field:
private readonly LibraryHealerActionHandler _healerActionHandler;

// Add optional constructor parameter near other optional DI:
LibraryHealerActionHandler healerActionHandler = null

// Assign in constructor:
_healerActionHandler = healerActionHandler;

// Add before the switch in HandleAction:
if (action != null && action.StartsWith("healer/", StringComparison.OrdinalIgnoreCase))
{
    if (_healerActionHandler == null)
    {
        return new { error = "Library Healer is not available in this runtime" };
    }

    try
    {
        return _healerActionHandler.Handle(action, query);
    }
    catch (Exception ex)
    {
        return new { error = PathPrivacy.RedactMessage(ex.Message) };
    }
}
```

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~BrainarrOrchestratorHealerActionsTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: healer action routing tests pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add Brainarr.Plugin/Services/Healing/LibraryHealerActionHandler.cs Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs
git commit -m "feat: expose library healer read-only actions"
```

Expected: commit created.

---

### Task 8: DI Wiring and Read-Only Architecture Tests

**Files:**
- Modify: `Brainarr.Plugin/Services/Core/BrainarrOrchestratorFactory.cs`
- Modify: `Brainarr.Plugin/BrainarrImportList.cs`
- Modify: tests that construct `Brainarr` directly and need the new constructor arguments
- Test: `Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs`

**Interfaces:**
- Consumes: services from previous tasks
- Produces: DI registration for A1 services and test guard for forbidden APIs

- [ ] **Step 1: Write failing architecture tests**

Create `Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class LibraryHealerReadOnlyArchitectureTests
{
    [Fact]
    public void HealingProductionCode_ShouldNotReferenceForbiddenMutationApis()
    {
        var root = FindRepoRoot();
        var healingDir = Path.Combine(root, "Brainarr.Plugin", "Services", "Healing");
        var forbidden = new[]
        {
            "WriteTags(",
            "SyncTags(",
            "RemoveMusicBrainzTags(",
            ".UpdateMediaInfo(",
            ".DeleteMany(",
            ".Delete(",
            "IManageCommandQueue",
            "RefreshArtistCommand",
            "BulkRefreshArtistCommand",
            "RescanFoldersCommand",
            "AlbumSearchCommand",
            "IAIProvider",
            "IProviderFactory",
            "IProviderInvoker",
            "ILibraryAwarePromptBuilder"
        };

        var files = Directory.GetFiles(healingDir, "*.cs", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();
        var offenders = files
            .SelectMany(file => forbidden
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => file + " contains " + token))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void HealingProductionCode_ShouldNotUseMediaFileWriteOperations()
    {
        var root = FindRepoRoot();
        var healingDir = Path.Combine(root, "Brainarr.Plugin", "Services", "Healing");
        var forbidden = new[]
        {
            "File.Move(",
            "File.Delete(",
            "File.Copy(",
            "Directory.Move(",
            "Directory.Delete(",
            ".LastWriteTime =",
            ".LastWriteTimeUtc =",
            "SetLastWriteTime"
        };

        var offenders = Directory.GetFiles(healingDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => forbidden
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => file + " contains " + token))
            .ToList();

        offenders.Should().BeEmpty();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Brainarr.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Brainarr.sln not found");
    }
}
```

- [ ] **Step 2: Run tests to verify the guard exists**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerReadOnlyArchitectureTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected before DI changes: architecture tests pass once healing files exist; if they fail, remove the forbidden reference rather than weakening the guard.

- [ ] **Step 3: Inject Lidarr media/audio services into the plugin provider**

Modify `Brainarr.Plugin/BrainarrImportList.cs`:

```csharp
// Add using:
using NzbDrone.Core.MediaFiles;

// Add fields:
private readonly IMediaFileService _mediaFileService;
private readonly IAudioTagService _audioTagService;

// Add these parameters to the public constructor after IAlbumService albumService:
IMediaFileService mediaFileService,
IAudioTagService audioTagService,

// Pass them through to the internal constructor:
: this(httpClient, importListStatusService, configService, parsingService, artistService, albumService, mediaFileService, audioTagService, logger, orchestratorOverride: null)

// Add matching parameters to the internal constructor before Logger logger:
IMediaFileService mediaFileService,
IAudioTagService audioTagService,

// Assign in the constructor body:
_mediaFileService = mediaFileService ?? throw new ArgumentNullException(nameof(mediaFileService));
_audioTagService = audioTagService ?? throw new ArgumentNullException(nameof(audioTagService));

// Register in module.BuildServiceProvider:
services.AddSingleton(_mediaFileService);
services.AddSingleton(_audioTagService);
```

Update every direct `new Brainarr(...)` in tests to pass:

```csharp
Mock.Of<NzbDrone.Core.MediaFiles.IMediaFileService>(),
Mock.Of<NzbDrone.Core.MediaFiles.IAudioTagService>(),
```

Run after the constructor changes:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~BrainarrDependencyInjectionTests|FullyQualifiedName~BrainarrImportListIntegrationTests|FullyQualifiedName~BrainarrImportListTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: constructor-focused tests pass.

- [ ] **Step 4: Register healing services in DI**

Modify `Brainarr.Plugin/Services/Core/BrainarrOrchestratorFactory.cs`:

```csharp
// Add usings:
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;

// Add registrations after support services:
services.TryAddSingleton<IFileFingerprintService, FileFingerprintService>();
services.TryAddSingleton<ITagLibSymptomReader, LidarrAudioTagSymptomReader>();
services.TryAddSingleton<ILibraryHealerFindingStore>(_ => new LibraryHealerFindingStore());
services.TryAddSingleton<ILibraryHealerScanRunner, LibraryHealerScanRunner>();
services.TryAddSingleton<LibraryHealerActionHandler>();

// Add to BrainarrOrchestrator constructor call:
healerActionHandler: sp.GetService<LibraryHealerActionHandler>()
```

`IAudioTagService` and `IMediaFileService` must be provided by `BrainarrImportList` at runtime and by mocks in tests. Do not introduce service locator lookups inside healing services.

- [ ] **Step 5: Add DI smoke test**

Add to `Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs`:

```csharp
[Fact]
public void BrainarrOrchestratorFactory_ShouldRegisterHealerServices()
{
    var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
    services.AddSingleton(Brainarr.Tests.Helpers.TestLogger.CreateNullLogger());
    services.AddSingleton(Moq.Mock.Of<NzbDrone.Common.Http.IHttpClient>());
    services.AddSingleton(Moq.Mock.Of<NzbDrone.Core.Music.IArtistService>());
    services.AddSingleton(Moq.Mock.Of<NzbDrone.Core.Music.IAlbumService>());
    services.AddSingleton(Moq.Mock.Of<NzbDrone.Core.MediaFiles.IMediaFileService>());
    services.AddSingleton(Moq.Mock.Of<NzbDrone.Core.MediaFiles.IAudioTagService>());

    var configure = typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Core.BrainarrOrchestratorFactory)
        .GetMethod("ConfigureServices", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    configure!.Invoke(null, new object[] { services });

    using var provider = services.BuildServiceProvider();
    provider.GetService<NzbDrone.Core.ImportLists.Brainarr.Services.Healing.LibraryHealerActionHandler>()
        .Should()
        .NotBeNull();
}
```

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerReadOnlyArchitectureTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: architecture and DI smoke tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add Brainarr.Plugin/Services/Core/BrainarrOrchestratorFactory.cs Brainarr.Plugin/BrainarrImportList.cs Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs Brainarr.Tests/BrainarrDependencyInjectionTests.cs Brainarr.Tests/BrainarrImportListIntegrationTests.cs Brainarr.Tests/ImportList/BrainarrImportListTests.cs Brainarr.Tests/Observability/ObservabilityAdoptionTests.cs
git commit -m "feat: wire library healer read-only services"
```

Expected: commit created.

---

### Task 9: Documentation

**Files:**
- Create: `docs/library-healer.md`
- Modify: `docs/README.md`

**Interfaces:**
- Consumes: implemented A1 actions and labels
- Produces: user-facing read-only feature documentation

- [ ] **Step 1: Write user documentation**

Create `docs/library-healer.md`:

```markdown
# Brainarr Library Healer

Library Healer is a read-only diagnostic surface for Lidarr-managed track files. Milestone A1 detects files where Lidarr's own tag reader reports a missing or zero duration and records evidence for review.

## A1 Scope

A1 can:
- enumerate Lidarr artists and track files;
- call Lidarr's `IAudioTagService.ReadTags(string)` on each `TrackFile.Path`;
- classify read-only evidence as `FalsePositive`, `TagReaderSymptom`, or `NeedsHumanReview` in the default A1 scan;
- store findings under Brainarr's plugin AppData directory;
- show redacted paths by default.

A1 cannot:
- repair files;
- import files;
- delete files;
- replace files;
- enqueue Lidarr rescans or searches;
- write tags;
- call AI providers.

## Actions

- `healer/scan`: runs one bounded read-only diagnostic batch and stores current findings. It defaults to 100 files, caps at 500 files, supports `artistId`, `afterTrackFileId`, and `maxSeconds`, and returns `truncated=true` plus `nextAfterTrackFileId` when more files remain.
- `healer/getfindings`: returns recent findings with redacted paths.
- `healer/clearfindings`: clears Brainarr-owned findings.

## Safety Model

The A1 implementation is intentionally diagnostic. It includes architecture tests that block references to Lidarr mutation APIs, command queue actions, and media file move/delete/copy operations from the healing subsystem.

## Path Privacy

Default action output returns `basename#hash`, where `hash` is the first 12 hex characters of a SHA-256 hash of the normalized full path. This lets repeated findings correlate without exposing full local folder structure in screenshots or logs.

## Next Milestones

A2 may add richer probe evidence. A3 may add lossless repair-in-place only after a separate design review, crash-recovery journal, fixture matrix, and explicit opt-in.
```

- [ ] **Step 2: Link from docs index**

Modify `docs/README.md` by adding a Library Healer bullet near other feature docs:

```markdown
- [Library Healer](library-healer.md) - read-only diagnostics for Lidarr-managed track files with tag-reader duration symptoms.
```

- [ ] **Step 3: Verify docs contain no write-capability claims**

Run:

```powershell
rg -n "repair|delete|replace|rescan|search|write tags|import" docs/library-healer.md
```

Expected: matches appear only in the "A1 cannot" or "Next Milestones" sections.

- [ ] **Step 4: Commit**

Run:

```powershell
git add docs/library-healer.md docs/README.md
git commit -m "docs: document library healer read-only diagnostics"
```

Expected: commit created.

---

### Task 10: Adversarial Review and Hardening Pass

**Files:**
- Review all files changed in Tasks 2-9
- Modify only files required by review findings

**Interfaces:**
- Consumes: full A1 implementation
- Produces: reviewed, hardened A1 ready for final verification

- [ ] **Step 1: Run focused healing tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~Healing|FullyQualifiedName~BrainarrOrchestratorHealerActionsTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: `Failed: 0`.

- [ ] **Step 2: Run full Brainarr tests**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --no-build -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected: `Failed: 0`.

- [ ] **Step 3: Run adversarial code review**

Spawn a read-only reviewer with this prompt:

```text
Review the Library Healer A1 implementation in branch feature/import-brain-a1. Focus on data-loss risk, read-only boundary violations, path privacy leaks, Lidarr command queue or media mutation calls, sync-over-async deadlocks, long scan behavior, DI breakage, and test gaps. A1 is allowed to write only Brainarr-owned diagnostic files under plugin AppData and must not mutate media files or Lidarr state. Return Critical, Important, and Minor findings with file and line references.
```

Expected: no Critical or Important findings remain. Minor findings may be fixed or recorded with rationale.

- [ ] **Step 4: Fix accepted review findings using TDD**

For each accepted Critical or Important finding, add or extend the closest A1 test class and run the focused A1 safety slice:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerReadOnlyArchitectureTests|FullyQualifiedName~LibraryHealerScanRunnerTests|FullyQualifiedName~BrainarrOrchestratorHealerActionsTests|FullyQualifiedName~PathPrivacyTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
```

Expected before fix: test fails for the reviewed bug. After fix: focused test passes.

- [ ] **Step 5: Commit hardening changes**

Run:

```powershell
git status --short
git add Brainarr.Plugin/Services/Healing/LibraryHealerLabels.cs Brainarr.Plugin/Services/Healing/LibraryHealerEvidence.cs Brainarr.Plugin/Services/Healing/PathPrivacy.cs Brainarr.Plugin/Services/Healing/LibraryHealerClassifier.cs Brainarr.Plugin/Services/Healing/IFileFingerprintService.cs Brainarr.Plugin/Services/Healing/FileFingerprintService.cs Brainarr.Plugin/Services/Healing/ITagLibSymptomReader.cs Brainarr.Plugin/Services/Healing/LidarrAudioTagSymptomReader.cs Brainarr.Plugin/Services/Healing/LibraryHealerFindingStore.cs Brainarr.Plugin/Services/Healing/LibraryHealerScanRunner.cs Brainarr.Plugin/Services/Healing/LibraryHealerActionHandler.cs Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs Brainarr.Plugin/Services/Core/BrainarrOrchestratorFactory.cs Brainarr.Plugin/BrainarrImportList.cs Brainarr.Tests/Services/Healing/PathPrivacyTests.cs Brainarr.Tests/Services/Healing/LibraryHealerClassifierTests.cs Brainarr.Tests/Services/Healing/LidarrAudioTagSymptomReaderTests.cs Brainarr.Tests/Services/Healing/LibraryHealerFindingStoreTests.cs Brainarr.Tests/Services/Healing/LibraryHealerScanRunnerTests.cs Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs docs/library-healer.md docs/README.md
git commit -m "fix: harden library healer read-only diagnostics"
```

Expected: commit created if review changes were required. If no changes were required, record `No hardening commit needed; adversarial review found no Critical or Important issues` in the worker notes.

---

## Final Verification

Run these from the feature worktree:

```powershell
git status --short --branch
dotnet build Brainarr.sln -c Release -m:1 -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --no-build -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0
$forbiddenPattern = "WriteTags\(|SyncTags\(|RemoveMusicBrainzTags\(|UpdateMediaInfo\(|DeleteMany\(|IManageCommandQueue|RefreshArtistCommand|BulkRefreshArtistCommand|RescanFoldersCommand|AlbumSearchCommand|IAIProvider|IProviderFactory|IProviderInvoker|ILibraryAwarePromptBuilder|File\.Move\(|File\.Delete\(|File\.Copy\(|Directory\.Move\(|Directory\.Delete\("
if (rg -n $forbiddenPattern Brainarr.Plugin/Services/Healing) { throw "Forbidden Library Healer mutation reference found" }
$redFlagPattern = ('TO' + 'DO|TB' + 'D|fix' + 'me|should ' + 'maybe|\?\?\?')
if (rg -n $redFlagPattern docs/superpowers/plans docs/superpowers/specs docs/library-healer.md) { throw "Documentation red-flag marker found" }
```

Expected:
- build has `0 Error(s)`;
- tests have `Failed: 0`;
- forbidden API scan has no matches;
- red-flag scan has no matches.

## Merge Back to Main

After final verification and adversarial review are green:

```powershell
cd C:\R\Alex\github\brainarr
git status --short --branch
git fetch gitea
git merge --no-ff feature/import-brain-a1
dotnet build Brainarr.sln -c Release -m:1 -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --no-build -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected:
- `main` is clean before merge;
- merge has no conflicts;
- post-merge build has `0 Error(s)`;
- post-merge tests have `Failed: 0`.

Do not remove the worktree until the post-merge verification has passed and the user confirms cleanup.

# Task 4 Review Fix Report

## Scope

- Bounded `LidarrAudioTagSymptomReader` so one wrapper instance can have at most one active `IAudioTagService.ReadTags(string)` call.
- Kept the gate held until the underlying synchronous reader actually exits after timeout/cancellation.
- Updated timeout evidence to say the wrapper timed out waiting for Lidarr's audio tag reader.
- Hardened `FileFingerprintService` so expected filesystem churn and path/access/IO errors return a missing fingerprint.

## TDD Evidence

RED:

```text
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~LidarrAudioTagSymptomReaderTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0 -p:UseSharedCompilation=false -p:BuildInParallel=false

Failed: 2, Passed: 7, Skipped: 0, Total: 9
Read_ShouldNotStartSecondLidarrRead_WhenFirstTimedOutReadIsStillRunning:
  Expected secondResult.ReadSucceeded to be false, but found True.
Read_ShouldUsePreciseTimeoutEvidence_WhenWaitingForLidarrReaderTimesOut:
  Expected "Timed out waiting for Lidarr audio tag reader", but found "Timed out reading audio tags".
```

GREEN:

```text
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~LidarrAudioTagSymptomReaderTests" -p:LidarrPath=C:\R\Alex\github\.codex-worktrees\brainarr-import-brain-a1\ext\Lidarr\_output\net8.0 -p:UseSharedCompilation=false -p:BuildInParallel=false

Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9
```

## Notes

- The invalid-path fingerprint test is deterministic but the main TOCTOU behavior is covered by production catch handling rather than a brittle filesystem race test.
- Upstream Lidarr may still log raw paths inside `IAudioTagService.ReadTags(string)` before this wrapper sees exceptions. This change keeps the verified contract and only controls this wrapper's persisted diagnostic redaction.

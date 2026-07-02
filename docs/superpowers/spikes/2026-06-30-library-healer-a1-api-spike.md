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

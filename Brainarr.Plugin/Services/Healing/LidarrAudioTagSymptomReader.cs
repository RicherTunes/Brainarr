using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class LidarrAudioTagSymptomReader : ITagLibSymptomReader
{
    public const string ReaderBusyErrorType = "TagReaderBusyException";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly IAudioTagService _audioTagService;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly TimeSpan _timeout;

    public LidarrAudioTagSymptomReader(IAudioTagService audioTagService, TimeSpan? timeout = null)
    {
        _audioTagService = audioTagService ?? throw new ArgumentNullException(nameof(audioTagService));
        _timeout = timeout ?? DefaultTimeout;
    }

    public TagReaderEvidence Read(string path, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new TagReaderEvidence(
                true,
                false,
                null,
                nameof(OperationCanceledException),
                "Canceled reading audio tags");
        }

        try
        {
            if (!_readGate.Wait(_timeout, cancellationToken))
            {
                return BusyEvidence();
            }

            var readTask = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        return _audioTagService.ReadTags(path);
                    }
                    finally
                    {
                        _readGate.Release();
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            var parsed = readTask
                .WaitAsync(_timeout, cancellationToken)
                .GetAwaiter()
                .GetResult();

            return new TagReaderEvidence(
                true,
                true,
                parsed?.Duration.TotalSeconds,
                null,
                null,
                BuildMetadataEvidence(parsed));
        }
        catch (TimeoutException)
        {
            return TimeoutEvidence();
        }
        catch (OperationCanceledException)
        {
            return new TagReaderEvidence(
                true,
                false,
                null,
                nameof(OperationCanceledException),
                "Canceled reading audio tags");
        }
        catch (Exception ex)
        {
            return new TagReaderEvidence(
                true,
                false,
                null,
                ex.GetType().Name,
                PathPrivacy.RedactMessage(ex.Message, path));
        }
    }

    private static TagReaderEvidence TimeoutEvidence()
    {
        return new TagReaderEvidence(
            true,
            false,
            null,
            nameof(TimeoutException),
            "Timed out waiting for Lidarr audio tag reader");
    }

    private static TagReaderEvidence BusyEvidence()
    {
        return new TagReaderEvidence(
            true,
            false,
            null,
            ReaderBusyErrorType,
            "Lidarr audio tag reader is busy with a prior timed-out read");
    }

    private static TagMetadataEvidence BuildMetadataEvidence(ParsedTrackInfo? parsed)
    {
        var titlePresent = HasValue(parsed?.Title);
        var artistPresent = HasValue(parsed?.ArtistTitle);
        var albumPresent = HasValue(parsed?.AlbumTitle);
        var anyMusicBrainzIdPresent =
            HasValue(parsed?.ArtistMBId)
            || HasValue(parsed?.AlbumMBId)
            || HasValue(parsed?.ReleaseMBId)
            || HasValue(parsed?.RecordingMBId)
            || HasValue(parsed?.TrackMBId);

        var missing = new List<string>();
        if (!titlePresent)
        {
            missing.Add(TagMetadataFields.Title);
        }

        if (!artistPresent)
        {
            missing.Add(TagMetadataFields.Artist);
        }

        if (!albumPresent)
        {
            missing.Add(TagMetadataFields.Album);
        }

        if (!anyMusicBrainzIdPresent)
        {
            missing.Add(TagMetadataFields.MusicBrainzId);
        }

        return new TagMetadataEvidence(
            titlePresent,
            artistPresent,
            albumPresent,
            anyMusicBrainzIdPresent,
            missing);
    }

    private static bool HasValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }
}

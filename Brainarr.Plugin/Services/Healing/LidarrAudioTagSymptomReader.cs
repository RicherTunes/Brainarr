using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class LidarrAudioTagSymptomReader : ITagLibSymptomReader
{
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
                return TimeoutEvidence();
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

            return new TagReaderEvidence(true, true, parsed?.Duration.TotalSeconds, null, null);
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
}

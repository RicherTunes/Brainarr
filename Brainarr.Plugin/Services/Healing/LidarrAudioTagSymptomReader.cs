using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class LidarrAudioTagSymptomReader : ITagLibSymptomReader
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly IAudioTagService _audioTagService;
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
            var parsed = Task.Run(() => _audioTagService.ReadTags(path))
                .WaitAsync(_timeout, cancellationToken)
                .GetAwaiter()
                .GetResult();

            return new TagReaderEvidence(true, true, parsed?.Duration.TotalSeconds, null, null);
        }
        catch (TimeoutException)
        {
            return new TagReaderEvidence(
                true,
                false,
                null,
                nameof(TimeoutException),
                "Timed out reading audio tags");
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
}

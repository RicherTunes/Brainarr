namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public interface ITagLibSymptomReader
{
    TagReaderEvidence Read(string path, CancellationToken cancellationToken);
}

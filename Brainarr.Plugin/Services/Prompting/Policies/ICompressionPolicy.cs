namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;

public interface ICompressionPolicy
{
    int MinAlbumsPerGroup { get; }
    double MaxRelaxedInflation { get; }
    int AbsoluteRelaxedCap { get; }
}

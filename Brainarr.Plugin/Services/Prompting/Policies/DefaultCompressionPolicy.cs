namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;

public sealed class DefaultCompressionPolicy : ICompressionPolicy
{
    public int MinAlbumsPerGroup => 3;
    public double MaxRelaxedInflation => 3.0;
    public int AbsoluteRelaxedCap => 5000;
}

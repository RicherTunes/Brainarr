namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;

public interface IContextPolicy
{
    int DetermineTargetArtistCount(int totalArtists, int tokenBudget);
    int DetermineTargetAlbumCount(int totalAlbums, int tokenBudget);
}

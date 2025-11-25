using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;

public sealed class DefaultContextPolicy : IContextPolicy
{
    public int DetermineTargetArtistCount(int totalArtists, int tokenBudget)
    {
        if (totalArtists <= 50)
        {
            return Math.Min(40, totalArtists);
        }

        if (totalArtists <= 200)
        {
            return Math.Min(60, Math.Max(30, totalArtists / 2));
        }

        return Math.Min(90, Math.Max(32, tokenBudget / 260));
    }

    public int DetermineTargetAlbumCount(int totalAlbums, int tokenBudget)
    {
        if (totalAlbums <= 120)
        {
            return Math.Min(100, totalAlbums);
        }

        if (totalAlbums <= 400)
        {
            return Math.Min(160, Math.Max(60, totalAlbums / 2));
        }

        return Math.Min(220, Math.Max(70, tokenBudget / 120));
    }
}

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration.Enums
{
    /// <summary>
    /// Discovery modes for music recommendation generation
    /// </summary>
    public enum DiscoveryMode
    {
        Similar = 0,      // Very similar to existing library
        Adjacent = 1,     // Related genres
        Exploratory = 2   // New genres to explore
    }
}
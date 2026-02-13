namespace NzbDrone.Core.ImportLists.Brainarr
{
    public class IterationProfile
    {
        public bool EnableRefinement { get; set; }
        public int MaxIterations { get; set; }
        public int ZeroStop { get; set; }
        public int LowStop { get; set; }
        public int CooldownMs { get; set; }
        public bool GuaranteeExactTarget { get; set; }
    }
}

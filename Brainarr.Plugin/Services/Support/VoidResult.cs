namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    // Helper class for void async operations with RateLimiter
    public class VoidResult
    {
        public static readonly VoidResult Instance = new VoidResult();
        private VoidResult() { }
    }
}

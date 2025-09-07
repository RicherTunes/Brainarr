using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry
{
    public enum BrainarrEvent
    {
        ProviderSelected,
        CacheHit,
        CacheMiss,
        CircuitOpened,
        SanitizationComplete,
        ReviewQueued
    }

    public static class EventLogger
    {
        public static void Log(Logger logger, BrainarrEvent evt, string details)
        {
            if (logger == null) return;
            try
            {
                logger.Info($"[{Services.CorrelationContext.Current}] [Event:{evt}] {details}");
            }
            catch { }
        }
    }
}

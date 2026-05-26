using System.Collections.Generic;
using NLog;
using Lidarr.Plugin.Common.TestKit.Helpers;

namespace Brainarr.Tests.Helpers
{
    /// <summary>
    /// Thin shim preserving the original <c>Brainarr.Tests.Helpers.TestLogger</c> API.
    /// All implementation is delegated to <see cref="NLogTestLogger"/> in
    /// <c>Lidarr.Plugin.Common.TestKit</c>.
    /// </summary>
    /// <remarks>
    /// Callers keep their existing <c>using Brainarr.Tests.Helpers;</c> and
    /// <c>TestLogger.*</c> call sites unchanged. The class name is intentionally
    /// preserved so that the ~100 call sites in <c>Brainarr.Tests</c> do not need
    /// mass-renaming — only new tests should prefer <see cref="NLogTestLogger"/>
    /// directly.
    /// </remarks>
    public static class TestLogger
    {
        /// <inheritdoc cref="NLogTestLogger.Create"/>
        public static Logger Create(string name = "TestLogger")
            => NLogTestLogger.Create(name);

        /// <inheritdoc cref="NLogTestLogger.CreateNullLogger"/>
        public static Logger CreateNullLogger(string name = "NullLogger")
            => NLogTestLogger.CreateNullLogger(name);

        /// <inheritdoc cref="NLogTestLogger.GetLoggedMessages"/>
        public static IList<string> GetLoggedMessages()
            => NLogTestLogger.GetLoggedMessages();

        /// <inheritdoc cref="NLogTestLogger.ClearLoggedMessages"/>
        public static void ClearLoggedMessages()
            => NLogTestLogger.ClearLoggedMessages();
    }
}

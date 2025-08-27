using System;
using System.Collections.Generic;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Brainarr.Tests.Helpers
{
    /// <summary>
    /// Test helper that provides a real NLog Logger instance configured for testing.
    /// This replaces Mock&lt;Logger&gt; which doesn't work since Logger is a sealed class.
    /// </summary>
    public static class TestLogger
    {
        private static Logger _testLogger;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets a real NLog Logger instance configured for testing.
        /// This logger writes to an in-memory target for test verification.
        /// </summary>
        public static Logger Create(string name = "TestLogger")
        {
            lock (_lock)
            {
                if (_testLogger == null)
                {
                    var config = new LoggingConfiguration();
                    
                    // Create memory target for test verification
                    var memoryTarget = new MemoryTarget("testMemory")
                    {
                        Layout = "${level:uppercase=true}: ${message} ${exception:format=tostring}"
                    };
                    
                    config.AddTarget(memoryTarget);
                    config.AddRuleForAllLevels(memoryTarget);
                    
                    LogManager.Configuration = config;
                    _testLogger = LogManager.GetLogger(name);
                }
                
                return _testLogger;
            }
        }

        /// <summary>
        /// Creates a no-op logger that discards all log messages.
        /// Use this when you don't care about logging in your test.
        /// </summary>
        public static Logger CreateNullLogger(string name = "NullLogger")
        {
            var config = new LoggingConfiguration();
            
            // Create null target that discards everything
            var nullTarget = new NullTarget("testNull");
            config.AddTarget(nullTarget);
            config.AddRuleForAllLevels(nullTarget);
            
            var tempConfig = LogManager.Configuration;
            LogManager.Configuration = config;
            var logger = LogManager.GetLogger(name);
            LogManager.Configuration = tempConfig;
            
            return logger;
        }

        /// <summary>
        /// Gets logged messages from the memory target for test assertions.
        /// Only works with loggers created via Create() method.
        /// </summary>
        public static IList<string> GetLoggedMessages()
        {
            var memoryTarget = LogManager.Configuration?.FindTargetByName<MemoryTarget>("testMemory");
            return memoryTarget?.Logs ?? new List<string>();
        }

        /// <summary>
        /// Clears all logged messages from the memory target.
        /// </summary>
        public static void ClearLoggedMessages()
        {
            var memoryTarget = LogManager.Configuration?.FindTargetByName<MemoryTarget>("testMemory");
            memoryTarget?.Logs.Clear();
        }
    }
}
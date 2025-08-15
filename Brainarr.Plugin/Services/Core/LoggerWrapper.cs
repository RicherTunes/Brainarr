using System;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class LoggerWrapper : ILogger
    {
        private readonly Logger _logger;

        public LoggerWrapper(Logger logger)
        {
            _logger = logger;
        }

        public void Debug(string message) => _logger.Debug(message);
        public void Info(string message) => _logger.Info(message);
        public void Warn(string message) => _logger.Warn(message);
        public void Error(string message) => _logger.Error(message);
        public void Error(Exception exception, string message) => _logger.Error(exception, message);
    }
}
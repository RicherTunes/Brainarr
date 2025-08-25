using System;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services;

/// <summary>
/// Wrapper for NLog Logger to implement ILogger interface for better testability.
/// </summary>
public class NLogWrapper : ILogger
{
    private readonly Logger _logger;

    public NLogWrapper(Logger logger)
    {
        _logger = logger;
    }

    public void Debug(string message) => _logger.Debug(message);
    public void Debug(string message, params object[] args) => _logger.Debug(message, args);
    public void Info(string message) => _logger.Info(message);
    public void Info(string message, params object[] args) => _logger.Info(message, args);
    public void Warn(string message) => _logger.Warn(message);
    public void Warn(string message, params object[] args) => _logger.Warn(message, args);
    public void Error(string message) => _logger.Error(message);
    public void Error(string message, params object[] args) => _logger.Error(message, args);
    public void Error(Exception exception, string message) => _logger.Error(exception, message);
    public void Error(Exception exception, string message, params object[] args) => _logger.Error(exception, message, args);
}
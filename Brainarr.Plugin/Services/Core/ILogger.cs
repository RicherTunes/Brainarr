using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface ILogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(Exception exception, string message);
    }
}
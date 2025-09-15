using System;
using System.Reflection;

namespace Brainarr.Plugin.Services.Security
{
    internal static class UserAgentHelper
    {
        public static string Build()
        {
            string version = "1.0.0";
            try
            {
                version = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? version;
            }
            catch
            {
                // best-effort; keep safe default
            }

            return $"Brainarr/{version} (+https://github.com/RicherTunes/Brainarr)";
        }
    }
}

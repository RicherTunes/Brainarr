using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Brainarr.TestKit.Providers.Runtime
{
    public static class TestAssemblyResolver
    {
        private static bool _initialized;
        private static string? _baseDir;

        public static void Initialize(string? lidarrPath = null)
        {
            if (_initialized) return;

            _baseDir = ResolveBaseDir(lidarrPath);

            // Only wire the resolver if we have a valid directory
            if (!string.IsNullOrWhiteSpace(_baseDir) && Directory.Exists(_baseDir))
            {
                AssemblyLoadContext.Default.Resolving += OnResolve;
            }

            _initialized = true;
        }

        private static Assembly? OnResolve(AssemblyLoadContext context, AssemblyName name)
        {
            if (string.IsNullOrEmpty(_baseDir)) return null;

            // Try direct DLL match in Lidarr assemblies directory
            var candidate = Path.Combine(_baseDir!, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                try { return context.LoadFromAssemblyPath(candidate); }
                catch { /* fallthrough */ }
            }

            return null;
        }

        private static string? ResolveBaseDir(string? lidarrPath)
        {
            if (!string.IsNullOrWhiteSpace(lidarrPath))
            {
                return Path.GetFullPath(lidarrPath);
            }

            var env = Environment.GetEnvironmentVariable("LIDARR_PATH");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            {
                return Path.GetFullPath(env);
            }

            // Common CI/local extraction locations (relative to solution root)
            string BaseDir() => AppContext.BaseDirectory;

            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(BaseDir(), "..", "..", "..", "..", "ext", "Lidarr-docker", "_output", "net6.0")),
                Path.GetFullPath(Path.Combine(BaseDir(), "..", "..", "..", "..", "ext", "Lidarr", "_output", "net6.0"))
            };

            foreach (var c in candidates)
            {
                if (Directory.Exists(c)) return c;
            }

            return null;
        }
    }
}

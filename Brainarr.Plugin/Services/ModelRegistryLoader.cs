using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Loads the bundled model registry document, supporting both on-disk copies and embedded fallbacks.
    /// </summary>
    public class ModelRegistryLoader
    {
        private const string RegistryFileName = "models.example.json";
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly string _baseDirectory;
        private readonly Assembly _resourceAssembly;
        private readonly Logger _logger;

        public ModelRegistryLoader(string? baseDirectory = null, Assembly? resourceAssembly = null, Logger? logger = null)
        {
            _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
            _resourceAssembly = resourceAssembly ?? typeof(ModelRegistryLoader).Assembly;
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Loads the model registry document from disk or the embedded fallback resource.
        /// </summary>
        public ModelRegistryDocument Load()
        {
            var json = LoadRawJson();
            try
            {
                var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json, SerializerOptions);
                if (document == null)
                {
                    throw new InvalidOperationException("Model registry deserialization returned null.");
                }

                return document;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to parse model registry document.", ex);
            }
        }

        /// <summary>
        /// Returns the raw JSON contents of the model registry.
        /// </summary>
        public string LoadRawJson()
        {
            var diskPath = ResolveDiskPath();
            if (diskPath != null && File.Exists(diskPath))
            {
                _logger.Debug($"Loading model registry from disk: {diskPath}");
                return File.ReadAllText(diskPath, Encoding.UTF8);
            }

            _logger.Debug("Model registry file not found on disk, using embedded fallback.");
            using var stream = OpenEmbeddedResource() ?? throw new InvalidOperationException("Embedded model registry not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private string? ResolveDiskPath()
        {
            foreach (var candidate in EnumerateCandidatePaths())
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private IEnumerable<string> EnumerateCandidatePaths()
        {
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string path)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    unique.Add(Path.GetFullPath(path));
                }
            }

            foreach (var root in GetSearchRoots())
            {
                Add(Path.Combine(root, RegistryFileName));
                Add(Path.Combine(root, "docs", RegistryFileName));
            }

            foreach (var path in unique)
            {
                yield return path;
            }
        }

        private IEnumerable<string> GetSearchRoots()
        {
            var roots = new List<string>();
            if (!string.IsNullOrWhiteSpace(_baseDirectory))
            {
                roots.Add(_baseDirectory);
            }

            var assemblyDirectory = Path.GetDirectoryName(_resourceAssembly.Location);
            if (!string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                roots.Add(assemblyDirectory);
            }

            // Walk up the directory tree to cover publish outputs and development roots.
            foreach (var root in roots.ToList())
            {
                var dir = new DirectoryInfo(root);
                while (dir != null)
                {
                    if (!roots.Contains(dir.FullName))
                    {
                        roots.Add(dir.FullName);
                    }

                    dir = dir.Parent;
                }
            }

            return roots.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private Stream? OpenEmbeddedResource()
        {
            var resourceName = _resourceAssembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("docs.models.example.json", StringComparison.OrdinalIgnoreCase));

            return resourceName != null
                ? _resourceAssembly.GetManifestResourceStream(resourceName)
                : null;
        }
    }
}

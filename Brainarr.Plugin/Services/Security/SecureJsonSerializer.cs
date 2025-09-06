using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Brainarr.Plugin.Services.Security
{
    /// <summary>
    /// Secure JSON serialization service that prevents deserialization attacks
    /// </summary>
    public static class SecureJsonSerializer
    {
        private static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions
        {
            // Security settings
            MaxDepth = 10, // Prevent deeply nested objects that could cause stack overflow
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = false, // Strict JSON parsing
            ReadCommentHandling = JsonCommentHandling.Skip,
            
            // Performance settings
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            
            // Converters for common types
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        private static readonly JsonSerializerOptions StrictOptions = new JsonSerializerOptions
        {
            // Even stricter security settings
            MaxDepth = 5,
            PropertyNameCaseInsensitive = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// Maximum allowed JSON size (10MB default)
        /// </summary>
        private const int MaxJsonSize = 10 * 1024 * 1024; // 10MB

        /// <summary>
        /// Deserialize JSON string to strongly typed object with security checks
        /// </summary>
        public static T Deserialize<T>(string json, bool strict = false) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentNullException(nameof(json), "JSON content cannot be null or empty");
            }

            // Check for excessive size
            if (json.Length > MaxJsonSize)
            {
                throw new InvalidOperationException($"JSON content exceeds maximum allowed size of {MaxJsonSize} bytes");
            }

            // Check for suspicious patterns that might indicate attacks
            ValidateJsonContent(json);

            try
            {
                var options = strict ? StrictOptions : DefaultOptions;
                return JsonSerializer.Deserialize<T>(json, options) ?? throw new InvalidOperationException("Deserialization returned null");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to deserialize JSON: {ex.Message}", ex);
            }
            catch (NotSupportedException ex)
            {
                throw new InvalidOperationException($"Deserialization not supported for type: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deserialize JSON stream to strongly typed object with security checks
        /// </summary>
        public static async Task<T> DeserializeAsync<T>(Stream stream, bool strict = false) where T : class
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            // Check stream size
            if (stream.CanSeek && stream.Length > MaxJsonSize)
            {
                throw new InvalidOperationException($"JSON stream exceeds maximum allowed size of {MaxJsonSize} bytes");
            }

            try
            {
                var options = strict ? StrictOptions : DefaultOptions;
                return await JsonSerializer.DeserializeAsync<T>(stream, options) ?? throw new InvalidOperationException("Deserialization returned null");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to deserialize JSON stream: {ex.Message}", ex);
            }
            catch (NotSupportedException ex)
            {
                throw new InvalidOperationException($"Deserialization not supported for type: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Serialize object to JSON string with security checks
        /// </summary>
        public static string Serialize<T>(T obj, bool strict = false) where T : class
        {
            if (obj == null)
            {
                return "null";
            }

            try
            {
                var options = strict ? StrictOptions : DefaultOptions;
                var json = JsonSerializer.Serialize(obj, options);
                
                // Verify output size
                if (json.Length > MaxJsonSize)
                {
                    throw new InvalidOperationException($"Serialized JSON exceeds maximum allowed size of {MaxJsonSize} bytes");
                }
                
                return json;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to serialize object: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Serialize object to JSON stream with security checks
        /// </summary>
        public static async Task SerializeAsync<T>(Stream stream, T obj, bool strict = false) where T : class
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (obj == null)
            {
                var nullBytes = Encoding.UTF8.GetBytes("null");
                await stream.WriteAsync(nullBytes, 0, nullBytes.Length);
                return;
            }

            try
            {
                var options = strict ? StrictOptions : DefaultOptions;
                await JsonSerializer.SerializeAsync(stream, obj, options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to serialize object to stream: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Try to deserialize JSON with error handling
        /// </summary>
        public static bool TryDeserialize<T>(string json, out T? result, out string? error) where T : class
        {
            result = null;
            error = null;

            try
            {
                result = Deserialize<T>(json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Parse JSON to JsonDocument for safe inspection
        /// </summary>
        public static JsonDocument ParseDocument(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (json.Length > MaxJsonSize)
            {
                throw new InvalidOperationException($"JSON content exceeds maximum allowed size of {MaxJsonSize} bytes");
            }

            ValidateJsonContent(json);

            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 10,
                AllowTrailingCommas = false
            };

            return JsonDocument.Parse(json, options);
        }

        /// <summary>
        /// Parse JSON in a relaxed mode intended for provider responses.
        /// Skips heuristic content checks for strings like "<script>", but preserves size and depth protections.
        /// </summary>
        public static JsonDocument ParseDocumentRelaxed(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (json.Length > MaxJsonSize)
            {
                throw new InvalidOperationException($"JSON content exceeds maximum allowed size of {MaxJsonSize} bytes");
            }

            // Structural safety checks (nesting depth)
            int maxNestingDepth = 0;
            int currentDepth = 0;
            foreach (char c in json)
            {
                if (c == '{' || c == '[')
                {
                    currentDepth++;
                    maxNestingDepth = Math.Max(maxNestingDepth, currentDepth);
                }
                else if (c == '}' || c == ']')
                {
                    currentDepth--;
                }
            }
            if (maxNestingDepth > 20)
            {
                throw new InvalidOperationException($"JSON nesting depth exceeds safe limit: {maxNestingDepth}");
            }

            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 10,
                AllowTrailingCommas = false
            };

            return JsonDocument.Parse(json, options);
        }

        /// <summary>
        /// Validate JSON content for suspicious patterns
        /// </summary>
        private static void ValidateJsonContent(string json)
        {
            // Heuristic checks for potentially dangerous content in strict contexts
            string[] suspiciousPatterns = new[]
            {
                "__proto__",           // Prototype pollution
                "constructor",         // Constructor manipulation
                "$type",              // .NET type injection
                "$id",                // Reference loops
                "$ref",               // Reference loops
                "__defineGetter__",   // Property manipulation
                "__defineSetter__",   // Property manipulation
                "__lookupGetter__",   // Property manipulation
                "__lookupSetter__",   // Property manipulation
                "function(",          // Function constructor
                "eval(",              // Eval injection
                "settimeout(",        // Timeout injection
                "setinterval(",       // Interval injection
                "<script",            // Script injection
                "javascript:",        // JavaScript protocol
                "data:text/html",     // Data URI injection
                "vbscript:",          // VBScript protocol
                "onclick",            // Event handler injection
                "onerror",            // Event handler injection
                "onload"              // Event handler injection
            };

            var lowerJson = json.ToLowerInvariant();
            foreach (var pattern in suspiciousPatterns)
            {
                if (lowerJson.Contains(pattern.ToLowerInvariant()))
                {
                    throw new InvalidOperationException($"Potentially malicious JSON content detected: contains '{pattern}'");
                }
            }

            // Check for excessive nesting (simple check)
            int maxNestingDepth = 0;
            int currentDepth = 0;
            foreach (char c in json)
            {
                if (c == '{' || c == '[')
                {
                    currentDepth++;
                    maxNestingDepth = Math.Max(maxNestingDepth, currentDepth);
                }
                else if (c == '}' || c == ']')
                {
                    currentDepth--;
                }
            }

            if (maxNestingDepth > 20)
            {
                throw new InvalidOperationException($"JSON nesting depth exceeds safe limit: {maxNestingDepth}");
            }

            // Check for excessive array size indicators
            if (System.Text.RegularExpressions.Regex.IsMatch(json, @"\[\s*\d{7,}\s*\]"))
            {
                throw new InvalidOperationException("JSON contains potentially excessive array size");
            }
        }

        /// <summary>
        /// Create custom options with specific settings
        /// </summary>
        public static JsonSerializerOptions CreateOptions(
            int maxDepth = 10,
            bool caseInsensitive = true,
            bool writeIndented = false)
        {
            return new JsonSerializerOptions
            {
                MaxDepth = Math.Min(maxDepth, 20), // Cap at 20 for safety
                PropertyNameCaseInsensitive = caseInsensitive,
                AllowTrailingCommas = false,
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = writeIndented,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
        }
    }
}

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public sealed class StopSensitivityJsonConverter : JsonConverter<StopSensitivity>
    {
        public override StopSensitivity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    var raw = reader.GetString();
                    if (string.IsNullOrWhiteSpace(raw)) return StopSensitivity.Normal;

                    // Try direct enum parse first
                    if (Enum.TryParse<StopSensitivity>(raw, ignoreCase: true, out var parsed))
                    {
                        return parsed;
                    }

                    var norm = raw.Trim().ToLowerInvariant()
                        .Replace("_", "")
                        .Replace("-", "")
                        .Replace(" ", "");

                    // Backwards-compat aliases from earlier versions / labels
                    return norm switch
                    {
                        // Historical label used in help text/UI
                        "balanced" or "medium" => StopSensitivity.Normal,

                        // Common synonyms
                        "lenient" or "low" or "loose" => StopSensitivity.Lenient,
                        "strict" or "high" or "tight" => StopSensitivity.Strict,
                        "aggressive" or "verystrict" or "max" => StopSensitivity.Aggressive,
                        "off" or "none" or "disabled" => StopSensitivity.Off,

                        _ => StopSensitivity.Normal
                    };
                }

                if (reader.TokenType == JsonTokenType.Number)
                {
                    if (reader.TryGetInt32(out var ival))
                    {
                        // Accept in-range numeric representation
                        if (Enum.IsDefined(typeof(StopSensitivity), ival))
                        {
                            return (StopSensitivity)ival;
                        }
                    }
                    return StopSensitivity.Normal;
                }

                if (reader.TokenType == JsonTokenType.Null)
                {
                    return StopSensitivity.Normal;
                }

                // Fallback: try default enum converter behavior via string
                var asString = reader.GetString();
                if (asString != null && Enum.TryParse<StopSensitivity>(asString, true, out var direct))
                {
                    return direct;
                }
            }
            catch
            {
                // Swallow and fall back to safe default
            }

            return StopSensitivity.Normal;
        }

        public override void Write(Utf8JsonWriter writer, StopSensitivity value, JsonSerializerOptions options)
        {
            // Emit camelCase string to align with Lidarr's STJ settings
            string str = value switch
            {
                StopSensitivity.Off => "off",
                StopSensitivity.Lenient => "lenient",
                StopSensitivity.Normal => "normal",
                StopSensitivity.Strict => "strict",
                StopSensitivity.Aggressive => "aggressive",
                _ => "normal"
            };
            writer.WriteStringValue(str);
        }
    }
}

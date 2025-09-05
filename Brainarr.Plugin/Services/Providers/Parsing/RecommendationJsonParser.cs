using System;
using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Brainarr.Plugin.Services.Security;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing
{
    /// <summary>
    /// Central JSON parser for provider recommendation payloads.
    /// Accepts either {"recommendations": [...]}, an array root, or a single object.
    /// Enforces basic schema and clamps values to safe ranges.
    /// </summary>
    public static class RecommendationJsonParser
    {
        public static List<Recommendation> Parse(string json, Logger logger = null)
        {
            var results = new List<Recommendation>();
            if (string.IsNullOrWhiteSpace(json)) return results;

            try
            {
                using var doc = SecureJsonSerializer.ParseDocument(json);
                var root = doc.RootElement;

                JsonElement array = default;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("recommendations", out var recsProp) &&
                    recsProp.ValueKind == JsonValueKind.Array)
                {
                    array = recsProp;
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    array = root;
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    TryAddRecommendation(root, results);
                    return results;
                }

                if (array.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in array.EnumerateArray())
                    {
                        TryAddRecommendation(item, results);
                    }
                }
                else
                {
                    // As a last resort, attempt to extract a fenced ```json block
                    var extracted = TryExtractJsonArrayFromText(json);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        using var doc2 = SecureJsonSerializer.ParseDocument(extracted);
                        if (doc2.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in doc2.RootElement.EnumerateArray())
                            {
                                TryAddRecommendation(item, results);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "Failed to parse recommendations JSON");
                // Fallback: attempt to extract the first JSON array from the raw text and parse that
                try
                {
                    var extracted = TryExtractJsonArrayFromText(json);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        using var doc2 = SecureJsonSerializer.ParseDocument(extracted);
                        if (doc2.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in doc2.RootElement.EnumerateArray())
                            {
                                TryAddRecommendation(item, results);
                            }
                        }
                    }
                }
                catch (Exception ex2)
                {
                    logger?.Warn(ex2, "Fallback array extraction also failed");
                }
            }

            return results;
        }

        private static void TryAddRecommendation(JsonElement item, List<Recommendation> results)
        {
            try
            {
                if (item.ValueKind != JsonValueKind.Object) return;

                string artist = TryGetString(item, "artist") ?? TryGetString(item, "a") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(artist)) return; // artist is required

                string album = TryGetString(item, "album") ?? TryGetString(item, "l") ?? string.Empty;
                string genre = TryGetString(item, "genre") ?? TryGetString(item, "g");
                string reason = TryGetString(item, "reason") ?? TryGetString(item, "r");

                int? year = TryGetInt(item, "year");
                double conf = TryGetDouble(item, "confidence") ?? 0.85;
                conf = Math.Clamp(conf, 0.0, 1.0);

                results.Add(new Recommendation
                {
                    Artist = artist.Trim(),
                    Album = album?.Trim() ?? string.Empty,
                    Genre = string.IsNullOrWhiteSpace(genre) ? null : genre.Trim(),
                    Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                    Year = year,
                    Confidence = conf
                });
            }
            catch
            {
                // Skip malformed item
            }
        }

        private static string TryExtractJsonArrayFromText(string text)
        {
            // Extract first JSON array between '[' and ']' if present
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }
            return null;
        }

        private static string TryGetString(JsonElement obj, string prop)
        {
            if (obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
            return null;
        }

        private static int? TryGetInt(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
            return null;
        }

        private static double? TryGetDouble(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out var s)) return s;
            return null;
        }
    }
}

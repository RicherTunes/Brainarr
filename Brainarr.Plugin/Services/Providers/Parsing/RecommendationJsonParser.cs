using System;
using System.Collections.Generic;
using System.Globalization;
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

            // Strip a leading/trailing Markdown code fence (```json … ```, or a bare ```) BEFORE the
            // strict parse. Verbose chat models (Z.AI Coding / GLM in particular, but also OpenRouter,
            // DeepSeek, etc.) wrap their JSON answer in a fence; the strict parse then throws on the
            // opening backtick ("'`' is an invalid start of a value") and only the salvage path below
            // recovered the items — emitting a misleading exception-bearing Debug line on every
            // otherwise-successful run. Cleaning the fence up front lets the strict parse succeed, so
            // there is no exception to capture and no noise. This is a generic clean (not GLM-only) and
            // a no-op for unfenced payloads, so it cannot regress providers that already return raw JSON.
            json = StripCodeFence(json);

            // Captured (not logged immediately): the primary parse may throw on a truncated/fenced
            // payload that salvage recovers below. The log LEVEL is decided at the end from the outcome.
            Exception parseFailure = null;

            try
            {
                // Use relaxed parsing since provider text may include HTML/script literals as data
                using var doc = SecureJsonSerializer.ParseDocumentRelaxed(json);
                var root = doc.RootElement;

                JsonElement array = default;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    // Primary: recommendations property (case-insensitive)
                    if (root.TryGetProperty("recommendations", out var recsProp) && recsProp.ValueKind == JsonValueKind.Array)
                    {
                        array = recsProp;
                    }
                    else if (root.TryGetProperty("albums", out var albumsProp) && albumsProp.ValueKind == JsonValueKind.Array)
                    {
                        array = albumsProp;
                    }
                    else
                    {
                        foreach (var prop in root.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, "recommendations", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                array = prop.Value; break;
                            }
                            if (string.Equals(prop.Name, "albums", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                array = prop.Value; break;
                            }
                        }
                        // Secondary: nested data.recommendations
                        if (array.ValueKind != JsonValueKind.Array && root.TryGetProperty("data", out var dataObj) && dataObj.ValueKind == JsonValueKind.Object)
                        {
                            if (dataObj.TryGetProperty("recommendations", out var nested) && nested.ValueKind == JsonValueKind.Array)
                            {
                                array = nested;
                            }
                        }
                        // If still not array, try single object mapping
                        if (array.ValueKind != JsonValueKind.Array)
                        {
                            TryAddRecommendation(root, results);
                            return results;
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    array = root;
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
                        using var doc2 = SecureJsonSerializer.ParseDocumentRelaxed(extracted);
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
                // Defer: do NOT warn here. Truncated/fenced payloads (routine for verbose models such
                // as GLM) throw the primary parse but are recovered by the salvage pass below. Warning
                // unconditionally here produced misleading WARN spam on every successful run.
                parseFailure = ex;
                // Fallback: attempt to extract the first JSON array from the raw text and parse that
                try
                {
                    var extracted = TryExtractJsonArrayFromText(json);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        using var doc2 = SecureJsonSerializer.ParseDocumentRelaxed(extracted);
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
                    // Secondary best-effort attempt; object salvage still runs below. Debug, not Warn.
                    logger?.Debug(ex2, "Fallback array extraction also failed; will attempt object salvage");
                }
            }

            // Last-resort salvage for TRUNCATED responses: when a provider hits its max_tokens cap
            // mid-array the JSON has no closing ']' (and TryExtractJsonArrayFromText finds no terminator),
            // so both attempts above recover nothing even though dozens of complete objects precede the
            // cut. This is routine for verbose models (e.g. Z.AI Coding / GLM, which pads ```json output).
            // Scan for balanced top-level {...} objects and parse each, discarding only the partial tail.
            if (results.Count == 0)
            {
                var before = results.Count;
                SalvageObjectsFromText(json, results);
                if (results.Count > before)
                {
                    logger?.Debug("Recovered {0} recommendation(s) by salvaging objects from a truncated/invalid JSON array", results.Count - before);
                }
            }

            // Decide the log level for a primary-parse failure from the final outcome: a recovery
            // (fallback or salvage) is routine and logs at Debug; only a TOTAL loss warrants WARN.
            if (parseFailure != null)
            {
                if (results.Count > 0)
                {
                    logger?.Debug(parseFailure, "Primary recommendations-JSON parse failed but recovered {0} item(s) via fallback/salvage; not warning", results.Count);
                }
                else
                {
                    logger?.Warn(parseFailure, "Failed to parse recommendations JSON (0 recovered after fallback + salvage)");
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
                // Track whether the model actually supplied a (finite) confidence. When it didn't, we
                // still set a display default but mark ConfidenceProvided=false so the confidence floor
                // doesn't silently drop the item when the user raises the floor above that default.
                var rawConf = TryGetDouble(item, "confidence");
                var confProvided = rawConf.HasValue && double.IsFinite(rawConf.Value);
                double conf = rawConf ?? 0.85;
                if (double.IsNaN(conf) || double.IsInfinity(conf)) { conf = 0.85; confProvided = false; }
                if (conf < 0.0) conf = 0.0; // lower bound only; do not clamp upper to preserve provider semantics

                results.Add(new Recommendation
                {
                    Artist = artist.Trim(),
                    Album = album?.Trim() ?? string.Empty,
                    Genre = string.IsNullOrWhiteSpace(genre) ? null : genre.Trim(),
                    Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                    Year = year,
                    Confidence = conf,
                    ConfidenceProvided = confProvided
                });
            }
            catch
            {
                // Skip malformed item
            }
        }

        /// <summary>
        /// Recovers complete JSON objects from text that is not valid JSON as a whole — typically a
        /// recommendation array truncated at the provider's max_tokens cap (no closing brace/bracket).
        /// Walks the text tracking a container stack (<c>{</c> / <c>[</c>) while respecting string
        /// literals and escapes, and extracts every balanced object whose <em>immediately enclosing
        /// container is an array</em> — i.e. the array elements. Doing it by container type rather than
        /// absolute brace depth means it works whether the model emits a bare root array
        /// (<c>[{...},{...}]</c>) or an object-wrapped one (<c>{"recommendations":[{...},{...}]}</c>)
        /// that truncates before the wrapper closes (GLM emits both shapes interchangeably). Nested
        /// sub-objects (e.g. a per-item <c>"meta":{...}</c>) are not extracted separately because their
        /// enclosing container is an object, not an array; they ride along inside their parent element.
        /// The final partial element (whose closing brace never arrives) is left unextracted.
        /// </summary>
        private static void SalvageObjectsFromText(string text, List<Recommendation> results)
        {
            if (string.IsNullOrEmpty(text)) return;

            var containers = new Stack<char>();
            int braceDepth = 0;
            int objStart = -1;
            int objDepth = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    if (escape) { escape = false; }
                    else if (c == '\\') { escape = true; }
                    else if (c == '"') { inString = false; }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '[':
                        containers.Push('[');
                        break;
                    case ']':
                        if (containers.Count > 0 && containers.Peek() == '[') containers.Pop();
                        break;
                    case '{':
                        // Only the elements of an array are candidate recommendations; an object whose
                        // enclosing container is itself an object (or the document root) is a wrapper or
                        // a nested field, not an element.
                        if (objStart < 0 && containers.Count > 0 && containers.Peek() == '[')
                        {
                            objStart = i;
                            objDepth = braceDepth;
                        }
                        containers.Push('{');
                        braceDepth++;
                        break;
                    case '}':
                        if (containers.Count > 0 && containers.Peek() == '{')
                        {
                            containers.Pop();
                            braceDepth--;
                        }
                        if (objStart >= 0 && braceDepth == objDepth)
                        {
                            var objText = text.Substring(objStart, i - objStart + 1);
                            objStart = -1;
                            objDepth = -1;
                            try
                            {
                                using var objDoc = SecureJsonSerializer.ParseDocumentRelaxed(objText);
                                if (objDoc.RootElement.ValueKind == JsonValueKind.Object)
                                {
                                    TryAddRecommendation(objDoc.RootElement, results);
                                }
                            }
                            catch
                            {
                                // Skip an individual object that still won't parse.
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Removes a Markdown code fence wrapping the payload so the strict parse sees raw JSON.
        /// Handles the common chat-model shapes: an opening <c>```</c> optionally followed by a
        /// language tag (<c>```json</c>, <c>```JSON</c>) on its own line, and a closing <c>```</c>
        /// line. The closing fence is optional — a response truncated at the token cap keeps the
        /// opening fence but loses the close, and we still want to drop the leading backtick that
        /// makes the strict parse throw. Returns the input unchanged when no fence is present (so it
        /// never alters raw-JSON payloads), and leaves any non-fence prose around the JSON intact for
        /// the existing array-extraction / salvage passes to handle.
        /// </summary>
        private static string StripCodeFence(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var trimmed = text.Trim();

            // Only act when the payload actually starts with a fence; otherwise leave it untouched so
            // prose-wrapped JSON (e.g. "Some text before ```json [..] ```") still flows to the
            // text-extraction fallback unchanged.
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return text;
            }

            // Drop the opening fence line in full (covers ``` and ```<lang>). The fence marker runs to
            // the end of its line; the JSON begins on the next line.
            var afterOpen = trimmed.Substring(3);
            var newline = afterOpen.IndexOf('\n');
            var body = newline >= 0 ? afterOpen.Substring(newline + 1) : afterOpen;

            // Drop a trailing closing fence if present (it may be absent on a truncated response).
            body = body.TrimEnd();
            if (body.EndsWith("```", StringComparison.Ordinal))
            {
                body = body.Substring(0, body.Length - 3);
            }

            return body.Trim();
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
            // Case-insensitive property lookup
            if (obj.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in obj.EnumerateObject())
                {
                    if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    {
                        return p.Value.GetString();
                    }
                }
            }
            return null;
        }

        private static int? TryGetInt(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
            return null;
        }

        private static double? TryGetDouble(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var s)) return s;
            return null;
        }
    }
}

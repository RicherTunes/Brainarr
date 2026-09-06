using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared
{
    /// <summary>
    /// Parses the buffered Server-Sent-Events body returned by the OpenAI Codex ChatGPT-backend
    /// Responses API (<c>POST /backend-api/codex/responses</c> with <c>stream:true</c>).
    ///
    /// <para>
    /// The host's IHttpClient buffers the whole response, so we get the entire SSE stream as one
    /// string and reconstruct the final message from it. Event shape (live-confirmed 2026-08):
    /// a sequence of <c>event: &lt;type&gt;\n data: &lt;json&gt;</c> blocks. The assistant text
    /// arrives as incremental <c>response.output_text.delta</c> events (each <c>data.delta</c> a
    /// chunk) and is finalized by a single <c>response.output_text.done</c> (<c>data.text</c> ==
    /// the full string). Usage totals ride on the terminal <c>response.completed</c> event under
    /// <c>data.response.usage</c>.
    /// </para>
    ///
    /// <para>
    /// Text strategy: accumulate the deltas (handles multi-part output); if no delta was seen,
    /// fall back to the <c>output_text.done</c> text. This is resilient to the backend switching
    /// between chunked and single-shot delivery.
    /// </para>
    /// </summary>
    public static class CodexSseParser
    {
        public static CodexParsedResponse Parse(string? body)
        {
            var result = new CodexParsedResponse();
            if (string.IsNullOrWhiteSpace(body))
            {
                return result;
            }

            var deltas = new StringBuilder();
            string? doneText = null;
            var sawRecognizedEvent = false;

            // SSE lines are separated by \n (\r\n tolerated) and events by a blank line. Per the
            // SSE spec, a data field split across multiple `data:` lines is one payload joined by
            // '\n' — accumulate per event block, then parse the joined payload. Malformed events
            // are skipped without losing the rest of the stream.
            var dataLines = new List<string>();
            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    dataLines.Add(line.Substring("data:".Length).TrimStart());
                    continue;
                }

                if (line.Length > 0)
                {
                    continue; // `event:`/`id:`/comment lines carry no payload we need
                }

                // Blank line = end of event block.
                ProcessEventData(string.Join("\n", dataLines), result, deltas, ref doneText, ref sawRecognizedEvent);
                dataLines.Clear();
            }

            ProcessEventData(string.Join("\n", dataLines), result, deltas, ref doneText, ref sawRecognizedEvent);

            result.Text = deltas.Length > 0 ? deltas.ToString() : (doneText ?? string.Empty);

            // A non-empty body that yielded zero recognized events is not an empty answer — it is
            // a stream we could not understand (proxy injection, backend format change, truncated
            // body). Returning empty-with-success here would log "0 recommendations" with no cause
            // and record auth success for a call that never really completed.
            if (!sawRecognizedEvent)
            {
                result.ErrorDetail ??= "The Codex stream contained no recognized response events.";
            }

            return result;
        }

        private static void ProcessEventData(
            string json, CodexParsedResponse result, StringBuilder deltas, ref string? doneText, ref bool sawRecognizedEvent)
        {
            if (json.Length == 0 || json == "[DONE]")
            {
                return;
            }

            JObject evt;
            try
            {
                evt = JObject.Parse(json);
            }
            catch
            {
                return; // skip a malformed event, keep parsing the rest
            }

            var type = evt.Value<string>("type");
            switch (type)
            {
                case "response.output_text.delta":
                    sawRecognizedEvent = true;
                    deltas.Append(evt.Value<string>("delta") ?? string.Empty);
                    break;

                case "response.output_text.done":
                    sawRecognizedEvent = true;
                    doneText = evt.Value<string>("text") ?? doneText;
                    break;

                case "response.completed":
                    sawRecognizedEvent = true;
                    ApplyCompleted(evt, result);
                    break;

                case "response.incomplete":
                    // Terminal event for content-filter stops and length truncation: the response
                    // object carries status "incomplete" plus incomplete_details.reason. Record
                    // the reason so the caller can surface why the text is short instead of
                    // treating a truncated answer as a clean success.
                    sawRecognizedEvent = true;
                    ApplyCompleted(evt, result);
                    result.ErrorDetail ??= ExtractIncompleteReason(evt);
                    break;

                case "response.failed":
                case "error":
                    sawRecognizedEvent = true;
                    result.ErrorDetail ??= ExtractError(evt);
                    break;
            }
        }

        private static string? ExtractIncompleteReason(JObject evt)
        {
            var response = evt["response"] as JObject;
            var reason = response?["incomplete_details"]?.Value<string>("reason");
            return string.IsNullOrEmpty(reason)
                ? "The Codex stream ended incomplete."
                : $"The Codex stream ended incomplete ({reason}).";
        }

        private static void ApplyCompleted(JObject evt, CodexParsedResponse result)
        {
            var response = evt["response"] as JObject;
            if (response == null) return;

            result.FinishReason = response.Value<string>("status") ?? result.FinishReason;

            if (response["usage"] is JObject usage)
            {
                result.InputTokens = usage.Value<int?>("input_tokens");
                result.OutputTokens = usage.Value<int?>("output_tokens");
            }
        }

        private static string? ExtractError(JObject evt)
        {
            if (evt["error"] is JObject err)
            {
                return err.Value<string>("message") ?? err.ToString();
            }
            if (evt["response"] is JObject resp && resp["error"] is JObject respErr)
            {
                return respErr.Value<string>("message") ?? respErr.ToString();
            }
            return evt.Value<string>("message");
        }
    }

    /// <summary>Reconstructed result of a Codex Responses SSE stream.</summary>
    public sealed class CodexParsedResponse
    {
        public string Text { get; set; } = string.Empty;
        public string? FinishReason { get; set; }
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public string? ErrorDetail { get; set; }
    }
}

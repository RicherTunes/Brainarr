using System;
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

            // SSE lines are separated by \n (\r\n tolerated). Data payloads are single-line JSON
            // per event in this stream, so we can process line-by-line without joining multi-line
            // data fields.
            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var json = line.Substring("data:".Length).Trim();
                if (json.Length == 0 || json == "[DONE]")
                {
                    continue;
                }

                JObject evt;
                try
                {
                    evt = JObject.Parse(json);
                }
                catch
                {
                    continue; // skip a malformed event, keep parsing the rest
                }

                var type = evt.Value<string>("type");
                switch (type)
                {
                    case "response.output_text.delta":
                        deltas.Append(evt.Value<string>("delta") ?? string.Empty);
                        break;

                    case "response.output_text.done":
                        doneText = evt.Value<string>("text") ?? doneText;
                        break;

                    case "response.completed":
                        ApplyCompleted(evt, result);
                        break;

                    case "response.failed":
                    case "error":
                        result.ErrorDetail ??= ExtractError(evt);
                        break;
                }
            }

            result.Text = deltas.Length > 0 ? deltas.ToString() : (doneText ?? string.Empty);
            return result;
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

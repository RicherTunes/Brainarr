using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Capabilities;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.StructuredOutputs;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared
{
    /// <summary>
    /// Builds OpenAI-compatible chat request bodies based on provider capabilities.
    /// Returns a preferred body followed by compatible fallbacks.
    /// </summary>
    public static class ChatRequestFactory
    {
        public static IEnumerable<object> BuildBodies(
            NzbDrone.Core.ImportLists.Brainarr.AIProvider provider,
            string model,
            string systemContent,
            string userContent,
            double temperature,
            int maxTokens,
            bool preferStructured = true)
        {
            var caps = ProviderCapabilities.Get(provider);
            var list = new List<object>();

            // Preferred structured JSON via JSON Schema when supported and requested
            if (preferStructured && caps.UsesOpenAIChatCompletions &&
                (caps.ResponseFormats & ResponseFormatSupport.JsonSchema) != 0)
            {
                var schemaFormat = StructuredOutputSchemas.GetRecommendationResponseFormat();
                list.Add(new
                {
                    model = model,
                    messages = new[]
                    {
                        new { role = "system", content = systemContent },
                        new { role = "user", content = userContent }
                    },
                    temperature = temperature,
                    max_tokens = maxTokens,
                    response_format = schemaFormat,
                    stream = false
                });
            }

            // Text response_format variant (widely supported on OpenAI-compatible servers)
            if (caps.UsesOpenAIChatCompletions && (caps.ResponseFormats & ResponseFormatSupport.Text) != 0)
            {
                list.Add(new
                {
                    model = model,
                    messages = new[]
                    {
                        new { role = "system", content = systemContent },
                        new { role = "user", content = userContent }
                    },
                    temperature = temperature,
                    max_tokens = maxTokens,
                    response_format = new { type = "text" },
                    stream = false
                });
            }

            // Bare request without response_format for maximum compatibility
            if (caps.UsesOpenAIChatCompletions)
            {
                list.Add(new
                {
                    model = model,
                    messages = new[]
                    {
                        new { role = "system", content = systemContent },
                        new { role = "user", content = userContent }
                    },
                    temperature = temperature,
                    max_tokens = maxTokens,
                    stream = false
                });
            }

            return list;
        }
    }
}

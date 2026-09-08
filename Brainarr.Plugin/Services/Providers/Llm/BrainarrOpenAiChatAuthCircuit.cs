using System;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Providers.OpenAi;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>Adapts Brainarr's credential-scoped circuit to Common's provider contract.</summary>
    internal sealed class BrainarrOpenAiChatAuthCircuit : IOpenAiChatAuthCircuit
    {
        private readonly LlmAuthCircuit _circuit;

        public BrainarrOpenAiChatAuthCircuit(LlmAuthCircuit circuit)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
        }

        public bool IsOpen(string providerId, string credential, out string? reason)
            => _circuit.IsOpen(providerId, credential, out reason);

        public void RecordAuthFailure(
            string providerId,
            string credential,
            LlmProviderException error)
            => _circuit.RecordAuthFailure(providerId, credential, error);

        public void RecordSuccess(string providerId, string credential)
            => _circuit.RecordSuccess(providerId, credential);
    }
}

using Xunit;

namespace Brainarr.Tests.Services.Tokenization
{
    /// <summary>
    /// Serializes tests that assert on <c>ModelTokenizerRegistry</c>'s PROCESS-WIDE static
    /// <c>_fallbackWarn</c> gate. The gate's cache keys (e.g. <c>empty-model-key:&lt;default&gt;</c>,
    /// <c>default-fallback:*</c>) are shared across every <c>ModelTokenizerRegistry</c> instance in the
    /// process, and many parallel test classes (EnhancedLibraryAnalysisTests, the *PromptBuilderTests,
    /// ...) construct a real registry and trigger fallbacks while building prompts. A concurrent fire of
    /// the <c>&lt;default&gt;</c> key between this class's ctor reset
    /// (<c>ResetFallbackWarnStateForTests</c>) and its warn-count assertion consumed one warn, making
    /// <c>Logs_Warning_When_Falling_Back_To_Default_Tokenizer</c> flake (Expected 2, Actual 1).
    /// <para>DisableParallelization runs this collection serially with respect to all others — the same
    /// mechanism LimiterRegistryBounded / OrchestratorIntegration use for process-wide static state — so
    /// no parallel test can race the gate while these tests reset and assert on it.</para>
    /// </summary>
    [CollectionDefinition("TokenizerFallbackGate", DisableParallelization = true)]
    public sealed class TokenizerFallbackGateCollection
    {
    }
}

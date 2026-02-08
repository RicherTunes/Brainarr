using Xunit;

namespace Brainarr.Tests.Infrastructure
{
    /// <summary>
    /// Serialized collection for tests that flake under full-suite parallel execution
    /// due to thread-pool saturation or static shared state (HostGateRegistry,
    /// NLog LogManager, SecureProviderBase regex).
    /// Follow-up: remove serialization by eliminating shared statics / using fake clocks.
    /// See: https://github.com/RicherTunes/Brainarr/issues/TBD
    /// </summary>
    [CollectionDefinition("ThreadSensitive", DisableParallelization = true)]
    public class ThreadSensitiveTestCollection { }
}

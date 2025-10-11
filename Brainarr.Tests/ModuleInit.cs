using System.Runtime.CompilerServices;
using Brainarr.TestKit.Providers.Runtime;

namespace Brainarr.Tests
{
    public static class ModuleInit
    {
        [ModuleInitializer]
        public static void Init() => TestAssemblyResolver.Initialize(null);
    }
}

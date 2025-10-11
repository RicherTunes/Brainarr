using System.Runtime.CompilerServices;
using Brainarr.TestKit.Providers.Runtime;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init() => TestAssemblyResolver.Initialize(null);
}

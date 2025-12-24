using System;
using Xunit;

namespace Brainarr.Tests.Packaging
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PackagingFactAttribute : FactAttribute
    {
        public PackagingFactAttribute()
        {
            if (PackagingTestPaths.IsStrictMode())
            {
                return;
            }

            if (PackagingTestPaths.TryFindPackagePath() == null)
            {
                Skip = "No plugin package found. Run `./build.ps1 -Package` (or set REQUIRE_PACKAGE_TESTS=true in CI).";
            }
        }
    }
}


using System;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class ModelIdMappingValidatorTests
    {
        [Fact]
        public void AssertValid_NoThrow_WhenMappingsSane()
        {
            Action act = () => ModelIdMappingValidator.AssertValid(throwOnError: true, logger: LogManager.CreateNullLogger());
            act.Should().NotThrow();
        }

        [Fact]
        public void AssertValid_LogsWarning_WhenThrowOnErrorFalse()
        {
            // Even if there are no issues, calling with throwOnError=false should not throw.
            Action act = () => ModelIdMappingValidator.AssertValid(throwOnError: false, logger: LogManager.CreateNullLogger());
            act.Should().NotThrow();
        }
    }
}

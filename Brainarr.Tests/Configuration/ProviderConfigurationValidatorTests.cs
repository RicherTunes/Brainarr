using System;
using System.Collections.Generic;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class ProviderConfigurationValidatorTests
    {
        [Fact]
        public void OllamaConfiguration_MissingUrl_FailsValidation()
        {
            var config = new OllamaProviderConfiguration
            {
                Url = string.Empty,
                Model = "llama2"
            };

            var result = config.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("URL"));
        }

        [Fact]
        public void OllamaConfiguration_ValidValues_Passes()
        {
            var config = new OllamaProviderConfiguration
            {
                Url = "http://localhost:11434",
                Model = "llama2",
                Temperature = 0.5,
                TopP = 0.8,
                MaxTokens = 2048
            };

            var result = config.Validate();

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void LMStudioConfiguration_InvalidTemperature_Fails()
        {
            var config = new LMStudioProviderConfiguration
            {
                Url = "http://localhost:1234",
                Model = "mistral",
                Temperature = 1.5
            };

            var result = config.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Temperature"));
        }
    }
}

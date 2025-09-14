using FluentAssertions;
using Brainarr.Plugin.Configuration.Providers;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class LegacyProviderSettingsValidatorTests
    {
        [Fact]
        public void OpenAISettingsValidator_accepts_valid_and_rejects_invalid_values()
        {
            var validator = new OpenAISettingsValidator();
            var good = new OpenAISettings { ApiKey = "x", Model = "gpt-4o-mini", Temperature = 0.7, MaxTokens = 2000 };
            var bad = new OpenAISettings { ApiKey = "", Model = "", Temperature = -1, MaxTokens = 50 };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }

        [Fact]
        public void AnthropicSettingsValidator_accepts_valid_and_rejects_invalid_values()
        {
            var validator = new AnthropicSettingsValidator();
            var good = new AnthropicSettings { ApiKey = "x", Model = "claude", Temperature = 0.5, MaxTokens = 1000 };
            var bad = new AnthropicSettings { ApiKey = "", Model = "", Temperature = 2.0, MaxTokens = 50 };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }

        [Fact]
        public void PerplexitySettingsValidator_accepts_valid_and_rejects_invalid_values()
        {
            var validator = new PerplexitySettingsValidator();
            var good = new PerplexitySettings { ApiKey = "p", Model = PerplexityModel.Sonar_Large, Temperature = 0.7, MaxTokens = 2000 };
            var bad = new PerplexitySettings { ApiKey = "", Model = PerplexityModel.Sonar_Large, Temperature = 2.5, MaxTokens = 50 };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }

        [Fact]
        public void GroqSettingsValidator_accepts_valid_and_rejects_invalid_values()
        {
            var validator = new GroqSettingsValidator();
            var good = new GroqSettings { ApiKey = "g", Model = GroqModel.Llama33_70B, Temperature = 0.7, MaxTokens = 2000 };
            var bad = new GroqSettings { ApiKey = "", Model = GroqModel.Llama33_70B, Temperature = -0.1, MaxTokens = 50 };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }

        [Fact]
        public void GeminiSettingsValidator_accepts_valid_and_rejects_invalid_values()
        {
            var validator = new GeminiSettingsValidator();
            var good = new GeminiSettings { ApiKey = "k", Model = GeminiModel.Gemini_15_Flash, Temperature = 0.7, MaxTokens = 2000 };
            var bad = new GeminiSettings { ApiKey = "", Model = GeminiModel.Gemini_15_Flash, Temperature = 3.0, MaxTokens = 50 };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }

        [Fact]
        public void DeepSeekSettingsValidator_accepts_valid_and_rejects_invalid_values()
        {
            var validator = new DeepSeekSettingsValidator();
            var good = new DeepSeekSettings { ApiKey = "d", Model = DeepSeekModel.DeepSeek_Chat, Temperature = 0.7, MaxTokens = 2000 };
            var bad = new DeepSeekSettings { ApiKey = "", Model = DeepSeekModel.DeepSeek_Chat, Temperature = 2.5, MaxTokens = 50 };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }

        [Fact]
        public void OllamaSettingsValidator_accepts_valid_and_rejects_invalid_values()
        {
            var validator = new OllamaSettingsValidator();
            var good = new OllamaSettings { Endpoint = "http://localhost:11434", ModelName = "llama3", Temperature = 0.7, MaxTokens = 2000 };
            var bad = new OllamaSettings { Endpoint = "javascript:alert(1)", ModelName = "", Temperature = -1, MaxTokens = 10 };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }

        [Fact]
        public void LMStudioSettingsValidator_accepts_valid_and_rejects_invalid_values()
        {
            var validator = new LMStudioSettingsValidator();
            var good = new LMStudioSettings { Endpoint = "http://localhost:1234", ModelName = "mistral", Temperature = 0.7, MaxTokens = 2000 };
            var bad = new LMStudioSettings { Endpoint = "file:///etc/passwd", ModelName = "", Temperature = -1, MaxTokens = 10 };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }
    }
}

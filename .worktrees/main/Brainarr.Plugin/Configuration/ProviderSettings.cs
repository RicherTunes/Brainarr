using FluentValidation.Results;

namespace Brainarr.Plugin.Configuration
{
    public abstract class ProviderSettings
    {
        public abstract ValidationResult Validate();
    }
}

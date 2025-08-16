using FluentValidation;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers
{
    /// <summary>
    /// Base interface for all provider-specific settings.
    /// </summary>
    public interface IProviderSettings
    {
        /// <summary>
        /// Validates the provider settings.
        /// </summary>
        bool IsValid();

        /// <summary>
        /// Gets validation errors if any.
        /// </summary>
        FluentValidation.Results.ValidationResult Validate();
    }

    /// <summary>
    /// Base class for provider settings with common validation logic.
    /// </summary>
    public abstract class BaseProviderSettings<T> : IProviderSettings where T : class
    {
        protected abstract AbstractValidator<T> GetValidator();

        public virtual bool IsValid()
        {
            return Validate().IsValid;
        }

        public virtual FluentValidation.Results.ValidationResult Validate()
        {
            var validator = GetValidator();
            return validator.Validate((T)(object)this);
        }
    }
}
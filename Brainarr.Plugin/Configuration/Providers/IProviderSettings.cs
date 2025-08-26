using FluentValidation;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers
{
    /// <summary>
    /// Base interface for all provider-specific settings.
    /// Provides common operations needed by BrainarrSettings to eliminate switch statements.
    /// </summary>
    public interface IProviderSettings
    {
        /// <summary>
        /// Gets the API key for cloud providers, or null for local providers.
        /// </summary>
        string? GetApiKey();
        
        /// <summary>
        /// Gets the selected model name.
        /// </summary>
        string? GetModel();
        
        /// <summary>
        /// Gets the base URL for the provider (for local providers).
        /// </summary>
        string? GetBaseUrl();
        
        /// <summary>
        /// Gets the provider type identifier.
        /// </summary>
        AIProvider ProviderType { get; }
        
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
        
        public abstract string? GetApiKey();
        public abstract string? GetModel();
        public abstract string? GetBaseUrl();
        public abstract AIProvider ProviderType { get; }
        
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
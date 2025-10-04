using System;
using System.Collections.Generic;
using FluentValidationResult = FluentValidation.Results.ValidationResult;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.ConfigurationValidation
{
    public sealed class ConfigurationValidationSummary
    {
        public ConfigurationValidationSummary(FluentValidationResult validationResult)
        {
            ValidationResult = validationResult ?? new FluentValidationResult();
            Warnings = new List<string>();
            Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public FluentValidationResult ValidationResult { get; }

        public bool IsValid => ValidationResult?.IsValid ?? true;

        public IList<string> Warnings { get; }

        public IDictionary<string, object> Metadata { get; }

        public void AddWarning(string warning)
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                Warnings.Add(warning);
            }
        }
    }
}

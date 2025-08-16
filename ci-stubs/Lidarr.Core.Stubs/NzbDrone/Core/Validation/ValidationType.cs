using FluentValidation.Results;
using System.Collections.Generic;

namespace NzbDrone.Core.Validation
{
    public enum ValidationType
    {
        Warning = 0,
        Error = 1
    }

    public class NzbDroneValidationResult : ValidationResult
    {
        public bool HasWarnings => Warnings.Count > 0;
        public List<NzbDroneValidationFailure> Warnings { get; }

        public NzbDroneValidationResult()
        {
            Warnings = new List<NzbDroneValidationFailure>();
        }

        public NzbDroneValidationResult(IList<ValidationFailure> failures) : base(failures)
        {
            Warnings = new List<NzbDroneValidationFailure>();
        }
    }

    public class NzbDroneValidationFailure : ValidationFailure
    {
        public ValidationType Type { get; set; }
        public bool IsWarning => Type == ValidationType.Warning;

        public NzbDroneValidationFailure() : base()
        {
        }

        public NzbDroneValidationFailure(string propertyName, string errorMessage) : base(propertyName, errorMessage)
        {
        }

        public NzbDroneValidationFailure(string propertyName, string errorMessage, object attemptedValue) : base(propertyName, errorMessage, attemptedValue)
        {
        }
    }

    public static class RuleBuilderExtensions
    {
        public static void SetValidator<T, TProperty>(this FluentValidation.IRuleBuilder<T, TProperty> ruleBuilder, FluentValidation.IValidator<TProperty> validator)
        {
            // Stub implementation
        }
    }
}
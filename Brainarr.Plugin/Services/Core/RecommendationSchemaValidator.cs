using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IRecommendationSchemaValidator
    {
        SanitizationReport Validate(List<Recommendation> recommendations);
    }

    // Fast, deterministic shape check on the normalized Recommendation objects
    public class RecommendationSchemaValidator : IRecommendationSchemaValidator
    {
        private readonly Logger _logger;

        public RecommendationSchemaValidator(Logger logger)
        {
            _logger = logger;
        }

        public SanitizationReport Validate(List<Recommendation> recommendations)
        {
            var report = new SanitizationReport { TotalItems = recommendations?.Count ?? 0 };
            if (recommendations == null) return report;

            foreach (var r in recommendations)
            {
                if (r == null)
                {
                    report.DroppedItems++;
                    report.Warnings.Add("Null recommendation dropped");
                    continue;
                }

                // Artist required
                if (string.IsNullOrWhiteSpace(r.Artist))
                {
                    report.DroppedItems++;
                    report.Warnings.Add("Missing artist dropped");
                    continue;
                }

                // Clamp confidence if sanitizer missed anything
                if (r.Confidence < 0.0 || r.Confidence > 1.0)
                {
                    report.ClampedConfidences++;
                }

                // Count trims (non-mutating; sanitizer should have trimmed already)
                if (!string.IsNullOrEmpty(r.Artist) && r.Artist != r.Artist.Trim()) report.TrimmedFields++;
                if (!string.IsNullOrEmpty(r.Album) && r.Album != r.Album.Trim()) report.TrimmedFields++;
                if (!string.IsNullOrEmpty(r.Genre) && r.Genre != r.Genre.Trim()) report.TrimmedFields++;
                if (!string.IsNullOrEmpty(r.Reason) && r.Reason != r.Reason.Trim()) report.TrimmedFields++;
            }

            try
            {
                _logger.Info($"[Schema] items={report.TotalItems} dropped={report.DroppedItems} clamped={report.ClampedConfidences} trimmed={report.TrimmedFields}");
            }
            catch { }

            return report;
        }
    }
}


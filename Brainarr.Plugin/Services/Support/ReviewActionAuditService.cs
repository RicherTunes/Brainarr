using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    internal sealed class ReviewActionAuditService
    {
        private readonly Logger _logger;
        private readonly string _auditPath;
        private readonly object _lock = new object();

        public ReviewActionAuditService(Logger logger, string dataPath = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _auditPath = Path.Combine(
                dataPath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Brainarr",
                "review_action_audit.jsonl");
        }

        public void Write(ReviewActionAuditEvent auditEvent)
        {
            if (auditEvent == null)
            {
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(_auditPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                lock (_lock)
                {
                    var json = JsonSerializer.Serialize(auditEvent);
                    File.AppendAllText(_auditPath, json + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to persist review action audit event");
            }
        }

        public string GetAuditPath()
        {
            return _auditPath;
        }

        public static string SanitizeActor(string actor)
        {
            if (string.IsNullOrWhiteSpace(actor))
            {
                return "system";
            }

            var trimmed = actor.Trim();
            if (trimmed.Length > 64)
            {
                trimmed = trimmed.Substring(0, 64);
            }

            return trimmed;
        }
    }

    internal sealed record ReviewActionAuditEvent(
        string Id,
        string Action,
        string Actor,
        bool DryRun,
        string Mode,
        int PendingCount,
        int CandidateCount,
        int ApprovedCount,
        int ReleasedCount,
        int Cap,
        bool Capped,
        IReadOnlyList<string> ReasonCodes,
        DateTime OccurredAtUtc);
}

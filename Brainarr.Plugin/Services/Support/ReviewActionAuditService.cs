using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    internal sealed class ReviewActionAuditService
    {
        private readonly Logger _logger;
        private readonly string _auditPath;
        private readonly object _lock = new object();
        private readonly int _maxEntries;
        private readonly int _retentionDays;
        private readonly long _maxBytes;
        private readonly Func<DateTime> _utcNow;

        public ReviewActionAuditService(Logger logger, string dataPath = null)
            : this(logger, dataPath, 2000, 30, 256 * 1024, null)
        {
        }

        public ReviewActionAuditService(
            Logger logger,
            string dataPath,
            int maxEntries,
            int retentionDays,
            long maxBytes,
            Func<DateTime> utcNow)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _auditPath = Path.Combine(
                dataPath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Brainarr",
                "review_action_audit.jsonl");
            _maxEntries = Math.Max(1, maxEntries);
            _retentionDays = Math.Max(1, retentionDays);
            _maxBytes = Math.Max(1024, maxBytes);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
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
                    EnforceRetentionLocked();
                    var json = JsonSerializer.Serialize(auditEvent);
                    File.AppendAllText(_auditPath, json + Environment.NewLine);
                    EnforceRetentionLocked();
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

        public bool TryGetByIdempotencyKey(string action, string idempotencyKey, out ReviewActionAuditEvent auditEvent)
        {
            auditEvent = null;
            var normalizedKey = SanitizeIdempotencyKey(idempotencyKey);
            if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(normalizedKey) || !File.Exists(_auditPath))
            {
                return false;
            }

            try
            {
                lock (_lock)
                {
                    foreach (var line in File.ReadLines(_auditPath).Reverse())
                    {
                        if (!TryParseAuditLine(line, out var parsed))
                        {
                            continue;
                        }

                        if (string.Equals(parsed.Action, action, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(parsed.IdempotencyKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
                        {
                            auditEvent = parsed;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed reading review action audit history for idempotency lookup");
            }

            return false;
        }

        public IReadOnlyList<ReviewActionAuditEvent> GetRecent(string action, int limit)
        {
            var boundedLimit = Math.Clamp(limit, 1, 200);
            if (!File.Exists(_auditPath))
            {
                return Array.Empty<ReviewActionAuditEvent>();
            }

            try
            {
                lock (_lock)
                {
                    var events = new List<ReviewActionAuditEvent>();
                    foreach (var line in File.ReadLines(_auditPath).Reverse())
                    {
                        if (!TryParseAuditLine(line, out var parsed))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(action)
                            && !string.Equals(parsed.Action, action, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        events.Add(parsed);
                        if (events.Count >= boundedLimit)
                        {
                            break;
                        }
                    }

                    return events;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed reading review action audit history");
                return Array.Empty<ReviewActionAuditEvent>();
            }
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

        public static string SanitizeIdempotencyKey(string idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return null;
            }

            var normalized = idempotencyKey.Trim();
            if (normalized.Length > 80)
            {
                normalized = normalized.Substring(0, 80);
            }

            return normalized;
        }

        private void EnforceRetentionLocked()
        {
            if (!File.Exists(_auditPath))
            {
                return;
            }

            var cutoff = _utcNow().AddDays(-_retentionDays);
            var events = new List<ReviewActionAuditEvent>();
            foreach (var line in File.ReadLines(_auditPath))
            {
                if (TryParseAuditLine(line, out var parsed))
                {
                    events.Add(parsed);
                }
            }

            events = events
                .Where(e => e.OccurredAtUtc >= cutoff)
                .ToList();

            if (events.Count > _maxEntries)
            {
                events = events.Skip(events.Count - _maxEntries).ToList();
            }

            while (events.Count > 1 && EstimateSize(events) > _maxBytes)
            {
                events.RemoveAt(0);
            }

            Rewrite(events);
        }

        private void Rewrite(IReadOnlyList<ReviewActionAuditEvent> events)
        {
            var lines = events.Select(auditEvent => JsonSerializer.Serialize(auditEvent)).ToList();
            if (lines.Count == 0)
            {
                File.WriteAllText(_auditPath, string.Empty);
                return;
            }

            File.WriteAllText(_auditPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        private static long EstimateSize(IReadOnlyList<ReviewActionAuditEvent> events)
        {
            long size = 0;
            foreach (var auditEvent in events)
            {
                var line = JsonSerializer.Serialize(auditEvent);
                size += System.Text.Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            }

            return size;
        }

        private static bool TryParseAuditLine(string line, out ReviewActionAuditEvent parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            try
            {
                parsed = JsonSerializer.Deserialize<ReviewActionAuditEvent>(line);
                return parsed != null;
            }
            catch
            {
                return false;
            }
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
        DateTime OccurredAtUtc,
        string IdempotencyKey = null);
}

using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Handles review queue UI actions extracted from BrainarrOrchestrator.
    /// Manages review accept/reject/never workflows, approval application,
    /// and styles option retrieval.
    /// </summary>
    internal sealed class ReviewQueueActionHandler
    {
        private readonly ReviewQueueService _reviewQueue;
        private readonly RecommendationHistory _history;
        private readonly IStyleCatalogService _styleCatalog;
        private readonly RecommendationTriageAdvisor _triageAdvisor;
        private readonly Action _persistSettingsCallback;
        private readonly ReviewActionAuditService _auditService;
        private readonly Logger _logger;

        public ReviewQueueActionHandler(
            ReviewQueueService reviewQueue,
            RecommendationHistory history,
            IStyleCatalogService styleCatalog,
            RecommendationTriageAdvisor triageAdvisor,
            Action persistSettingsCallback,
            Logger logger)
            : this(reviewQueue, history, styleCatalog, triageAdvisor, persistSettingsCallback, logger, null)
        {
        }

        public ReviewQueueActionHandler(
            ReviewQueueService reviewQueue,
            RecommendationHistory history,
            IStyleCatalogService styleCatalog,
            RecommendationTriageAdvisor triageAdvisor,
            Action persistSettingsCallback,
            Logger logger,
            ReviewActionAuditService auditService)
        {
            _reviewQueue = reviewQueue ?? throw new ArgumentNullException(nameof(reviewQueue));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _styleCatalog = styleCatalog ?? throw new ArgumentNullException(nameof(styleCatalog));
            _triageAdvisor = triageAdvisor ?? throw new ArgumentNullException(nameof(triageAdvisor));
            _persistSettingsCallback = persistSettingsCallback;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _auditService = auditService ?? new ReviewActionAuditService(_logger);
        }

        public object GetStylesOptions(IDictionary<string, string> query)
        {
            try
            {
                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var q = Get(query, "query") ?? string.Empty;
                var items = _styleCatalog.Search(q, 50)
                    .Select(s => new { value = s.Slug, name = s.Name })
                    .ToList();
                return new { options = items };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "styles/getoptions failed");
                return new { options = Array.Empty<object>() };
            }
        }

        public object HandleReviewUpdate(IDictionary<string, string> query, ReviewQueueService.ReviewStatus status)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
            {
                return new { ok = false, error = "artist and album are required" };
            }

            var ok = _reviewQueue.SetStatus(artist, album, status, notes);
            if (ok && status == ReviewQueueService.ReviewStatus.Rejected)
            {
                // Record rejection in history (soft negative feedback)
                _history.MarkAsRejected(artist, album, reason: notes);
            }
            return new { ok };
        }

        public object HandleReviewNever(IDictionary<string, string> query)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            if (string.IsNullOrWhiteSpace(artist))
            {
                return new { ok = false, error = "artist is required" };
            }
            var ok = _reviewQueue.SetStatus(artist, album, ReviewQueueService.ReviewStatus.Never, notes);
            // Strong negative constraint
            _history.MarkAsDisliked(artist, album, RecommendationHistory.DislikeLevel.NeverAgain);
            return new { ok };
        }

        public object GetReviewOptions()
        {
            var items = _reviewQueue.GetPending();
            var options = items
                .Select(i => new
                {
                    value = $"{i.Artist}|{i.Album}",
                    name = string.IsNullOrWhiteSpace(i.Album)
                        ? i.Artist
                        : $"{i.Artist} — {i.Album}{(i.Year.HasValue ? " (" + i.Year.Value + ")" : string.Empty)}"
                })
                .OrderBy(o => o.name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
            return new { options };
        }

        public object GetReviewSummaryOptions()
        {
            var (pending, accepted, rejected, never) = _reviewQueue.GetCounts();
            var options = new List<object>
            {
                new { value = $"pending:{pending}", name = $"Pending: {pending}" },
                new { value = $"accepted:{accepted}", name = $"Accepted (released): {accepted}" },
                new { value = $"rejected:{rejected}", name = $"Rejected: {rejected}" },
                new { value = $"never:{never}", name = $"Never Again: {never}" }
            };
            return new { options };
        }

        public object GetReviewActionAudit(IDictionary<string, string> query)
        {
            var action = query != null && query.TryGetValue("action", out var rawAction) && !string.IsNullOrWhiteSpace(rawAction)
                ? rawAction.Trim()
                : "review/applytriage";
            var requestedLimit = TryParseNonNegativeInt(query, "limit");
            var limit = requestedLimit.HasValue && requestedLimit.Value > 0 ? Math.Min(requestedLimit.Value, 200) : 20;

            var events = _auditService.GetRecent(action, limit);
            var items = events
                .Select(entry => new
                {
                    id = entry.Id,
                    action = entry.Action,
                    actor = entry.Actor,
                    mode = entry.Mode,
                    dryRun = entry.DryRun,
                    pending = entry.PendingCount,
                    candidates = entry.CandidateCount,
                    approved = entry.ApprovedCount,
                    released = entry.ReleasedCount,
                    cap = entry.Cap,
                    capped = entry.Capped,
                    reasonCodes = entry.ReasonCodes ?? Array.Empty<string>(),
                    occurredAtUtc = entry.OccurredAtUtc.ToString("O"),
                    idempotencyKey = RedactIdempotencyKey(entry.IdempotencyKey)
                })
                .ToList();

            var summary = new
            {
                approved = events.Sum(entry => entry.ApprovedCount),
                released = events.Sum(entry => entry.ReleasedCount),
                cappedRuns = events.Count(entry => entry.Capped)
            };

            return new
            {
                ok = true,
                action,
                limit,
                count = items.Count,
                items,
                summary
            };
        }

        public object GetReviewTriageOptions(BrainarrSettings settings)
        {
            settings ??= new BrainarrSettings();
            var pending = _reviewQueue.GetPending();
            var items = pending
                .Select(i =>
                {
                    var triage = _triageAdvisor.Analyze(i, settings);
                    var baseName = string.IsNullOrWhiteSpace(i.Album) ? i.Artist : $"{i.Artist} — {i.Album}";
                    var primaryReasonCode = triage.ReasonCodes.FirstOrDefault() ?? "CONSISTENT_SIGNALS";
                    var displayName = $"{baseName} · {triage.SuggestedAction.ToUpperInvariant()} · {primaryReasonCode}";
                    return new
                    {
                        value = $"{i.Artist}|{i.Album}",
                        name = displayName,
                        baseName,
                        action = triage.SuggestedAction,
                        confidenceBand = triage.ConfidenceBand,
                        riskScore = triage.RiskScore,
                        rationale = string.Join("; ", triage.Reasons),
                        reasonCodes = triage.ReasonCodes,
                        reasons = triage.DetailedReasons.Select(reason => new
                        {
                            code = reason.Code,
                            message = reason.Message,
                            weight = reason.Weight
                        }),
                        explanation = BuildExplanation(triage)
                    };
                })
                .OrderByDescending(x => x.riskScore)
                .ThenBy(x => x.name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            var summary = new
            {
                accept = items.Count(x => x.action == "accept"),
                review = items.Count(x => x.action == "review"),
                reject = items.Count(x => x.action == "reject")
            };

            return new { options = items, summary };
        }

        private static string BuildExplanation(ReviewTriageResult triage)
        {
            var topReason = triage.DetailedReasons?
                .OrderByDescending(reason => Math.Abs(reason.Weight))
                .ThenByDescending(reason => reason.Weight)
                .FirstOrDefault();

            if (topReason == null)
            {
                return $"Suggested action: {triage.SuggestedAction}.";
            }

            return $"Suggested action: {triage.SuggestedAction}. Primary signal: {topReason.Message}.";
        }

        public object ApplyApprovalsNow(BrainarrSettings settings, IDictionary<string, string> query)
        {
            settings ??= new BrainarrSettings();

            if (IsEnabled(query, "dryRun"))
            {
                return SimulateSelectionApply(settings, query);
            }

            var keys = ResolveSelectionKeys(settings, query);
            int applied = 0;
            foreach (var key in keys)
            {
                var parts = (key ?? "").Split('|');
                if (parts.Length >= 2)
                {
                    if (_reviewQueue.SetStatus(parts[0], parts[1], ReviewQueueService.ReviewStatus.Accepted))
                    {
                        applied++;
                    }
                }
            }

            var accepted = _reviewQueue.DequeueAccepted();
            // Clear approval selections in memory (persist by saving settings from UI)
            settings.ReviewApproveKeys = Array.Empty<string>();

            // Attempt to persist the cleared selections, if a persistence callback was provided
            TryPersistSettings();

            return new
            {
                ok = true,
                approved = applied,
                released = accepted.Count,
                cleared = true,
                note = "Selections cleared in memory; click Save to persist clearing in settings"
            };
        }

        public object SimulateReviewApply(BrainarrSettings settings, IDictionary<string, string> query)
        {
            settings ??= new BrainarrSettings();
            if (!settings.EnableAutoReviewTriageActions)
            {
                return AutoTriageDisabledResult("review/applysimulation");
            }

            var mode = query != null && query.TryGetValue("mode", out var m) && !string.IsNullOrWhiteSpace(m)
                ? m.Trim()
                : "triage";

            if (string.Equals(mode, "selection", StringComparison.OrdinalIgnoreCase))
            {
                return SimulateSelectionApply(settings, query);
            }

            var pending = _reviewQueue.GetPending();
            var options = pending
                .Select(item =>
                {
                    var triage = _triageAdvisor.Analyze(item, settings);
                    return new
                    {
                        value = $"{item.Artist}|{item.Album}",
                        name = string.IsNullOrWhiteSpace(item.Album) ? item.Artist : $"{item.Artist} — {item.Album}",
                        action = triage.SuggestedAction,
                        riskScore = triage.RiskScore,
                        reasonCodes = triage.ReasonCodes,
                        explanation = BuildExplanation(triage)
                    };
                })
                .OrderByDescending(x => x.riskScore)
                .ThenBy(x => x.name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            var summary = new
            {
                accept = options.Count(x => x.action == "accept"),
                review = options.Count(x => x.action == "review"),
                reject = options.Count(x => x.action == "reject")
            };

            var audit = new
            {
                kind = "review/apply-simulation",
                dryRun = true,
                mode = "triage",
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                pendingCount = pending.Count,
                evaluatedCount = options.Count,
                policy = new
                {
                    minConfidence = settings.MinConfidence,
                    requireMbids = settings.RequireMbids,
                    recommendationMode = settings.RecommendationMode.ToString()
                }
            };

            return new
            {
                ok = true,
                dryRun = true,
                mode = "triage",
                wouldApprove = summary.accept,
                wouldRelease = summary.accept,
                options,
                summary,
                audit
            };
        }

        public object ApplyTriageSuggestions(BrainarrSettings settings, IDictionary<string, string> query)
        {
            settings ??= new BrainarrSettings();
            if (!settings.EnableAutoReviewTriageActions)
            {
                return AutoTriageDisabledResult("review/applytriage");
            }

            var idempotencyKey = ReviewActionAuditService.SanitizeIdempotencyKey(
                query != null && query.TryGetValue("idempotencyKey", out var rawKey) ? rawKey : null);
            if (!string.IsNullOrWhiteSpace(idempotencyKey)
                && _auditService.TryGetByIdempotencyKey("review/applytriage", idempotencyKey, out var previous))
            {
                return new
                {
                    ok = true,
                    replay = true,
                    dryRun = false,
                    mode = "triage",
                    actor = previous.Actor,
                    idempotencyKey,
                    cap = previous.Cap,
                    capped = previous.Capped,
                    candidates = previous.CandidateCount,
                    approved = previous.ApprovedCount,
                    released = previous.ReleasedCount,
                    reasonCodes = previous.ReasonCodes,
                    audit = new
                    {
                        id = previous.Id,
                        path = _auditService.GetAuditPath()
                    }
                };
            }

            var pending = _reviewQueue.GetPending();
            var triageCandidates = pending
                .Select(item => new
                {
                    Item = item,
                    Triage = _triageAdvisor.Analyze(item, settings)
                })
                .Where(entry => entry.Triage.SuggestedAction == "accept")
                .OrderBy(entry => entry.Triage.RiskScore)
                .ThenBy(entry => $"{entry.Item.Artist} — {entry.Item.Album}", StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            var configuredCap = Math.Max(1, settings.MaxAutoReviewActionsPerRun);
            var requestedMax = TryParseNonNegativeInt(query, "max");
            var effectiveCap = requestedMax.HasValue ? Math.Min(requestedMax.Value, configuredCap) : configuredCap;

            var selected = triageCandidates.Take(effectiveCap).ToList();
            var applied = 0;
            foreach (var candidate in selected)
            {
                if (_reviewQueue.SetStatus(candidate.Item.Artist, candidate.Item.Album, ReviewQueueService.ReviewStatus.Accepted))
                {
                    applied++;
                }
            }

            var releasedItems = _reviewQueue.DequeueAccepted();
            var actor = ReviewActionAuditService.SanitizeActor(query != null && query.TryGetValue("actor", out var rawActor) ? rawActor : null);
            var reasonCodes = selected
                .SelectMany(candidate => candidate.Triage.ReasonCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var selectedItems = selected
                .Select(candidate => new ReviewActionAuditItem(
                    candidate.Item.Artist,
                    candidate.Item.Album,
                    candidate.Item.Genre,
                    candidate.Item.Confidence,
                    candidate.Item.Reason,
                    candidate.Item.Year,
                    candidate.Item.ArtistMusicBrainzId,
                    candidate.Item.AlbumMusicBrainzId))
                .ToList();

            var capped = triageCandidates.Count > selected.Count;
            var auditId = Guid.NewGuid().ToString("N");
            _auditService.Write(new ReviewActionAuditEvent(
                Id: auditId,
                Action: "review/applytriage",
                Actor: actor,
                DryRun: false,
                Mode: "triage",
                PendingCount: pending.Count,
                CandidateCount: triageCandidates.Count,
                ApprovedCount: applied,
                ReleasedCount: releasedItems.Count,
                Cap: effectiveCap,
                Capped: capped,
                ReasonCodes: reasonCodes,
                OccurredAtUtc: DateTime.UtcNow,
                IdempotencyKey: idempotencyKey,
                Items: selectedItems));

            var preview = selected.Select(candidate => new
            {
                value = $"{candidate.Item.Artist}|{candidate.Item.Album}",
                name = $"{candidate.Item.Artist} — {candidate.Item.Album}",
                riskScore = candidate.Triage.RiskScore,
                reasonCodes = candidate.Triage.ReasonCodes
            }).ToList();

            return new
            {
                ok = true,
                replay = false,
                dryRun = false,
                mode = "triage",
                actor,
                idempotencyKey,
                cap = effectiveCap,
                capped,
                candidates = triageCandidates.Count,
                approved = applied,
                released = releasedItems.Count,
                options = preview,
                audit = new
                {
                    id = auditId,
                    path = _auditService.GetAuditPath()
                }
            };
        }

        public object RollbackTriageApplication(IDictionary<string, string> query)
        {
            var requestedId = query != null && query.TryGetValue("id", out var rawId) && !string.IsNullOrWhiteSpace(rawId)
                ? rawId.Trim()
                : null;

            ReviewActionAuditEvent target;
            if (!string.IsNullOrWhiteSpace(requestedId))
            {
                if (!_auditService.TryGetById(requestedId, out target)
                    || !string.Equals(target.Action, "review/applytriage", StringComparison.OrdinalIgnoreCase))
                {
                    return new { ok = false, code = "AUDIT_NOT_FOUND", error = "No matching review/applytriage audit entry found.", id = requestedId };
                }
            }
            else
            {
                target = _auditService.GetRecent("review/applytriage", 1).FirstOrDefault();
                if (target == null)
                {
                    return new { ok = false, code = "AUDIT_NOT_FOUND", error = "No review/applytriage audit entry found." };
                }
            }

            if (target.Items == null || target.Items.Count == 0)
            {
                return new { ok = false, code = "AUDIT_NOT_ROLLBACKABLE", error = "Audit entry does not contain rollback payload.", id = target.Id };
            }

            var idempotencyKey = ReviewActionAuditService.SanitizeIdempotencyKey(
                query != null && query.TryGetValue("idempotencyKey", out var rawKey) ? rawKey : null);
            if (!string.IsNullOrWhiteSpace(idempotencyKey)
                && _auditService.TryGetByIdempotencyKey("review/rollbacktriage", idempotencyKey, out var previous))
            {
                return new
                {
                    ok = true,
                    replay = true,
                    actor = previous.Actor,
                    idempotencyKey,
                    rollbackOf = previous.RollbackOfId,
                    restored = previous.ReleasedCount,
                    skipped = Math.Max(0, previous.CandidateCount - previous.ReleasedCount),
                    audit = new
                    {
                        id = previous.Id,
                        path = _auditService.GetAuditPath()
                    }
                };
            }

            var beforePendingCount = _reviewQueue.GetPending().Count;
            var toRestore = target.Items.Select(ToRecommendation).ToList();
            _reviewQueue.Enqueue(toRestore, reason: $"Rollback from audit {target.Id}");
            var afterPendingCount = _reviewQueue.GetPending().Count;
            var restored = Math.Max(0, afterPendingCount - beforePendingCount);
            var skipped = Math.Max(0, toRestore.Count - restored);

            var actor = ReviewActionAuditService.SanitizeActor(query != null && query.TryGetValue("actor", out var rawActor) ? rawActor : null);
            var rollbackAuditId = Guid.NewGuid().ToString("N");
            _auditService.Write(new ReviewActionAuditEvent(
                Id: rollbackAuditId,
                Action: "review/rollbacktriage",
                Actor: actor,
                DryRun: false,
                Mode: "rollback",
                PendingCount: beforePendingCount,
                CandidateCount: toRestore.Count,
                ApprovedCount: 0,
                ReleasedCount: restored,
                Cap: toRestore.Count,
                Capped: false,
                ReasonCodes: target.ReasonCodes ?? Array.Empty<string>(),
                OccurredAtUtc: DateTime.UtcNow,
                IdempotencyKey: idempotencyKey,
                RollbackOfId: target.Id,
                Items: target.Items));

            return new
            {
                ok = true,
                replay = false,
                actor,
                idempotencyKey,
                rollbackOf = target.Id,
                restored,
                skipped,
                audit = new
                {
                    id = rollbackAuditId,
                    path = _auditService.GetAuditPath()
                }
            };
        }

        public object ClearApprovalSelections(BrainarrSettings settings)
        {
            settings.ReviewApproveKeys = Array.Empty<string>();
            TryPersistSettings();
            return new { ok = true, cleared = true, note = "Selections cleared and persisted (if supported)." };
        }

        public object RejectOrNeverSelected(BrainarrSettings settings, IDictionary<string, string> query, ReviewQueueService.ReviewStatus status)
        {
            settings ??= new BrainarrSettings();
            var keys = ResolveSelectionKeys(settings, query);

            int applied = 0;
            foreach (var key in keys)
            {
                var parts = (key ?? "").Split('|');
                if (parts.Length >= 2)
                {
                    if (_reviewQueue.SetStatus(parts[0], parts[1], status))
                    {
                        applied++;
                        if (status == ReviewQueueService.ReviewStatus.Rejected)
                        {
                            _history.MarkAsRejected(parts[0], parts[1], reason: "Batch reject");
                        }
                        else if (status == ReviewQueueService.ReviewStatus.Never)
                        {
                            _history.MarkAsDisliked(parts[0], parts[1], RecommendationHistory.DislikeLevel.NeverAgain);
                        }
                    }
                }
            }

            // For symmetry, clear selection in memory after action
            settings.ReviewApproveKeys = Array.Empty<string>();
            TryPersistSettings();
            return new { ok = true, updated = applied, cleared = true, note = "Selections cleared and persisted (if supported)." };
        }

        internal void TryPersistSettings()
        {
            try
            {
                _persistSettingsCallback?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to persist Brainarr settings automatically");
            }
        }

        private object SimulateSelectionApply(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var pending = _reviewQueue.GetPending();
            var pendingByKey = pending.ToDictionary(
                item => $"{item.Artist}|{item.Album}",
                item => item,
                StringComparer.OrdinalIgnoreCase);

            var keys = ResolveSelectionKeys(settings, query);
            var selected = keys
                .Where(key => pendingByKey.ContainsKey(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(key =>
                {
                    var item = pendingByKey[key];
                    return new
                    {
                        value = key,
                        name = string.IsNullOrWhiteSpace(item.Album) ? item.Artist : $"{item.Artist} — {item.Album}",
                        action = "accept",
                        reasonCodes = new[] { "MANUAL_SELECTION" },
                        explanation = "Selected in review approvals and would be released."
                    };
                })
                .OrderBy(x => x.name, StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            var audit = new
            {
                kind = "review/apply-simulation",
                dryRun = true,
                mode = "selection",
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                pendingCount = pending.Count,
                selectedCount = keys.Count,
                matchedCount = selected.Count
            };

            return new
            {
                ok = true,
                dryRun = true,
                mode = "selection",
                wouldApprove = selected.Count,
                wouldRelease = selected.Count,
                options = selected,
                audit
            };
        }

        private static List<string> ResolveSelectionKeys(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var keysCsv = query != null && query.TryGetValue("keys", out var value) ? value : null;
            var keys = new List<string>();
            if (!string.IsNullOrWhiteSpace(keysCsv))
            {
                keys.AddRange(keysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (settings?.ReviewApproveKeys != null)
            {
                keys.AddRange(settings.ReviewApproveKeys);
            }

            return keys;
        }

        private static bool IsEnabled(IDictionary<string, string> query, string key)
        {
            if (query == null || !query.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static object AutoTriageDisabledResult(string action)
        {
            return new
            {
                ok = false,
                error = "Auto review triage actions are disabled. Enable the setting first.",
                code = "AUTO_TRIAGE_DISABLED",
                action
            };
        }

        private static int? TryParseNonNegativeInt(IDictionary<string, string> query, string key)
        {
            if (query == null || !query.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : null;
        }

        private static Recommendation ToRecommendation(ReviewActionAuditItem item)
        {
            return new Recommendation
            {
                Artist = item.Artist,
                Album = item.Album,
                Genre = item.Genre,
                Confidence = item.Confidence,
                Reason = item.Reason,
                Year = item.Year,
                ArtistMusicBrainzId = item.ArtistMusicBrainzId,
                AlbumMusicBrainzId = item.AlbumMusicBrainzId,
                MusicBrainzId = item.AlbumMusicBrainzId
            };
        }

        private static string RedactIdempotencyKey(string idempotencyKey)
        {
            var normalized = ReviewActionAuditService.SanitizeIdempotencyKey(idempotencyKey);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (normalized.Length <= 8)
            {
                return "********";
            }

            return $"{normalized.Substring(0, 4)}...{normalized.Substring(normalized.Length - 4)}";
        }
    }
}

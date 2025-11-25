using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using NLog.Targets;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Logging
{
    /// <summary>
    /// Secure structured logging with automatic sensitive data masking and performance tracking.
    /// </summary>
    public interface ISecureLogger
    {
        void LogInfo(string message, object context = null);
        void LogWarning(string message, object context = null);
        void LogError(Exception exception, string message, object context = null);
        void LogDebug(string message, object context = null);
        void LogPerformance(string operation, TimeSpan duration, object context = null);
        void LogSecurity(SecurityEventType eventType, string message, object context = null);
        IDisposable BeginScope(string scopeName, object context = null);
    }

    public class SecureStructuredLogger : ISecureLogger
    {
        private readonly Logger _logger;
        private readonly ISensitiveDataMasker _dataMasker;
        private readonly ILogEnricher _enricher;
        private readonly LogConfiguration _config;
        private readonly AsyncLocal<LogScope> _currentScope = new();

        // Performance thresholds
        private const int SlowOperationThresholdMs = 1000;
        private const int CriticalOperationThresholdMs = 5000;

        public SecureStructuredLogger(
            Logger logger,
            ISensitiveDataMasker dataMasker = null,
            ILogEnricher enricher = null,
            LogConfiguration config = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dataMasker = dataMasker ?? new SensitiveDataMasker();
            _enricher = enricher ?? new DefaultLogEnricher();
            _config = config ?? LogConfiguration.Default;
        }

        public void LogInfo(string message, object context = null)
        {
            LogInternal(LogLevel.Info, message, null, context);
        }

        public void LogWarning(string message, object context = null)
        {
            LogInternal(LogLevel.Warn, message, null, context);
        }

        public void LogError(Exception exception, string message, object context = null)
        {
            LogInternal(LogLevel.Error, message, exception, context);
        }

        public void LogDebug(string message, object context = null)
        {
            if (_config.EnableDebugLogging)
            {
                LogInternal(LogLevel.Debug, message, null, context);
            }
        }

        public void LogPerformance(string operation, TimeSpan duration, object context = null)
        {
            var level = LogLevel.Debug;
            var additionalContext = new Dictionary<string, object>();

            if (duration.TotalMilliseconds > CriticalOperationThresholdMs)
            {
                level = LogLevel.Error;
                additionalContext["performance_alert"] = "critical";
            }
            else if (duration.TotalMilliseconds > SlowOperationThresholdMs)
            {
                level = LogLevel.Warn;
                additionalContext["performance_alert"] = "slow";
            }

            var perfContext = new
            {
                operation,
                duration_ms = duration.TotalMilliseconds,
                duration_formatted = FormatDuration(duration),
                performance_data = additionalContext
            };

            var mergedContext = MergeContexts(context, perfContext);
            LogInternal(level, $"Performance: {operation} completed in {duration.TotalMilliseconds:F2}ms", null, mergedContext);
        }

        public void LogSecurity(SecurityEventType eventType, string message, object context = null)
        {
            var securityContext = new
            {
                security_event = eventType.ToString(),
                severity = GetSecuritySeverity(eventType),
                timestamp_utc = DateTime.UtcNow,
                alert_required = IsAlertRequired(eventType)
            };

            var mergedContext = MergeContexts(context, securityContext);

            var level = eventType switch
            {
                SecurityEventType.AuthenticationFailed => LogLevel.Warn,
                SecurityEventType.AuthorizationDenied => LogLevel.Warn,
                SecurityEventType.SuspiciousActivity => LogLevel.Error,
                SecurityEventType.DataExposure => LogLevel.Fatal,
                SecurityEventType.ApiKeyCompromised => LogLevel.Fatal,
                _ => LogLevel.Info
            };

            LogInternal(level, $"[SECURITY] {message}", null, mergedContext);

            // Trigger security alerts if needed
            if (IsAlertRequired(eventType))
            {
                TriggerSecurityAlert(eventType, message, mergedContext);
            }
        }

        public IDisposable BeginScope(string scopeName, object context = null)
        {
            var scope = new LogScope
            {
                Name = scopeName,
                Context = context,
                StartTime = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid().ToString("N"),
                Parent = _currentScope.Value
            };

            _currentScope.Value = scope;

            LogDebug($"Entering scope: {scopeName}", new { scope_id = scope.CorrelationId });

            return new ScopeDisposer(() =>
            {
                var duration = DateTime.UtcNow - scope.StartTime;
                LogDebug($"Exiting scope: {scopeName}", new
                {
                    scope_id = scope.CorrelationId,
                    duration_ms = duration.TotalMilliseconds
                });
                _currentScope.Value = scope.Parent;
            });
        }

        private void LogInternal(LogLevel level, string message, Exception exception, object context)
        {
            try
            {
                // Mask sensitive data in message
                message = _dataMasker.MaskSensitiveData(message);

                // Create structured log event
                var logEvent = CreateLogEvent(level, message, exception, context);

                // Enrich with additional data
                _enricher.Enrich(logEvent);

                // Add scope information
                if (_currentScope.Value != null)
                {
                    logEvent.Properties["scope"] = SerializeScope(_currentScope.Value);
                }

                // Log based on configuration
                if (_config.UseStructuredLogging)
                {
                    LogStructured(logEvent);
                }
                else
                {
                    LogTraditional(level, FormatLogMessage(logEvent), exception);
                }

                // Track metrics
                TrackLogMetrics(level);
            }
            catch (Exception ex)
            {
                // Fallback logging if structured logging fails
                _logger.Error($"Logging failed: {ex.Message}. Original message: {message}");
            }
        }

        private LogEvent CreateLogEvent(LogLevel level, string message, Exception exception, object context)
        {
            var logEvent = new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = level.ToString(),
                Message = message,
                Properties = new Dictionary<string, object>()
            };

            // Add context
            if (context != null)
            {
                var maskedContext = _dataMasker.MaskSensitiveDataInObject(context);
                logEvent.Properties["context"] = SerializeContext(maskedContext);
            }

            // Add exception details
            if (exception != null)
            {
                logEvent.Properties["exception"] = new
                {
                    type = exception.GetType().Name,
                    message = _dataMasker.MaskSensitiveData(exception.Message),
                    stacktrace = _config.IncludeStackTrace ?
                        _dataMasker.MaskSensitiveData(exception.StackTrace) : null,
                    inner = exception.InnerException != null ?
                        _dataMasker.MaskSensitiveData(exception.InnerException.Message) : null
                };
            }

            // Add caller information
            logEvent.Properties["caller"] = GetCallerInfo();

            return logEvent;
        }

        private void LogStructured(LogEvent logEvent)
        {
            var json = JsonSerializer.Serialize(logEvent, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            _logger.Log(ParseLogLevel(logEvent.Level), json);
        }

        private void LogTraditional(LogLevel level, string message, Exception exception)
        {
            if (exception != null)
            {
                _logger.Log(level, exception, message);
            }
            else
            {
                _logger.Log(level, message);
            }
        }

        private string FormatLogMessage(LogEvent logEvent)
        {
            var sb = new StringBuilder();
            sb.Append($"[{logEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
            sb.Append($"[{logEvent.Level}] ");
            sb.Append(logEvent.Message);

            if (logEvent.Properties.Any())
            {
                sb.Append(" | ");
                foreach (var prop in logEvent.Properties)
                {
                    if (prop.Value != null)
                    {
                        sb.Append($"{prop.Key}={SerializeValue(prop.Value)} ");
                    }
                }
            }

            return sb.ToString();
        }

        private string SerializeContext(object context)
        {
            try
            {
                return JsonSerializer.Serialize(context, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    MaxDepth = 5
                });
            }
            catch
            {
                return context?.ToString() ?? "null";
            }
        }

        private string SerializeValue(object value)
        {
            if (value == null) return "null";
            if (value is string s) return s;
            if (value.GetType().IsPrimitive) return value.ToString();

            try
            {
                return JsonSerializer.Serialize(value, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    MaxDepth = 2
                });
            }
            catch
            {
                return value.ToString();
            }
        }

        private string SerializeScope(LogScope scope)
        {
            var scopeInfo = new
            {
                name = scope.Name,
                correlation_id = scope.CorrelationId,
                duration_ms = (DateTime.UtcNow - scope.StartTime).TotalMilliseconds,
                depth = GetScopeDepth(scope)
            };

            return SerializeContext(scopeInfo);
        }

        private int GetScopeDepth(LogScope scope)
        {
            var depth = 0;
            var current = scope;
            while (current != null)
            {
                depth++;
                current = current.Parent;
            }
            return depth;
        }

        private object MergeContexts(object context1, object context2)
        {
            if (context1 == null) return context2;
            if (context2 == null) return context1;

            // Convert to dictionaries and merge
            var dict1 = ConvertToDict(context1);
            var dict2 = ConvertToDict(context2);

            foreach (var kvp in dict2)
            {
                dict1[kvp.Key] = kvp.Value;
            }

            return dict1;
        }

        private Dictionary<string, object> ConvertToDict(object obj)
        {
            if (obj is Dictionary<string, object> dict)
                return new Dictionary<string, object>(dict);

            var result = new Dictionary<string, object>();
            foreach (var prop in obj.GetType().GetProperties())
            {
                result[prop.Name] = prop.GetValue(obj);
            }
            return result;
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds < 1)
                return $"{duration.TotalMilliseconds:F2}ms";
            if (duration.TotalMinutes < 1)
                return $"{duration.TotalSeconds:F2}s";
            return $"{duration.TotalMinutes:F2}m";
        }

        private string GetSecuritySeverity(SecurityEventType eventType)
        {
            return eventType switch
            {
                SecurityEventType.DataExposure => "critical",
                SecurityEventType.ApiKeyCompromised => "critical",
                SecurityEventType.SuspiciousActivity => "high",
                SecurityEventType.AuthorizationDenied => "medium",
                SecurityEventType.AuthenticationFailed => "low",
                _ => "info"
            };
        }

        private bool IsAlertRequired(SecurityEventType eventType)
        {
            return eventType == SecurityEventType.DataExposure ||
                   eventType == SecurityEventType.ApiKeyCompromised ||
                   eventType == SecurityEventType.SuspiciousActivity;
        }

        private void TriggerSecurityAlert(SecurityEventType eventType, string message, object context)
        {
            // This would integrate with your alerting system
            _logger.Fatal($"SECURITY ALERT: {eventType} - {message}");
        }

        private object GetCallerInfo([CallerMemberName] string memberName = "",
                                    [CallerFilePath] string filePath = "",
                                    [CallerLineNumber] int lineNumber = 0)
        {
            return new
            {
                method = memberName,
                file = System.IO.Path.GetFileName(filePath),
                line = lineNumber
            };
        }

        private LogLevel ParseLogLevel(string level)
        {
            return level?.ToLower() switch
            {
                "debug" => LogLevel.Debug,
                "info" => LogLevel.Info,
                "warn" => LogLevel.Warn,
                "error" => LogLevel.Error,
                "fatal" => LogLevel.Fatal,
                _ => LogLevel.Info
            };
        }

        private void TrackLogMetrics(LogLevel level)
        {
            // Track log levels for monitoring
            MetricsCollector.IncrementCounter($"logs_{level.ToString().ToLower()}");
        }

        private class LogScope
        {
            public string Name { get; set; }
            public object Context { get; set; }
            public DateTime StartTime { get; set; }
            public string CorrelationId { get; set; }
            public LogScope Parent { get; set; }
        }

        private class ScopeDisposer : IDisposable
        {
            private readonly Action _onDispose;

            public ScopeDisposer(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                _onDispose?.Invoke();
            }
        }
    }

    /// <summary>
    /// Masks sensitive data in log messages and objects.
    /// </summary>
    public interface ISensitiveDataMasker
    {
        string MaskSensitiveData(string input);
        object MaskSensitiveDataInObject(object obj);
    }

    public class SensitiveDataMasker : ISensitiveDataMasker
    {
        private readonly List<SensitivePattern> _patterns = new()
        {
            // API Keys
            new SensitivePattern(@"(api[_-]?key|apikey|api_secret)[\s:='""]+([A-Za-z0-9\-_]{20,})", "API_KEY", 2),
            new SensitivePattern(@"sk-[A-Za-z0-9]{40,}", "OPENAI_KEY"),
            new SensitivePattern(@"sk-ant-[A-Za-z0-9]{90,}", "ANTHROPIC_KEY"),
            new SensitivePattern(@"gsk_[A-Za-z0-9]{50,}", "GROQ_KEY"),

            // Passwords
            new SensitivePattern(@"(password|passwd|pwd)[\s:='""]+([^\s'""]+)", "PASSWORD", 2),

            // Email addresses
            new SensitivePattern(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", "EMAIL"),

            // Credit cards
            new SensitivePattern(@"\b(?:\d{4}[-\s]?){3}\d{4}\b", "CREDIT_CARD"),

            // IP addresses
            new SensitivePattern(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", "IP_ADDRESS"),

            // JWT tokens
            new SensitivePattern(@"eyJ[A-Za-z0-9\-_]+\.eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+", "JWT_TOKEN"),

            // File paths with user info
            new SensitivePattern(@"\/(?:home|users)\/[^\/\s]+", "USER_PATH"),
            new SensitivePattern(@"C:\\Users\\[^\\]+", "WINDOWS_USER_PATH"),

            // URLs with credentials
            new SensitivePattern(@"(https?:\/\/)([^:]+):([^@]+)@", "URL_CREDENTIALS", 0, "$1[REDACTED]:[REDACTED]@")
        };

        public string MaskSensitiveData(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var masked = input;

            foreach (var pattern in _patterns)
            {
                masked = pattern.Apply(masked);
            }

            return masked;
        }

        public object MaskSensitiveDataInObject(object obj)
        {
            if (obj == null) return null;

            if (obj is string str)
                return MaskSensitiveData(str);

            if (obj is Dictionary<string, object> dict)
            {
                var maskedDict = new Dictionary<string, object>();
                foreach (var kvp in dict)
                {
                    var key = kvp.Key.ToLower();
                    if (IsSensitiveField(key))
                    {
                        maskedDict[kvp.Key] = "[REDACTED]";
                    }
                    else
                    {
                        maskedDict[kvp.Key] = MaskSensitiveDataInObject(kvp.Value);
                    }
                }
                return maskedDict;
            }

            // For other objects, mask string properties
            var type = obj.GetType();
            if (type.IsClass && type != typeof(string))
            {
                var clone = Activator.CreateInstance(type);
                foreach (var prop in type.GetProperties())
                {
                    if (prop.CanRead && prop.CanWrite)
                    {
                        var value = prop.GetValue(obj);
                        if (value is string strValue)
                        {
                            prop.SetValue(clone, IsSensitiveField(prop.Name) ?
                                "[REDACTED]" : MaskSensitiveData(strValue));
                        }
                        else
                        {
                            prop.SetValue(clone, value);
                        }
                    }
                }
                return clone;
            }

            return obj;
        }

        private bool IsSensitiveField(string fieldName)
        {
            var sensitiveFields = new[]
            {
                "password", "passwd", "pwd", "secret", "token",
                "apikey", "api_key", "api-key", "authorization",
                "auth", "credential", "private", "ssn", "tax"
            };

            var lower = fieldName.ToLower();
            return sensitiveFields.Any(f => lower.Contains(f));
        }

        private class SensitivePattern
        {
            private readonly Regex _regex;
            private readonly string _replacement;
            private readonly int _groupToMask;
            private readonly string _customReplacement;

            public SensitivePattern(string pattern, string label, int groupToMask = 0, string customReplacement = null)
            {
                _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                _replacement = $"[{label}_REDACTED]";
                _groupToMask = groupToMask;
                _customReplacement = customReplacement;
            }

            public string Apply(string input)
            {
                if (_customReplacement != null)
                {
                    return _regex.Replace(input, _customReplacement);
                }

                if (_groupToMask > 0)
                {
                    return _regex.Replace(input, match =>
                    {
                        var groups = match.Groups;
                        if (groups.Count > _groupToMask)
                        {
                            var start = groups[_groupToMask].Index - match.Index;
                            var length = groups[_groupToMask].Length;
                            var result = match.Value.Substring(0, start) + _replacement;
                            if (start + length < match.Value.Length)
                            {
                                result += match.Value.Substring(start + length);
                            }
                            return result;
                        }
                        return _replacement;
                    });
                }

                return _regex.Replace(input, _replacement);
            }
        }
    }

    public interface ILogEnricher
    {
        void Enrich(LogEvent logEvent);
    }

    public class DefaultLogEnricher : ILogEnricher
    {
        public void Enrich(LogEvent logEvent)
        {
            logEvent.Properties["machine"] = Environment.MachineName;
            logEvent.Properties["process_id"] = Process.GetCurrentProcess().Id;
            logEvent.Properties["thread_id"] = Thread.CurrentThread.ManagedThreadId;
            logEvent.Properties["app_version"] = GetAppVersion();

            // Add correlation ID if available
            if (CorrelationContext.Current != null)
            {
                logEvent.Properties["correlation_id"] = CorrelationContext.Current.CorrelationId;
            }
        }

        private string GetAppVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "1.0.0";
        }
    }

    public class LogEvent
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Properties { get; set; }
    }

    public class LogConfiguration
    {
        public bool UseStructuredLogging { get; set; } = true;
        public bool EnableDebugLogging { get; set; } = false;
        public bool IncludeStackTrace { get; set; } = true;
        public bool MaskSensitiveData { get; set; } = true;

        public static LogConfiguration Default => new();

        public static LogConfiguration Production => new()
        {
            UseStructuredLogging = true,
            EnableDebugLogging = false,
            IncludeStackTrace = false,
            MaskSensitiveData = true
        };

        public static LogConfiguration Development => new()
        {
            UseStructuredLogging = true,
            EnableDebugLogging = true,
            IncludeStackTrace = true,
            MaskSensitiveData = true
        };
    }

    public enum SecurityEventType
    {
        AuthenticationFailed,
        AuthorizationDenied,
        SuspiciousActivity,
        DataExposure,
        ApiKeyCompromised,
        AccessGranted,
        ConfigurationChanged
    }

    // Helper classes referenced but not defined elsewhere
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<CorrelationInfo> _current = new();

        public static CorrelationInfo Current => _current.Value;

        public static string StartNew()
        {
            var correlationId = Guid.NewGuid().ToString("N");
            _current.Value = new CorrelationInfo { CorrelationId = correlationId };
            return correlationId;
        }

        public class CorrelationInfo
        {
            public string CorrelationId { get; set; }
        }
    }

    public static class MetricsCollector
    {
        private static readonly ConcurrentDictionary<string, long> _counters = new();

        public static void IncrementCounter(string name)
        {
            _counters.AddOrUpdate(name, 1, (_, value) => value + 1);
        }

        public static void Record(object metric)
        {
            // Placeholder for metrics recording
        }
    }

    // Extension methods for easier logging
    public static class LoggerExtensions
    {
        public static void InfoWithCorrelation(this Logger logger, string message)
        {
            var correlationId = CorrelationContext.Current?.CorrelationId ?? "no-correlation";
            logger.Info($"[{correlationId}] {message}");
        }

        public static void DebugWithCorrelation(this Logger logger, string message)
        {
            var correlationId = CorrelationContext.Current?.CorrelationId ?? "no-correlation";
            logger.Debug($"[{correlationId}] {message}");
        }

        public static void WarnWithCorrelation(this Logger logger, string message)
        {
            var correlationId = CorrelationContext.Current?.CorrelationId ?? "no-correlation";
            logger.Warn($"[{correlationId}] {message}");
        }

        public static void ErrorWithCorrelation(this Logger logger, string message)
        {
            var correlationId = CorrelationContext.Current?.CorrelationId ?? "no-correlation";
            logger.Error($"[{correlationId}] {message}");
        }

        public static void ErrorWithCorrelation(this Logger logger, Exception ex, string message)
        {
            var correlationId = CorrelationContext.Current?.CorrelationId ?? "no-correlation";
            logger.Error(ex, $"[{correlationId}] {message}");
        }
    }
}

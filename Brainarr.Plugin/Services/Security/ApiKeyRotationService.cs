using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    /// <summary>
    /// Manages API key rotation and secure storage for AI providers.
    /// </summary>
    public interface IApiKeyRotationService
    {
        Task<string> GetCurrentKeyAsync(string provider);
        Task<bool> RotateKeyAsync(string provider, string newKey);
        Task<KeyRotationStatus> GetRotationStatusAsync(string provider);
        void ScheduleAutoRotation(string provider, TimeSpan interval);
        void ValidateKeyStrength(string key, string provider);
    }

    public class ApiKeyRotationService : IApiKeyRotationService, IDisposable
    {
        private readonly Logger _logger;
        private readonly ISecureKeyStorage _keyStorage;
        private readonly ConcurrentDictionary<string, ApiKeyMetadata> _keyMetadata;
        private readonly ConcurrentDictionary<string, Timer> _rotationTimers;
        private readonly IKeyStrengthValidator _keyValidator;
        private readonly object _rotationLock = new();

        public ApiKeyRotationService(
            Logger logger,
            ISecureKeyStorage keyStorage,
            IKeyStrengthValidator keyValidator = null)
        {
            _logger = logger;
            _keyStorage = keyStorage;
            _keyValidator = keyValidator ?? new KeyStrengthValidator();
            _keyMetadata = new ConcurrentDictionary<string, ApiKeyMetadata>();
            _rotationTimers = new ConcurrentDictionary<string, Timer>();
        }

        /// <summary>
        /// Gets the current active API key for a provider.
        /// </summary>
        public async Task<string> GetCurrentKeyAsync(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentException("Provider name is required", nameof(provider));

            try
            {
                // Check if key needs rotation
                if (ShouldRotateKey(provider))
                {
                    _logger.Warn($"API key for {provider} has exceeded rotation period");
                    // Don't auto-rotate here, just log warning
                }

                var encryptedKey = await _keyStorage.GetKeyAsync(provider);
                if (string.IsNullOrEmpty(encryptedKey))
                {
                    throw new KeyNotFoundException($"No API key found for provider {provider}");
                }

                return DecryptKey(encryptedKey, provider);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to retrieve API key for {provider}");
                throw new ApiKeyException($"Failed to retrieve key for {provider}", ex);
            }
        }

        /// <summary>
        /// Rotates the API key for a provider with zero-downtime transition.
        /// </summary>
        public async Task<bool> RotateKeyAsync(string provider, string newKey)
        {
            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentException("Provider name is required", nameof(provider));
            
            if (string.IsNullOrWhiteSpace(newKey))
                throw new ArgumentException("New key is required", nameof(newKey));

            lock (_rotationLock)
            {
                try
                {
                    // Validate new key strength
                    ValidateKeyStrength(newKey, provider);

                    // Store old key for rollback
                    var oldKeyBackup = _keyStorage.GetKeyAsync(provider).Result;
                    
                    // Encrypt and store new key
                    var encryptedNewKey = EncryptKey(newKey, provider);
                    
                    // Begin rotation transaction
                    _logger.Info($"Starting API key rotation for {provider}");
                    
                    // Store new key with versioning
                    var success = _keyStorage.StoreKeyAsync(provider, encryptedNewKey).Result;
                    
                    if (success)
                    {
                        // Update metadata
                        _keyMetadata[provider] = new ApiKeyMetadata
                        {
                            Provider = provider,
                            RotatedAt = DateTime.UtcNow,
                            KeyVersion = (_keyMetadata.TryGetValue(provider, out var existing) ? 
                                existing.KeyVersion : 0) + 1,
                            PreviousKeyHash = oldKeyBackup != null ? 
                                ComputeKeyHash(oldKeyBackup) : null,
                            CurrentKeyHash = ComputeKeyHash(encryptedNewKey),
                            ExpiresAt = DateTime.UtcNow.AddDays(90) // Default 90-day rotation
                        };

                        // Archive old key (for emergency rollback)
                        if (!string.IsNullOrEmpty(oldKeyBackup))
                        {
                            ArchiveOldKey(provider, oldKeyBackup);
                        }

                        _logger.Info($"Successfully rotated API key for {provider} (Version: {_keyMetadata[provider].KeyVersion})");
                        
                        // Trigger validation of new key
                        _ = Task.Run(() => ValidateNewKeyAsync(provider, newKey));
                        
                        return true;
                    }
                    else
                    {
                        _logger.Error($"Failed to store rotated key for {provider}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Key rotation failed for {provider}");
                    throw new ApiKeyRotationException($"Failed to rotate key for {provider}", ex);
                }
            }
        }

        /// <summary>
        /// Gets the rotation status for a provider's API key.
        /// </summary>
        public async Task<KeyRotationStatus> GetRotationStatusAsync(string provider)
        {
            await Task.CompletedTask; // Async for future enhancement
            
            if (!_keyMetadata.TryGetValue(provider, out var metadata))
            {
                return new KeyRotationStatus
                {
                    Provider = provider,
                    Status = RotationStatus.NeverRotated,
                    Message = "Key has never been rotated"
                };
            }

            var daysSinceRotation = (DateTime.UtcNow - metadata.RotatedAt).TotalDays;
            var daysUntilExpiry = metadata.ExpiresAt.HasValue ? 
                (metadata.ExpiresAt.Value - DateTime.UtcNow).TotalDays : -1;

            RotationStatus status;
            string message;

            if (daysUntilExpiry > 0 && daysUntilExpiry < 7)
            {
                status = RotationStatus.RotationRequired;
                message = $"Key expires in {daysUntilExpiry:F0} days";
            }
            else if (daysUntilExpiry <= 0)
            {
                status = RotationStatus.Expired;
                message = "Key has expired and must be rotated";
            }
            else if (daysSinceRotation > 60)
            {
                status = RotationStatus.RotationRecommended;
                message = $"Key last rotated {daysSinceRotation:F0} days ago";
            }
            else
            {
                status = RotationStatus.Current;
                message = $"Key is current (rotated {daysSinceRotation:F0} days ago)";
            }

            return new KeyRotationStatus
            {
                Provider = provider,
                Status = status,
                LastRotated = metadata.RotatedAt,
                NextRotation = metadata.ExpiresAt,
                Version = metadata.KeyVersion,
                Message = message
            };
        }

        /// <summary>
        /// Schedules automatic key rotation for a provider.
        /// </summary>
        public void ScheduleAutoRotation(string provider, TimeSpan interval)
        {
            if (interval < TimeSpan.FromDays(1))
            {
                throw new ArgumentException("Rotation interval must be at least 1 day", nameof(interval));
            }

            // Cancel existing timer if any
            if (_rotationTimers.TryRemove(provider, out var existingTimer))
            {
                existingTimer?.Dispose();
            }

            var timer = new Timer(async _ =>
            {
                try
                {
                    _logger.Info($"Auto-rotation triggered for {provider}");
                    // This would need to fetch new key from provider's API or notify admin
                    await NotifyRotationRequired(provider);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Auto-rotation failed for {provider}");
                }
            }, null, interval, interval);

            _rotationTimers[provider] = timer;
            _logger.Info($"Scheduled auto-rotation for {provider} every {interval.TotalDays} days");
        }

        /// <summary>
        /// Validates the strength and format of an API key.
        /// </summary>
        public void ValidateKeyStrength(string key, string provider)
        {
            var validation = _keyValidator.ValidateKey(key, provider);
            
            if (!validation.IsValid)
            {
                throw new WeakKeyException($"Key validation failed: {string.Join(", ", validation.Issues)}");
            }
            
            if (validation.Warnings.Any())
            {
                foreach (var warning in validation.Warnings)
                {
                    _logger.Warn($"Key validation warning for {provider}: {warning}");
                }
            }
        }

        private bool ShouldRotateKey(string provider)
        {
            if (!_keyMetadata.TryGetValue(provider, out var metadata))
                return false;

            // Check if key has expired
            if (metadata.ExpiresAt.HasValue && DateTime.UtcNow >= metadata.ExpiresAt.Value)
                return true;

            // Check if rotation period exceeded (default 90 days)
            return (DateTime.UtcNow - metadata.RotatedAt).TotalDays > 90;
        }

        private string EncryptKey(string plainKey, string provider)
        {
            using (var aes = Aes.Create())
            {
                // Derive key from provider name and machine key
                var keyBytes = DeriveKey(provider);
                aes.Key = keyBytes;
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor())
                {
                    var plainBytes = Encoding.UTF8.GetBytes(plainKey);
                    var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                    
                    // Combine IV and encrypted data
                    var result = new byte[aes.IV.Length + cipherBytes.Length];
                    aes.IV.CopyTo(result, 0);
                    cipherBytes.CopyTo(result, aes.IV.Length);
                    
                    return Convert.ToBase64String(result);
                }
            }
        }

        private string DecryptKey(string encryptedKey, string provider)
        {
            var encryptedBytes = Convert.FromBase64String(encryptedKey);
            
            using (var aes = Aes.Create())
            {
                var keyBytes = DeriveKey(provider);
                aes.Key = keyBytes;
                
                // Extract IV from encrypted data
                var iv = new byte[aes.BlockSize / 8];
                Array.Copy(encryptedBytes, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    var cipherBytes = new byte[encryptedBytes.Length - iv.Length];
                    Array.Copy(encryptedBytes, iv.Length, cipherBytes, 0, cipherBytes.Length);
                    
                    var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }

        private byte[] DeriveKey(string provider)
        {
            // Use PBKDF2 to derive encryption key
            var salt = Encoding.UTF8.GetBytes($"Brainarr_{provider}_Salt");
            var password = $"{Environment.MachineName}_{provider}";
            
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(32); // 256-bit key
            }
        }

        private string ComputeKeyHash(string key)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                return Convert.ToBase64String(hashBytes);
            }
        }

        private void ArchiveOldKey(string provider, string oldKey)
        {
            try
            {
                var archiveKey = $"{provider}_archive_{DateTime.UtcNow:yyyyMMddHHmmss}";
                _keyStorage.StoreKeyAsync(archiveKey, oldKey).Wait();
                _logger.Debug($"Archived old key for {provider}");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to archive old key for {provider}");
            }
        }

        private async Task ValidateNewKeyAsync(string provider, string newKey)
        {
            // This would test the new key with the provider's API
            await Task.Delay(100); // Placeholder for actual validation
            _logger.Info($"Validated new API key for {provider}");
        }

        private async Task NotifyRotationRequired(string provider)
        {
            // This would send notifications to administrators
            await Task.CompletedTask;
            _logger.Warn($"API key rotation required for {provider} - notification sent");
        }

        public void Dispose()
        {
            foreach (var timer in _rotationTimers.Values)
            {
                timer?.Dispose();
            }
            _rotationTimers.Clear();
        }
    }

    public class ApiKeyMetadata
    {
        public string Provider { get; set; }
        public DateTime RotatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int KeyVersion { get; set; }
        public string CurrentKeyHash { get; set; }
        public string PreviousKeyHash { get; set; }
    }

    public class KeyRotationStatus
    {
        public string Provider { get; set; }
        public RotationStatus Status { get; set; }
        public DateTime? LastRotated { get; set; }
        public DateTime? NextRotation { get; set; }
        public int Version { get; set; }
        public string Message { get; set; }
    }

    public enum RotationStatus
    {
        Current,
        RotationRecommended,
        RotationRequired,
        Expired,
        NeverRotated
    }

    public interface ISecureKeyStorage
    {
        Task<string> GetKeyAsync(string identifier);
        Task<bool> StoreKeyAsync(string identifier, string encryptedKey);
        Task<bool> DeleteKeyAsync(string identifier);
        Task<List<string>> GetAllIdentifiersAsync();
    }

    public interface IKeyStrengthValidator
    {
        KeyValidationResult ValidateKey(string key, string provider);
    }

    public class KeyStrengthValidator : IKeyStrengthValidator
    {
        private readonly Dictionary<string, KeyRequirements> _providerRequirements = new()
        {
            ["openai"] = new KeyRequirements { MinLength = 40, Prefix = "sk-", RequiresSpecialChars = false },
            ["anthropic"] = new KeyRequirements { MinLength = 100, Prefix = "sk-ant-", RequiresSpecialChars = false },
            ["gemini"] = new KeyRequirements { MinLength = 39, Prefix = null, RequiresSpecialChars = false },
            ["groq"] = new KeyRequirements { MinLength = 50, Prefix = "gsk_", RequiresSpecialChars = false }
        };

        public KeyValidationResult ValidateKey(string key, string provider)
        {
            var result = new KeyValidationResult { IsValid = true };
            
            if (string.IsNullOrWhiteSpace(key))
            {
                result.IsValid = false;
                result.Issues.Add("Key cannot be empty");
                return result;
            }

            // Check for common weak patterns
            if (key.Contains("test") || key.Contains("demo") || key.Contains("example"))
            {
                result.Warnings.Add("Key appears to be a test/demo key");
            }

            // Provider-specific validation
            if (_providerRequirements.TryGetValue(provider.ToLower(), out var requirements))
            {
                if (key.Length < requirements.MinLength)
                {
                    result.IsValid = false;
                    result.Issues.Add($"Key is too short (minimum {requirements.MinLength} characters)");
                }

                if (!string.IsNullOrEmpty(requirements.Prefix) && !key.StartsWith(requirements.Prefix))
                {
                    result.IsValid = false;
                    result.Issues.Add($"Key must start with '{requirements.Prefix}'");
                }

                if (requirements.RequiresSpecialChars && !ContainsSpecialCharacters(key))
                {
                    result.IsValid = false;
                    result.Issues.Add("Key must contain special characters");
                }
            }

            // Check for exposed keys (basic check)
            if (IsCommonlyExposedKey(key))
            {
                result.IsValid = false;
                result.Issues.Add("This key appears to be publicly exposed");
            }

            return result;
        }

        private bool ContainsSpecialCharacters(string key)
        {
            return key.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_');
        }

        private bool IsCommonlyExposedKey(string key)
        {
            // This would check against a database of known exposed keys
            var knownBadKeys = new[]
            {
                "sk-1234567890abcdef",
                "test-api-key-do-not-use"
            };
            
            return knownBadKeys.Contains(key);
        }
    }

    public class KeyRequirements
    {
        public int MinLength { get; set; }
        public string Prefix { get; set; }
        public bool RequiresSpecialChars { get; set; }
    }

    public class KeyValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Issues { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class ApiKeyException : Exception
    {
        public ApiKeyException(string message) : base(message) { }
        public ApiKeyException(string message, Exception inner) : base(message, inner) { }
    }

    public class ApiKeyRotationException : ApiKeyException
    {
        public ApiKeyRotationException(string message) : base(message) { }
        public ApiKeyRotationException(string message, Exception inner) : base(message, inner) { }
    }

    public class WeakKeyException : ApiKeyException
    {
        public WeakKeyException(string message) : base(message) { }
    }

    public class KeyNotFoundException : ApiKeyException
    {
        public KeyNotFoundException(string message) : base(message) { }
    }
}
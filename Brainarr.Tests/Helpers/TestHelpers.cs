using System.Diagnostics;

namespace Brainarr.Tests.Helpers
{
    // Helper class for void async operations with RateLimiter
    public class VoidResult
    {
        public static readonly VoidResult Instance = new VoidResult();
        private VoidResult() { }
    }

    /// <summary>
    /// Provides common test utilities for cleanup and diagnostics.
    /// </summary>
    public static class TestCleanup
    {
        /// <summary>
        /// Safely deletes a temporary directory, logging any failures to diagnostics
        /// instead of silently swallowing exceptions.
        /// </summary>
        /// <param name="path">The directory path to delete.</param>
        /// <param name="recursive">Whether to delete subdirectories and files.</param>
        public static void TryDeleteDirectory(string? path, bool recursive = true)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive);
                }
            }
            catch (IOException ex)
            {
                // File in use - common in tests, log for debugging
                Debug.WriteLine($"[TestCleanup] Could not delete '{path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                // Permission issue - log for debugging
                Debug.WriteLine($"[TestCleanup] Access denied deleting '{path}': {ex.Message}");
            }
            catch (Exception ex)
            {
                // Unexpected error - log with full details
                Debug.WriteLine($"[TestCleanup] Unexpected error deleting '{path}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely deletes a file, logging any failures to diagnostics.
        /// </summary>
        /// <param name="path">The file path to delete.</param>
        public static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[TestCleanup] Could not delete file '{path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"[TestCleanup] Access denied deleting file '{path}': {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestCleanup] Unexpected error deleting file '{path}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}

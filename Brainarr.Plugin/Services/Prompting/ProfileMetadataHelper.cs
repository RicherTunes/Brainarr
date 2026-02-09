using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

/// <summary>
/// Eliminates duplicated metadata access patterns from <see cref="LibraryPromptRenderer"/>.
/// Every method is null-safe and returns a sensible default when the key is missing.
/// </summary>
internal static class ProfileMetadataHelper
{
    /// <summary>
    /// Returns the string representation of a metadata value, or <paramref name="defaultValue"/> if absent.
    /// </summary>
    public static string GetString(LibraryProfile profile, string key, string defaultValue = "")
    {
        if (profile.Metadata?.ContainsKey(key) == true)
        {
            return profile.Metadata[key].ToString() ?? defaultValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// Returns a strongly-typed metadata value if the key exists and the stored object is assignable to
    /// <typeparamref name="T"/>; otherwise returns <c>default</c>.
    /// </summary>
    public static T? GetTyped<T>(LibraryProfile profile, string key) where T : class
    {
        if (profile.Metadata?.ContainsKey(key) == true && profile.Metadata[key] is T typed)
        {
            return typed;
        }

        return default;
    }

    /// <summary>
    /// Returns a numeric metadata value, or <c>null</c> if the key is missing or not convertible.
    /// </summary>
    public static double? GetDouble(LibraryProfile profile, string key)
    {
        if (profile.Metadata?.ContainsKey(key) == true)
        {
            try
            {
                return Convert.ToDouble(profile.Metadata[key]);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the top <paramref name="count"/> entries from a <c>Dictionary&lt;string, TValue&gt;</c>
    /// metadata value, ordered descending by value, and formats each entry with <paramref name="formatter"/>.
    /// Returns an empty string when the key is missing or the dictionary is empty.
    /// </summary>
    public static string GetTopN<TValue>(
        LibraryProfile profile,
        string key,
        int count,
        Func<KeyValuePair<string, TValue>, string> formatter,
        Func<KeyValuePair<string, TValue>, bool>? filter = null) where TValue : IComparable<TValue>
    {
        if (profile.Metadata?.ContainsKey(key) != true ||
            profile.Metadata[key] is not Dictionary<string, TValue> dict ||
            !dict.Any())
        {
            return string.Empty;
        }

        IEnumerable<KeyValuePair<string, TValue>> source = dict;
        if (filter != null)
        {
            source = source.Where(filter);
        }

        return string.Join(", ", source
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .Select(formatter));
    }
}

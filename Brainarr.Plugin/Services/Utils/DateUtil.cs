using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Utils;

internal static class DateUtil
{
    public static DateTime NormalizeMin(DateTime? value)
    {
        return value ?? DateTime.MinValue;
    }
}

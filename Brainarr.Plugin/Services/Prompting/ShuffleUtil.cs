using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

internal static class ShuffleUtil
{
    public static void ShuffleInPlace<T>(IList<T> list, Random rng)
    {
        if (list == null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        if (rng == null)
        {
            throw new ArgumentNullException(nameof(rng));
        }

        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

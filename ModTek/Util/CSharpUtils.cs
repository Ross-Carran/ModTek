﻿using System.Collections.Generic;
using System.Linq;

namespace ModTek.Util;

internal static class CSharpUtils
{
    internal static IEnumerator<T> Enumerate<T>(params IEnumerator<T>[] enumerators)
    {
        foreach (var enumerator in enumerators)
        {
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }
    }

    internal static string AsTextList(this IEnumerable<string> items, string prefix = "\n - ")
    {
        return items?.Aggregate("", (current, item) => current + prefix + item);
    }
}
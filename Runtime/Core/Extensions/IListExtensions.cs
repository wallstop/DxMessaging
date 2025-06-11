namespace DxMessaging.Core.Extensions
{
    using System;
    using System.Collections.Generic;

    internal static class IListExtensions
    {
        public static void Shift<T>(this IList<T> list, int amount)
        {
            if (list is not { Count: > 1 })
            {
                return;
            }

            int count = list.Count;
            amount %= count;
            amount += count;
            amount %= count;
            if (amount == 0)
            {
                return;
            }

            Reverse(list, 0, count - 1);
            Reverse(list, 0, amount - 1);
            Reverse(list, amount, count - 1);
        }

        public static void Reverse<T>(this IList<T> list, int start, int end)
        {
            if (start < 0 || list.Count <= start)
            {
                throw new ArgumentException(nameof(start));
            }
            if (end < 0 || list.Count <= end)
            {
                throw new ArgumentException(nameof(end));
            }

            while (start < end)
            {
                (list[start], list[end]) = (list[end], list[start]);
                start++;
                end--;
            }
        }
    }
}

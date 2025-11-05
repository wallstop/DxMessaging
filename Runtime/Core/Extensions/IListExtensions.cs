namespace DxMessaging.Core.Extensions
{
    using System;
    using System.Collections.Generic;

    internal static class IListExtensions
    {
        /// <summary>
        /// Rotates the list contents in-place by the specified offset.
        /// </summary>
        /// <typeparam name="T">Element type held by the list.</typeparam>
        /// <param name="list">List to rotate. Must have at least two elements to perform work.</param>
        /// <param name="amount">
        /// Number of positions to rotate. Positive values move elements toward the end (wrapping),
        /// negative values move them toward the start.
        /// </param>
        /// <example>
        /// <code>
        /// var numbers = new List&lt;int&gt; { 1, 2, 3, 4 };
        /// numbers.Shift(1); // numbers becomes { 4, 1, 2, 3 }
        /// numbers.Shift(-2); // numbers becomes { 2, 3, 4, 1 }
        /// </code>
        /// </example>
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

        /// <summary>
        /// Reverses the order of elements in-place within the inclusive range.
        /// </summary>
        /// <typeparam name="T">Element type held by the list.</typeparam>
        /// <param name="list">List whose segment should be reversed.</param>
        /// <param name="start">Zero-based index of the first element in the range.</param>
        /// <param name="end">Zero-based index of the last element in the range.</param>
        /// <exception cref="ArgumentException">Thrown when the start or end value is outside the list bounds.</exception>
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

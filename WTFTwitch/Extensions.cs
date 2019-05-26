using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WTFTwitch
{
    public static class Extensions
    {
        public static V Get<T, V>(this ConcurrentDictionary<T, V> dict, T key)
        {
            V value;
            return dict.TryGetValue(key, out value) ? value : default(V);
        }

        public static bool HasKey<T, V>(this ConcurrentDictionary<T, V> dict, T key)
        {
            return dict.TryGetValue(key, out V value);
        }

        public static V Remove<T, V>(this ConcurrentDictionary<T, V> dict, T key)
        {
            V value = default(V);

            while (dict.HasKey(key) && !dict.TryRemove(key, out value))
                continue;

            return value;
        }

        public static T Dequeue<T>(this ConcurrentQueue<T> queue)
        {
            T value = default(T);

            while (queue.Count > 0 && !queue.TryDequeue(out value))
                continue;

            return value;
        }

        public static string AsPrettyReadable(this TimeSpan timeSpan)
        {
            var parts = new List<string>();
            if (timeSpan.Days != 0)
                parts.Add($"{timeSpan.Days}d");
            if (timeSpan.Hours != 0)
                parts.Add($"{timeSpan.Hours}h");
            if (timeSpan.Minutes != 0)
                parts.Add($"{timeSpan.Minutes}m");
            if (timeSpan.Seconds != 0)
                parts.Add($"{timeSpan.Seconds}s");

            return string.Join(" ", parts);
        }

        public static bool IsEmpty(this DateTime date)
        {
            return date.Ticks == 0;
        }
    }
}

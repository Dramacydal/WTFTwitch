﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WTFShared.Database;
using WTFShared.Tasks;

namespace WTFShared
{
    public static class Extensions
    {
        public static V Get<T, V>(this ConcurrentDictionary<T, V> dict, T key)
        {
            var value = default(V);

            while (dict.ContainsKey(key)  && !dict.TryGetValue(key, out value))
                continue;

            return value;
        }

        public static V GetOrAdd<T, V>(this ConcurrentDictionary<T, V> dict, T key) where V : new()
        {
            var res = dict.GetOrAdd(key, new V());

            return res;
        }

        public static bool HasKey<T, V>(this ConcurrentDictionary<T, V> dict, T key)
        {
            return dict.TryGetValue(key, out var value);
        }

        public static V Remove<T, V>(this ConcurrentDictionary<T, V> dict, T key)
        {
            var value = default(V);

            while (dict.ContainsKey(key) && !dict.TryRemove(key, out value))
                continue;

            return value;
        }

        public static T Dequeue<T>(this ConcurrentQueue<T> queue)
        {
            var value = default(T);

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
            if (timeSpan.Seconds != 0 || parts.Count == 0)
                parts.Add($"{timeSpan.Seconds}s");

            return string.Join(" ", parts);
        }

        public static bool IsEmpty(this DateTime date)
        {
            return date == default(DateTime);
        }

        public static string Info(this Exception e)
        {
            var ex = e;

            var parts = new List<string>();
            do
            {
                parts.Add(ex.Message);
                ex = ex.InnerException;
            } while (ex != null);

            var message = string.Join(" ", parts);

            return message + e.StackTrace;
        }

        public static ResultRows ReadAll(this MySqlDataReader reader)
        {
            var res = new ResultRows();

            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                res.Add(values);
            }

            return res;
        }

        public static bool IsPending(this WTFTaskStatus status)
        {
            return status == WTFTaskStatus.None || status == WTFTaskStatus.Executing || status == WTFTaskStatus.Processing;
        }

        public static int IndexOf<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            var index = 0;
            foreach (var item in source)
            {
                if (predicate.Invoke(item))
                {
                    return index;
                }
                index++;
            }

            return -1;
        }

        public static IEnumerable<IEnumerable<T>> Split<T>(this IEnumerable<T> array, int size)
        {
            for (var i = 0; i < (float)array.Count() / size; i++)
            {
                yield return array.Skip(i * size).Take(size);
            }
        }
    }
}

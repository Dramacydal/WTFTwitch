using System;
using System.Runtime.Caching;

namespace WTFTwitch.Bot
{
    static class CacheHelper
    {
        private static MemoryCache _cache => MemoryCache.Default;

        private static readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(5);

        public static bool Save(string key, object value, TimeSpan expiration = default(TimeSpan))
        {
            if (expiration.TotalSeconds == 0)
                expiration = _defaultExpiration;

            return _cache.Add(key, value, new CacheItemPolicy() { AbsoluteExpiration = DateTime.UtcNow.Add(expiration) });
        }

        public static bool Contains(string key) => _cache.Contains(key);

        public static bool Load<T>(string key, out T value)
        {
            if (!Contains(key))
            {
                value = default(T);

                return false;
            }

            value = (T)_cache.Get(key);
            return true;
        }
    }
}

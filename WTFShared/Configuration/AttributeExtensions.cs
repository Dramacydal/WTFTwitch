using System;
using System.Collections.Generic;
using System.Linq;

namespace WTFShared.Configuration
{
    public static class AttributeExtensions
    {
        public static TValue GetAttributeValue<TAttribute, TValue>(
            this Type type,
            Func<TAttribute, TValue> valueSelector)
            where TAttribute : Attribute
        {
            var attribute = type.GetAttributes<TAttribute>().FirstOrDefault();
            return attribute != null ? valueSelector(attribute) : default(TValue);
        }

        public static IEnumerable<TAttribute> GetAttributes<TAttribute>(this Type type) where TAttribute : Attribute
        {
            return type.GetCustomAttributes(typeof(TAttribute), true).Select(_ => _ as TAttribute);
        }
    }
}
using System.Collections.Generic;
using System.Reflection;

namespace NaughtyAttributes.Editor
{
    // Local patch (Pistol project): attributes of a field or method never change while the
    // editor is running, so look them up once instead of on every Inspector repaint.
    internal static class AttributeCache<T> where T : class
    {
        private static readonly Dictionary<MemberInfo, T[]> _cache = new Dictionary<MemberInfo, T[]>();

        public static T[] All(MemberInfo member)
        {
            if (!_cache.TryGetValue(member, out T[] attributes))
            {
                attributes = (T[])member.GetCustomAttributes(typeof(T), true);
                _cache[member] = attributes;
            }

            return attributes;
        }

        public static T First(MemberInfo member)
        {
            T[] attributes = All(member);
            return attributes.Length > 0 ? attributes[0] : null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace NaughtyAttributes.Editor
{
    public static class ReflectionUtility
    {
        public static IEnumerable<FieldInfo> GetAllFields(object target, Func<FieldInfo, bool> predicate)
        {
            if (target == null)
            {
                Debug.LogError("The target object is null. Check for missing scripts.");
                yield break;
            }

            List<Type> types = GetSelfAndBaseTypes(target);

            for (int i = types.Count - 1; i >= 0; i--)
            {
                IEnumerable<FieldInfo> fieldInfos = types[i]
                    .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(predicate);

                foreach (var fieldInfo in fieldInfos)
                {
                    yield return fieldInfo;
                }
            }
        }

        public static IEnumerable<PropertyInfo> GetAllProperties(object target, Func<PropertyInfo, bool> predicate)
        {
            if (target == null)
            {
                Debug.LogError("The target object is null. Check for missing scripts.");
                yield break;
            }

            List<Type> types = GetSelfAndBaseTypes(target);

            for (int i = types.Count - 1; i >= 0; i--)
            {
                IEnumerable<PropertyInfo> propertyInfos = types[i]
                    .GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(predicate);

                foreach (var propertyInfo in propertyInfos)
                {
                    yield return propertyInfo;
                }
            }
        }

        public static IEnumerable<MethodInfo> GetAllMethods(object target, Func<MethodInfo, bool> predicate)
        {
            if (target == null)
            {
                Debug.LogError("The target object is null. Check for missing scripts.");
                yield break;
            }

            List<Type> types = GetSelfAndBaseTypes(target);

            for (int i = types.Count - 1; i >= 0; i--)
            {
                IEnumerable<MethodInfo> methodInfos = types[i]
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(predicate);

                foreach (var methodInfo in methodInfos)
                {
                    yield return methodInfo;
                }
            }
        }

        // Local patch (Pistol project): lookups by name are cached per type (null results too),
        // because the Inspector repeats them for every field on every repaint.
        private static readonly Dictionary<(Type, string), FieldInfo> _fieldCache = new Dictionary<(Type, string), FieldInfo>();
        private static readonly Dictionary<(Type, string), PropertyInfo> _propertyCache = new Dictionary<(Type, string), PropertyInfo>();
        private static readonly Dictionary<(Type, string), MethodInfo> _methodCache = new Dictionary<(Type, string), MethodInfo>();

        public static FieldInfo GetField(object target, string fieldName)
        {
            if (target == null)
            {
                return GetAllFields(target, f => f.Name.Equals(fieldName, StringComparison.Ordinal)).FirstOrDefault();
            }

            var key = (target.GetType(), fieldName);
            if (!_fieldCache.TryGetValue(key, out FieldInfo field))
            {
                field = GetAllFields(target, f => f.Name.Equals(fieldName, StringComparison.Ordinal)).FirstOrDefault();
                _fieldCache[key] = field;
            }

            return field;
        }

        public static PropertyInfo GetProperty(object target, string propertyName)
        {
            if (target == null)
            {
                return GetAllProperties(target, p => p.Name.Equals(propertyName, StringComparison.Ordinal)).FirstOrDefault();
            }

            var key = (target.GetType(), propertyName);
            if (!_propertyCache.TryGetValue(key, out PropertyInfo property))
            {
                property = GetAllProperties(target, p => p.Name.Equals(propertyName, StringComparison.Ordinal)).FirstOrDefault();
                _propertyCache[key] = property;
            }

            return property;
        }

        public static MethodInfo GetMethod(object target, string methodName)
        {
            if (target == null)
            {
                return GetAllMethods(target, m => m.Name.Equals(methodName, StringComparison.Ordinal)).FirstOrDefault();
            }

            var key = (target.GetType(), methodName);
            if (!_methodCache.TryGetValue(key, out MethodInfo method))
            {
                method = GetAllMethods(target, m => m.Name.Equals(methodName, StringComparison.Ordinal)).FirstOrDefault();
                _methodCache[key] = method;
            }

            return method;
        }

        public static Type GetListElementType(Type listType)
        {
            if (listType.IsGenericType)
            {
                return listType.GetGenericArguments()[0];
            }
            else
            {
                return listType.GetElementType();
            }
        }

        /// <summary>
        ///		Get type and all base types of target, sorted as following:
        ///		<para />[target's type, base type, base's base type, ...]
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        private static List<Type> GetSelfAndBaseTypes(object target)
        {
            List<Type> types = new List<Type>()
            {
                target.GetType()
            };

            while (types.Last().BaseType != null)
            {
                types.Add(types.Last().BaseType);
            }

            return types;
        }
    }
}

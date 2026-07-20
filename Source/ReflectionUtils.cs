using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    internal static class ReflectionUtils
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Reflection member discovery is performed from several frequently refreshed UI paths.
        // Cache both hits and misses so each ordered (Type, candidate names) lookup walks the
        // inheritance hierarchy at most once for the lifetime of the process.
        private static readonly object MemberCacheLock = new object();
        private static readonly Dictionary<MemberLookupKey, FieldInfo> FieldCache =
            new Dictionary<MemberLookupKey, FieldInfo>();
        private static readonly Dictionary<MemberLookupKey, PropertyInfo> PropertyCache =
            new Dictionary<MemberLookupKey, PropertyInfo>();

        public static FieldInfo FindField(Type type, params string[] names)
        {
            MemberLookupKey key = new MemberLookupKey(type, BuildNamesKey(names));

            lock (MemberCacheLock)
            {
                if (FieldCache.TryGetValue(key, out FieldInfo cached))
                    return cached;
            }

            FieldInfo result = null;
            foreach (Type current in EnumerateTypeHierarchy(type))
            {
                if (names == null)
                    break;

                foreach (string name in names)
                {
                    if (string.IsNullOrEmpty(name))
                        continue;

                    result = current.GetField(name, InstanceFlags);
                    if (result != null)
                        break;
                }

                if (result != null)
                    break;
            }

            lock (MemberCacheLock)
                FieldCache[key] = result;

            return result;
        }

        public static PropertyInfo FindProperty(Type type, params string[] names)
        {
            MemberLookupKey key = new MemberLookupKey(type, BuildNamesKey(names));

            lock (MemberCacheLock)
            {
                if (PropertyCache.TryGetValue(key, out PropertyInfo cached))
                    return cached;
            }

            PropertyInfo result = null;
            foreach (Type current in EnumerateTypeHierarchy(type))
            {
                if (names == null)
                    break;

                foreach (string name in names)
                {
                    if (string.IsNullOrEmpty(name))
                        continue;

                    result = current.GetProperty(name, InstanceFlags);
                    if (result != null)
                        break;
                }

                if (result != null)
                    break;
            }

            lock (MemberCacheLock)
                PropertyCache[key] = result;

            return result;
        }

        public static object GetMemberObject(object obj, Type type, params string[] names)
        {
            return GetMemberObject(obj, type, null, names);
        }

        public static object GetMemberObject(object obj, Type type, string context, params string[] names)
        {
            if (obj == null)
                return null;

            type = type ?? obj.GetType();
            PropertyInfo property = FindProperty(type, names);
            if (property != null)
            {
                try { return property.GetValue(obj, null); }
                catch (Exception ex)
                {
                    RRLog.VerboseExceptionOnce("ReflectionUtils:GetProperty:" + (context ?? property.Name),
                        "ReflectionUtils: failed to read property '" + property.Name + "'" + FormatContext(context), ex);
                }
            }

            FieldInfo field = FindField(type, names);
            if (field != null)
            {
                try { return field.GetValue(obj); }
                catch (Exception ex)
                {
                    RRLog.VerboseExceptionOnce("ReflectionUtils:GetField:" + (context ?? field.Name),
                        "ReflectionUtils: failed to read field '" + field.Name + "'" + FormatContext(context), ex);
                }
            }

            return null;
        }

        public static string GetString(object obj, Type type, params string[] names)
        {
            object value = GetMemberObject(obj, type, names);
            return value == null ? null : value.ToString();
        }

        public static Texture GetTexture(object obj, Type type, params string[] names)
        {
            object value = GetMemberObject(obj, type, names);
            return value as Texture;
        }

        public static int GetIntLike(object obj, Type type, int fallback, params string[] names)
        {
            object value = GetMemberObject(obj, type, names);
            if (value == null)
                return fallback;

            if (value is int i)
                return i;

            if (value is short s)
                return s;

            if (value is byte b)
                return b;

            if (value is long l)
                return (int)l;

            if (value is float f)
                return Mathf.RoundToInt(f);

            if (value is double d)
                return (int)Math.Round(d);

            if (int.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out int parsed))
                return parsed;

            return fallback;
        }

        public static double GetDouble(object obj, Type type, double fallback, params string[] names)
        {
            object value = GetMemberObject(obj, type, names);
            if (value == null)
                return fallback;

            if (value is double d)
                return d;

            if (value is float f)
                return f;

            if (value is int i)
                return i;

            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                return parsed;

            return fallback;
        }

        public static float GetFloat(object obj, Type type, float fallback, params string[] names)
        {
            object value = GetMemberObject(obj, type, names);
            if (value == null)
                return fallback;

            if (value is float f)
                return f;

            if (value is double d)
                return (float)d;

            if (value is int i)
                return i;

            if (float.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float parsed))
                return parsed;

            return fallback;
        }

        public static bool GetBool(object obj, Type type, bool fallback, params string[] names)
        {
            object value = GetMemberObject(obj, type, names);
            if (value == null)
                return fallback;

            if (value is bool b)
                return b;

            if (bool.TryParse(value.ToString(), out bool parsed))
                return parsed;

            if (int.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out int intValue))
                return intValue != 0;

            return fallback;
        }


        public static bool TrySetFieldValue(object obj, Type type, object value, string context, params string[] names)
        {
            if (obj == null)
                return false;

            type = type ?? obj.GetType();
            FieldInfo field = FindField(type, names);
            if (field == null)
                return false;

            try
            {
                field.SetValue(obj, value);
                return true;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("ReflectionUtils:SetField:" + (context ?? field.Name),
                    "ReflectionUtils: failed to set field '" + field.Name + "'" + FormatContext(context), ex);
                return false;
            }
        }

        public static bool TrySetPropertyValue(object obj, Type type, object value, string context, params string[] names)
        {
            if (obj == null)
                return false;

            type = type ?? obj.GetType();
            PropertyInfo property = FindProperty(type, names);
            if (property == null || !property.CanWrite)
                return false;

            try
            {
                property.SetValue(obj, value, null);
                return true;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("ReflectionUtils:SetProperty:" + (context ?? property.Name),
                    "ReflectionUtils: failed to set property '" + property.Name + "'" + FormatContext(context), ex);
                return false;
            }
        }

        public static bool TryInvoke(MethodInfo method, object target, object[] args, string context, out object result)
        {
            result = null;
            if (method == null)
                return false;

            try
            {
                result = method.Invoke(target, args);
                return true;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("ReflectionUtils:Invoke:" + (context ?? method.Name),
                    "ReflectionUtils: failed to invoke method '" + method.Name + "'" + FormatContext(context), ex);
                return false;
            }
        }

        public static bool TryInvoke(MethodInfo method, object target, object[] args, string context)
        {
            object ignored;
            return TryInvoke(method, target, args, context, out ignored);
        }


        private static string BuildNamesKey(string[] names)
        {
            if (names == null || names.Length == 0)
                return string.Empty;

            // Length-prefix each value so candidate sequences cannot collide when names
            // contain punctuation or delimiter characters. Candidate order is significant.
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i] ?? string.Empty;
                builder.Append(name.Length);
                builder.Append(':');
                builder.Append(name);
                builder.Append(';');
            }

            return builder.ToString();
        }

        private struct MemberLookupKey : IEquatable<MemberLookupKey>
        {
            private readonly Type _type;
            private readonly string _namesKey;

            public MemberLookupKey(Type type, string namesKey)
            {
                _type = type;
                _namesKey = namesKey ?? string.Empty;
            }

            public bool Equals(MemberLookupKey other)
            {
                return ReferenceEquals(_type, other._type) &&
                       string.Equals(_namesKey, other._namesKey, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is MemberLookupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _type == null ? 0 : _type.GetHashCode();
                    return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_namesKey);
                }
            }
        }

        private static string FormatContext(string context)
        {
            return string.IsNullOrEmpty(context) ? string.Empty : (" (" + context + ")");
        }

        private static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
        {
            for (Type current = type; current != null; current = current.BaseType)
                yield return current;
        }
    }
}

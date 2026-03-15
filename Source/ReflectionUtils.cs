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

        public static FieldInfo FindField(Type type, params string[] names)
        {
            foreach (Type current in EnumerateTypeHierarchy(type))
            {
                foreach (string name in names)
                {
                    if (string.IsNullOrEmpty(name))
                        continue;

                    FieldInfo field = current.GetField(name, InstanceFlags);
                    if (field != null)
                        return field;
                }
            }

            return null;
        }

        public static PropertyInfo FindProperty(Type type, params string[] names)
        {
            foreach (Type current in EnumerateTypeHierarchy(type))
            {
                foreach (string name in names)
                {
                    if (string.IsNullOrEmpty(name))
                        continue;

                    PropertyInfo property = current.GetProperty(name, InstanceFlags);
                    if (property != null)
                        return property;
                }
            }

            return null;
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

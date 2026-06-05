using System;
using System.Globalization;
using System.Reflection;

namespace RosterRotation
{
    /// <summary>
    /// Reflection helpers for KSP's version-sensitive ProtoCrewMember fields.
    /// Keeping this isolated makes suit/veteran/badass code easier to audit.
    /// </summary>
    internal static class EACKerbalMemberAccess
    {
        internal static bool ReadBool(object obj, bool fallback, params string[] names)
        {
            object raw;
            if (!TryRead(obj, out raw, names) || raw == null) return fallback;
            try
            {
                if (raw is bool b) return b;
                return Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
            }
            catch { return fallback; }
        }

        internal static bool WriteBool(object obj, bool value, params string[] names)
        {
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || obj == null) continue;
                var type = obj.GetType();
                try
                {
                    var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && (f.FieldType == typeof(bool) || f.FieldType == typeof(Boolean)))
                    {
                        bool old = (bool)f.GetValue(obj);
                        if (old == value) return false;
                        f.SetValue(obj, value);
                        return true;
                    }

                    var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.CanRead && p.CanWrite && (p.PropertyType == typeof(bool) || p.PropertyType == typeof(Boolean)))
                    {
                        bool old = (bool)p.GetValue(obj, null);
                        if (old == value) return false;
                        p.SetValue(obj, value, null);
                        return true;
                    }
                }
                catch { /* try next member name */ }
            }
            return false;
        }

        internal static bool TryRead(object obj, out object value, params string[] names)
        {
            value = null;
            if (obj == null) return false;
            var type = obj.GetType();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null) { value = f.GetValue(obj); return true; }
                    var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.CanRead) { value = p.GetValue(obj, null); return true; }
                }
                catch { /* try next */ }
            }
            return false;
        }

        internal static bool TryWriteString(object obj, string stringValue, params string[] names)
        {
            if (obj == null || stringValue == null) return false;
            var type = obj.GetType();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        string old = f.GetValue(obj) as string;
                        if (string.Equals(old, stringValue, StringComparison.OrdinalIgnoreCase)) return true;
                        f.SetValue(obj, stringValue);
                        return true;
                    }

                    var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                    {
                        string old = null;
                        if (p.CanRead) old = p.GetValue(obj, null) as string;
                        if (string.Equals(old, stringValue, StringComparison.OrdinalIgnoreCase)) return true;
                        p.SetValue(obj, stringValue, null);
                        return true;
                    }
                }
                catch { /* try next */ }
            }
            return false;
        }

        internal static bool TryWriteIntLike(object obj, int intValue, params string[] names)
        {
            if (obj == null) return false;
            var type = obj.GetType();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null)
                    {
                        object old = f.GetValue(obj);
                        object wanted = ConvertIntToMemberType(intValue, f.FieldType);
                        if (old != null && old.Equals(wanted)) return false;
                        f.SetValue(obj, wanted);
                        return true;
                    }

                    var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.CanRead && p.CanWrite)
                    {
                        object old = p.GetValue(obj, null);
                        object wanted = ConvertIntToMemberType(intValue, p.PropertyType);
                        if (old != null && old.Equals(wanted)) return false;
                        p.SetValue(obj, wanted, null);
                        return true;
                    }
                }
                catch { /* try next */ }
            }
            return false;
        }

        private static object ConvertIntToMemberType(int value, Type memberType)
        {
            if (memberType == null) return value;
            if (memberType.IsEnum) return Enum.ToObject(memberType, value);
            if (memberType == typeof(byte)) return (byte)value;
            if (memberType == typeof(short)) return (short)value;
            if (memberType == typeof(long)) return (long)value;
            if (memberType == typeof(float)) return (float)value;
            if (memberType == typeof(double)) return (double)value;
            return value;
        }
    }
}

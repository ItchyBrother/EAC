using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RosterRotation
{
    public static class CrewRandRWriter
    {
        private sealed class ExtTypeAccessors
        {
            public FieldInfo ProtoReferenceField;
            public PropertyInfo ProtoReferenceProperty;
            public FieldInfo LastMissionEndField;
            public PropertyInfo LastMissionEndProperty;
            public FieldInfo LastMissionDurationField;
            public PropertyInfo LastMissionDurationProperty;
            public FieldInfo CurrentMissionStartField;
            public PropertyInfo CurrentMissionStartProperty;
        }

        private static readonly Dictionary<Type, ExtTypeAccessors> _accessorCache =
            new Dictionary<Type, ExtTypeAccessors>();

        public static bool TrySetVacationUntil(string kerbalName, double untilUT)
        {
            try
            {
                var asm = AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a?.name != null &&
                                         a.name.IndexOf("crewrandr", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?.assembly;

                if (asm == null || string.IsNullOrEmpty(kerbalName))
                    return false;

                var rosterType = asm.GetTypes().FirstOrDefault(t => (t.FullName ?? string.Empty).Contains("CrewRandRRoster"));
                if (rosterType == null)
                    return false;

                var instProp = rosterType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var rosterInst = instProp?.GetValue(null, null);
                if (rosterInst == null)
                    return false;

                var extProp = rosterType.GetProperty("ExtDataSet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var extSet = extProp?.GetValue(rosterInst, null) as IEnumerable;
                if (extSet == null)
                    return false;

                foreach (var ext in extSet)
                {
                    if (ext == null) continue;

                    var extType = ext.GetType();
                    var accessors = GetAccessors(extType);
                    var pcm = GetProtoReference(ext, accessors);
                    if (pcm == null || !string.Equals(pcm.name, kerbalName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double currentExpiry;
                    if (!CrewRandRAdapter.TryGetVacationUntilByName(kerbalName, out currentExpiry) || currentExpiry <= 0)
                        return false;

                    double delta = untilUT - currentExpiry;
                    if (delta <= 1e-3)
                    {
                        pcm.inactive = true;
                        pcm.inactiveTimeEnd = Math.Max(pcm.inactiveTimeEnd, untilUT);
                        return true;
                    }

                    double lastMissionEnd;
                    if (!TryReadNamedNumeric(ext, accessors.LastMissionEndField, accessors.LastMissionEndProperty, out lastMissionEnd))
                        return false;

                    if (!TryWriteNamedNumeric(ext, accessors.LastMissionEndField, accessors.LastMissionEndProperty, lastMissionEnd + delta))
                        return false;

                    // Keep the roster status and EAC mirror aligned with CrewRandR's computed vacation.
                    TrySetVacationStatus(asm, pcm);
                    pcm.inactive = true;
                    pcm.inactiveTimeEnd = Math.Max(pcm.inactiveTimeEnd, untilUT);

                    RRLog.Verbose($"[EAC] CrewRandR vacation extended for {pcm.name}: currentExpiry={currentExpiry:0.###}, targetUntil={untilUT:0.###}, delta={delta:0.###}, lastMissionEnd(old)={lastMissionEnd:0.###}, lastMissionEnd(new)={(lastMissionEnd + delta):0.###}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"CrewRandRWriter.TrySetVacationUntil failed: {ex}");
            }

            return false;
        }

        private static ExtTypeAccessors GetAccessors(Type extType)
        {
            if (_accessorCache.TryGetValue(extType, out var cached))
                return cached;

            var built = new ExtTypeAccessors();

            foreach (var f in extType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f == null) continue;

                if (built.ProtoReferenceField == null && string.Equals(f.Name, "ProtoReference", StringComparison.Ordinal))
                    built.ProtoReferenceField = f;
                else if (built.LastMissionEndField == null && string.Equals(f.Name, "LastMissionEndTime", StringComparison.Ordinal))
                    built.LastMissionEndField = f;
                else if (built.LastMissionDurationField == null && string.Equals(f.Name, "LastMissionDuration", StringComparison.Ordinal))
                    built.LastMissionDurationField = f;
                else if (built.CurrentMissionStartField == null && string.Equals(f.Name, "CurrentMissionStartTime", StringComparison.Ordinal))
                    built.CurrentMissionStartField = f;
            }

            foreach (var p in extType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p == null) continue;

                if (built.ProtoReferenceProperty == null && string.Equals(p.Name, "ProtoReference", StringComparison.Ordinal))
                    built.ProtoReferenceProperty = p;
                else if (built.LastMissionEndProperty == null && string.Equals(p.Name, "LastMissionEndTime", StringComparison.Ordinal))
                    built.LastMissionEndProperty = p;
                else if (built.LastMissionDurationProperty == null && string.Equals(p.Name, "LastMissionDuration", StringComparison.Ordinal))
                    built.LastMissionDurationProperty = p;
                else if (built.CurrentMissionStartProperty == null && string.Equals(p.Name, "CurrentMissionStartTime", StringComparison.Ordinal))
                    built.CurrentMissionStartProperty = p;
            }

            _accessorCache[extType] = built;
            return built;
        }

        private static ProtoCrewMember GetProtoReference(object ext, ExtTypeAccessors accessors)
        {
            try
            {
                if (accessors.ProtoReferenceField != null)
                    return accessors.ProtoReferenceField.GetValue(ext) as ProtoCrewMember;

                if (accessors.ProtoReferenceProperty != null)
                    return accessors.ProtoReferenceProperty.GetValue(ext, null) as ProtoCrewMember;
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("CrewRandRWriter.cs:147", "Suppressed exception in CrewRandRWriter.cs:147", ex); }

            return null;
        }

        private static bool TrySetVacationStatus(Assembly asm, ProtoCrewMember pcm)
        {
            if (asm == null || pcm == null) return false;

            try
            {
                foreach (var t in asm.GetTypes())
                {
                    var f = t.GetField("ROSTERSTATUS_VACATION", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f == null || f.FieldType != typeof(ProtoCrewMember.RosterStatus)) continue;
                    pcm.rosterStatus = (ProtoCrewMember.RosterStatus)f.GetValue(null);
                    return true;
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("CrewRandRWriter.cs:166", "Suppressed exception in CrewRandRWriter.cs:166", ex); }

            return false;
        }

        private static bool TryReadNamedNumeric(object ext, FieldInfo field, PropertyInfo property, out double value)
        {
            value = 0;
            if (TryReadNumericField(ext, field, out value)) return true;
            if (TryReadNumericProperty(ext, property, out value)) return true;
            return false;
        }

        private static bool TryWriteNamedNumeric(object ext, FieldInfo field, PropertyInfo property, double value)
        {
            if (TryWriteNumericField(ext, field, value)) return true;
            if (TryWriteNumericProperty(ext, property, value)) return true;
            return false;
        }

        private static bool TryReadNumericField(object ext, FieldInfo field, out double value)
        {
            value = 0;
            if (field == null) return false;
            try
            {
                object raw = field.GetValue(ext);
                if (raw == null) return false;
                value = Convert.ToDouble(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadNumericProperty(object ext, PropertyInfo property, out double value)
        {
            value = 0;
            if (property == null || !property.CanRead) return false;
            try
            {
                object raw = property.GetValue(ext, null);
                if (raw == null) return false;
                value = Convert.ToDouble(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryWriteNumericField(object ext, FieldInfo field, double value)
        {
            if (field == null) return false;
            try
            {
                if (field.FieldType == typeof(float)) field.SetValue(ext, (float)value);
                else field.SetValue(ext, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryWriteNumericProperty(object ext, PropertyInfo property, double value)
        {
            if (property == null || !property.CanWrite) return false;
            try
            {
                if (property.PropertyType == typeof(float)) property.SetValue(ext, (float)value, null);
                else property.SetValue(ext, value, null);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

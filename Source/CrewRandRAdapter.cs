using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RosterRotation
{
    public static class CrewRandRAdapter
    {
        private static bool _inited;
        private static Assembly _asm;

        // Cached: CrewRandR.ROSTERSTATUS_VACATION
        private static ProtoCrewMember.RosterStatus? _vacStatus;

        // Cached roster ext data access
        private static object _rosterInstance;        // CrewRandRRoster.Instance
        private static IEnumerable _extDataSet;       // CrewRandRRoster.Instance.ExtDataSet

        private static readonly Dictionary<Type, ExtTypeAccessors> _extTypeAccessors =
            new Dictionary<Type, ExtTypeAccessors>();

        private sealed class ExtTypeAccessors
        {
            public FieldInfo ProtoReferenceField;
            public PropertyInfo ProtoReferenceProperty;
            public FieldInfo[] CandidateFields = Array.Empty<FieldInfo>();
            public PropertyInfo[] CandidateProperties = Array.Empty<PropertyInfo>();
            public FieldInfo[] NumericFields = Array.Empty<FieldInfo>();
            public PropertyInfo[] NumericProperties = Array.Empty<PropertyInfo>();
        }

        public static bool IsInstalled()
        {
            EnsureInit();
            return _asm != null;
        }

        public static void InvalidateVacationCache()
        {
            _rosterInstance = null;
            _extDataSet = null;
        }

        /// <summary>True if CrewRandR says the Kerbal is on vacation.</summary>
        public static bool IsOnVacation(ProtoCrewMember kerbal)
        {
            EnsureInit();
            if (_asm == null || kerbal == null) return false;

            if (_vacStatus.HasValue && kerbal.rosterStatus == _vacStatus.Value)
                return true;

            return false;
        }

        public static bool TryGetVacationUntilByName(string kerbalName, out double untilUT)
        {
            untilUT = 0;
            EnsureInit();
            if (_asm == null || string.IsNullOrEmpty(kerbalName)) return false;

            try
            {
                double nowUT = Planetarium.GetUniversalTime();

                // Flight -> KSC scene changes can invalidate CrewRandR's singleton and its ext-data enumerable.
                // Always try the current handle first, then force one refresh if nothing useful is found.
                if (TryGetVacationUntilByNameInternal(kerbalName, nowUT, out untilUT))
                    return untilUT > 0;

                InvalidateVacationCache();
                TryInitRosterExtAccess();

                if (TryGetVacationUntilByNameInternal(kerbalName, nowUT, out untilUT))
                    return untilUT > 0;
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] CrewRandRAdapter.TryGetVacationUntilByName failed: {ex}");
            }

            return false;
        }

        private static bool TryGetVacationUntilByNameInternal(string kerbalName, double nowUT, out double untilUT)
        {
            untilUT = 0;

            if (_extDataSet == null || _rosterInstance == null)
                TryInitRosterExtAccess();
            if (_extDataSet == null) return false;

            foreach (var ext in _extDataSet)
            {
                if (ext == null) continue;

                var accessors = GetAccessors(ext.GetType());
                var pcm = GetProtoReference(ext, accessors);
                if (pcm == null || !string.Equals(pcm.name, kerbalName, StringComparison.OrdinalIgnoreCase))
                    continue;

                untilUT = ExtractVacationUntil(ext, accessors, nowUT);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to get the UT when vacation ends. Returns false if not available.
        /// If it returns true and untilUT > nowUT, you can show R&R time remaining.
        /// </summary>
        public static bool TryGetVacationUntil(ProtoCrewMember kerbal, out double untilUT)
        {
            untilUT = 0;
            EnsureInit();
            if (_asm == null || kerbal == null) return false;

            return TryGetVacationUntilByName(kerbal.name, out untilUT);
        }

        private static void EnsureInit()
        {
            if (_inited) return;
            _inited = true;

            try
            {
                var loaded = AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a?.name != null &&
                                         a.name.IndexOf("crewrandr", StringComparison.OrdinalIgnoreCase) >= 0);

                _asm = loaded?.assembly;
                if (_asm == null) return;

                foreach (var t in SafeGetTypes(_asm))
                {
                    var f = t.GetField("ROSTERSTATUS_VACATION", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && f.FieldType == typeof(ProtoCrewMember.RosterStatus))
                    {
                        _vacStatus = (ProtoCrewMember.RosterStatus)f.GetValue(null);
                        RRLog.Verbose($"[RosterRotation] CrewRandRAdapter: found ROSTERSTATUS_VACATION = {_vacStatus.Value}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] CrewRandRAdapter init failed: {ex}");
                _asm = null;
            }
        }

        private static ExtTypeAccessors GetAccessors(Type extType)
        {
            if (_extTypeAccessors.TryGetValue(extType, out var cached))
                return cached;

            var built = new ExtTypeAccessors();
            var numericFields = new List<FieldInfo>();
            var candidateFields = new List<FieldInfo>();
            var numericProperties = new List<PropertyInfo>();
            var candidateProperties = new List<PropertyInfo>();

            foreach (var f in extType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f == null) continue;

                if (built.ProtoReferenceField == null && string.Equals(f.Name, "ProtoReference", StringComparison.Ordinal))
                    built.ProtoReferenceField = f;

                if (f.FieldType == typeof(double) || f.FieldType == typeof(float))
                {
                    numericFields.Add(f);
                    if (LooksLikeVacationEndName(f.Name)) candidateFields.Add(f);
                }
            }

            foreach (var p in extType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p == null) continue;

                if (built.ProtoReferenceProperty == null && string.Equals(p.Name, "ProtoReference", StringComparison.Ordinal))
                    built.ProtoReferenceProperty = p;

                if (!p.CanRead) continue;
                if (p.PropertyType == typeof(double) || p.PropertyType == typeof(float))
                {
                    numericProperties.Add(p);
                    if (LooksLikeVacationEndName(p.Name)) candidateProperties.Add(p);
                }
            }

            built.NumericFields = numericFields.ToArray();
            built.CandidateFields = candidateFields.ToArray();
            built.NumericProperties = numericProperties.ToArray();
            built.CandidateProperties = candidateProperties.ToArray();
            _extTypeAccessors[extType] = built;
            return built;
        }

        private static bool LooksLikeVacationEndName(string memberName)
        {
            if (string.IsNullOrEmpty(memberName)) return false;
            string n = memberName.ToLowerInvariant();
            return n.Contains("vac") && (n.Contains("end") || n.Contains("until") || n.Contains("return") || n.Contains("next"));
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
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("CrewRandRAdapter.cs:221", "Suppressed exception in CrewRandRAdapter.cs:221", ex); }

            return null;
        }

        private static double ExtractVacationUntil(object ext, ExtTypeAccessors accessors, double nowUT)
        {
            double value;

            for (int i = 0; i < accessors.CandidateFields.Length; i++)
            {
                if (TryReadNumericField(ext, accessors.CandidateFields[i], out value) && value > 0)
                    return value;
            }

            for (int i = 0; i < accessors.CandidateProperties.Length; i++)
            {
                if (TryReadNumericProperty(ext, accessors.CandidateProperties[i], out value) && value > 0)
                    return value;
            }

            double bestFuture = 0;
            for (int i = 0; i < accessors.NumericFields.Length; i++)
            {
                if (!TryReadNumericField(ext, accessors.NumericFields[i], out value)) continue;
                if (value > nowUT && value > bestFuture) bestFuture = value;
            }

            for (int i = 0; i < accessors.NumericProperties.Length; i++)
            {
                if (!TryReadNumericProperty(ext, accessors.NumericProperties[i], out value)) continue;
                if (value > nowUT && value > bestFuture) bestFuture = value;
            }

            return bestFuture;
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

        private static void TryInitRosterExtAccess()
        {
            try
            {
                _rosterInstance = null;
                _extDataSet = null;

                if (_asm == null) return;

                var rosterType = SafeGetTypes(_asm).FirstOrDefault(t => t.FullName != null && t.FullName.Contains("CrewRandRRoster"));
                if (rosterType == null) return;

                var instProp = rosterType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _rosterInstance = instProp?.GetValue(null, null);
                if (_rosterInstance == null) return;

                var extProp = rosterType.GetProperty("ExtDataSet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _extDataSet = extProp?.GetValue(_rosterInstance, null) as IEnumerable;
            }
            catch
            {
                _rosterInstance = null;
                _extDataSet = null;
            }
        }

        private static Type[] SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }

        public static bool IsOnVacationByName(string kerbalName, double nowUT)
        {
            return TryGetVacationUntilByName(kerbalName, out var untilUT) && untilUT > nowUT;
        }
    }
}
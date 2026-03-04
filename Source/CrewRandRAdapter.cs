using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

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

        public static bool IsInstalled()
        {
            EnsureInit();
            return _asm != null;
        }

        /// <summary>True if CrewRandR says the Kerbal is on vacation.</summary>
        public static bool IsOnVacation(ProtoCrewMember kerbal)
        {
            EnsureInit();
            if (_asm == null || kerbal == null) return false;

            if (_vacStatus.HasValue && kerbal.rosterStatus == _vacStatus.Value)
                return true;

            // fallback: no known vacation status => assume not on vacation
            return false;
        }

        public static bool TryGetVacationUntilByName(string kerbalName, out double untilUT)
        {
            untilUT = 0;
            EnsureInit();
            if (_asm == null || string.IsNullOrEmpty(kerbalName)) return false;

            try
            {
                if (_extDataSet == null) TryInitRosterExtAccess();
                if (_extDataSet == null) return false;

                double nowUT = Planetarium.GetUniversalTime();

                foreach (var ext in _extDataSet)
                {
                    if (ext == null) continue;

                    var extType = ext.GetType();

                    // Match on ProtoReference
                    var protoRef =
                        extType.GetField("ProtoReference", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ext)
                        ?? extType.GetProperty("ProtoReference", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ext, null);

                    if (protoRef is ProtoCrewMember pcm && pcm.name == kerbalName)
                    {
                        // Find a field/property that looks like vacation end
                        // We'll log the candidates once for debugging.
                        double bestFuture = 0;

                        foreach (var f in extType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (f.FieldType != typeof(double) && f.FieldType != typeof(float)) continue;

                            string n = f.Name.ToLowerInvariant();
                            double v = Convert.ToDouble(f.GetValue(ext));

                            if (v <= 0) continue;

                            // prefer things that look like vacation end
                            bool nameMatch = n.Contains("vac") && (n.Contains("end") || n.Contains("until") || n.Contains("return") || n.Contains("next"));
                            if (nameMatch) { untilUT = v; return true; }

                            // fallback: future-looking timestamp
                            if (v > nowUT && v > bestFuture) bestFuture = v;
                        }

                        foreach (var p in extType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (!p.CanRead) continue;
                            if (p.PropertyType != typeof(double) && p.PropertyType != typeof(float)) continue;

                            string n = p.Name.ToLowerInvariant();
                            double v = Convert.ToDouble(p.GetValue(ext, null));

                            if (v <= 0) continue;

                            bool nameMatch = n.Contains("vac") && (n.Contains("end") || n.Contains("until") || n.Contains("return") || n.Contains("next"));
                            if (nameMatch) { untilUT = v; return true; }

                            if (v > nowUT && v > bestFuture) bestFuture = v;
                        }

                        if (bestFuture > 0)
                        {
                            untilUT = bestFuture;
                            return true;
                        }

                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] CrewRandRAdapter.TryGetVacationUntilByName failed: {ex}");
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

            // Must be on vacation to have a meaningful "until"
            if (!IsOnVacation(kerbal)) return false;

            try
            {
                // Load ext data set lazily
                if (_extDataSet == null) TryInitRosterExtAccess();

                if (_extDataSet == null) return false;

                // Find the KerbalExtData entry matching this kerbal
                foreach (var ext in _extDataSet)
                {
                    if (ext == null) continue;

                    // ext.ProtoReference (field or property) points to the ProtoCrewMember
                    var extType = ext.GetType();
                    var protoRef =
                        extType.GetField("ProtoReference", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ext)
                        ?? extType.GetProperty("ProtoReference", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ext, null);

                    if (protoRef is ProtoCrewMember pcm && pcm.name == kerbal.name)
                    {
                        // Heuristic: look for any double/float field/property with "vac" + ("end"/"until"/"return")
                        // If not found, fall back to the largest double-ish that is in the future.
                        double best = 0;

                        foreach (var f in extType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (f.FieldType != typeof(double) && f.FieldType != typeof(float)) continue;

                            string n = f.Name.ToLowerInvariant();
                            double v = Convert.ToDouble(f.GetValue(ext));

                            if (v <= 0) continue;

                            bool nameMatch = n.Contains("vac") && (n.Contains("end") || n.Contains("until") || n.Contains("return"));
                            if (nameMatch) { untilUT = v; return true; }

                            if (v > best) best = v;
                        }

                        foreach (var p in extType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (!p.CanRead) continue;
                            if (p.PropertyType != typeof(double) && p.PropertyType != typeof(float)) continue;

                            string n = p.Name.ToLowerInvariant();
                            double v = Convert.ToDouble(p.GetValue(ext, null));

                            if (v <= 0) continue;

                            bool nameMatch = n.Contains("vac") && (n.Contains("end") || n.Contains("until") || n.Contains("return"));
                            if (nameMatch) { untilUT = v; return true; }

                            if (v > best) best = v;
                        }

                        // Fallback: if we found a plausible future-ish time, use it
                        if (best > 0)
                        {
                            untilUT = best;
                            return true;
                        }

                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] CrewRandRAdapter.TryGetVacationUntil failed: {ex}");
            }

            return false;
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

                // Find ROSTERSTATUS_VACATION
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

        private static void TryInitRosterExtAccess()
        {
            try
            {
                if (_asm == null) return;

                // Find CrewRandRRoster type (name contains it)
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
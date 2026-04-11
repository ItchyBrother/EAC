// EAC - Enhanced Astronaut Complex - Mod.FlightTracker.cs
// Partial class: FlightTracker mod reflection bridge.
// Discovers and queries the FlightTracker mod API at runtime so EAC can
// display flight counts and mission hours from FlightTracker when installed.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    public partial class RosterRotationKSCUI
    {
        // ── FlightTracker API (method-based) ───────────────────────────────────
        private static bool         _searchedFlightTrackerFlightsMethod;
        private static MethodInfo   _cachedFlightTrackerFlightsMethod;
        private static Type         _cachedFlightTrackerApiType;
        private static FieldInfo    _cachedFlightTrackerApiInstanceField;
        private static PropertyInfo _cachedFlightTrackerApiInstanceProperty;

        // ── FlightTracker store (field-based fallback) ─────────────────────────
        private static bool         _searchedFlightTrackerStore;
        private static Type         _cachedFlightTrackerStoreType;
        private static FieldInfo    _cachedFlightTrackerStoreInstanceField;
        private static PropertyInfo _cachedFlightTrackerStoreInstanceProperty;
        private static FieldInfo    _cachedFlightTrackerFlightsField;

        // ── FlightTracker mission hours ────────────────────────────────────────
        private static bool       _searchedFlightTrackerMissionHoursMethod;
        private static MethodInfo _cachedFlightTrackerMissionHoursMethod;

        // ── One-time sync flag ─────────────────────────────────────────────────
        private bool _flightTrackerSyncExecutedThisSession;

        // ── API discovery ──────────────────────────────────────────────────────

        private static MethodInfo GetFlightTrackerFlightsMethod()
        {
            if (_searchedFlightTrackerFlightsMethod) return _cachedFlightTrackerFlightsMethod;
            _searchedFlightTrackerFlightsMethod = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        try
                        {
                            var mi = t.GetMethod("GetNumberOfFlights",
                                BindingFlags.Public | BindingFlags.Instance,
                                null, new[] { typeof(string) }, null);
                            if (mi != null)
                            {
                                _cachedFlightTrackerFlightsMethod      = mi;
                                _cachedFlightTrackerApiType            = t;
                                _cachedFlightTrackerApiInstanceProperty = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                _cachedFlightTrackerApiInstanceField    = t.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                RRLog.Info($"[EAC] FlightTracker API detected: {t.FullName}.GetNumberOfFlights(string) [instance]");
                                return _cachedFlightTrackerFlightsMethod;
                            }

                            mi = t.GetMethod("GetNumberOfFlights",
                                BindingFlags.Public | BindingFlags.Static,
                                null, new[] { typeof(string) }, null);
                            if (mi != null)
                            {
                                _cachedFlightTrackerFlightsMethod = mi;
                                _cachedFlightTrackerApiType       = t;
                                RRLog.Info($"[EAC] FlightTracker API detected: {t.FullName}.GetNumberOfFlights(string) [static]");
                                return _cachedFlightTrackerFlightsMethod;
                            }
                        }
                        catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.GetFlightsMethod:" + (t?.FullName ?? "?"), "Suppressed", ex); }
                    }
                }
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.GetFlightsMethodOuter", "Suppressed", ex); }

            return null;
        }

        private static object GetFlightTrackerApiInstance()
        {
            try
            {
                if (_cachedFlightTrackerApiInstanceProperty != null)
                    return _cachedFlightTrackerApiInstanceProperty.GetValue(null, null);
                if (_cachedFlightTrackerApiInstanceField != null)
                    return _cachedFlightTrackerApiInstanceField.GetValue(null);
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.GetApiInstance", "Suppressed", ex); }
            return null;
        }

        private static bool ResolveFlightTrackerStore()
        {
            if (_searchedFlightTrackerStore)
                return _cachedFlightTrackerStoreType != null && _cachedFlightTrackerFlightsField != null;
            _searchedFlightTrackerStore = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;

                        FieldInfo flightsField = null;
                        try { flightsField = t.GetField("Flights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.ResolveStore.Field:" + (t?.FullName ?? "?"), "Suppressed", ex); }
                        if (flightsField == null) continue;

                        FieldInfo    instanceField = null;
                        PropertyInfo instanceProp  = null;
                        try { instanceField = t.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.ResolveStore.InstanceField", "Suppressed", ex); }
                        if (instanceField == null)
                        {
                            try { instanceProp = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                            catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.ResolveStore.InstanceProp", "Suppressed", ex); }
                        }
                        if (instanceField == null && instanceProp == null) continue;

                        _cachedFlightTrackerStoreType            = t;
                        _cachedFlightTrackerStoreInstanceField   = instanceField;
                        _cachedFlightTrackerStoreInstanceProperty = instanceProp;
                        _cachedFlightTrackerFlightsField         = flightsField;
                        RRLog.Info($"[EAC] FlightTracker store detected: {t.FullName}.Flights");
                        return true;
                    }
                }
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.ResolveStoreOuter", "Suppressed", ex); }

            return false;
        }

        private static bool TryGetFlightTrackerFlightsDictionary(out IDictionary dict, out string reason)
        {
            dict   = null;
            reason = null;

            try
            {
                if (!ResolveFlightTrackerStore()) { reason = "store type not found"; return false; }

                object instance = null;
                if (_cachedFlightTrackerStoreInstanceProperty != null)
                    instance = _cachedFlightTrackerStoreInstanceProperty.GetValue(null, null);
                else if (_cachedFlightTrackerStoreInstanceField != null)
                    instance = _cachedFlightTrackerStoreInstanceField.GetValue(null);

                if (instance == null) { reason = "store instance unavailable"; return false; }

                dict = _cachedFlightTrackerFlightsField.GetValue(instance) as IDictionary;
                if (dict == null) { reason = "Flights dictionary unavailable"; return false; }

                return true;
            }
            catch (Exception ex) { reason = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        private static bool TryGetFlightTrackerFlights(string kerbalName, out int flights, out string reason)
        {
            flights = 0;
            reason  = null;
            if (string.IsNullOrEmpty(kerbalName)) { reason = "kerbal name missing"; return false; }

            try
            {
                var api = GetFlightTrackerFlightsMethod();
                if (api != null)
                {
                    if (!api.IsStatic)
                    {
                        var target = GetFlightTrackerApiInstance();
                        if (target == null)
                        {
                            reason = "FlightTracker API instance unavailable";
                        }
                        else
                        {
                            object raw = api.Invoke(target, new object[] { kerbalName });
                            if (raw != null) { flights = Math.Max(0, Convert.ToInt32(raw)); reason = "api"; return true; }
                            reason = "FlightTracker API returned null";
                        }
                    }
                    else
                    {
                        object raw = api.Invoke(null, new object[] { kerbalName });
                        if (raw != null) { flights = Math.Max(0, Convert.ToInt32(raw)); reason = "api"; return true; }
                        reason = "FlightTracker API returned null";
                    }
                }
                else
                {
                    reason = "FlightTracker API not found";
                }
            }
            catch (Exception ex) { reason = ex.GetType().Name + ": " + ex.Message; }

            IDictionary dict;
            string dictReason;
            if (TryGetFlightTrackerFlightsDictionary(out dict, out dictReason))
            {
                try
                {
                    object raw = dict.Contains(kerbalName) ? dict[kerbalName] : 0;
                    flights = Math.Max(0, Convert.ToInt32(raw));
                    reason  = "store";
                    return true;
                }
                catch (Exception ex) { reason = ex.GetType().Name + ": " + ex.Message; return false; }
            }

            if (!string.IsNullOrEmpty(dictReason))
                reason = string.IsNullOrEmpty(reason) ? dictReason : (reason + "; " + dictReason);
            return false;
        }

        private static bool TrySetFlightTrackerFlights(string kerbalName, int newValue, out int previousValue, out string reason)
        {
            previousValue = 0;
            reason        = null;
            IDictionary dict;
            if (!TryGetFlightTrackerFlightsDictionary(out dict, out reason)) return false;

            try
            {
                previousValue = dict.Contains(kerbalName) ? Math.Max(0, Convert.ToInt32(dict[kerbalName])) : 0;
                dict[kerbalName] = Math.Max(0, newValue);
                return true;
            }
            catch (Exception ex) { reason = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        private static void LogDisplayedFlightsChoice(ProtoCrewMember k, int eacFlights, bool ftAvailable, int ftFlights, string ftReason, string usingSource)
        {
            if (!RRLog.VerboseEnabled || k == null || string.IsNullOrEmpty(k.name)) return;
            string ftText = ftAvailable ? ftFlights.ToString() : ("unavailable: " + (string.IsNullOrEmpty(ftReason) ? "unknown" : ftReason));
            RRLog.VerboseOnce(
                "DisplayedFlights:" + k.name + ":" + eacFlights + ":" + ftText + ":" + usingSource,
                $"[EAC] Flight comparison for {k.name}: EAC={eacFlights}, FlightTracker={ftText}; using={usingSource}");
        }

        internal static int GetDisplayedFlights(ProtoCrewMember k, RosterRotationState.KerbalRecord r)
        {
            int fallback = Math.Max(0, r?.Flights ?? 0);
            if (k == null || string.IsNullOrEmpty(k.name)) return fallback;

            try
            {
                int ftFlights; string ftReason;
                if (TryGetFlightTrackerFlights(k.name, out ftFlights, out ftReason))
                {
                    LogDisplayedFlightsChoice(k, fallback, true, ftFlights, ftReason, "FlightTracker");
                    return Math.Max(0, ftFlights);
                }
                LogDisplayedFlightsChoice(k, fallback, false, 0, ftReason, "EAC");
                return fallback;
            }
            catch (Exception ex)
            {
                RRLog.Warn($"FlightTracker lookup failed for {k?.name}: {ex.Message}");
                LogDisplayedFlightsChoice(k, fallback, false, 0, ex.Message, "EAC");
                return fallback;
            }
        }

        // ── One-time sync ──────────────────────────────────────────────────────

        internal void MaybeRunPendingFlightTrackerSync()
        {
            if (_flightTrackerSyncExecutedThisSession) return;
            if (!RosterRotationState.SyncFlightTrackerFromEacOnce) return;
            _flightTrackerSyncExecutedThisSession = true;
            RunFlightTrackerSyncFromEacOnce();
        }

        private void RunFlightTrackerSyncFromEacOnce()
        {
            int compared = 0, updated = 0, skipped = 0;
            RRLog.Verbose("[EAC] One-time FlightTracker sync requested by save flag. Starting EAC -> FlightTracker comparison.");

            try
            {
                foreach (var kvp in RosterRotationState.Records)
                {
                    string kerbalName = kvp.Key;
                    var    rec        = kvp.Value;
                    if (string.IsNullOrEmpty(kerbalName) || rec == null) continue;

                    int    eacFlights = Math.Max(0, rec.Flights);
                    int    ftFlights; string ftReason;
                    bool   ftAvailable = TryGetFlightTrackerFlights(kerbalName, out ftFlights, out ftReason);
                    compared++;

                    if (!ftAvailable)
                    {
                        RRLog.Verbose($"[EAC] FlightTracker sync skipped for {kerbalName}: EAC={eacFlights}, FlightTracker unavailable ({ftReason ?? "unknown"}).");
                        skipped++;
                        continue;
                    }

                    RRLog.Verbose($"[EAC] FlightTracker sync compare for {kerbalName}: EAC={eacFlights}, FlightTracker={ftFlights}.");
                    if (eacFlights <= ftFlights) continue;

                    int prev; string setReason;
                    if (TrySetFlightTrackerFlights(kerbalName, eacFlights, out prev, out setReason))
                    {
                        updated++;
                        RRLog.Verbose($"[EAC] Synced FlightTracker flights for {kerbalName}: FlightTracker={prev} -> EAC={eacFlights}.");
                    }
                    else
                    {
                        skipped++;
                        RRLog.Verbose($"[EAC] Failed to sync FlightTracker flights for {kerbalName}: EAC={eacFlights}, FlightTracker={ftFlights}, reason={setReason ?? "unknown"}.");
                    }
                }
            }
            finally
            {
                RosterRotationState.SyncFlightTrackerFromEacOnce = false;
                RRLog.Verbose($"[EAC] One-time FlightTracker sync finished. Compared={compared}, Updated={updated}, Skipped={skipped}. Save flag reset to False.");
                SaveScheduler.RequestImmediateSave("FlightTracker sync");
                RRLog.Verbose("[EAC] Saved persistent.sfs after one-time FlightTracker sync.");
            }
        }

        // ── Mission hours ──────────────────────────────────────────────────────

        private static MethodInfo GetFlightTrackerMissionHoursMethod()
        {
            if (_searchedFlightTrackerMissionHoursMethod) return _cachedFlightTrackerMissionHoursMethod;
            _searchedFlightTrackerMissionHoursMethod = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        try
                        {
                            var mi = t.GetMethod("GetRecordedMissionTimeHours",   BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                                  ?? t.GetMethod("GetRecordedMissionTimeHours",   BindingFlags.Public | BindingFlags.Static,   null, new[] { typeof(string) }, null)
                                  ?? t.GetMethod("GetRecordedMissionTimeSeconds", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                                  ?? t.GetMethod("GetRecordedMissionTimeSeconds", BindingFlags.Public | BindingFlags.Static,   null, new[] { typeof(string) }, null);
                            if (mi == null) continue;

                            _cachedFlightTrackerMissionHoursMethod = mi;
                            if (_cachedFlightTrackerApiType == null)
                            {
                                _cachedFlightTrackerApiType            = t;
                                _cachedFlightTrackerApiInstanceProperty = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                _cachedFlightTrackerApiInstanceField    = t.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            }
                            RRLog.Info($"[EAC] FlightTracker time API detected: {t.FullName}.{mi.Name}(string) [{(mi.IsStatic ? "static" : "instance")}]");
                            return _cachedFlightTrackerMissionHoursMethod;
                        }
                        catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.GetHoursMethod:" + (t?.FullName ?? "?"), "Suppressed", ex); }
                    }
                }
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("FT.GetHoursMethodOuter", "Suppressed", ex); }

            return null;
        }

        internal static bool TryGetFlightTrackerRecordedHours(string kerbalName, out double hours, out string reason)
        {
            hours  = 0.0;
            reason = null;
            if (string.IsNullOrEmpty(kerbalName)) { reason = "kerbal name missing"; return false; }

            try
            {
                var api = GetFlightTrackerMissionHoursMethod();
                if (api == null) { reason = "FlightTracker time API not found"; return false; }

                object target = null;
                if (!api.IsStatic)
                {
                    target = GetFlightTrackerApiInstance();
                    if (target == null) { reason = "FlightTracker API instance unavailable"; return false; }
                }

                object raw = api.Invoke(target, new object[] { kerbalName });
                if (raw == null) { reason = "FlightTracker time API returned null"; return false; }

                double value = Convert.ToDouble(raw);
                if ((api.Name ?? string.Empty).IndexOf("Seconds", StringComparison.OrdinalIgnoreCase) >= 0)
                    value /= 3600.0;

                hours  = Math.Max(0.0, value);
                reason = "api";
                return true;
            }
            catch (Exception ex) { reason = ex.GetType().Name + ": " + ex.Message; return false; }
        }
    }
}

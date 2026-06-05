// EAC - DeepFreeze compatibility: small reflection bridge.
//
// This deliberately avoids a hard DeepFreeze reference and keeps the integration
// isolated.  It reads the same public DeepFreeze API members documented by the
// DeepFreezeUpdated wrapper: Instance, APIReady, FrozenKerbals, and DFIDeathFatal.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RosterRotation.DeepFreeze
{
    internal static class EACDeepFreezeBridge
    {
        private const float InitRetrySeconds = 10f;
        private const float RefreshThrottleSeconds = 0.25f;
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        private static readonly Dictionary<string, EACDeepFreezeKerbalInfo> _frozenKerbals =
            new Dictionary<string, EACDeepFreezeKerbalInfo>(StringComparer.Ordinal);

        private static float _nextInitTryRT;
        private static float _lastRefreshRT = -10f;
        private static Type _deepFreezeType;
        private static object _deepFreezeInstance;
        private static FieldInfo _instanceField;
        private static FieldInfo _apiReadyField;
        private static MethodInfo _getFrozenKerbalsMethod;
        private static MethodInfo _getFrozenKerbalsListMethod;
        private static MethodInfo _getDeathFatalMethod;
        private static bool _apiReady;
        private static bool _installed;
        private static bool _loggedReady;
        private static bool _loggedNotInstalled;
        private static int _lastLoggedFrozenCount = -1;

        internal static bool Installed => _installed || _deepFreezeType != null;
        internal static bool APIReady => _apiReady;
        internal static bool FatalOptionEnabled { get; private set; }

        internal static IReadOnlyDictionary<string, EACDeepFreezeKerbalInfo> FrozenKerbals => _frozenKerbals;

        internal static void Update(bool force = false)
        {
            EnsureInitialized();

            float nowRT = Time.realtimeSinceStartup;
            if (!force && nowRT - _lastRefreshRT < RefreshThrottleSeconds)
                return;

            _lastRefreshRT = nowRT;
            RefreshState();
        }

        internal static bool IsFrozen(ProtoCrewMember kerbal)
        {
            return kerbal != null && IsFrozen(kerbal.name);
        }

        internal static bool IsFrozen(string kerbalName)
        {
            return !string.IsNullOrEmpty(kerbalName) && _frozenKerbals.ContainsKey(kerbalName);
        }

        internal static bool TryGetFrozenInfo(string kerbalName, out EACDeepFreezeKerbalInfo info)
        {
            if (!string.IsNullOrEmpty(kerbalName) && _frozenKerbals.TryGetValue(kerbalName, out info))
                return true;

            info = null;
            return false;
        }

        internal static double GetFrozenLastUpdateUT(string kerbalName, double fallbackUT)
        {
            if (TryGetFrozenInfo(kerbalName, out var info) && info != null && info.LastUpdateUT > 0 && info.LastUpdateUT <= fallbackUT)
                return info.LastUpdateUT;

            return fallbackUT;
        }


        internal static bool HasDeepFreezerOnActiveVessel()
        {
            try { return HasDeepFreezerOnVessel(FlightGlobals.ActiveVessel); }
            catch { return false; }
        }

        internal static bool HasDeepFreezerOnVessel(Vessel vessel)
        {
            if (vessel == null || vessel.parts == null) return false;

            try
            {
                foreach (Part part in vessel.parts)
                {
                    if (part == null || part.Modules == null) continue;
                    for (int i = 0; i < part.Modules.Count; i++)
                    {
                        PartModule module = null;
                        try { module = part.Modules[i]; } catch { continue; }
                        if (module == null) continue;

                        Type type = module.GetType();
                        string fullName = type != null ? type.FullName : null;
                        string name = type != null ? type.Name : null;
                        if (string.Equals(fullName, "DF.DeepFreezer", StringComparison.Ordinal) ||
                            string.Equals(name, "DeepFreezer", StringComparison.Ordinal))
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static void EnsureInitialized()
        {
            // DeepFreeze recreates its ScenarioModule instance during scene changes.
            // Do not keep using a stale instance captured in a previous scene.
            if (_deepFreezeType != null)
            {
                RefreshInstanceReference();
                return;
            }

            if (Time.realtimeSinceStartup < _nextInitTryRT) return;
            _nextInitTryRT = Time.realtimeSinceStartup + InitRetrySeconds;

            try
            {
                _deepFreezeType = FindLoadedType("DF.DeepFreeze");
                _installed = _deepFreezeType != null;

                if (_deepFreezeType == null)
                {
                    if (!_loggedNotInstalled && RRLog.VerboseEnabled)
                    {
                        _loggedNotInstalled = true;
                        RRLog.Verbose("[EAC] DeepFreeze not detected; compatibility bridge idle.");
                    }
                    return;
                }

                _instanceField = _deepFreezeType.GetField("Instance", PublicStatic);
                _apiReadyField = _deepFreezeType.GetField("APIReady", PublicStatic);
                _getFrozenKerbalsMethod = _deepFreezeType.GetMethod("get_FrozenKerbals", PublicInstance);
                _getFrozenKerbalsListMethod = _deepFreezeType.GetMethod("get_FrozenKerbalsList", PublicInstance);
                _getDeathFatalMethod = _deepFreezeType.GetMethod("get_DFIDeathFatal", PublicInstance);

                RefreshInstanceReference();
                if (_deepFreezeInstance == null) return;

                _lastRefreshRT = Time.realtimeSinceStartup;
                RefreshState();
            }
            catch (Exception ex)
            {
                _deepFreezeInstance = null;
                _apiReady = false;
                RRLog.VerboseExceptionOnce("EAC.DeepFreeze.Init", "DeepFreeze bridge initialization failed", ex);
            }
        }

        private static void RefreshInstanceReference()
        {
            try
            {
                object current = _instanceField != null ? _instanceField.GetValue(null) : null;
                if (ReferenceEquals(current, _deepFreezeInstance)) return;

                _deepFreezeInstance = current;
                _apiReady = false;
                _frozenKerbals.Clear();
                _lastLoggedFrozenCount = -1;
                _loggedReady = false;

                if (_deepFreezeInstance != null)
                    RRLog.Verbose("[EAC] DeepFreeze instance reference refreshed for scene=" + HighLogic.LoadedScene);
            }
            catch (Exception ex)
            {
                _deepFreezeInstance = null;
                _apiReady = false;
                RRLog.VerboseExceptionOnce("EAC.DeepFreeze.RefreshInstance", "DeepFreeze instance refresh failed", ex);
            }
        }

        private static void RefreshState()
        {
            RefreshInstanceReference();

            if (_deepFreezeInstance == null)
            {
                _apiReady = false;
                FatalOptionEnabled = false;
                _frozenKerbals.Clear();
                return;
            }

            try
            {
                _apiReady = _apiReadyField != null && Convert.ToBoolean(_apiReadyField.GetValue(null));
                FatalOptionEnabled = ReadBool(_getDeathFatalMethod, _deepFreezeInstance, false);

                if (!_apiReady)
                {
                    _frozenKerbals.Clear();
                    return;
                }

                if (!_loggedReady)
                {
                    _loggedReady = true;
                    RRLog.Info("[EAC] DeepFreeze compatibility bridge initialized.");
                }

                bool refreshed = false;
                if (_getFrozenKerbalsMethod != null)
                {
                    var rawFrozen = _getFrozenKerbalsMethod.Invoke(_deepFreezeInstance, null) as IDictionary;
                    RefreshFrozenKerbalCache(rawFrozen);
                    refreshed = rawFrozen != null;
                }

                // Some DeepFreeze builds expose the list accessor more reliably
                // during flight-scene churn.  Use it as a fallback/secondary source.
                if (_getFrozenKerbalsListMethod != null && (!refreshed || _frozenKerbals.Count == 0))
                {
                    object rawList = _getFrozenKerbalsListMethod.Invoke(_deepFreezeInstance, null);
                    RefreshFrozenKerbalCacheFromEnumerable(rawList as IEnumerable);
                }

                if (RRLog.VerboseEnabled && _lastLoggedFrozenCount != _frozenKerbals.Count)
                {
                    _lastLoggedFrozenCount = _frozenKerbals.Count;
                    RRLog.Verbose("[EAC] DeepFreeze frozen cache count=" + _frozenKerbals.Count);
                }
            }
            catch (Exception ex)
            {
                _apiReady = false;
                FatalOptionEnabled = false;
                _frozenKerbals.Clear();
                RRLog.VerboseExceptionOnce("EAC.DeepFreeze.Refresh", "DeepFreeze bridge refresh failed", ex);
            }
        }

        private static void RefreshFrozenKerbalCache(IDictionary rawFrozen)
        {
            _frozenKerbals.Clear();
            if (rawFrozen == null) return;

            foreach (DictionaryEntry entry in rawFrozen)
            {
                string name = entry.Key as string;
                if (string.IsNullOrEmpty(name) || entry.Value == null) continue;

                _frozenKerbals[name] = SnapshotKerbalInfo(name, entry.Value);
            }
        }


        private static void RefreshFrozenKerbalCacheFromEnumerable(IEnumerable rawList)
        {
            if (rawList == null) return;

            foreach (object item in rawList)
            {
                if (item == null) continue;

                string name = ReadMember(item, "Key") as string;
                object rawInfo = ReadMember(item, "Value");
                if (string.IsNullOrEmpty(name) || rawInfo == null) continue;

                _frozenKerbals[name] = SnapshotKerbalInfo(name, rawInfo);
            }
        }

        private static EACDeepFreezeKerbalInfo SnapshotKerbalInfo(string name, object rawInfo)
        {
            double lastUpdate = ReadDouble(rawInfo, "lastUpdate", 0);
            ProtoCrewMember.RosterStatus? status = ReadEnum<ProtoCrewMember.RosterStatus>(rawInfo, "status");
            ProtoCrewMember.KerbalType? type = ReadEnum<ProtoCrewMember.KerbalType>(rawInfo, "type");
            Guid vesselID = ReadGuid(rawInfo, "vesselID", Guid.Empty);
            string vesselName = ReadString(rawInfo, "vesselName", string.Empty);
            uint partID = ReadUInt(rawInfo, "partID", 0);
            int seatIdx = ReadInt(rawInfo, "seatIdx", -1);
            string seatName = ReadString(rawInfo, "seatName", string.Empty);
            string experienceTraitName = ReadString(rawInfo, "experienceTraitName", string.Empty);

            return new EACDeepFreezeKerbalInfo(
                name, lastUpdate, status, type, vesselID, vesselName, partID, seatIdx, seatName, experienceTraitName);
        }

        private static Type FindLoadedType(string fullName)
        {
            Type found = null;
            try
            {
                AssemblyLoader.loadedAssemblies.TypeOperation(t =>
                {
                    if (t != null && string.Equals(t.FullName, fullName, StringComparison.Ordinal))
                        found = t;
                });
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.DeepFreeze.FindType", "DeepFreeze type scan failed", ex);
            }

            return found;
        }

        private static bool ReadBool(MethodInfo method, object owner, bool fallback)
        {
            if (method == null || owner == null) return fallback;
            try { return Convert.ToBoolean(method.Invoke(owner, null)); }
            catch { return fallback; }
        }

        private static string ReadString(object owner, string name, string fallback)
        {
            object value = ReadMember(owner, name);
            return value != null ? value.ToString() : fallback;
        }

        private static int ReadInt(object owner, string name, int fallback)
        {
            object value = ReadMember(owner, name);
            if (value == null) return fallback;
            try { return Convert.ToInt32(value); }
            catch { return fallback; }
        }

        private static uint ReadUInt(object owner, string name, uint fallback)
        {
            object value = ReadMember(owner, name);
            if (value == null) return fallback;
            try { return Convert.ToUInt32(value); }
            catch { return fallback; }
        }

        private static double ReadDouble(object owner, string name, double fallback)
        {
            object value = ReadMember(owner, name);
            if (value == null) return fallback;
            try { return Convert.ToDouble(value); }
            catch { return fallback; }
        }

        private static Guid ReadGuid(object owner, string name, Guid fallback)
        {
            object value = ReadMember(owner, name);
            if (value == null) return fallback;
            if (value is Guid guid) return guid;
            try { return new Guid(value.ToString()); }
            catch { return fallback; }
        }

        private static TEnum? ReadEnum<TEnum>(object owner, string name) where TEnum : struct
        {
            object value = ReadMember(owner, name);
            if (value == null) return null;

            try
            {
                if (value is TEnum already) return already;
                if (value is int i) return (TEnum)Enum.ToObject(typeof(TEnum), i);
                if (value is uint u) return (TEnum)Enum.ToObject(typeof(TEnum), u);
                if (value is string s && Enum.IsDefined(typeof(TEnum), s))
                    return (TEnum)Enum.Parse(typeof(TEnum), s);

                return (TEnum)Enum.ToObject(typeof(TEnum), Convert.ToInt32(value));
            }
            catch
            {
                return null;
            }
        }

        private static object ReadMember(object owner, string name)
        {
            if (owner == null || string.IsNullOrEmpty(name)) return null;
            var type = owner.GetType();

            try
            {
                var field = type.GetField(name, PublicInstance);
                if (field != null) return field.GetValue(owner);

                var prop = type.GetProperty(name, PublicInstance);
                if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(owner, null);
            }
            catch { /* ignored; reflection compatibility layer */ }

            return null;
        }
    }
}

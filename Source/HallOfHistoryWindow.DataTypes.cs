// EAC - HallOfHistoryWindow.DataTypes
// Extracted data cache types and FlightTracker shim for Hall of History.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using KSP;

namespace RosterRotation
{
    public partial class HallOfHistoryWindow
    {
        private class HallOfHistoryCache
        {
            public readonly List<MemorialEntry> Memorials = new List<MemorialEntry>();
            public List<MilestoneEntry> Milestones = new List<MilestoneEntry>();
            public string LastLoadError;
            public string LastPath;
            public DateTime LastWriteUtc;
            public string DataSummary = string.Empty;

            public void Clear()
            {
                Memorials.Clear();
                Milestones.Clear();
                LastLoadError = null;
                DataSummary = string.Empty;
            }

            public bool IsFreshFor(string path, DateTime stamp)
            {
                return string.Equals(LastPath, path, StringComparison.OrdinalIgnoreCase) && LastWriteUtc == stamp;
            }
        }

        private class MemorialEntry
        {
            public string Name;
            public string Role;
            public string StatusText;
            public string StatusDetailText;
            public string ServiceTag;
            public string CauseText;
            public string RecordedDateText;
            public int Flights;
            public int WorldFirsts;
            public string HoursText;
            public int Age;
            public string AgeText;
            public int ExperienceLevel;
            public string ExperienceStars;
            public bool IsVeteran;
            public int RetiredExperienceLevel = -1;
            public string RetiredExperienceStars;
            public float Courage01;
            public float Stupidity01;
            public string CourageText;
            public string StupidityText;
            public string FlagUrl;
            public string Citation;
            public string Notes;
        }

        private class MilestoneEntry
        {
            public string Key;
            public string Title;
            public string Subtitle;
            public string Description;
            public string DateText;
            public string TimeText;
            public string RecordValueText;
            public string SecondaryPillText;
            public string DayGroupLabel;
            public double SortValue;
            public string BodyText;
            public string VesselText;
            public string CrewText;
            public string[] CrewNames;
            public int CrewCount;
            public string CrewSummaryText;
            public string KindText;
            public string CategoryText;
            public string SourcePathText;
        }

        private class ProtoCrewSnapshot
        {
            public string Name;
            public string TraitTitle;
            public bool IsDead;
            public int Flights = -1;
            public int ExperienceLevel = -1;
            public bool IsVeteran;
            public float Courage = float.NaN;
            public float Stupidity = float.NaN;
            public double UT = -1d;
            public string FlagUrl;

            public int ScoreForCompleteness()
            {
                int score = 0;
                if (!string.IsNullOrEmpty(Name)) score += 2;
                if (!string.IsNullOrEmpty(TraitTitle)) score += 2;
                if (Flights >= 0) score++;
                if (ExperienceLevel >= 0) score++;
                if (IsVeteran) score++;
                if (!float.IsNaN(Courage)) score++;
                if (!float.IsNaN(Stupidity)) score++;
                if (UT >= 0d) score++;
                if (!string.IsNullOrEmpty(FlagUrl)) score++;
                if (IsDead) score++;
                return score;
            }

            public static ProtoCrewSnapshot FromProto(ProtoCrewMember pcm)
            {
                if (pcm == null) return null;
                return new ProtoCrewSnapshot
                {
                    Name = pcm.name,
                    TraitTitle = pcm.experienceTrait != null ? pcm.experienceTrait.Title : pcm.trait,
                    IsDead = pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing,
                    Flights = ReadProtoFlights(pcm),
                    ExperienceLevel = ReadProtoExperienceLevel(pcm),
                    Courage = pcm.courage,
                    Stupidity = pcm.stupidity,
                    FlagUrl = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flagURL : null,
                    IsVeteran = ReadProtoVeteran(pcm)
                };
            }

            public static ProtoCrewSnapshot FromConfig(ConfigNode kerbal)
            {
                if (kerbal == null) return null;

                var s = new ProtoCrewSnapshot();
                s.Name = ReadValue(kerbal, "name");
                s.TraitTitle = ReadValue(kerbal, "trait", "job", "type");

                string status = ReadValue(kerbal, "rosterStatus", "state", "status");
                s.IsDead = !string.IsNullOrEmpty(status) &&
                    (status.Equals("Dead", StringComparison.OrdinalIgnoreCase) ||
                     status.Equals("Missing", StringComparison.OrdinalIgnoreCase));

                s.Flights = CountRosterFlights(kerbal);
                s.ExperienceLevel = ResolveRosterExperienceLevel(kerbal);
                s.IsVeteran = ReadBool(kerbal, false, "veteran", "Veteran", "isVeteran", "IsVeteran");

                float f;
                if (float.TryParse(ReadValue(kerbal, "courage", "brave"), NumberStyles.Any, CultureInfo.InvariantCulture, out f))
                    s.Courage = f;
                if (float.TryParse(ReadValue(kerbal, "stupidity", "dumb"), NumberStyles.Any, CultureInfo.InvariantCulture, out f))
                    s.Stupidity = f;

                double d;
                if (double.TryParse(ReadValue(kerbal, "UT", "ut", "time", "ToD"), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    s.UT = d;

                s.FlagUrl = ReadValue(kerbal, "flagURL", "flagUrl", "flag");
                return s;
            }
        }

        private class EacRecordSnapshot
        {
            public string Name;
            public string TraitTitle;
            public bool IsDead;
            public bool IsMia;
            public string ServiceTag;
            public string CauseOfDeath;
            public int Flights = -1;
            public double MissionHours = -1d;
            public double BirthUT = -1d;
            public double DeathUT = -1d;
            public double MissionStartUT = -1d;
            public double LastSeenUT = -1d;
            public bool Retired;
            public double RetiredUT = -1d;
            public int LastAgedYears = -1;
            public float Courage = float.NaN;
            public float Stupidity = float.NaN;
            public string FlagUrl;
            public string Citation;
            public string Notes;
            public string LastMission;
            public string SourceSummary;
            public int ExperienceAtRetire = -1;

            public int ScoreForCompleteness()
            {
                int score = 0;
                if (!string.IsNullOrEmpty(Name)) score += 2;
                if (!string.IsNullOrEmpty(TraitTitle)) score += 2;
                if (!string.IsNullOrEmpty(ServiceTag)) score++;
                if (!string.IsNullOrEmpty(CauseOfDeath)) score += 2;
                if (Flights >= 0) score++;
                if (MissionHours >= 0) score++;
                if (BirthUT >= 0) score++;
                if (DeathUT >= 0) score++;
                if (MissionStartUT >= 0) score++;
                if (LastSeenUT >= 0) score++;
                if (Retired) score++;
                if (RetiredUT > 0d) score++;
                if (LastAgedYears >= 0) score++;
                if (!float.IsNaN(Courage)) score++;
                if (!float.IsNaN(Stupidity)) score++;
                if (!string.IsNullOrEmpty(FlagUrl)) score++;
                if (!string.IsNullOrEmpty(Citation)) score++;
                if (!string.IsNullOrEmpty(Notes)) score++;
                if (!string.IsNullOrEmpty(LastMission)) score++;
                if (IsDead) score++;
                if (IsMia) score++;
                if (ExperienceAtRetire >= 0) score++;
                return score;
            }

            public static EacRecordSnapshot FromLiveRecord(string name, object liveRecord)
            {
                if (liveRecord == null) return null;
                var t = liveRecord.GetType();
                var s = new EacRecordSnapshot();
                s.Name = name;
                s.TraitTitle = ReadMemberString(liveRecord, t, "TraitTitle", "Trait", "Role", "Career");
                s.ServiceTag = ReadMemberString(liveRecord, t, "ServiceTag", "StatusTag", "ServiceRecord");
                s.CauseOfDeath = ReadMemberString(liveRecord, t, "CauseOfDeath", "Cause", "DeathCause");
                s.Citation = ReadMemberString(liveRecord, t, "Citation", "MemorialCitation", "Epitaph");
                s.Notes = ReadMemberString(liveRecord, t, "Notes", "Summary", "Remark");
                s.LastMission = ReadMemberString(liveRecord, t, "LastMission", "MissionName", "Mission");
                s.FlagUrl = ReadMemberString(liveRecord, t, "FlagUrl", "FlagURL", "Flag");
                s.Flights = ReadMemberIntLike(liveRecord, t, "Flights", "FlightCount", "MissionCount", -1);
                s.MissionHours = ReadMemberDouble(liveRecord, t, "MissionHours", "Hours", "TotalMissionHours", -1d);
                s.BirthUT = ReadMemberDouble(liveRecord, t, "BirthUT", "BirthTime", -1d);
                s.DeathUT = ReadMemberDouble(liveRecord, t, "DeathUT", "DeathTime", -1d);
                s.MissionStartUT = ReadMemberDouble(liveRecord, t, "MissionStartUT", "MissionStartTime", -1d);
                s.LastSeenUT = ReadMemberDouble(liveRecord, t, "LastSeenUT", "LastUpdateUT", "LastSeenTime", -1d);
                s.Retired = ReadMemberBool(liveRecord, t, "Retired", "retired", false);
                s.RetiredUT = ReadMemberDouble(liveRecord, t, "RetiredUT", "retiredUT", -1d);
                s.LastAgedYears = ReadMemberIntLike(liveRecord, t, "LastAgedYears", "Age", "AgeYears", -1);
                s.Courage = ReadMemberFloat(liveRecord, t, "Courage", "courage", float.NaN);
                s.Stupidity = ReadMemberFloat(liveRecord, t, "Stupidity", "stupidity", float.NaN);
                s.ExperienceAtRetire = ReadMemberIntLike(liveRecord, t, "ExperienceAtRetire", "experienceAtRetire", -1);

                string recordStatus = ReadMemberString(liveRecord, t, "Status", "State", "RosterState");
                s.IsDead =
                    ReadMemberBool(liveRecord, t, "IsDead", "Dead", false) ||
                    (!string.IsNullOrEmpty(recordStatus) &&
                     (recordStatus.Equals("Dead", StringComparison.OrdinalIgnoreCase) ||
                      recordStatus.Equals("Killed", StringComparison.OrdinalIgnoreCase) ||
                      recordStatus.Equals("Missing", StringComparison.OrdinalIgnoreCase))) ||
                    s.DeathUT > 0d ||
                    !string.IsNullOrEmpty(s.CauseOfDeath);

                s.IsMia = ReadMemberBool(liveRecord, t, "IsMIA", "IsMia", "MIA", false);
                s.SourceSummary = "Live EAC record";
                return s;
            }

            public static EacRecordSnapshot FromConfig(ConfigNode node)
            {
                if (node == null) return null;
                var s = new EacRecordSnapshot();
                s.Name = ReadValue(node, "name", "kerbalName", "Kerbal", "CrewName");
                s.TraitTitle = ReadValue(node, "TraitTitle", "trait", "Role", "Career");
                s.ServiceTag = ReadValue(node, "ServiceTag", "StatusTag", "ServiceRecord");
                s.CauseOfDeath = ReadValue(node, "CauseOfDeath", "Cause", "DeathCause");
                s.Citation = ReadValue(node, "Citation", "MemorialCitation", "Epitaph");
                s.Notes = ReadValue(node, "Notes", "Summary", "Remark");
                s.LastMission = ReadValue(node, "LastMission", "MissionName", "Mission");
                s.FlagUrl = ReadValue(node, "FlagUrl", "FlagURL", "Flag");
                s.IsDead = ReadBool(node, false, "IsDead", "Dead", "Killed") || HasDeathState(node);
                s.IsMia = ReadBool(node, false, "IsMIA", "IsMia", "MIA");
                s.Flights = ReadInt(node, -1, "Flights", "FlightCount", "MissionCount");
                s.MissionHours = ReadDouble(node, -1d, "MissionHours", "Hours", "TotalMissionHours");
                s.BirthUT = ReadDouble(node, -1d, "BirthUT", "BirthTime");
                s.DeathUT = ReadDouble(node, -1d, "DeathUT", "DeathTime");
                s.MissionStartUT = ReadDouble(node, -1d, "MissionStartUT", "MissionStartTime");
                s.LastSeenUT = ReadDouble(node, -1d, "LastSeenUT", "LastUpdateUT", "LastSeenTime");
                s.Retired = ReadBool(node, false, "Retired", "retired");
                s.RetiredUT = ReadDouble(node, -1d, "RetiredUT", "retiredUT");
                s.LastAgedYears = ReadInt(node, -1, "LastAgedYears", "Age", "AgeYears");
                s.Courage = ReadFloat(node, float.NaN, "Courage", "courage");
                s.Stupidity = ReadFloat(node, float.NaN, "Stupidity", "stupidity");
                s.ExperienceAtRetire = ReadIntLike(node, -1, "ExperienceAtRetire", "experienceAtRetire");
                s.SourceSummary = "persistent.sfs Record node";
                return string.IsNullOrEmpty(s.Name) ? null : s;
            }

            private static bool HasDeathState(ConfigNode node)
            {
                string state = ReadValue(node, "Status", "State", "RosterState");
                return !string.IsNullOrEmpty(state) &&
                       (state.Equals("Dead", StringComparison.OrdinalIgnoreCase) ||
                        state.Equals("Killed", StringComparison.OrdinalIgnoreCase) ||
                        state.Equals("Missing", StringComparison.OrdinalIgnoreCase));
            }

            private static string ReadMemberString(object obj, Type type, params string[] names)
            {
                return ReflectionUtils.GetString(obj, type, names);
            }


            private static int ReadMemberIntLike(object obj, Type type, string n1, string n2, int fallback)
            {
                return ReflectionUtils.GetIntLike(obj, type, fallback, n1, n2);
            }

            private static int ReadMemberIntLike(object obj, Type type, string n1, string n2, string n3, int fallback)
            {
                return ReflectionUtils.GetIntLike(obj, type, fallback, n1, n2, n3);
            }

            private static double ReadMemberDouble(object obj, Type type, string n1, string n2, double fallback)
            {
                return ReflectionUtils.GetDouble(obj, type, fallback, n1, n2);
            }

            private static double ReadMemberDouble(object obj, Type type, string n1, string n2, string n3, double fallback)
            {
                return ReflectionUtils.GetDouble(obj, type, fallback, n1, n2, n3);
            }

            private static float ReadMemberFloat(object obj, Type type, string n1, string n2, float fallback)
            {
                return ReflectionUtils.GetFloat(obj, type, fallback, n1, n2);
            }

            private static bool ReadMemberBool(object obj, Type type, string n1, string n2, bool fallback)
            {
                return ReflectionUtils.GetBool(obj, type, fallback, n1, n2);
            }

            private static bool ReadMemberBool(object obj, Type type, string n1, string n2, string n3, bool fallback)
            {
                return ReflectionUtils.GetBool(obj, type, fallback, n1, n2, n3);
            }
        }

        private static bool ReadBool(ConfigNode node, bool fallback, params string[] keys)
        {
            string value = ReadValue(node, keys);
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static int ReadInt(ConfigNode node, int fallback, params string[] keys)
        {
            string value = ReadValue(node, keys);
            int parsed;
            return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static int ReadIntLike(ConfigNode node, int fallback, params string[] keys)
        {
            string value = ReadValue(node, keys);
            int parsed;
            return TryParseIntLike(value, out parsed) ? parsed : fallback;
        }

        private static double ReadDouble(ConfigNode node, double fallback, params string[] keys)
        {
            string value = ReadValue(node, keys);
            double parsed;
            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static float ReadFloat(ConfigNode node, float fallback, params string[] keys)
        {
            string value = ReadValue(node, keys);
            float parsed;
            return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static class FlightTrackerShim
        {
            private static bool _checked;
            private static bool _available;
            private static bool _searchedFlightsMethod;
            private static MethodInfo _cachedFlightsMethod;
            private static Type _cachedApiType;
            private static FieldInfo _cachedApiInstanceField;
            private static PropertyInfo _cachedApiInstanceProperty;
            private static bool _searchedMissionHoursMethod;
            private static MethodInfo _cachedMissionHoursMethod;
            private static bool _searchedTimeLoggedStore;
            private static Type _cachedTimeLoggedStoreType;
            private static FieldInfo _cachedTimeLoggedStoreInstanceField;
            private static PropertyInfo _cachedTimeLoggedStoreInstanceProperty;
            private static FieldInfo _cachedTimeLoggedDictionaryField;
            private static PropertyInfo _cachedTimeLoggedDictionaryProperty;

            public static bool Available
            {
                get
                {
                    Ensure();
                    return _available;
                }
            }

            public static int GetFlightCount(string kerbalName)
            {
                Ensure();
                if (!_available || string.IsNullOrEmpty(kerbalName))
                    return -1;

                try
                {
                    var api = GetFlightsMethod();
                    if (api == null)
                        return -1;

                    object target = null;
                    if (!api.IsStatic)
                    {
                        target = GetApiInstance();
                        if (target == null)
                            return -1;
                    }

                    object raw = api.Invoke(target, new object[] { kerbalName });
                    return raw != null ? Math.Max(0, Convert.ToInt32(raw, CultureInfo.InvariantCulture)) : -1;
                }
                catch
                {
                    return -1;
                }
            }

            public static double GetHours(string kerbalName)
            {
                Ensure();
                if (!_available || string.IsNullOrEmpty(kerbalName))
                    return -1d;

                try
                {
                    var api = GetMissionHoursMethod();
                    if (api != null)
                    {
                        object target = null;
                        if (!api.IsStatic)
                        {
                            target = GetApiInstance();
                            if (target == null)
                                return -1d;
                        }

                        object raw = api.Invoke(target, new object[] { kerbalName });
                        if (raw == null)
                            return -1d;

                        double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        if ((api.Name ?? string.Empty).IndexOf("Seconds", StringComparison.OrdinalIgnoreCase) >= 0)
                            value /= 3600.0;

                        return Math.Max(0d, value);
                    }
                }
                catch
                {
                }

                return TryGetTimeLoggedHours(kerbalName, out double hours) ? hours : -1d;
            }

            private static void Ensure()
            {
                if (_checked) return;
                _checked = true;
                try
                {
                    _available = AssemblyLoader.loadedAssemblies.Any(a => a != null && a.assembly != null && a.assembly.GetName().Name.IndexOf("FlightTracker", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                catch
                {
                    _available = false;
                }
            }

            private static MethodInfo GetFlightsMethod()
            {
                if (_searchedFlightsMethod)
                    return _cachedFlightsMethod;

                _searchedFlightsMethod = true;
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
                                var mi = t.GetMethod("GetNumberOfFlights", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                                      ?? t.GetMethod("GetNumberOfFlights", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                                if (mi == null) continue;

                                _cachedFlightsMethod = mi;
                                CacheApiType(t);
                                return _cachedFlightsMethod;
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }

                return null;
            }

            private static MethodInfo GetMissionHoursMethod()
            {
                if (_searchedMissionHoursMethod)
                    return _cachedMissionHoursMethod;

                _searchedMissionHoursMethod = true;
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
                                var mi = t.GetMethod("GetRecordedMissionTimeHours", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                                      ?? t.GetMethod("GetRecordedMissionTimeHours", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)
                                      ?? t.GetMethod("GetRecordedMissionTimeSeconds", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                                      ?? t.GetMethod("GetRecordedMissionTimeSeconds", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                                if (mi == null) continue;

                                _cachedMissionHoursMethod = mi;
                                CacheApiType(t);
                                return _cachedMissionHoursMethod;
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }

                return null;
            }

            private static void CacheApiType(Type type)
            {
                if (type == null || _cachedApiType != null)
                    return;

                _cachedApiType = type;
                try { _cachedApiInstanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                catch { }
                try { _cachedApiInstanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
                catch { }
            }

            private static object GetApiInstance()
            {
                try
                {
                    if (_cachedApiInstanceProperty != null)
                        return _cachedApiInstanceProperty.GetValue(null, null);
                    if (_cachedApiInstanceField != null)
                        return _cachedApiInstanceField.GetValue(null);
                }
                catch
                {
                }

                return null;
            }

            private static bool ResolveTimeLoggedStore()
            {
                if (_searchedTimeLoggedStore)
                    return _cachedTimeLoggedStoreType != null && (_cachedTimeLoggedDictionaryField != null || _cachedTimeLoggedDictionaryProperty != null);

                _searchedTimeLoggedStore = true;
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

                            FieldInfo dictField = null;
                            PropertyInfo dictProp = null;
                            foreach (string memberName in new[] { "KerbalFlightTime", "TimeLogged" })
                            {
                                try { dictField = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                                catch { }
                                if (dictField != null && typeof(IDictionary).IsAssignableFrom(dictField.FieldType))
                                    break;
                                dictField = null;

                                try { dictProp = t.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                                catch { }
                                if (dictProp != null && typeof(IDictionary).IsAssignableFrom(dictProp.PropertyType))
                                    break;
                                dictProp = null;
                            }

                            if (dictField == null && dictProp == null) continue;

                            FieldInfo instanceField = null;
                            PropertyInfo instanceProp = null;
                            try { instanceField = t.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                            catch { }
                            if (instanceField == null)
                            {
                                try { instanceProp = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                                catch { }
                            }

                            if (instanceField == null && instanceProp == null) continue;

                            _cachedTimeLoggedStoreType = t;
                            _cachedTimeLoggedStoreInstanceField = instanceField;
                            _cachedTimeLoggedStoreInstanceProperty = instanceProp;
                            _cachedTimeLoggedDictionaryField = dictField;
                            _cachedTimeLoggedDictionaryProperty = dictProp;
                            return true;
                        }
                    }
                }
                catch
                {
                }

                return false;
            }

            private static bool TryGetTimeLoggedHours(string kerbalName, out double hours)
            {
                hours = -1d;
                if (string.IsNullOrEmpty(kerbalName) || !ResolveTimeLoggedStore())
                    return false;

                try
                {
                    object instance = null;
                    if (_cachedTimeLoggedStoreInstanceProperty != null)
                        instance = _cachedTimeLoggedStoreInstanceProperty.GetValue(null, null);
                    else if (_cachedTimeLoggedStoreInstanceField != null)
                        instance = _cachedTimeLoggedStoreInstanceField.GetValue(null);

                    if (instance == null)
                        return false;

                    var dict = _cachedTimeLoggedDictionaryField != null
                        ? _cachedTimeLoggedDictionaryField.GetValue(instance) as IDictionary
                        : _cachedTimeLoggedDictionaryProperty != null
                            ? _cachedTimeLoggedDictionaryProperty.GetValue(instance, null) as IDictionary
                            : null;
                    if (dict == null || !dict.Contains(kerbalName))
                        return false;

                    object raw = dict[kerbalName];
                    if (raw == null)
                        return false;

                    hours = Math.Max(0d, Convert.ToDouble(raw, CultureInfo.InvariantCulture));
                    return true;
                }
                catch
                {
                    hours = -1d;
                    return false;
                }
            }
        }
    }
}

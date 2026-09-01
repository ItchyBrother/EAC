// EAC - Hall of History service records / Final Frontier-lite view.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RosterRotation
{
    public partial class HallOfHistoryWindow
    {
        private const float ServiceRecordsUiCacheSeconds = 0.25f;

        private Vector2 _serviceListScroll;
        private Vector2 _serviceDetailScroll;
        private string _selectedServiceKerbal;
        private GUIStyle _serviceFirstStarStyle;
        private GUIStyle _serviceFirstBadgeStyle;
        private List<string> _cachedServiceNames;
        private float _lastServiceNamesCacheRT = -1f;
        private readonly Dictionary<string, ServiceRecordRenderCache> _serviceRenderCache =
            new Dictionary<string, ServiceRecordRenderCache>(StringComparer.OrdinalIgnoreCase);

        private sealed class ServiceRecordRenderCache
        {
            internal int SortedCareerEventCount = -1;
            internal List<RosterRotationState.CareerEventRecord> SortedCareerEvents;
            internal int HistoricFirstEventCount = -1;
            internal int HistoricFirstMilestoneCount = -1;
            internal List<string> HistoricFirsts;
            internal int DistinctionEventCount = -1;
            internal int DistinctionFlights = -1;
            internal List<string> Distinctions;
        }

        private void DrawServiceRecordsTab()
        {
            const float paneGap = 8f;
            float contentWidth = Mathf.Max(860f, _window.width - 24f);
            float leftPaneWidth = Mathf.Clamp(contentWidth * 0.36f, 360f, 460f);
            float rightPaneWidth = Mathf.Max(520f, contentWidth - leftPaneWidth - paneGap);

            List<string> names = GetServiceRecordNames();
            if (string.IsNullOrEmpty(_selectedServiceKerbal) || !names.Contains(_selectedServiceKerbal))
                _selectedServiceKerbal = names.Count > 0 ? names[0] : null;

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(leftPaneWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label("Kerbal Service Records", _subheaderStyle);
            GUILayout.Box(string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Label("Personal career history reconstructed from stock CAREER_LOG and enriched by EAC from this version forward.", _smallMutedStyle);
            GUILayout.Space(4f);

            _serviceListScroll = GUILayout.BeginScrollView(_serviceListScroll, false, true, GUILayout.ExpandHeight(true));
            if (names.Count == 0)
            {
                GUILayout.Label("No EAC service records found yet.", _mutedStyle);
            }
            else
            {
                for (int i = 0; i < names.Count; i++)
                    DrawServiceRecordCard(names[i]);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(paneGap);

            GUILayout.BeginVertical(GUILayout.Width(rightPaneWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label("Service File", _subheaderStyle);
            GUILayout.Box(string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            _serviceDetailScroll = GUILayout.BeginScrollView(_serviceDetailScroll, false, true, GUILayout.ExpandHeight(true));
            if (!string.IsNullOrEmpty(_selectedServiceKerbal))
                DrawServiceRecordDetail(_selectedServiceKerbal);
            else
                GUILayout.Label("Select a Kerbal to view their service file.", _mutedStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private List<string> GetServiceRecordNames()
        {
            float rt = Time.realtimeSinceStartup;
            if (_cachedServiceNames != null &&
                _lastServiceNamesCacheRT >= 0f &&
                rt - _lastServiceNamesCacheRT < ServiceRecordsUiCacheSeconds)
                return _cachedServiceNames;

            var names = new List<string>();
            foreach (var kvp in RosterRotationState.Records)
            {
                var rec = kvp.Value;
                if (rec == null) continue;
                if (rec.Flights <= 0 && rec.FlightHistory.Count == 0 && rec.CareerEvents.Count == 0 && !rec.Retired && rec.DeathUT <= 0) continue;
                names.Add(kvp.Key);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            _cachedServiceNames = names;
            _lastServiceNamesCacheRT = rt;
            return _cachedServiceNames;
        }

        private void DrawServiceRecordCard(string name)
        {
            RosterRotationState.KerbalRecord rec;
            if (!RosterRotationState.Records.TryGetValue(name, out rec) || rec == null) return;
            bool selected = string.Equals(_selectedServiceKerbal, name, StringComparison.Ordinal);

            GUILayout.BeginVertical(selected ? _selectedCardStyle : _cardStyle);

            List<string> historicFirsts = GetHistoricFirstsCached(name, rec);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(name, _nameStyle, GUILayout.ExpandWidth(true)))
                _selectedServiceKerbal = name;
            if (historicFirsts.Count > 0)
            {
                EnsureServiceFirstStyles();
                string badgeText = historicFirsts.Count == 1
                    ? "★ FIRST"
                    : "★ ×" + historicFirsts.Count;
                GUILayout.Label(badgeText, _serviceFirstBadgeStyle, GUILayout.Width(68f));
            }
            GUILayout.EndHorizontal();

            string status = rec.Retired ? "Retired" : rec.DeathUT > 0 ? "Deceased" : "Service record";
            GUILayout.Label(status + "  •  " + rec.Flights + " recovered flight" + (rec.Flights == 1 ? "" : "s"), _smallMutedStyle);
            GUILayout.Label(rec.CareerEvents.Count + " recorded accomplishment" + (rec.CareerEvents.Count == 1 ? "" : "s") + "  •  " + FormatServiceTrackedTime(rec.TotalTrackedFlightUT) + " EAC-tracked time", _smallMutedStyle);
            GUILayout.EndVertical();
            GUILayout.Space(3f);
        }

        private void DrawServiceRecordDetail(string name)
        {
            RosterRotationState.KerbalRecord rec;
            if (!RosterRotationState.Records.TryGetValue(name, out rec) || rec == null)
            {
                GUILayout.Label("Service record unavailable.", _mutedStyle);
                return;
            }

            string role = string.IsNullOrEmpty(rec.OriginalTrait) ? "Kerbal" : rec.OriginalTrait;

            GUILayout.BeginHorizontal(_plaqueBodyStyle);
            DrawPortraitBlock(name, role, 118f, 150f, true);
            GUILayout.Space(12f);
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label(name, _plaqueTitleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            GUILayout.Label(role, _mutedStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(4f);
            GUILayout.Label(BuildServiceSummary(rec), _mutedStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            List<string> historicFirsts = GetHistoricFirstsCached(name, rec);
            if (historicFirsts.Count > 0)
            {
                GUILayout.Label("Historic Firsts", _sectionHeaderStyle);
                for (int i = 0; i < historicFirsts.Count; i++)
                    DrawHistoricFirstCard(historicFirsts[i]);
                GUILayout.Space(8f);
            }

            GUILayout.Label("Career Distinctions", _sectionHeaderStyle);
            List<string> distinctions = GetServiceDistinctionsCached(name, rec);
            if (distinctions.Count == 0)
                GUILayout.Label("No distinctions reconstructed yet.", _smallMutedStyle);
            else
            {
                for (int i = 0; i < distinctions.Count; i++)
                    GUILayout.Label("• " + distinctions[i], _wrapStyle);
            }

            GUILayout.Space(10f);
            GUILayout.Label("Mission Record", _sectionHeaderStyle);
            if (rec.FlightHistory.Count == 0)
            {
                GUILayout.Label("No detailed EAC mission records yet. Earlier missions may still appear below as stock career-log accomplishments.", _smallMutedStyle);
            }
            else
            {
                // Native EAC flight history is appended chronologically at recovery.
                // Render newest-first by walking the stored list backwards instead of
                // allocating and sorting a copy on every IMGUI pass.
                for (int i = rec.FlightHistory.Count - 1; i >= 0; i--)
                {
                    var flight = rec.FlightHistory[i];
                    if (flight == null) continue;
                    string number = flight.FlightNumber > 0 ? "Flight #" + flight.FlightNumber : "Flight";
                    string date = flight.EndUT > 0 ? RosterRotationState.FormatGameDateYD(flight.EndUT) : "date unknown";
                    string vessel = string.IsNullOrEmpty(flight.VesselName) ? "vessel unknown" : flight.VesselName;
                    string type = string.IsNullOrEmpty(flight.FlightType) ? "mission" : flight.FlightType;
                    string body = string.IsNullOrEmpty(flight.BodyName) ? "" : " • " + flight.BodyName;
                    GUILayout.Label(number + " — " + date, _nameStyle);
                    GUILayout.Label(vessel + " • " + type + body + " • " + FormatServiceTrackedTime(flight.DurationUT), _smallMutedStyle);
                    GUILayout.Space(3f);
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label("Career Log Evidence", _sectionHeaderStyle);
            if (rec.CareerEvents.Count == 0)
            {
                GUILayout.Label("No stock CAREER_LOG accomplishments have been imported for this Kerbal.", _smallMutedStyle);
            }
            else
            {
                List<RosterRotationState.CareerEventRecord> eventsList = GetSortedCareerEventsCached(name, rec);

                int lastFlight = int.MinValue;
                for (int i = 0; i < eventsList.Count; i++)
                {
                    var e = eventsList[i];
                    if (e == null) continue;
                    if (e.FlightNumber != lastFlight)
                    {
                        lastFlight = e.FlightNumber;
                        GUILayout.Label(e.FlightNumber > 0 ? "Flight #" + e.FlightNumber : "Unnumbered flight", _nameStyle);
                    }
                    string body = string.IsNullOrEmpty(e.BodyName) ? "" : " — " + e.BodyName;
                    GUILayout.BeginHorizontal();
                    if (e.IsProgramFirst)
                    {
                        EnsureServiceFirstStyles();
                        GUILayout.Label("★ FIRST", _serviceFirstBadgeStyle, GUILayout.Width(68f));
                    }
                    GUILayout.Label("  • " + FriendlyEventName(e.EventType) + body, _wrapStyle, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void DrawHistoricFirstCard(string text)
        {
            EnsureServiceFirstStyles();
            GUILayout.BeginHorizontal(_cardStyle);
            GUILayout.Label("★", _serviceFirstStarStyle, GUILayout.Width(34f), GUILayout.Height(34f));
            GUILayout.Space(4f);
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("PROGRAM FIRST", _serviceFirstBadgeStyle);
            GUILayout.Label(text, _nameStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);
        }

        private void EnsureServiceFirstStyles()
        {
            if (_serviceFirstStarStyle != null && _serviceFirstBadgeStyle != null) return;

            _serviceFirstStarStyle = new GUIStyle(_nameStyle ?? GUI.skin.label);
            _serviceFirstStarStyle.fontSize = 24;
            _serviceFirstStarStyle.fontStyle = FontStyle.Bold;
            _serviceFirstStarStyle.alignment = TextAnchor.MiddleCenter;
            _serviceFirstStarStyle.normal.textColor = new Color(1f, 0.78f, 0.18f, 1f);

            _serviceFirstBadgeStyle = new GUIStyle(_smallMutedStyle ?? GUI.skin.label);
            _serviceFirstBadgeStyle.fontStyle = FontStyle.Bold;
            _serviceFirstBadgeStyle.normal.textColor = new Color(1f, 0.78f, 0.18f, 1f);
        }

        private void InvalidateServiceRecordCaches()
        {
            _cachedServiceNames = null;
            _lastServiceNamesCacheRT = -1f;
            _serviceRenderCache.Clear();
        }

        private ServiceRecordRenderCache GetServiceRenderCache(string kerbalName)
        {
            ServiceRecordRenderCache cache;
            if (!_serviceRenderCache.TryGetValue(kerbalName ?? string.Empty, out cache) || cache == null)
            {
                cache = new ServiceRecordRenderCache();
                _serviceRenderCache[kerbalName ?? string.Empty] = cache;
            }
            return cache;
        }

        private List<RosterRotationState.CareerEventRecord> GetSortedCareerEventsCached(
            string kerbalName,
            RosterRotationState.KerbalRecord rec)
        {
            ServiceRecordRenderCache cache = GetServiceRenderCache(kerbalName);
            int count = rec != null ? rec.CareerEvents.Count : 0;
            if (cache.SortedCareerEvents != null && cache.SortedCareerEventCount == count)
                return cache.SortedCareerEvents;

            var eventsList = rec != null
                ? new List<RosterRotationState.CareerEventRecord>(rec.CareerEvents)
                : new List<RosterRotationState.CareerEventRecord>();
            eventsList.Sort((a, b) =>
            {
                int af = a != null ? a.FlightNumber : 0;
                int bf = b != null ? b.FlightNumber : 0;
                int cmp = bf.CompareTo(af);
                if (cmp != 0) return cmp;
                return string.Compare(a != null ? a.EventType : "", b != null ? b.EventType : "", StringComparison.OrdinalIgnoreCase);
            });

            cache.SortedCareerEvents = eventsList;
            cache.SortedCareerEventCount = count;
            return cache.SortedCareerEvents;
        }

        private List<string> GetHistoricFirstsCached(
            string kerbalName,
            RosterRotationState.KerbalRecord rec)
        {
            ServiceRecordRenderCache cache = GetServiceRenderCache(kerbalName);
            int eventCount = rec != null ? rec.CareerEvents.Count : 0;
            int milestoneCount = _cache != null && _cache.Milestones != null ? _cache.Milestones.Count : 0;
            if (cache.HistoricFirsts != null &&
                cache.HistoricFirstEventCount == eventCount &&
                cache.HistoricFirstMilestoneCount == milestoneCount)
                return cache.HistoricFirsts;

            cache.HistoricFirsts = BuildHistoricFirsts(kerbalName, rec);
            cache.HistoricFirstEventCount = eventCount;
            cache.HistoricFirstMilestoneCount = milestoneCount;
            return cache.HistoricFirsts;
        }

        private List<string> GetServiceDistinctionsCached(
            string kerbalName,
            RosterRotationState.KerbalRecord rec)
        {
            ServiceRecordRenderCache cache = GetServiceRenderCache(kerbalName);
            int eventCount = rec != null ? rec.CareerEvents.Count : 0;
            int flights = rec != null ? rec.Flights : 0;
            if (cache.Distinctions != null &&
                cache.DistinctionEventCount == eventCount &&
                cache.DistinctionFlights == flights)
                return cache.Distinctions;

            cache.Distinctions = rec != null ? BuildServiceDistinctions(rec) : new List<string>();
            cache.DistinctionEventCount = eventCount;
            cache.DistinctionFlights = flights;
            return cache.Distinctions;
        }

        private List<string> BuildHistoricFirsts(string kerbalName, RosterRotationState.KerbalRecord rec)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (rec != null)
            {
                for (int i = 0; i < rec.CareerEvents.Count; i++)
                {
                    RosterRotationState.CareerEventRecord e = rec.CareerEvents[i];
                    if (e == null || !e.IsProgramFirst) continue;
                    string key = EACCareerHistory.BuildProgramFirstKey(e.EventType, e.BodyName);
                    if (string.IsNullOrEmpty(key)) continue;
                    string text = EACCareerHistory.FriendlyProgramFirstKey(key);
                    if (seen.Add(text)) result.Add(text);
                }
            }

            // Older saves can sometimes identify the crew of a stock ProgressTracking
            // milestone even though CAREER_LOG has no trustworthy date. Use those named
            // crew milestones as historical evidence, but do not infer crew when KSP left
            // the field blank.
            if (_cache != null && _cache.Milestones != null && !string.IsNullOrEmpty(kerbalName))
            {
                for (int i = 0; i < _cache.Milestones.Count; i++)
                {
                    MilestoneEntry milestone = _cache.Milestones[i];
                    if (milestone == null || string.IsNullOrEmpty(milestone.CrewText)) continue;
                    if (!ContainsCrewName(milestone.CrewText, kerbalName)) continue;
                    string text = string.IsNullOrEmpty(milestone.Title)
                        ? "Hall of History milestone"
                        : milestone.Title;
                    if (seen.Add(text)) result.Add(text);
                }
            }

            return result;
        }

        private static string BuildServiceSummary(RosterRotationState.KerbalRecord rec)
        {
            string status = rec.Retired ? "Retired" : rec.DeathUT > 0 ? "Deceased" : "Active/recorded";
            return status + "  •  " + rec.Flights + " recovered flights  •  " + FormatServiceTrackedTime(rec.TotalTrackedFlightUT) + " EAC-tracked mission time  •  " + rec.CareerEvents.Count + " career-log events";
        }

        private static List<string> BuildServiceDistinctions(RosterRotationState.KerbalRecord rec)
        {
            var result = new List<string>();
            if (rec.Flights >= 50) result.Add("50 Mission Veteran");
            else if (rec.Flights >= 25) result.Add("25 Mission Veteran");
            else if (rec.Flights >= 10) result.Add("10 Mission Veteran");
            else if (rec.Flights >= 5) result.Add("5 Mission Veteran");
            else if (rec.Flights >= 1) result.Add("First Recovered Mission");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rec.CareerEvents.Count; i++)
            {
                var e = rec.CareerEvents[i];
                if (e == null || string.IsNullOrEmpty(e.EventType)) continue;
                string body = string.IsNullOrEmpty(e.BodyName) ? "" : " " + e.BodyName;
                string distinction = null;
                if (e.EventType.Equals("Orbit", StringComparison.OrdinalIgnoreCase)) distinction = "Orbited" + body;
                else if (e.EventType.Equals("Suborbit", StringComparison.OrdinalIgnoreCase) || e.EventType.Equals("SubOrbital", StringComparison.OrdinalIgnoreCase)) distinction = "Suborbital Flight" + body;
                else if (e.EventType.Equals("Flyby", StringComparison.OrdinalIgnoreCase)) distinction = "Flyby" + body;
                else if (e.EventType.Equals("Escape", StringComparison.OrdinalIgnoreCase)) distinction = "Escaped" + body + " SOI";
                else if (e.EventType.Equals("ExitVessel", StringComparison.OrdinalIgnoreCase)) distinction = "EVA" + body;
                else if (e.EventType.Equals("PlantFlag", StringComparison.OrdinalIgnoreCase)) distinction = "Planted Flag" + body;
                else if (e.EventType.IndexOf("Land", StringComparison.OrdinalIgnoreCase) >= 0) distinction = "Landed" + body;
                if (!string.IsNullOrEmpty(distinction) && seen.Add(distinction)) result.Add(distinction);
            }
            return result;
        }

        private static string FormatServiceTrackedTime(double seconds)
        {
            if (seconds <= 0) return "—";
            double days = seconds / RosterRotationState.DaySeconds;
            if (days >= 1.0) return days.ToString("0.0") + "d";
            double hours = seconds / 3600.0;
            if (hours >= 1.0) return hours.ToString("0.0") + "h";
            return Math.Max(1.0, seconds / 60.0).ToString("0") + "m";
        }

        private static string FriendlyEventName(string type)
        {
            if (string.IsNullOrEmpty(type)) return "Recorded event";
            if (type.Equals("ExitVessel", StringComparison.OrdinalIgnoreCase)) return "EVA";
            if (type.Equals("PlantFlag", StringComparison.OrdinalIgnoreCase)) return "Planted flag";
            if (type.Equals("Suborbit", StringComparison.OrdinalIgnoreCase) || type.Equals("SubOrbital", StringComparison.OrdinalIgnoreCase)) return "Suborbital flight";
            return type;
        }
    }
}

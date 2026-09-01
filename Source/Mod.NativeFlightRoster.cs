// EAC - Enhanced Astronaut Complex - Mod.NativeFlightRoster.cs
// Native flight roster/history UI for issue #46.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RosterRotation
{
    public partial class RosterRotationKSCUI
    {
        private Vector2 _flightRosterScroll;
        private string _expandedFlightKerbal;
        private List<string> _cachedFlightRosterNames;
        private float _lastFlightRosterNamesCacheRT = -1f;

        private void DrawFlightRosterTab(KerbalRoster roster, double nowUT)
        {
            List<string> names = GetFlightRosterNamesCached();

            GUILayout.Label("Flight Roster: " + names.Count + " Kerbals");
            GUILayout.Label("Detailed history is recorded by EAC for recoveries made after this feature is installed; the existing total Flights count is retained for earlier missions.");
            GUILayout.Space(6);

            const float nameWidth = 230f;
            const float flightsWidth = 65f;
            const float sinceWidth = 135f;
            const float totalWidth = 120f;
            const float typeWidth = 100f;
            const float vesselWidth = 230f;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Kerbal", GUILayout.Width(nameWidth));
            GUILayout.Label("Flights", GUILayout.Width(flightsWidth));
            GUILayout.Label("Since last", GUILayout.Width(sinceWidth));
            GUILayout.Label("Tracked time", GUILayout.Width(totalWidth));
            GUILayout.Label("Last type", GUILayout.Width(typeWidth));
            GUILayout.Label("Last vessel", GUILayout.Width(vesselWidth));
            GUILayout.EndHorizontal();
            DrawHRule();

            _flightRosterScroll = GUILayout.BeginScrollView(_flightRosterScroll);
            for (int n = 0; n < names.Count; n++)
            {
                string name = names[n];
                RosterRotationState.KerbalRecord rec;
                if (!RosterRotationState.Records.TryGetValue(name, out rec) || rec == null) continue;

                RosterRotationState.FlightRecord latest = GetLatestFlightRecord(rec);
                bool expanded = string.Equals(_expandedFlightKerbal, name, StringComparison.Ordinal);

                GUILayout.BeginHorizontal();
                string buttonLabel = (expanded ? "▼ " : "▶ ") + name;
                if (GUILayout.Button(buttonLabel, GUILayout.Width(nameWidth)))
                    _expandedFlightKerbal = expanded ? null : name;
                GUILayout.Label(rec.Flights.ToString(), GUILayout.Width(flightsWidth));
                GUILayout.Label(rec.LastFlightUT > 0 ? RosterRotationState.FormatTimeAgo(rec.LastFlightUT, nowUT) : "—", GUILayout.Width(sinceWidth));
                GUILayout.Label(FormatTrackedFlightTime(rec.TotalTrackedFlightUT), GUILayout.Width(totalWidth));
                GUILayout.Label(latest != null && !string.IsNullOrEmpty(latest.FlightType) ? latest.FlightType : "—", GUILayout.Width(typeWidth));
                GUILayout.Label(latest != null && !string.IsNullOrEmpty(latest.VesselName) ? latest.VesselName : "—", GUILayout.Width(vesselWidth));
                GUILayout.EndHorizontal();

                if (expanded)
                    DrawKerbalFlightHistory(rec);
            }

            if (names.Count == 0)
                GUILayout.Label("No recovered flights have been recorded yet.");

            GUILayout.EndScrollView();
        }

        private List<string> GetFlightRosterNamesCached()
        {
            float rt = Time.realtimeSinceStartup;
            if (_cachedFlightRosterNames != null &&
                _lastFlightRosterNamesCacheRT >= 0f &&
                rt - _lastFlightRosterNamesCacheRT < UiCacheSeconds)
                return _cachedFlightRosterNames;

            var names = new List<string>();
            foreach (var kvp in RosterRotationState.Records)
            {
                var rec = kvp.Value;
                if (rec == null) continue;
                if (rec.Flights <= 0 && rec.FlightHistory.Count == 0) continue;
                names.Add(kvp.Key);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            _cachedFlightRosterNames = names;
            _lastFlightRosterNamesCacheRT = rt;
            return _cachedFlightRosterNames;
        }

        private static RosterRotationState.FlightRecord GetLatestFlightRecord(RosterRotationState.KerbalRecord rec)
        {
            if (rec == null || rec.FlightHistory.Count == 0) return null;
            // EACNativeFlightHistory appends completed flights chronologically.
            return rec.FlightHistory[rec.FlightHistory.Count - 1];
        }

        private static void DrawKerbalFlightHistory(RosterRotationState.KerbalRecord rec)
        {
            if (rec == null) return;
            if (rec.FlightHistory.Count == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(24);
                GUILayout.Label("No detailed EAC history for earlier flights.");
                GUILayout.EndHorizontal();
                return;
            }

            // Native history is append-chronological, so reverse iteration gives
            // newest-first display with no per-frame list allocation or sort.
            for (int i = rec.FlightHistory.Count - 1; i >= 0; i--)
            {
                var flight = rec.FlightHistory[i];
                if (flight == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Space(24);
                string number = flight.FlightNumber > 0 ? "#" + flight.FlightNumber : "Flight";
                GUILayout.Label(number, GUILayout.Width(55));
                GUILayout.Label(flight.EndUT > 0 ? RosterRotationState.FormatGameDateYD(flight.EndUT) : "—", GUILayout.Width(115));
                GUILayout.Label(string.IsNullOrEmpty(flight.FlightType) ? "—" : flight.FlightType, GUILayout.Width(100));
                GUILayout.Label(string.IsNullOrEmpty(flight.VesselName) ? "—" : flight.VesselName, GUILayout.Width(230));
                GUILayout.Label(string.IsNullOrEmpty(flight.BodyName) ? "—" : flight.BodyName, GUILayout.Width(100));
                GUILayout.Label(FormatTrackedFlightTime(flight.DurationUT), GUILayout.Width(120));
                GUILayout.EndHorizontal();
            }
        }

        private static string FormatTrackedFlightTime(double seconds)
        {
            if (seconds <= 0) return "—";
            double days = seconds / RosterRotationState.DaySeconds;
            if (days >= 1.0) return days.ToString("0.0") + "d";
            double hours = seconds / 3600.0;
            if (hours >= 1.0) return hours.ToString("0.0") + "h";
            return Math.Max(1.0, seconds / 60.0).ToString("0") + "m";
        }
    }
}

using System;
using System.Collections.Generic;

namespace RosterRotation
{
    /// <summary>
    /// Computes EAC veteran eligibility from class, flight count, flight hours,
    /// and milestone criteria. This class has no side effects.
    /// </summary>
    internal static class EACVeteranEligibility
    {
        internal static bool QualifiesForVeteran(ProtoCrewMember kerbal, RosterRotationState.KerbalRecord rec, out int flights, out double hours, out string milestoneKey)
        {
            flights = GetBestFlightCount(kerbal, rec);
            hours = GetBestFlightHours(kerbal);
            milestoneKey = TryFindMilestoneKey(kerbal);

            if (flights < Math.Max(0, RosterRotationState.EACVeteranFlightsRequired)) return false;
            if (hours < Math.Max(0.0, RosterRotationState.EACVeteranHoursRequired)) return false;
            if (RosterRotationState.EACVeteranRequireMilestone && string.IsNullOrEmpty(milestoneKey)) return false;
            return true;
        }

        internal static bool IsAllowedVeteranClass(string trait)
        {
            if (string.IsNullOrEmpty(trait)) return true;
            if (string.Equals(trait, "Pilot", StringComparison.OrdinalIgnoreCase)) return RosterRotationState.EACAllowPilotVeterans;
            if (string.Equals(trait, "Engineer", StringComparison.OrdinalIgnoreCase)) return RosterRotationState.EACAllowEngineerVeterans;
            if (string.Equals(trait, "Scientist", StringComparison.OrdinalIgnoreCase)) return RosterRotationState.EACAllowScientistVeterans;
            return false;
        }

        private static int GetBestFlightCount(ProtoCrewMember kerbal, RosterRotationState.KerbalRecord rec)
        {
            int count = rec != null ? Math.Max(0, rec.Flights) : 0;
            try
            {
                int ftFlights;
                string ftReason;
                if (RosterRotationKSCUI.TryGetFlightTrackerRecordedFlights(kerbal != null ? kerbal.name : null, out ftFlights, out ftReason))
                    count = Math.Max(count, ftFlights);
            }
            catch { /* fallback below */ }

            count = Math.Max(count, CountFlightsFromCareerLog(kerbal));
            return count;
        }

        private static double GetBestFlightHours(ProtoCrewMember kerbal)
        {
            if (kerbal == null) return 0.0;
            try
            {
                double hours;
                string reason;
                if (RosterRotationKSCUI.TryGetFlightTrackerRecordedHours(kerbal.name, out hours, out reason))
                    return Math.Max(0.0, hours);
            }
            catch { /* no FlightTracker time source */ }
            return 0.0;
        }

        private static int CountFlightsFromCareerLog(ProtoCrewMember kerbal)
        {
            if (kerbal == null) return 0;
            try
            {
                var node = new ConfigNode("KERBAL");
                kerbal.Save(node);
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectFlightIds(node.GetNode("CAREER_LOG"), ids);
                CollectFlightIds(node.GetNode("careerLog"), ids);
                return ids.Count;
            }
            catch { return 0; }
        }

        private static void CollectFlightIds(ConfigNode logNode, HashSet<string> ids)
        {
            if (logNode == null || ids == null) return;
            foreach (ConfigNode.Value value in logNode.values)
            {
                if (value == null || string.IsNullOrEmpty(value.name) || string.IsNullOrEmpty(value.value)) continue;
                if (string.Equals(value.name, "flight", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.name, "flights", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (value.value.IndexOf("Flight,", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.value.IndexOf("Recover", StringComparison.OrdinalIgnoreCase) >= 0)
                    ids.Add(value.name);
            }
        }

        private static string TryFindMilestoneKey(ProtoCrewMember kerbal)
        {
            if (kerbal == null) return "";
            try
            {
                var node = new ConfigNode("KERBAL");
                kerbal.Save(node);
                string key = FindMilestoneInLog(node.GetNode("CAREER_LOG"));
                if (!string.IsNullOrEmpty(key)) return key;
                return FindMilestoneInLog(node.GetNode("careerLog"));
            }
            catch { return ""; }
        }

        private static string FindMilestoneInLog(ConfigNode logNode)
        {
            if (logNode == null) return "";
            foreach (ConfigNode.Value value in logNode.values)
            {
                if (value == null || string.IsNullOrEmpty(value.value)) continue;
                string entry = value.value;
                string e = entry.ToLowerInvariant();
                if (e.Contains("land")) return "landing";
                if (e.Contains("orbit")) return "orbit";
                if (e.Contains("escape")) return "escape";
                if (e.Contains("flyby")) return "flyby";
                if (e.Contains("suborbit")) return "suborbit";
                if (e.Contains("eva")) return "eva";
                if (e.Contains("mun")) return "mun";
                if (e.Contains("minmus")) return "minmus";
            }
            return "";
        }
    }
}

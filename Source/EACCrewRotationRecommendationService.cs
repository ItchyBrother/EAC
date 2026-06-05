using System;
using System.Collections.Generic;

namespace RosterRotation
{
    /// <summary>
    /// Builds advisory-only crew rotation suggestions. This service does not
    /// modify stock crew slots or vessel manifests.
    /// </summary>
    internal static class EACCrewRotationRecommendationService
    {
        internal static List<CrewSuggestion> BuildSuggestions()
        {
            double nowUT = 0.0;
            try { nowUT = Planetarium.GetUniversalTime(); } catch { nowUT = 0.0; }

            var list = new List<CrewSuggestion>();

            try
            {
                var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
                if (roster == null) return list;

                for (int i = 0; i < roster.Count; i++)
                {
                    ProtoCrewMember k;
                    try { k = roster[i]; } catch { continue; }
                    if (!IsEligible(k, nowUT)) continue;

                    RosterRotationState.KerbalRecord rec;
                    RosterRotationState.Records.TryGetValue(k.name, out rec);
                    int flights = RosterRotationKSCUI.GetDisplayedFlights(k, rec);
                    double hours = GetRecordedHours(k.name);
                    double daysSince = GetDaysSinceLastFlight(rec, nowUT);

                    string label = ResolveLabel(k, rec, flights, hours, daysSince);
                    double score = ScoreCandidate(k, rec, flights, hours, daysSince, label);

                    list.Add(new CrewSuggestion
                    {
                        Name = k.name,
                        Trait = SafeTrait(k),
                        Level = SafeLevel(k),
                        Flights = flights,
                        Hours = hours,
                        DaysSinceFlight = daysSince,
                        Label = label,
                        LabelPriority = GetLabelPriority(label),
                        Score = score
                    });
                }

                list.Sort((a, b) =>
                {
                    int cmp = a.LabelPriority.CompareTo(b.LabelPriority);
                    if (cmp != 0) return cmp;
                    cmp = b.Score.CompareTo(a.Score);
                    if (cmp != 0) return cmp;
                    cmp = a.Flights.CompareTo(b.Flights);
                    if (cmp != 0) return cmp;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACCrewRotationRecommendationService.BuildSuggestions", "Suppressed exception rebuilding crew rotation suggestions", ex);
            }

            return list;
        }

        internal static int GetLabelPriority(string label)
        {
            if (string.Equals(label, "Needs experience", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(label, "Due for flight", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(label, "Long service priority", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(label, "Recently flew", StringComparison.OrdinalIgnoreCase)) return 4;
            return 99;
        }

        private static bool IsEligible(ProtoCrewMember k, double nowUT)
        {
            if (k == null) return false;
            if (k.type != ProtoCrewMember.KerbalType.Crew) return false;
            if (k.rosterStatus != ProtoCrewMember.RosterStatus.Available) return false;
            if (k.inactive && k.inactiveTimeEnd > nowUT) return false;
            if (CrewRandRAdapter.IsOnVacationByName(k.name, nowUT)) return false;

            RosterRotationState.KerbalRecord rec;
            if (RosterRotationState.Records.TryGetValue(k.name, out rec) && rec != null)
            {
                if (rec.Retired) return false;
                if (RecoveryLeaveService.IsEacRecoveryActive(k, rec, nowUT)) return false;
                if (rec.Training != TrainingType.None) return false;
                if (rec.DeathUT > 0 || rec.DiedOnMission || rec.PendingMissionDeath) return false;
            }

            return true;
        }

        private static string ResolveLabel(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int flights, double hours, double daysSince)
        {
            int level = SafeLevel(k);

            if (flights == 0 || level <= 1)
                return "Needs experience";

            if (daysSince >= 180.0 && (flights >= 3 || hours >= 24.0))
                return "Long service priority";

            if (daysSince >= 0.0 && daysSince <= 14.0)
                return "Recently flew";

            return "Due for flight";
        }

        private static double ScoreCandidate(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int flights, double hours, double daysSince, string label)
        {
            double score = 0.0;

            if (daysSince < 0.0)
                score += 24.0; // never flown / no recorded last mission
            else
                score += Math.Min(daysSince, 360.0) / 10.0;

            int level = SafeLevel(k);
            if (level <= 0) score += 9.0;
            else if (level == 1) score += 6.0;
            else if (level == 2) score += 3.0;

            if (flights == 0) score += 6.0;
            if (string.Equals(label, "Recently flew", StringComparison.OrdinalIgnoreCase)) score -= 20.0;
            if (string.Equals(label, "Long service priority", StringComparison.OrdinalIgnoreCase)) score += 4.0;

            return score;
        }

        private static double GetRecordedHours(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName)) return 0.0;
            double hours; string reason;
            if (RosterRotationKSCUI.TryGetFlightTrackerRecordedHours(kerbalName, out hours, out reason))
                return Math.Max(0.0, hours);
            return 0.0;
        }

        private static double GetDaysSinceLastFlight(RosterRotationState.KerbalRecord rec, double nowUT)
        {
            if (rec == null || rec.LastFlightUT <= 0.0 || nowUT <= 0.0) return -1.0;
            double seconds = Math.Max(0.0, nowUT - rec.LastFlightUT);
            double daySeconds = Math.Max(1.0, RosterRotationState.DaySeconds);
            return seconds / daySeconds;
        }

        private static int SafeLevel(ProtoCrewMember k)
        {
            if (k == null) return 0;
            try { return Math.Max(0, k.experienceLevel); } catch { return 0; }
        }

        private static string SafeTrait(ProtoCrewMember k)
        {
            if (k == null) return string.Empty;
            try
            {
                string trait = k.trait;
                return string.IsNullOrEmpty(trait) ? "Crew" : trait;
            }
            catch { return "Crew"; }
        }
    }
}

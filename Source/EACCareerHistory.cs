using System;
using System.Collections.Generic;
using System.Globalization;

namespace RosterRotation
{
    /// <summary>
    /// Imports the stock CAREER_LOG as a conservative legacy service record. The stock
    /// log is treated as evidence: EAC records only fields KSP actually supplies and
    /// never invents dates, vessels, or durations for historical flights.
    ///
    /// From the point EAC is installed, recovery-time imports receive a real EAC event
    /// timestamp. Those timestamped events can safely support program-first recognition;
    /// undated legacy events can block a false "first", but are never guessed into one.
    /// </summary>
    internal static class EACCareerHistory
    {
        internal static void SyncAllAvailableCrew()
        {
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null) return;
            var roster = HighLogic.CurrentGame.CrewRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                ProtoCrewMember pcm;
                try { pcm = roster[i]; } catch { continue; }
                if (pcm == null || string.IsNullOrEmpty(pcm.name)) continue;
                if (pcm.type != ProtoCrewMember.KerbalType.Crew) continue;

                // Do not fold an in-progress mission into the undated legacy import.
                // Its FLIGHT_LOG is captured with a real EAC timestamp at recovery,
                // which is what makes reliable program-first recognition possible.
                if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) continue;

                RosterRotationState.KerbalRecord rec;
                if (!RosterRotationState.Records.TryGetValue(pcm.name, out rec) || rec == null)
                {
                    rec = RosterRotationState.GetOrCreate(pcm.name);
                    rec.OriginalTrait = pcm.trait;
                    rec.OriginalType = pcm.type;
                }
                SyncCareerLog(pcm, rec, 0, "StockCareerLog");
            }
        }

        internal static int SyncCareerLog(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec)
        {
            return SyncCareerLog(pcm, rec, 0, "StockCareerLog");
        }

        internal static int SyncCareerLog(
            ProtoCrewMember pcm,
            RosterRotationState.KerbalRecord rec,
            double eventUT,
            string source)
        {
            if (pcm == null || rec == null) return 0;
            try
            {
                ConfigNode kerbal = new ConfigNode("KERBAL");
                pcm.Save(kerbal);
                ConfigNode career = kerbal.GetNode("CAREER_LOG") ?? kerbal.GetNode("careerLog");
                if (career == null) return 0;

                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < rec.CareerEvents.Count; i++)
                {
                    var e = rec.CareerEvents[i];
                    if (e != null) known.Add(BuildKey(e.FlightNumber, e.EventType, e.BodyName));
                }

                int added = 0;
                foreach (ConfigNode.Value value in career.values)
                {
                    if (value == null || string.IsNullOrEmpty(value.name) || string.IsNullOrEmpty(value.value)) continue;
                    int flight;
                    if (!int.TryParse(value.name, NumberStyles.Integer, CultureInfo.InvariantCulture, out flight)) continue;

                    string[] parts = value.value.Split(new[] { ',' }, 2);
                    string kind = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                    string body = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                    if (!IsUsefulCareerEvent(kind)) continue;

                    string key = BuildKey(flight, kind, body);
                    if (!known.Add(key)) continue;
                    rec.CareerEvents.Add(new RosterRotationState.CareerEventRecord
                    {
                        FlightNumber = flight,
                        EventType = kind,
                        BodyName = body,
                        Source = string.IsNullOrEmpty(source) ? "StockCareerLog" : source,
                        EventUT = eventUT > 0 ? eventUT : 0,
                        IsProgramFirst = false
                    });
                    added++;
                }
                return added;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACCareerHistory.SyncCareerLog", "Suppressed exception while importing stock career history.", ex);
                return 0;
            }
        }

        internal static int SyncCurrentFlightLog(
            ProtoCrewMember pcm,
            RosterRotationState.KerbalRecord rec,
            double eventUT)
        {
            if (pcm == null || rec == null || eventUT <= 0) return 0;
            try
            {
                ConfigNode kerbal = new ConfigNode("KERBAL");
                pcm.Save(kerbal);
                ConfigNode flightLog = kerbal.GetNode("FLIGHT_LOG") ?? kerbal.GetNode("flightLog");
                if (flightLog == null) return 0;

                int flight = 0;
                int.TryParse(flightLog.GetValue("flight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out flight);

                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < rec.CareerEvents.Count; i++)
                {
                    RosterRotationState.CareerEventRecord e = rec.CareerEvents[i];
                    if (e != null) known.Add(BuildKey(e.FlightNumber, e.EventType, e.BodyName));
                }

                int added = 0;
                foreach (ConfigNode.Value value in flightLog.values)
                {
                    if (value == null || string.IsNullOrEmpty(value.value)) continue;
                    if (string.Equals(value.name, "flight", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value.name, "flights", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] parts = value.value.Split(new[] { ',' }, 2);
                    string kind = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                    string body = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                    if (!IsUsefulCareerEvent(kind)) continue;

                    string key = BuildKey(flight, kind, body);
                    if (!known.Add(key)) continue;
                    rec.CareerEvents.Add(new RosterRotationState.CareerEventRecord
                    {
                        FlightNumber = flight,
                        EventType = kind,
                        BodyName = body,
                        Source = "EACFlightLog",
                        EventUT = eventUT,
                        IsProgramFirst = false
                    });
                    added++;
                }
                return added;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACCareerHistory.SyncCurrentFlightLog",
                    "Suppressed exception while importing current flight history.", ex);
                return 0;
            }
        }

        /// <summary>
        /// Marks all members of the recovered crew when EAC can prove that their newly
        /// timestamped event is the program's first known occurrence. Any older or
        /// undated legacy evidence prevents an award; ambiguity is never promoted.
        /// </summary>
        internal static int MarkProgramFirstsForRecoveredCrew(IList<ProtoCrewMember> crew, double recoveryUT)
        {
            if (crew == null || crew.Count == 0 || recoveryUT <= 0) return 0;

            var crewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < crew.Count; i++)
            {
                ProtoCrewMember pcm = crew[i];
                if (pcm == null || string.IsNullOrEmpty(pcm.name)) continue;
                crewNames.Add(pcm.name);

                RosterRotationState.KerbalRecord rec;
                if (!RosterRotationState.Records.TryGetValue(pcm.name, out rec) || rec == null) continue;
                for (int e = 0; e < rec.CareerEvents.Count; e++)
                {
                    RosterRotationState.CareerEventRecord ev = rec.CareerEvents[e];
                    if (!IsRecoveryEventFromNow(ev, recoveryUT)) continue;
                    string key = BuildProgramFirstKey(ev.EventType, ev.BodyName);
                    if (!string.IsNullOrEmpty(key)) candidateKeys.Add(key);
                }
            }

            int marked = 0;
            foreach (string key in candidateKeys)
            {
                if (ProgramAlreadyHasEarlierEvidence(key, crewNames, recoveryUT))
                    continue;

                var awardedNames = new List<string>();
                foreach (string name in crewNames)
                {
                    RosterRotationState.KerbalRecord rec;
                    if (!RosterRotationState.Records.TryGetValue(name, out rec) || rec == null) continue;

                    bool kerbalMarked = false;
                    for (int e = 0; e < rec.CareerEvents.Count; e++)
                    {
                        RosterRotationState.CareerEventRecord ev = rec.CareerEvents[e];
                        if (!IsRecoveryEventFromNow(ev, recoveryUT)) continue;
                        if (!string.Equals(BuildProgramFirstKey(ev.EventType, ev.BodyName), key, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!ev.IsProgramFirst)
                        {
                            ev.IsProgramFirst = true;
                            marked++;
                        }
                        kerbalMarked = true;
                    }

                    if (kerbalMarked) awardedNames.Add(name);
                }

                if (awardedNames.Count > 0)
                    RRLog.Info("[EAC] Program first recorded: " + FriendlyProgramFirstKey(key)
                        + " — crew: " + string.Join(", ", awardedNames.ToArray()) + ".");
            }

            return marked;
        }

        private static bool ProgramAlreadyHasEarlierEvidence(
            string key,
            HashSet<string> currentCrew,
            double recoveryUT)
        {
            const double tolerance = 0.001;

            foreach (KeyValuePair<string, RosterRotationState.KerbalRecord> kvp in RosterRotationState.Records)
            {
                RosterRotationState.KerbalRecord rec = kvp.Value;
                if (rec == null) continue;

                for (int i = 0; i < rec.CareerEvents.Count; i++)
                {
                    RosterRotationState.CareerEventRecord ev = rec.CareerEvents[i];
                    if (ev == null) continue;
                    if (!string.Equals(BuildProgramFirstKey(ev.EventType, ev.BodyName), key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ev.IsProgramFirst) return true;

                    bool isCurrentCrewEvent =
                        currentCrew.Contains(kvp.Key) &&
                        ev.EventUT > 0 &&
                        Math.Abs(ev.EventUT - recoveryUT) <= tolerance;

                    if (isCurrentCrewEvent) continue;

                    // Undated stock CAREER_LOG evidence could predate EAC installation.
                    // Treat it as prior history rather than risk a false program first.
                    if (ev.EventUT <= 0) return true;
                    if (ev.EventUT < recoveryUT - tolerance) return true;
                }
            }

            return false;
        }

        private static bool IsRecoveryEventFromNow(RosterRotationState.CareerEventRecord ev, double recoveryUT)
        {
            return ev != null &&
                   ev.EventUT > 0 &&
                   Math.Abs(ev.EventUT - recoveryUT) <= 0.001 &&
                   !string.IsNullOrEmpty(BuildProgramFirstKey(ev.EventType, ev.BodyName));
        }

        internal static string BuildProgramFirstKey(string kind, string body)
        {
            string canonical = CanonicalProgramFirstEvent(kind);
            if (string.IsNullOrEmpty(canonical)) return null;
            return canonical + "|" + (body ?? "").Trim();
        }

        private static string CanonicalProgramFirstEvent(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            if (kind.Equals("Orbit", StringComparison.OrdinalIgnoreCase)) return "Orbit";
            if (kind.Equals("Suborbit", StringComparison.OrdinalIgnoreCase) ||
                kind.Equals("SubOrbital", StringComparison.OrdinalIgnoreCase)) return "Suborbit";
            if (kind.Equals("Flyby", StringComparison.OrdinalIgnoreCase)) return "Flyby";
            if (kind.Equals("Escape", StringComparison.OrdinalIgnoreCase)) return "Escape";
            if (kind.Equals("ExitVessel", StringComparison.OrdinalIgnoreCase) ||
                kind.Equals("Spacewalk", StringComparison.OrdinalIgnoreCase) ||
                kind.Equals("SurfaceEVA", StringComparison.OrdinalIgnoreCase)) return "EVA";
            if (kind.Equals("PlantFlag", StringComparison.OrdinalIgnoreCase) ||
                kind.Equals("FlagPlant", StringComparison.OrdinalIgnoreCase)) return "PlantFlag";
            if (kind.IndexOf("Land", StringComparison.OrdinalIgnoreCase) >= 0) return "Land";
            return null;
        }

        internal static string FriendlyProgramFirstKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "Historic first";
            string[] parts = key.Split(new[] { '|' }, 2);
            string kind = parts.Length > 0 ? parts[0] : "";
            string body = parts.Length > 1 ? parts[1] : "";
            string target = string.IsNullOrEmpty(body) ? "" : " " + body;

            if (kind == "Orbit") return "First crew to orbit" + target;
            if (kind == "Suborbit") return "First Suborbital Flight" + (string.IsNullOrEmpty(body) ? "" : " at " + body);
            if (kind == "Flyby") return "First crew to fly by" + target;
            if (kind == "Escape") return "First crew to escape" + target + " SOI";
            if (kind == "EVA") return "First EVA" + (string.IsNullOrEmpty(body) ? "" : " at " + body);
            if (kind == "PlantFlag") return "First flag planted" + (string.IsNullOrEmpty(body) ? "" : " on " + body);
            if (kind == "Land") return "First crew to land" + (string.IsNullOrEmpty(body) ? "" : " on " + body);
            return "Historic first" + target;
        }

        private static bool IsUsefulCareerEvent(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            return !kind.Equals("Flight", StringComparison.OrdinalIgnoreCase)
                && !kind.Equals("Recover", StringComparison.OrdinalIgnoreCase)
                && !kind.Equals("BoardVessel", StringComparison.OrdinalIgnoreCase)
                && !kind.Equals("Launch", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildKey(int flight, string kind, string body)
        {
            return flight.ToString(CultureInfo.InvariantCulture) + "|" + (kind ?? "").Trim() + "|" + (body ?? "").Trim();
        }
    }
}

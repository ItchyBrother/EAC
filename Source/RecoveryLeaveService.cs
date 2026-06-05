using System;
using System.Collections.Generic;
using System.Globalization;

namespace RosterRotation
{
    internal sealed class PendingCrewRandRExtension
    {
        public string KerbalName;
        public string VesselName;
        public double ExtraDays;
        public double QueuedUT;
    }

    internal static class RecoveryLeaveService
    {
        // A very short test hop can otherwise compute only a few seconds of
        // recovery leave, which expires before the Astronaut Complex can show it.
        // Keep any positive EAC-managed recovery visible for at least one in-game
        // hour, while still respecting the configured maximum recovery cap.
        private const double MinimumVisibleRecoverySeconds = 60d * 60d;

        private static readonly Dictionary<string, PendingCrewRandRExtension> PendingCrewRandRExtensions =
            new Dictionary<string, PendingCrewRandRExtension>(StringComparer.OrdinalIgnoreCase);

        internal static void SavePendingCrewRandRExtensions(ConfigNode root)
        {
            if (root == null) return;
            root.RemoveNodes("CrashPending");

            foreach (KeyValuePair<string, PendingCrewRandRExtension> kvp in PendingCrewRandRExtensions)
            {
                PendingCrewRandRExtension pending = kvp.Value;
                if (pending == null || string.IsNullOrEmpty(kvp.Key) || pending.ExtraDays <= 0) continue;

                ConfigNode node = root.AddNode("CrashPending");
                node.AddValue("kerbalName", kvp.Key);
                if (!string.IsNullOrEmpty(pending.VesselName)) node.AddValue("vesselName", pending.VesselName);
                node.AddValue("extraDays", pending.ExtraDays.ToString("R", CultureInfo.InvariantCulture));
                node.AddValue("queuedUT", pending.QueuedUT.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        internal static void LoadPendingCrewRandRExtensions(ConfigNode root)
        {
            PendingCrewRandRExtensions.Clear();
            if (root == null) return;

            ConfigNode[] nodes = root.GetNodes("CrashPending");
            if (nodes == null || nodes.Length == 0) return;

            for (int i = 0; i < nodes.Length; i++)
            {
                ConfigNode node = nodes[i];
                if (node == null) continue;

                string kerbalName = node.GetValue("kerbalName");
                if (string.IsNullOrEmpty(kerbalName)) continue;

                double extraDays;
                if (!double.TryParse(node.GetValue("extraDays"), NumberStyles.Float, CultureInfo.InvariantCulture, out extraDays) || extraDays <= 0)
                    continue;

                double queuedUT;
                if (!double.TryParse(node.GetValue("queuedUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out queuedUT))
                    queuedUT = 0;

                PendingCrewRandRExtension pending = new PendingCrewRandRExtension
                {
                    KerbalName = kerbalName,
                    VesselName = node.GetValue("vesselName"),
                    ExtraDays = extraDays,
                    QueuedUT = queuedUT
                };
                PendingCrewRandRExtensions[kerbalName] = pending;
            }

            RRLog.Verbose("[EAC] loaded pending CrewRandR crash extensions=" + PendingCrewRandRExtensions.Count);
        }

        internal static void QueuePendingCrewRandRExtension(ProtoCrewMember pcm, CrashTrackedVessel tracked, double extraDays)
        {
            if (pcm == null || string.IsNullOrEmpty(pcm.name)) return;
            if (extraDays <= 0) return;

            PendingCrewRandRExtension pending;
            if (!PendingCrewRandRExtensions.TryGetValue(pcm.name, out pending) || pending == null)
            {
                pending = new PendingCrewRandRExtension
                {
                    KerbalName = pcm.name,
                    VesselName = SafeVesselNameText(tracked),
                    QueuedUT = Planetarium.GetUniversalTime(),
                    ExtraDays = 0
                };
                PendingCrewRandRExtensions[pcm.name] = pending;
            }

            pending.ExtraDays += extraDays;
            pending.QueuedUT = Planetarium.GetUniversalTime();
            if (tracked != null && !string.IsNullOrEmpty(tracked.VesselName))
                pending.VesselName = tracked.VesselName;

            RRLog.Verbose("[EAC] queued CrewRandR crash extension for " + pcm.name
                + ": extraDays=" + pending.ExtraDays.ToString("0.#")
                + ", vessel=" + (string.IsNullOrEmpty(pending.VesselName) ? "<unknown>" : pending.VesselName));
        }

        internal static void TryApplyPendingCrewRandRExtensions()
        {
            if (PendingCrewRandRExtensions.Count == 0) return;
            if (!CrewRandRAdapter.IsInstalled()) return;
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null) return;

            double now = Planetarium.GetUniversalTime();
            List<string> completed = null;
            bool changed = false;

            foreach (KeyValuePair<string, PendingCrewRandRExtension> kvp in PendingCrewRandRExtensions)
            {
                string kerbalName = kvp.Key;
                PendingCrewRandRExtension pending = kvp.Value;
                if (pending == null || string.IsNullOrEmpty(kerbalName))
                {
                    if (completed == null) completed = new List<string>();
                    completed.Add(kerbalName);
                    continue;
                }

                ProtoCrewMember pcm = FindCrewMemberByName(kerbalName);
                if (pcm == null) continue;

                double baseUntil;
                if (!CrewRandRAdapter.TryGetVacationUntilByName(kerbalName, out baseUntil) || baseUntil <= now)
                {
                    RRLog.Verbose("[EAC] waiting for CrewRandR base vacation for " + kerbalName);
                    continue;
                }

                double targetUntil = baseUntil + pending.ExtraDays * RosterRotationState.DaySeconds;
                if (!CrewRandRWriter.TrySetVacationUntil(kerbalName, targetUntil))
                {
                    RRLog.Verbose("[EAC] CrewRandR extension write not ready for " + kerbalName);
                    continue;
                }

                pcm.inactive = true;
                pcm.inactiveTimeEnd = Math.Max(pcm.inactiveTimeEnd, targetUntil);
                RosterRotationState.KerbalRecord rec = RosterRotationState.GetOrCreate(kerbalName);
                rec.RestUntilUT = Math.Max(rec.RestUntilUT, targetUntil);

                RRLog.Verbose("[EAC] CrewRandR crash extension applied for " + kerbalName
                    + ": baseUntil=" + baseUntil.ToString("0.###")
                    + ", targetUntil=" + targetUntil.ToString("0.###")
                    + ", extraDays=" + pending.ExtraDays.ToString("0.#"));

                if (completed == null) completed = new List<string>();
                completed.Add(kerbalName);
                changed = true;
            }

            if (completed != null)
            {
                for (int i = 0; i < completed.Count; i++)
                    PendingCrewRandRExtensions.Remove(completed[i]);
            }

            if (changed)
            {
                SaveScheduler.RequestSave("CrewRandR crash extension");

                try { ACPatches.ForceRefresh(); }
                catch (Exception ex)
                {
                    RRLog.Warn("[EAC] AC refresh after CrewRandR crash extension failed: " + ex.Message);
                }
            }
        }

        internal static double GetEacRecoveryUntilUT(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec, double now)
        {
            if (CrewRandRAdapter.IsInstalled()) return 0;
            if (rec == null || rec.RestUntilUT <= now) return 0;

            double until = rec.RestUntilUT;
            if (pcm != null && pcm.inactive && pcm.inactiveTimeEnd > now)
                until = Math.Max(until, pcm.inactiveTimeEnd);
            return until > now ? until : 0;
        }

        internal static bool IsEacRecoveryActive(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec, double now)
        {
            return GetEacRecoveryUntilUT(pcm, rec, now) > now;
        }

        internal static bool SyncEacRecoveryLeavesForRoster(double now, string reason)
        {
            if (CrewRandRAdapter.IsInstalled()) return false;
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null) return false;

            bool changed = false;
            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
            {
                if (pcm == null || string.IsNullOrEmpty(pcm.name)) continue;
                if (pcm.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) continue;
                if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;

                RosterRotationState.KerbalRecord rec;
                if (!RosterRotationState.Records.TryGetValue(pcm.name, out rec) || rec == null) continue;
                if (rec.Retired || rec.RestUntilUT <= now) continue;

                // KSP can rebuild recovered crew as Available during the recovery/scene
                // transition and drop pcm.inactive.  EAC's saved RestUntilUT is the
                // authoritative recovery leave state, so rehydrate the stock fields
                // whenever we see an active EAC-managed recovery record.
                bool kerbalChanged = false;
                if (!pcm.inactive)
                {
                    pcm.inactive = true;
                    kerbalChanged = true;
                }
                if (pcm.inactiveTimeEnd < rec.RestUntilUT)
                {
                    pcm.inactiveTimeEnd = rec.RestUntilUT;
                    kerbalChanged = true;
                }

                if (kerbalChanged)
                {
                    changed = true;
                    RRLog.Verbose("[EAC] Rehydrated EAC recovery leave for " + pcm.name
                        + ": until=" + rec.RestUntilUT.ToString("0.###")
                        + (string.IsNullOrEmpty(reason) ? "" : ", reason=" + reason));
                }
            }

            if (changed)
                SaveScheduler.RequestSave("rehydrate EAC recovery leave");

            return changed;
        }

        internal static void ApplyDefaultRecoveryRestIfNeeded(Vessel vessel, double now)
        {
            if (vessel == null) return;
            List<ProtoCrewMember> crew = vessel.GetVesselCrew();
            ApplyDefaultRecoveryRestIfNeeded(crew, SafeVesselName(vessel), now);
        }

        internal static bool ApplyDefaultRecoveryRestIfNeeded(IEnumerable<ProtoCrewMember> crew, string vesselName, double now)
        {
            if (CrewRandRAdapter.IsInstalled())
            {
                RRLog.Verbose("[EAC] CrewRandR present; skipping EAC base recovery leave for vessel=" + SafeVesselName(vesselName));
                return false;
            }

            if (crew == null) return false;

            bool sawCrew = false;
            bool appliedAny = false;
            foreach (ProtoCrewMember pcm in crew)
            {
                if (pcm == null) continue;
                sawCrew = true;
                if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;

                RosterRotationState.KerbalRecord rec = RosterRotationState.GetOrCreate(pcm.name);
                if (rec.Retired) continue;

                double missionDays = GetMissionDays(pcm, rec, now);
                double baseRecoveryDays = ComputeBaseRecoveryDays(pcm, rec, now);
                if (baseRecoveryDays <= 0)
                {
                    RRLog.Verbose("[EAC] Base recovery leave disabled or zero for " + pcm.name
                        + ": missionDays=" + missionDays.ToString("0.###")
                        + ", percent=" + RosterRotationState.RecoveryLeavePercent.ToString("0.###")
                        + ", maxDays=" + Math.Max(0, RosterRotationState.RestDays).ToString("0.###")
                        + ", missionStartUT=" + rec.MissionStartUT.ToString("0.###")
                        + ", missionAccumulatedSeconds=" + rec.MissionAccumulatedUT.ToString("0.###"));
                    continue;
                }

                double restUntil = now + baseRecoveryDays * RosterRotationState.DaySeconds;
                if (pcm.inactiveTimeEnd > now)
                    restUntil = Math.Max(restUntil, pcm.inactiveTimeEnd);

                pcm.inactive = true;
                pcm.inactiveTimeEnd = restUntil;
                rec.RestUntilUT = Math.Max(rec.RestUntilUT, restUntil);
                appliedAny = true;

                RRLog.Verbose("[EAC] Base recovery leave applied for " + pcm.name
                    + ": missionDays=" + missionDays.ToString("0.###")
                    + ", percent=" + RosterRotationState.RecoveryLeavePercent.ToString("0.###")
                    + ", baseRecoveryDays=" + baseRecoveryDays.ToString("0.###")
                    + ", maxDays=" + Math.Max(0, RosterRotationState.RestDays).ToString("0.###")
                    + ", missionStartUT=" + rec.MissionStartUT.ToString("0.###")
                    + ", missionAccumulatedSeconds=" + rec.MissionAccumulatedUT.ToString("0.###")
                    + ", inactiveTimeEnd=" + pcm.inactiveTimeEnd.ToString("0.###")
                    + ", rec.RestUntilUT=" + rec.RestUntilUT.ToString("0.###"));
            }

            if (!appliedAny)
            {
                RRLog.Verbose("[EAC] Base recovery leave not applied for vessel=" + SafeVesselName(vesselName)
                    + (sawCrew ? ": no crew had a positive personal mission duration." : ": no recovered crew found."));
                return false;
            }

            SaveScheduler.RequestSave("base recovery leave");
            return true;
        }

        internal static CrashApplySource ApplyCrashRecoveryTime(
            ProtoCrewMember pcm,
            RosterRotationState.KerbalRecord rec,
            Vessel vessel,
            double now,
            CrashOutcome outcome,
            CrashTrackedVessel tracked)
        {
            bool crewRandRInstalled = CrewRandRAdapter.IsInstalled();
            double baseUntil = now;

            if (crewRandRInstalled)
            {
                double vacationUntil;
                if (CrewRandRAdapter.TryGetVacationUntilByName(pcm.name, out vacationUntil) && vacationUntil > now)
                    baseUntil = vacationUntil;
                else if (pcm.inactiveTimeEnd > now)
                    baseUntil = Math.Max(baseUntil, pcm.inactiveTimeEnd);

                double newUntil = baseUntil + outcome.ExtraDays * RosterRotationState.DaySeconds;
                if (CrewRandRWriter.TrySetVacationUntil(pcm.name, newUntil))
                {
                    pcm.inactive = true;
                    pcm.inactiveTimeEnd = Math.Max(pcm.inactiveTimeEnd, newUntil);
                    rec.RestUntilUT = Math.Max(rec.RestUntilUT, newUntil);
                    return CrashApplySource.CrewRandR;
                }

                double displayUntil = newUntil;
                if (displayUntil <= now)
                    displayUntil = now + outcome.ExtraDays * RosterRotationState.DaySeconds;

                pcm.inactive = true;
                pcm.inactiveTimeEnd = Math.Max(pcm.inactiveTimeEnd, displayUntil);
                rec.RestUntilUT = Math.Max(rec.RestUntilUT, displayUntil);

                try
                {
                    if (pcm.rosterStatus != ProtoCrewMember.RosterStatus.Dead
                     && pcm.rosterStatus != ProtoCrewMember.RosterStatus.Missing
                     && pcm.rosterStatus != ProtoCrewMember.RosterStatus.Assigned)
                    {
                        pcm.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                    }
                }
                catch (Exception ex)
                {
                    RRLog.VerboseExceptionOnce("RecoveryLeaveService.ApplyCrashRecoveryTime",
                        "Suppressed exception while setting roster status during pending CrewRandR extension.", ex);
                }

                QueuePendingCrewRandRExtension(pcm, tracked, outcome.ExtraDays);
                return CrashApplySource.CrewRandRPending;
            }

            double missionDays = GetMissionDays(pcm, rec, now);
            double baseRecoveryDays = ComputeBaseRecoveryDays(pcm, rec, now);
            baseUntil = now + baseRecoveryDays * RosterRotationState.DaySeconds;
            if (pcm.inactiveTimeEnd > now)
                baseUntil = Math.Max(baseUntil, pcm.inactiveTimeEnd);

            double eacUntil = baseUntil + outcome.ExtraDays * RosterRotationState.DaySeconds;
            pcm.inactive = true;
            pcm.inactiveTimeEnd = Math.Max(pcm.inactiveTimeEnd, eacUntil);
            rec.RestUntilUT = Math.Max(rec.RestUntilUT, eacUntil);

            RRLog.Verbose("[EAC] Crash recovery leave computed for " + pcm.name
                + ": missionDays=" + missionDays.ToString("0.###")
                + ", percent=" + RosterRotationState.RecoveryLeavePercent.ToString("0.###")
                + ", baseRecoveryDays=" + baseRecoveryDays.ToString("0.###")
                + ", extraDays=" + outcome.ExtraDays.ToString("0.###")
                + ", maxDays=" + Math.Max(0, RosterRotationState.RestDays).ToString("0.###")
                + ", missionStartUT=" + rec.MissionStartUT.ToString("0.###")
                + ", missionAccumulatedSeconds=" + rec.MissionAccumulatedUT.ToString("0.###")
                + ", until=" + eacUntil.ToString("0.###"));

            return CrashApplySource.EAC;
        }

        private static double ComputeBaseRecoveryDays(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec, double now)
        {
            double percent = Math.Max(0, RosterRotationState.RecoveryLeavePercent);
            if (percent <= 0) return 0;

            double missionDays = GetMissionDays(pcm, rec, now);
            if (missionDays <= 0) return 0;

            double computedDays = missionDays * (percent / 100.0);
            double maxDays = Math.Max(0, RosterRotationState.RestDays);
            if (maxDays <= 0) return 0;

            computedDays = Math.Min(computedDays, maxDays);

            if (computedDays > 0)
            {
                double daySeconds = RosterRotationState.DaySeconds;
                double minimumDays = daySeconds > 0 ? MinimumVisibleRecoverySeconds / daySeconds : 0;
                minimumDays = Math.Min(minimumDays, maxDays);
                computedDays = Math.Max(computedDays, minimumDays);
            }

            return Math.Max(0, computedDays);
        }

        private static double GetMissionDays(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec, double now)
        {
            if (pcm == null || rec == null) return 0;
            double daySeconds = RosterRotationState.DaySeconds;
            if (daySeconds <= 0) return 0;
            double missionSeconds = MissionTimeTracker.GetMissionSeconds(rec, now);
            if (missionSeconds <= 0) return 0;
            return missionSeconds / daySeconds;
        }

        private static ProtoCrewMember FindCrewMemberByName(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName)) return null;
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null) return null;

            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
            {
                if (pcm == null) continue;
                if (string.Equals(pcm.name, kerbalName, StringComparison.OrdinalIgnoreCase))
                    return pcm;
            }
            return null;
        }

        private static string SafeVesselName(Vessel vessel)
        {
            if (vessel == null) return "<null>";
            return !string.IsNullOrEmpty(vessel.vesselName) ? vessel.vesselName : vessel.id.ToString();
        }

        private static string SafeVesselName(string vesselName)
        {
            return !string.IsNullOrEmpty(vesselName) ? vesselName : "<unknown>";
        }

        private static string SafeVesselNameText(CrashTrackedVessel tracked)
        {
            if (tracked == null) return "the vessel";
            return !string.IsNullOrEmpty(tracked.VesselName) ? tracked.VesselName : tracked.VesselId.ToString();
        }
    }
}

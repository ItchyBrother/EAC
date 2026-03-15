using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace RosterRotation
{
    internal enum CrashOutcomeTier
    {
        NoInjury = 0,
        Minor = 1,
        Moderate = 2,
        Severe = 3,
        Critical = 4,
        PermanentDisability = 5
    }

    internal enum CrashApplySource
    {
        EAC = 0,
        CrewRandR = 1,
        CrewRandRPending = 2
    }

    internal sealed class CrashOutcome
    {
        public CrashOutcomeTier Tier;
        public int Roll;
        public int Modifier;
        public double ExtraDays;
        public string Label;
    }

    internal sealed class PendingCrewRandRExtension
    {
        public string KerbalName;
        public string VesselName;
        public double ExtraDays;
        public double QueuedUT;
    }

    internal sealed class CrashTrackedVessel
    {
        public Guid VesselId;
        public string VesselName;
        public int MaxPartsObserved;
        public int LastSeenParts;
        public int EstimatedLostParts;
        public int ExplosionEvents;
        public bool SawCrashEvent;
        public int MaxCrewObserved;
        public readonly HashSet<string> CrewSnapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> KilledCrew = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public double FirstObservedUT;
        public double LastObservedUT;

        public void SnapshotCrew(Vessel vessel)
        {
            if (vessel == null) return;
            List<ProtoCrewMember> crew = vessel.GetVesselCrew();
            if (crew == null) return;

            int liveCrew = 0;
            for (int i = 0; i < crew.Count; i++)
            {
                ProtoCrewMember pcm = crew[i];
                if (pcm == null || string.IsNullOrEmpty(pcm.name)) continue;
                CrewSnapshot.Add(pcm.name);
                CrashSeverityState.RememberCrewIncident(pcm.name, VesselId);
                liveCrew++;
            }

            MaxCrewObserved = Math.Max(MaxCrewObserved, liveCrew + KilledCrew.Count);
        }
    }

    internal static class CrashSeverityState
    {
        private static readonly Dictionary<Guid, CrashTrackedVessel> Tracked = new Dictionary<Guid, CrashTrackedVessel>();
        private static readonly Dictionary<string, Guid> CrewToVessel = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, PendingCrewRandRExtension> PendingCrewRandRExtensions = new Dictionary<string, PendingCrewRandRExtension>(StringComparer.OrdinalIgnoreCase);

        internal static void RememberCrewIncident(string kerbalName, Guid vesselId)
        {
            if (string.IsNullOrEmpty(kerbalName) || vesselId == Guid.Empty) return;
            CrewToVessel[kerbalName] = vesselId;
        }

        internal static void QueuePendingCrewRandRExtension(ProtoCrewMember pcm, CrashTrackedVessel tracked, double extraDays)
        {
            if (pcm == null || string.IsNullOrEmpty(pcm.name)) return;
            if (extraDays <= 0) return;

            PendingCrewRandRExtension pending;
            if (!PendingCrewRandRExtensions.TryGetValue(pcm.name, out pending) || pending == null)
            {
                pending = new PendingCrewRandRExtension();
                pending.KerbalName = pcm.name;
                pending.VesselName = SafeVesselNameText(tracked);
                pending.QueuedUT = Planetarium.GetUniversalTime();
                pending.ExtraDays = 0;
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

        internal static void SavePendingCrewRandRExtensions(ConfigNode root)
        {
            if (root == null) return;
            root.RemoveNodes("CrashPending");

            foreach (KeyValuePair<string, PendingCrewRandRExtension> kvp in PendingCrewRandRExtensions)
            {
                PendingCrewRandRExtension pending = kvp.Value;
                if (pending == null || string.IsNullOrEmpty(kvp.Key) || pending.ExtraDays <= 0) continue;

                ConfigNode n = root.AddNode("CrashPending");
                n.AddValue("kerbalName", kvp.Key);
                if (!string.IsNullOrEmpty(pending.VesselName)) n.AddValue("vesselName", pending.VesselName);
                n.AddValue("extraDays", pending.ExtraDays.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                n.AddValue("queuedUT", pending.QueuedUT.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
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
                ConfigNode n = nodes[i];
                if (n == null) continue;
                string kerbalName = n.GetValue("kerbalName");
                if (string.IsNullOrEmpty(kerbalName)) continue;

                double extraDays;
                if (!double.TryParse(n.GetValue("extraDays"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out extraDays) || extraDays <= 0)
                    continue;

                double queuedUT;
                if (!double.TryParse(n.GetValue("queuedUT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out queuedUT))
                    queuedUT = 0;

                PendingCrewRandRExtension pending = new PendingCrewRandRExtension();
                pending.KerbalName = kerbalName;
                pending.VesselName = n.GetValue("vesselName");
                pending.ExtraDays = extraDays;
                pending.QueuedUT = queuedUT;
                PendingCrewRandRExtensions[kerbalName] = pending;
            }

            RRLog.Verbose("[EAC] loaded pending CrewRandR crash extensions=" + PendingCrewRandRExtensions.Count);
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

        internal static void ObserveLoadedVessels()
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;

            IList<Vessel> loaded = FlightGlobals.VesselsLoaded;
            if (loaded == null) return;

            for (int i = 0; i < loaded.Count; i++)
            {
                Vessel vessel = loaded[i];
                if (vessel == null) continue;
                if (vessel.isEVA) continue;
                if (vessel.GetCrewCount() <= 0) continue;

                ObserveVessel(vessel);
            }
        }

        internal static void ObserveVessel(Vessel vessel)
        {
            if (vessel == null || vessel.id == Guid.Empty) return;
            if (vessel.GetCrewCount() <= 0) return;

            CrashTrackedVessel tracked;
            if (!Tracked.TryGetValue(vessel.id, out tracked) || tracked == null)
            {
                tracked = new CrashTrackedVessel();
                tracked.VesselId = vessel.id;
                tracked.VesselName = SafeVesselName(vessel);
                tracked.FirstObservedUT = Planetarium.GetUniversalTime();
                tracked.LastObservedUT = tracked.FirstObservedUT;
                tracked.MaxPartsObserved = CurrentPartCount(vessel);
                tracked.LastSeenParts = tracked.MaxPartsObserved;
                tracked.SnapshotCrew(vessel);
                Tracked[vessel.id] = tracked;

                RRLog.Verbose("[EAC] tracking crewed vessel=" + tracked.VesselName
                    + ", id=" + tracked.VesselId
                    + ", parts=" + tracked.MaxPartsObserved
                    + ", crew=" + tracked.MaxCrewObserved);
                return;
            }

            int currentParts = CurrentPartCount(vessel);
            if (currentParts > tracked.MaxPartsObserved)
                tracked.MaxPartsObserved = currentParts;

            if (tracked.LastSeenParts > 0 && currentParts > 0 && currentParts < tracked.LastSeenParts)
            {
                int delta = tracked.LastSeenParts - currentParts;
                tracked.EstimatedLostParts += delta;
                RRLog.Verbose("[EAC] part-loss delta tracked for vessel=" + tracked.VesselName
                    + ": delta=" + delta
                    + ", estimatedLostParts=" + tracked.EstimatedLostParts
                    + ", lastSeenParts=" + tracked.LastSeenParts
                    + ", currentParts=" + currentParts);
            }

            tracked.LastSeenParts = currentParts;
            tracked.LastObservedUT = Planetarium.GetUniversalTime();
            tracked.VesselName = SafeVesselName(vessel);
            tracked.SnapshotCrew(vessel);
        }

        internal static void MarkPartDestroyed(Part part)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (part == null || part.vessel == null) return;
            CrashTrackedVessel tracked = GetOrCreate(part.vessel);
            if (tracked == null) return;

            tracked.EstimatedLostParts = Math.Max(tracked.EstimatedLostParts, tracked.MaxPartsObserved - Math.Max(0, CurrentPartCount(part.vessel)));
            tracked.LastObservedUT = Planetarium.GetUniversalTime();
            tracked.SnapshotCrew(part.vessel);

            RRLog.Verbose("[EAC] onPartDie: vessel=" + SafeVesselName(part.vessel)
                + ", part=" + SafePartName(part)
                + ", estimatedLostParts=" + tracked.EstimatedLostParts);
        }

        internal static void MarkExplosionEvent(Vessel vessel)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (vessel == null) return;
            CrashTrackedVessel tracked = GetOrCreate(vessel);
            if (tracked == null) return;

            tracked.ExplosionEvents++;
            tracked.LastObservedUT = Planetarium.GetUniversalTime();
            tracked.SnapshotCrew(vessel);

            RRLog.Verbose("[EAC] onPartExplode: vessel=" + SafeVesselName(vessel)
                + ", explosions=" + tracked.ExplosionEvents
                + ", estimatedLostParts=" + tracked.EstimatedLostParts);
        }

        internal static void MarkCrashEvent(Vessel vessel, string context)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (vessel == null) return;
            CrashTrackedVessel tracked = GetOrCreate(vessel);
            if (tracked == null) return;

            tracked.SawCrashEvent = true;
            tracked.LastObservedUT = Planetarium.GetUniversalTime();
            tracked.SnapshotCrew(vessel);

            RRLog.Verbose("[EAC] onCrash: vessel=" + SafeVesselName(vessel)
                + ", context=" + (string.IsNullOrEmpty(context) ? "<none>" : context)
                + ", estimatedLostParts=" + tracked.EstimatedLostParts
                + ", explosions=" + tracked.ExplosionEvents);
        }

        internal static void MarkCrewDeath(ProtoCrewMember pcm)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (pcm == null || string.IsNullOrEmpty(pcm.name)) return;

            CrashTrackedVessel tracked = null;
            Guid vesselId;
            if (CrewToVessel.TryGetValue(pcm.name, out vesselId))
                Tracked.TryGetValue(vesselId, out tracked);

            if (tracked == null)
            {
                foreach (KeyValuePair<Guid, CrashTrackedVessel> kvp in Tracked)
                {
                    if (kvp.Value == null) continue;
                    if (!kvp.Value.CrewSnapshot.Contains(pcm.name)) continue;
                    tracked = kvp.Value;
                    CrewToVessel[pcm.name] = kvp.Key;
                    break;
                }
            }

            if (tracked == null) return;

            tracked.KilledCrew.Add(pcm.name);
            tracked.LastObservedUT = Planetarium.GetUniversalTime();
            tracked.MaxCrewObserved = Math.Max(tracked.MaxCrewObserved, tracked.CrewSnapshot.Count + tracked.KilledCrew.Count);

            RRLog.Verbose("[EAC] crew fatality tracked: vessel=" + tracked.VesselName
                + ", kerbal=" + pcm.name
                + ", fatalities=" + tracked.KilledCrew.Count);
        }

        internal static bool TryGet(Guid vesselId, out CrashTrackedVessel tracked)
        {
            return Tracked.TryGetValue(vesselId, out tracked) && tracked != null;
        }

        internal static void Forget(Guid vesselId)
        {
            CrashTrackedVessel tracked;
            if (!Tracked.TryGetValue(vesselId, out tracked) || tracked == null)
            {
                Tracked.Remove(vesselId);
                return;
            }

            foreach (string crewName in tracked.CrewSnapshot)
            {
                Guid mapped;
                if (CrewToVessel.TryGetValue(crewName, out mapped) && mapped == vesselId)
                    CrewToVessel.Remove(crewName);
            }
            foreach (string crewName in tracked.KilledCrew)
            {
                Guid mapped;
                if (CrewToVessel.TryGetValue(crewName, out mapped) && mapped == vesselId)
                    CrewToVessel.Remove(crewName);
            }

            RRLog.Verbose("[EAC] forgetting tracked vessel=" + tracked.VesselName
                + ", estimatedLostParts=" + tracked.EstimatedLostParts
                + ", explosions=" + tracked.ExplosionEvents
                + ", fatalities=" + tracked.KilledCrew.Count);

            Tracked.Remove(vesselId);
        }

        internal static void HandleRecovery(Vessel vessel, double now)
        {
            if (vessel == null) return;

            if (!RosterRotationState.CrashPenaltyEnabled)
            {
                Forget(vessel.id);
                ApplyDefaultRecoveryRestIfNeeded(vessel, now);
                return;
            }

            CrashTrackedVessel tracked;
            bool hadTracked = TryGet(vessel.id, out tracked);
            if (!hadTracked)
            {
                RRLog.Verbose("[EAC] recovery handler: no tracked crash state for vessel=" + SafeVesselName(vessel));
                ApplyDefaultRecoveryRestIfNeeded(vessel, now);
                return;
            }

            int currentParts = CurrentPartCount(vessel);
            if (tracked.MaxPartsObserved > 0 && currentParts > 0 && tracked.MaxPartsObserved > currentParts)
            {
                tracked.EstimatedLostParts = Math.Max(tracked.EstimatedLostParts, tracked.MaxPartsObserved - currentParts);
            }

            int totalCrew = Math.Max(tracked.MaxCrewObserved, tracked.CrewSnapshot.Count + tracked.KilledCrew.Count);
            int severityParts = Math.Max(tracked.EstimatedLostParts, tracked.ExplosionEvents);
            int fatalities = tracked.KilledCrew.Count;

            RRLog.Verbose("[EAC] Crash severity evaluation for vessel=" + SafeVesselName(vessel)
                + ", severityParts=" + severityParts
                + ", estimatedLostParts=" + tracked.EstimatedLostParts
                + ", explosions=" + tracked.ExplosionEvents
                + ", fatalities=" + fatalities
                + ", totalCrew=" + totalCrew
                + ", sawCrashEvent=" + tracked.SawCrashEvent);

            if (severityParts <= 0 && fatalities <= 0 && !tracked.SawCrashEvent)
            {
                RRLog.Verbose("[EAC] No crash injury outcome for vessel=" + SafeVesselName(vessel) + "; applying normal recovery rules only.");
                Forget(vessel.id);
                ApplyDefaultRecoveryRestIfNeeded(vessel, now);
                return;
            }

            List<ProtoCrewMember> survivors = vessel.GetVesselCrew();
            if (survivors == null || survivors.Count == 0)
            {
                RRLog.Verbose("[EAC] No surviving crew to process for vessel=" + SafeVesselName(vessel));
                Forget(vessel.id);
                return;
            }

            for (int i = 0; i < survivors.Count; i++)
            {
                ProtoCrewMember pcm = survivors[i];
                if (pcm == null) continue;
                if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;

                RosterRotationState.KerbalRecord rec = RosterRotationState.GetOrCreate(pcm.name);
                if (rec.Retired) continue;

                CrashOutcome outcome = RollOutcome(tracked, totalCrew);
                RRLog.Verbose("[EAC] Crash roll for " + pcm.name
                    + ": roll=" + outcome.Roll
                    + ", modifier=" + outcome.Modifier
                    + ", tier=" + outcome.Tier
                    + ", label=" + outcome.Label
                    + ", extraDays=" + outcome.ExtraDays.ToString("0.#"));

                if (outcome.Tier == CrashOutcomeTier.PermanentDisability)
                {
                    ApplyPermanentDisability(pcm, rec, now, tracked, outcome, totalCrew, severityParts, fatalities);
                }
                else if (outcome.Tier == CrashOutcomeTier.NoInjury)
                {
                    RRLog.Verbose("[EAC] Crash roll resulted in no injury for " + pcm.name
                        + ": roll=" + outcome.Roll
                        + ", modifier=" + outcome.Modifier);
                    ApplyDefaultRecoveryRestIfNeeded(vessel, now);
                    PostRecoveryNotification(pcm, tracked, outcome);
                }
                else
                {
                    CrashApplySource applySource = ApplyRecoveryTime(pcm, rec, now, outcome, tracked);
                    string sourceText = applySource == CrashApplySource.CrewRandR ? "CrewRandR"
                        : (applySource == CrashApplySource.CrewRandRPending ? "CrewRandR-pending" : "EAC");
                    RRLog.Verbose("[EAC] Recovery leave applied for " + pcm.name
                        + ": source=" + sourceText
                        + ", inactive=" + pcm.inactive
                        + ", inactiveTimeEnd=" + pcm.inactiveTimeEnd.ToString("0.###")
                        + ", rec.RestUntilUT=" + rec.RestUntilUT.ToString("0.###"));
                    PostRecoveryNotification(pcm, tracked, outcome);
                }
            }

            Forget(vessel.id);
            SaveScheduler.RequestSave("crash recovery");
        }

        private static void ApplyDefaultRecoveryRestIfNeeded(Vessel vessel, double now)
        {
            if (vessel == null) return;
            if (CrewRandRAdapter.IsInstalled())
            {
                RRLog.Verbose("[EAC] CrewRandR present; skipping EAC base restDays for vessel=" + SafeVesselName(vessel));
                return;
            }

            List<ProtoCrewMember> crew = vessel.GetVesselCrew();
            if (crew == null || crew.Count == 0) return;

            for (int i = 0; i < crew.Count; i++)
            {
                ProtoCrewMember pcm = crew[i];
                if (pcm == null) continue;
                if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;

                RosterRotationState.KerbalRecord rec = RosterRotationState.GetOrCreate(pcm.name);
                if (rec.Retired) continue;

                double restUntil = now + Math.Max(0, RosterRotationState.RestDays) * RosterRotationState.DaySeconds;
                if (pcm.inactiveTimeEnd > now)
                    restUntil = Math.Max(restUntil, pcm.inactiveTimeEnd);

                pcm.inactive = true;
                pcm.inactiveTimeEnd = restUntil;
                rec.RestUntilUT = Math.Max(rec.RestUntilUT, restUntil);

                RRLog.Verbose("[EAC] Base recovery rest applied for " + pcm.name
                    + ": restDays=" + RosterRotationState.RestDays
                    + ", inactiveTimeEnd=" + pcm.inactiveTimeEnd.ToString("0.###")
                    + ", rec.RestUntilUT=" + rec.RestUntilUT.ToString("0.###"));
            }
        }

        private static CrashOutcome RollOutcome(CrashTrackedVessel tracked, int totalCrew)
        {
            int modifier = ComputeSeverityModifier(tracked, totalCrew);
            int roll = UnityEngine.Random.Range(1, 101) + modifier;
            int noInjuryChance = ComputeNoInjuryChance(tracked, totalCrew);
            int noInjuryRoll = UnityEngine.Random.Range(1, 101);

            if (noInjuryRoll <= noInjuryChance)
                return new CrashOutcome { Tier = CrashOutcomeTier.NoInjury, Roll = roll, Modifier = modifier, ExtraDays = 0, Label = "escaped injury" };
            if (roll > 185)
                return new CrashOutcome { Tier = CrashOutcomeTier.PermanentDisability, Roll = roll, Modifier = modifier, ExtraDays = 0, Label = "permanently disabled" };
            if (roll > 140)
                return new CrashOutcome { Tier = CrashOutcomeTier.Critical, Roll = roll, Modifier = modifier, ExtraDays = 30, Label = "critically injured" };
            if (roll > 95)
                return new CrashOutcome { Tier = CrashOutcomeTier.Severe, Roll = roll, Modifier = modifier, ExtraDays = 14, Label = "seriously injured" };
            if (roll > 50)
                return new CrashOutcome { Tier = CrashOutcomeTier.Moderate, Roll = roll, Modifier = modifier, ExtraDays = 7, Label = "injured" };
            return new CrashOutcome { Tier = CrashOutcomeTier.Minor, Roll = roll, Modifier = modifier, ExtraDays = 3, Label = "shaken up" };
        }

        private static int ComputeNoInjuryChance(CrashTrackedVessel tracked, int totalCrew)
        {
            int parts = tracked != null ? Math.Max(tracked.EstimatedLostParts, tracked.ExplosionEvents) : 0;
            int fatalities = tracked != null ? tracked.KilledCrew.Count : 0;
            int crew = Math.Max(1, totalCrew);

            int chance = 35;
            chance -= Math.Min(24, Math.Max(0, parts) * 3);
            chance -= Math.Min(18, Math.Max(0, fatalities) * 12);
            if (tracked != null && tracked.SawCrashEvent)
                chance -= 4;
            if (crew > 1)
                chance -= Math.Min(6, crew - 1);

            return Mathf.Clamp(chance, 2, 35);
        }

        private static int ComputeSeverityModifier(CrashTrackedVessel tracked, int totalCrew)
        {
            int parts = tracked != null ? Math.Max(tracked.EstimatedLostParts, tracked.ExplosionEvents) : 0;
            int fatalities = tracked != null ? tracked.KilledCrew.Count : 0;
            int crew = Math.Max(1, totalCrew);
            int partsBonus = Math.Min(60, Math.Max(0, parts) * 4);
            int fatalityBonus = Math.Max(0, fatalities) * 25;
            int casualtyBonus = fatalities > 0 ? Mathf.RoundToInt(30f * fatalities / crew) : 0;
            int crashBonus = tracked != null && tracked.SawCrashEvent ? 10 : 0;
            return Math.Min(120, partsBonus + fatalityBonus + casualtyBonus + crashBonus);
        }

        private static CrashApplySource ApplyRecoveryTime(
            ProtoCrewMember pcm,
            RosterRotationState.KerbalRecord rec,
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
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("CrashSeverity.cs:662", "Suppressed exception in CrashSeverity.cs:662", ex); }

                CrashSeverityState.QueuePendingCrewRandRExtension(pcm, tracked, outcome.ExtraDays);
                return CrashApplySource.CrewRandRPending;
            }

            baseUntil = now + Math.Max(0, RosterRotationState.RestDays) * RosterRotationState.DaySeconds;
            if (pcm.inactiveTimeEnd > now)
                baseUntil = Math.Max(baseUntil, pcm.inactiveTimeEnd);

            double eacUntil = baseUntil + outcome.ExtraDays * RosterRotationState.DaySeconds;
            pcm.inactive = true;
            pcm.inactiveTimeEnd = Math.Max(pcm.inactiveTimeEnd, eacUntil);
            rec.RestUntilUT = Math.Max(rec.RestUntilUT, eacUntil);
            return CrashApplySource.EAC;
        }

        private static void ApplyPermanentDisability(
            ProtoCrewMember pcm,
            RosterRotationState.KerbalRecord rec,
            double now,
            CrashTrackedVessel tracked,
            CrashOutcome outcome,
            int totalCrew,
            int severityParts,
            int fatalities)
        {
            if (string.IsNullOrEmpty(rec.OriginalTrait)) rec.OriginalTrait = pcm.trait;
            rec.OriginalType = pcm.type;
            rec.Retired = true;
            rec.RetiredUT = now;
            rec.ExperienceAtRetire = 0;
            rec.RetirementScheduled = false;
            rec.RetirementScheduledUT = 0;
            rec.RetirementWarned = false;
            rec.RestUntilUT = Math.Max(rec.RestUntilUT, now + RosterRotationState.YearSeconds * 1000.0);

            pcm.inactive = true;
            pcm.inactiveTimeEnd = Math.Max(pcm.inactiveTimeEnd, rec.RestUntilUT);
            RosterRotationState.InvalidateRetiredCache();
            RetiredKerbalCleanupService.ResetAutoCleanupRequest(pcm.name);

            RRLog.Verbose("[EAC] Permanent disability applied for " + pcm.name
                + ": roll=" + outcome.Roll
                + ", modifier=" + outcome.Modifier
                + ", severityParts=" + severityParts
                + ", fatalities=" + fatalities
                + ", totalCrew=" + totalCrew);

            string body = pcm.name + " was permanently disabled in a crash on " + SafeVesselNameText(tracked)
                + ". Roll=" + outcome.Roll
                + " (modifier +" + outcome.Modifier + ")"
                + ", losses=" + severityParts
                + ", fatalities=" + fatalities
                + "."
                + " The kerbal has been medically retired with 0 stars and cannot be recalled.";

            RosterRotationState.PostNotification(
                EACNotificationType.Retirement,
                "Medical Retirement — " + pcm.name,
                body,
                MessageSystemButton.MessageButtonColor.RED,
                MessageSystemButton.ButtonIcons.ALERT,
                10f);
        }

        private static void PostRecoveryNotification(
            ProtoCrewMember pcm,
            CrashTrackedVessel tracked,
            CrashOutcome outcome)
        {
            string body;
            if (outcome.Tier == CrashOutcomeTier.NoInjury)
            {
                body = pcm.name + " was in a crash on " + SafeVesselNameText(tracked)
                    + ", but escaped injury and needs no additional recovery time.";
            }
            else
            {
                body = pcm.name + " was " + outcome.Label + " in a crash on " + SafeVesselNameText(tracked)
                    + ". They will need an additional " + outcome.ExtraDays.ToString("0.#") + " days to recover.";
            }

            RosterRotationState.PostNotification(
                "Crash Recovery — " + pcm.name,
                body,
                MessageSystemButton.MessageButtonColor.ORANGE,
                MessageSystemButton.ButtonIcons.ALERT,
                8f);
        }

        private static CrashTrackedVessel GetOrCreate(Vessel vessel)
        {
            if (vessel == null || vessel.id == Guid.Empty) return null;
            if (vessel.GetCrewCount() <= 0) return null;

            CrashTrackedVessel tracked;
            if (!Tracked.TryGetValue(vessel.id, out tracked) || tracked == null)
            {
                tracked = new CrashTrackedVessel();
                tracked.VesselId = vessel.id;
                tracked.VesselName = SafeVesselName(vessel);
                tracked.FirstObservedUT = Planetarium.GetUniversalTime();
                tracked.LastObservedUT = tracked.FirstObservedUT;
                tracked.MaxPartsObserved = CurrentPartCount(vessel);
                tracked.LastSeenParts = tracked.MaxPartsObserved;
                tracked.SnapshotCrew(vessel);
                Tracked[vessel.id] = tracked;

                RRLog.Verbose("[EAC] crash tracking created for vessel=" + tracked.VesselName
                    + ", id=" + tracked.VesselId
                    + ", parts=" + tracked.MaxPartsObserved
                    + ", crew=" + tracked.MaxCrewObserved);
            }
            return tracked;
        }

        private static int CurrentPartCount(Vessel vessel)
        {
            if (vessel == null || vessel.parts == null) return 0;
            return vessel.parts.Count;
        }

        private static string SafePartName(Part part)
        {
            if (part == null) return "<null>";
            if (part.partInfo != null && !string.IsNullOrEmpty(part.partInfo.title)) return part.partInfo.title;
            if (!string.IsNullOrEmpty(part.partName)) return part.partName;
            return !string.IsNullOrEmpty(part.name) ? part.name : "<unnamed>";
        }

        private static string SafeVesselName(Vessel vessel)
        {
            if (vessel == null) return "<null>";
            return !string.IsNullOrEmpty(vessel.vesselName) ? vessel.vesselName : vessel.id.ToString();
        }

        private static string SafeVesselNameText(CrashTrackedVessel tracked)
        {
            if (tracked == null) return "the vessel";
            return !string.IsNullOrEmpty(tracked.VesselName) ? tracked.VesselName : tracked.VesselId.ToString();
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class EACCrashIncidentCollector : MonoBehaviour
    {
        private float _nextObserveTime;

        private void Start()
        {
            RRLog.Verbose("[EAC] Crash incident collector initialized");
            GameEvents.onPartDie.Add(OnPartDie);
            GameEvents.onPartExplode.Add(OnPartExplode);
            GameEvents.onCrash.Add(OnCrash);
            GameEvents.onKerbalStatusChange.Add(OnKerbalStatusChange);
        }

        private void Update()
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (Time.time < _nextObserveTime) return;
            _nextObserveTime = Time.time + 1.5f;
            CrashSeverityState.ObserveLoadedVessels();
        }

        private void OnDestroy()
        {
            RRLog.Verbose("[EAC] Crash incident collector destroyed");
            GameEvents.onPartDie.Remove(OnPartDie);
            GameEvents.onPartExplode.Remove(OnPartExplode);
            GameEvents.onCrash.Remove(OnCrash);
            GameEvents.onKerbalStatusChange.Remove(OnKerbalStatusChange);
        }

        private static void OnPartDie(Part part)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            CrashSeverityState.MarkPartDestroyed(part);
        }

        private static void OnPartExplode(GameEvents.ExplosionReaction reaction)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            Vessel vessel = FlightGlobals.ActiveVessel;
            RRLog.Verbose("[EAC] onPartExplode fired for active vessel=" + (vessel != null ? vessel.vesselName : "<null>"));
            if (vessel != null)
                CrashSeverityState.MarkExplosionEvent(vessel);
        }

        private static void OnCrash(EventReport report)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            Vessel vessel = null;
            string originName = "<null>";
            string other = "<null>";
            string msg = "<null>";

            if (report != null)
            {
                if (report.origin != null)
                {
                    vessel = report.origin.vessel;
                    if (report.origin.partInfo != null && !string.IsNullOrEmpty(report.origin.partInfo.title))
                        originName = report.origin.partInfo.title;
                    else if (!string.IsNullOrEmpty(report.origin.name))
                        originName = report.origin.name;
                }
                if (!string.IsNullOrEmpty(report.other)) other = report.other;
                if (!string.IsNullOrEmpty(report.msg)) msg = report.msg;
            }

            RRLog.Verbose("[EAC] onCrash fired: origin=" + originName + ", other=" + other + ", msg=" + msg);
            if (vessel != null)
                CrashSeverityState.MarkCrashEvent(vessel, msg);
        }

        private static void OnKerbalStatusChange(ProtoCrewMember pcm, ProtoCrewMember.RosterStatus oldStatus, ProtoCrewMember.RosterStatus newStatus)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            RRLog.Verbose("[EAC] onKerbalStatusChange: kerbal=" + (pcm != null ? pcm.name : "<null>")
                + ", old=" + oldStatus
                + ", new=" + newStatus);

            if (newStatus == ProtoCrewMember.RosterStatus.Dead || newStatus == ProtoCrewMember.RosterStatus.Missing)
                CrashSeverityState.MarkCrewDeath(pcm);
        }
    }

    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public sealed class EACCrashDeferredVacationApplier : MonoBehaviour
    {
        private float _nextTryTime;

        private void Start()
        {
            RRLog.Verbose("[EAC] Crash deferred vacation applier active in scene=" + HighLogic.LoadedScene);
        }

        private void Update()
        {
            if (Time.time < _nextTryTime) return;
            _nextTryTime = Time.time + 1.0f;

            if (HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.TRACKSTATION)
                return;

            CrashSeverityState.TryApplyPendingCrewRandRExtensions();
        }
    }
}

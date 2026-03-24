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
        public int ImpactEvents;
        public bool SawCrashEvent;
        public double LastImpactUT;
        public int PendingSeparationDelta;
        public int DetachedPartsCreatedDuringGrace;
        public double SeparationGraceUntilUT;
        public double SeparationGraceStartedUT;
        public int MaxCrewObserved;
        public readonly HashSet<Guid> AttributedDetachedVessels = new HashSet<Guid>();
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

        internal sealed class DetachedVesselSnapshot
    {
        public Guid VesselId;
        public string VesselName;
        public int PartCount;
        public double FirstSeenUT;
        public double LastSeenUT;
        public Vector3d LastWorldPos;
    }

    internal static class CrashSeverityState
    {
        private static readonly Dictionary<Guid, CrashTrackedVessel> Tracked = new Dictionary<Guid, CrashTrackedVessel>();
        private static readonly Dictionary<Guid, DetachedVesselSnapshot> DetachedSnapshots = new Dictionary<Guid, DetachedVesselSnapshot>();
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

            RefreshDetachedSnapshots(loaded);

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

            double now = Planetarium.GetUniversalTime();
            if (tracked.LastSeenParts > 0 && currentParts > 0 && currentParts < tracked.LastSeenParts)
            {
                int delta = tracked.LastSeenParts - currentParts;
                StartImplicitSeparationGrace(tracked, now, "part-loss");
                tracked.PendingSeparationDelta += delta;
                InferDetachedVesselsForTracked(tracked, vessel, now);
                RRLog.Verbose("[EAC] part-loss delta buffered for vessel=" + tracked.VesselName
                    + ": delta=" + delta
                    + ", pendingDelta=" + tracked.PendingSeparationDelta
                    + ", detachedCreated=" + tracked.DetachedPartsCreatedDuringGrace
                    + ", graceRemaining=" + Math.Max(0.0, tracked.SeparationGraceUntilUT - now).ToString("0.###")
                    + ", lastSeenParts=" + tracked.LastSeenParts
                    + ", currentParts=" + currentParts);
            }
            else if (tracked.PendingSeparationDelta > 0 && tracked.SeparationGraceUntilUT > 0)
            {
                InferDetachedVesselsForTracked(tracked, vessel, now);
                if (now >= tracked.SeparationGraceUntilUT)
                    FinalizePendingSeparationLoss(tracked, "grace-expired");
            }

            tracked.LastSeenParts = currentParts;
            tracked.LastObservedUT = Planetarium.GetUniversalTime();
            tracked.VesselName = SafeVesselName(vessel);
            tracked.SnapshotCrew(vessel);
        }

        private static int ComputeNetBufferedSeparationLoss(CrashTrackedVessel tracked)
        {
            if (tracked == null) return 0;
            return Math.Max(0, tracked.PendingSeparationDelta - tracked.DetachedPartsCreatedDuringGrace);
        }

        private static void FinalizePendingSeparationLoss(CrashTrackedVessel tracked, string reason)
        {
            if (tracked == null) return;
            if (tracked.PendingSeparationDelta <= 0 && tracked.DetachedPartsCreatedDuringGrace <= 0)
            {
                tracked.SeparationGraceUntilUT = 0;
                return;
            }

            int netLoss = ComputeNetBufferedSeparationLoss(tracked);
            if (netLoss > 0)
            {
                tracked.EstimatedLostParts += netLoss;
            }

            RRLog.Verbose("[EAC] separation buffer finalized for vessel=" + tracked.VesselName
                + ": reason=" + (string.IsNullOrEmpty(reason) ? "<none>" : reason)
                + ", pendingDelta=" + tracked.PendingSeparationDelta
                + ", detachedCreated=" + tracked.DetachedPartsCreatedDuringGrace
                + ", netLoss=" + netLoss
                + ", estimatedLostParts=" + tracked.EstimatedLostParts);

            tracked.PendingSeparationDelta = 0;
            tracked.DetachedPartsCreatedDuringGrace = 0;
            tracked.SeparationGraceUntilUT = 0;
            tracked.SeparationGraceStartedUT = 0;
            tracked.AttributedDetachedVessels.Clear();
        }

        private static void StartImplicitSeparationGrace(CrashTrackedVessel tracked, double now, string context)
        {
            if (tracked == null) return;
            if (tracked.SeparationGraceUntilUT > now) return;

            tracked.SeparationGraceStartedUT = now;
            tracked.SeparationGraceUntilUT = now + 4.0;
            tracked.PendingSeparationDelta = 0;
            tracked.DetachedPartsCreatedDuringGrace = 0;
            tracked.AttributedDetachedVessels.Clear();

            RRLog.Verbose("[EAC] implicit separation grace started for vessel=" + tracked.VesselName
                + ", context=" + (string.IsNullOrEmpty(context) ? "<none>" : context)
                + ", graceUntilUT=" + tracked.SeparationGraceUntilUT.ToString("0.###"));
        }

        internal static void MarkStageSeparation(Vessel vessel, string context)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (vessel == null) return;
            CrashTrackedVessel tracked = GetOrCreate(vessel);
            if (tracked == null) return;

            double now = Planetarium.GetUniversalTime();
            double graceSeconds = 4.0;
            if (tracked.SeparationGraceUntilUT <= now)
            {
                tracked.SeparationGraceStartedUT = now;
                tracked.PendingSeparationDelta = 0;
                tracked.DetachedPartsCreatedDuringGrace = 0;
                tracked.AttributedDetachedVessels.Clear();
            }
            tracked.SeparationGraceUntilUT = Math.Max(tracked.SeparationGraceUntilUT, now + graceSeconds);
            tracked.LastObservedUT = now;
            tracked.SnapshotCrew(vessel);

            RRLog.Verbose("[EAC] separation grace started for vessel=" + SafeVesselName(vessel)
                + ", context=" + (string.IsNullOrEmpty(context) ? "<none>" : context)
                + ", graceUntilUT=" + tracked.SeparationGraceUntilUT.ToString("0.###")
                + ", pendingDelta=" + tracked.PendingSeparationDelta
                + ", detachedCreated=" + tracked.DetachedPartsCreatedDuringGrace);
        }

        private static void RefreshDetachedSnapshots(IList<Vessel> loaded)
        {
            double now = Planetarium.GetUniversalTime();
            HashSet<Guid> seenNow = new HashSet<Guid>();

            for (int i = 0; i < loaded.Count; i++)
            {
                Vessel vessel = loaded[i];
                if (vessel == null) continue;
                if (vessel.id == Guid.Empty) continue;
                if (vessel.isEVA) continue;
                if (vessel.GetCrewCount() > 0) continue;

                seenNow.Add(vessel.id);

                DetachedVesselSnapshot snap;
                if (!DetachedSnapshots.TryGetValue(vessel.id, out snap) || snap == null)
                {
                    snap = new DetachedVesselSnapshot();
                    snap.VesselId = vessel.id;
                    snap.FirstSeenUT = now;
                    DetachedSnapshots[vessel.id] = snap;
                }

                snap.LastSeenUT = now;
                snap.PartCount = CurrentPartCount(vessel);
                snap.VesselName = SafeVesselName(vessel);
                snap.LastWorldPos = vessel.GetWorldPos3D();
            }

            List<Guid> remove = null;
            foreach (KeyValuePair<Guid, DetachedVesselSnapshot> kvp in DetachedSnapshots)
            {
                DetachedVesselSnapshot snap = kvp.Value;
                if (snap == null)
                {
                    if (remove == null) remove = new List<Guid>();
                    remove.Add(kvp.Key);
                    continue;
                }

                if (!seenNow.Contains(kvp.Key) && now - snap.LastSeenUT > 15.0)
                {
                    if (remove == null) remove = new List<Guid>();
                    remove.Add(kvp.Key);
                }
            }

            if (remove != null)
            {
                for (int i = 0; i < remove.Count; i++)
                    DetachedSnapshots.Remove(remove[i]);
            }
        }

        private static void InferDetachedVesselsForTracked(CrashTrackedVessel tracked, Vessel sourceVessel, double now)
        {
            if (tracked == null) return;
            if (sourceVessel == null) return;
            if (tracked.PendingSeparationDelta <= tracked.DetachedPartsCreatedDuringGrace) return;
            if (tracked.SeparationGraceUntilUT <= 0) return;

            foreach (KeyValuePair<Guid, DetachedVesselSnapshot> kvp in DetachedSnapshots)
            {
                DetachedVesselSnapshot snap = kvp.Value;
                if (snap == null) continue;
                if (tracked.AttributedDetachedVessels.Contains(kvp.Key)) continue;
                if (snap.PartCount <= 0) continue;
                if (snap.FirstSeenUT + 1.5 < tracked.SeparationGraceStartedUT) continue;
                if (now - snap.LastSeenUT > 2.5) continue;

                Vessel detached = FindLoadedVessel(kvp.Key);
                Vector3d detachedPos = detached != null ? detached.GetWorldPos3D() : snap.LastWorldPos;
                double distance = Vector3d.Distance(sourceVessel.GetWorldPos3D(), detachedPos);

                string sourceName = SafeVesselName(sourceVessel);
                string detachedName = !string.IsNullOrEmpty(snap.VesselName) ? snap.VesselName : (detached != null ? SafeVesselName(detached) : string.Empty);
                bool sameFamily = !string.IsNullOrEmpty(sourceName) && !string.IsNullOrEmpty(detachedName)
                    && (string.Equals(sourceName, detachedName, StringComparison.OrdinalIgnoreCase)
                        || detachedName.StartsWith(sourceName, StringComparison.OrdinalIgnoreCase)
                        || sourceName.StartsWith(detachedName, StringComparison.OrdinalIgnoreCase)
                        || detachedName.IndexOf("Debris", StringComparison.OrdinalIgnoreCase) >= 0);

                double maxDistance = sameFamily ? 900.0 : 200.0;
                if (distance > maxDistance) continue;

                int remaining = tracked.PendingSeparationDelta - tracked.DetachedPartsCreatedDuringGrace;
                int credited = Math.Min(remaining, snap.PartCount);
                if (credited <= 0) break;

                tracked.DetachedPartsCreatedDuringGrace += credited;
                tracked.AttributedDetachedVessels.Add(kvp.Key);

                RRLog.Verbose("[EAC] inferred detached vessel during grace for source=" + tracked.VesselName
                    + ": detachedVessel=" + (!string.IsNullOrEmpty(detachedName) ? detachedName : kvp.Key.ToString())
                    + ", detachedParts=" + snap.PartCount
                    + ", creditedParts=" + credited
                    + ", pendingDelta=" + tracked.PendingSeparationDelta
                    + ", detachedCreated=" + tracked.DetachedPartsCreatedDuringGrace
                    + ", distance=" + distance.ToString("0.0"));

                if (tracked.DetachedPartsCreatedDuringGrace >= tracked.PendingSeparationDelta)
                    break;
            }
        }

        private static Vessel FindLoadedVessel(Guid vesselId)
        {
            if (vesselId == Guid.Empty) return null;
            IList<Vessel> loaded = FlightGlobals.VesselsLoaded;
            if (loaded == null) return null;

            for (int i = 0; i < loaded.Count; i++)
            {
                Vessel vessel = loaded[i];
                if (vessel == null) continue;
                if (vessel.id == vesselId) return vessel;
            }
            return null;
        }

        private static CrashTrackedVessel FindBestSeparationSource(Vessel createdVessel, bool requireGrace, double maxDistance)
        {
            if (createdVessel == null) return null;

            double now = Planetarium.GetUniversalTime();
            CrashTrackedVessel best = null;
            double bestScore = double.MaxValue;
            int graceCandidates = 0;

            foreach (KeyValuePair<Guid, CrashTrackedVessel> kvp in Tracked)
            {
                CrashTrackedVessel tracked = kvp.Value;
                if (tracked == null) continue;
                if (tracked.VesselId == createdVessel.id) continue;
                if (requireGrace && tracked.SeparationGraceUntilUT <= now) continue;
                graceCandidates++;

                Vessel sourceVessel = FindLoadedVessel(tracked.VesselId);
                if (sourceVessel == null)
                {
                    if (requireGrace && best == null)
                        best = tracked;
                    continue;
                }

                double distance = Vector3d.Distance(sourceVessel.GetWorldPos3D(), createdVessel.GetWorldPos3D());
                string sourceName = SafeVesselName(sourceVessel);
                string createdName = SafeVesselName(createdVessel);
                if (string.Equals(sourceName, createdName, StringComparison.OrdinalIgnoreCase))
                    distance -= 50.0;
                else if (!string.IsNullOrEmpty(sourceName) && !string.IsNullOrEmpty(createdName)
                    && (createdName.StartsWith(sourceName, StringComparison.OrdinalIgnoreCase)
                        || sourceName.StartsWith(createdName, StringComparison.OrdinalIgnoreCase)))
                    distance -= 25.0;

                if (distance > maxDistance)
                    continue;

                if (distance < bestScore)
                {
                    bestScore = distance;
                    best = tracked;
                }
            }

            if (best == null && graceCandidates == 1)
            {
                foreach (KeyValuePair<Guid, CrashTrackedVessel> kvp in Tracked)
                {
                    CrashTrackedVessel tracked = kvp.Value;
                    if (tracked == null) continue;
                    if (tracked.VesselId == createdVessel.id) continue;
                    if (tracked.SeparationGraceUntilUT <= now) continue;
                    best = tracked;
                    break;
                }
            }

            return best;
        }

        internal static void RegisterCreatedDetachedVessel(Vessel createdVessel, string context)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (createdVessel == null) return;
            if (createdVessel.isEVA) return;

            CrashTrackedVessel source = FindBestSeparationSource(createdVessel, true, 600.0);
            if (source == null)
            {
                source = FindBestSeparationSource(createdVessel, false, 150.0);
                if (source != null)
                {
                    source.SeparationGraceUntilUT = Math.Max(source.SeparationGraceUntilUT, Planetarium.GetUniversalTime() + 4.0);
                    RRLog.Verbose("[EAC] inferred separation grace from onVesselCreate for source=" + source.VesselName
                        + ", createdVessel=" + SafeVesselName(createdVessel));
                }
            }
            if (source == null) return;

            int createdParts = CurrentPartCount(createdVessel);
            if (createdParts <= 0) return;

            DetachedVesselSnapshot snap;
            if (!DetachedSnapshots.TryGetValue(createdVessel.id, out snap) || snap == null)
            {
                snap = new DetachedVesselSnapshot();
                snap.VesselId = createdVessel.id;
                snap.FirstSeenUT = Planetarium.GetUniversalTime();
                DetachedSnapshots[createdVessel.id] = snap;
            }
            snap.LastSeenUT = Planetarium.GetUniversalTime();
            snap.PartCount = createdParts;
            snap.VesselName = SafeVesselName(createdVessel);
            snap.LastWorldPos = createdVessel.GetWorldPos3D();

            source.DetachedPartsCreatedDuringGrace += createdParts;
            source.AttributedDetachedVessels.Add(createdVessel.id);
            source.LastObservedUT = Planetarium.GetUniversalTime();

            RRLog.Verbose("[EAC] detached vessel attributed to separation grace source=" + source.VesselName
                + ": createdVessel=" + SafeVesselName(createdVessel)
                + ", createdParts=" + createdParts
                + ", pendingDelta=" + source.PendingSeparationDelta
                + ", detachedCreated=" + source.DetachedPartsCreatedDuringGrace
                + ", context=" + (string.IsNullOrEmpty(context) ? "<none>" : context));
        }

        private static bool RegisterImpactEvent(CrashTrackedVessel tracked, string sourceTag)
        {
            if (tracked == null) return false;

            double now = Planetarium.GetUniversalTime();
            if (tracked.LastImpactUT > 0 && now - tracked.LastImpactUT < 0.75)
            {
                RRLog.Verbose("[EAC] impact event throttled for vessel=" + tracked.VesselName
                    + ", source=" + sourceTag
                    + ", deltaUT=" + (now - tracked.LastImpactUT).ToString("0.###"));
                tracked.LastObservedUT = now;
                return false;
            }

            tracked.ImpactEvents++;
            tracked.LastImpactUT = now;
            tracked.LastObservedUT = now;
            return true;
        }

        internal static void MarkCollisionEvent(Vessel vessel, string context)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (vessel == null) return;
            CrashTrackedVessel tracked = GetOrCreate(vessel);
            if (tracked == null) return;

            tracked.SnapshotCrew(vessel);
            bool counted = RegisterImpactEvent(tracked, "collision");
            RRLog.Verbose("[EAC] onCollision: vessel=" + SafeVesselName(vessel)
                + ", counted=" + counted
                + ", impacts=" + tracked.ImpactEvents
                + ", context=" + (string.IsNullOrEmpty(context) ? "<none>" : context));
        }

        internal static void MarkPartDestroyed(Part part)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (part == null || part.vessel == null) return;
            CrashTrackedVessel tracked = GetOrCreate(part.vessel);
            if (tracked == null) return;

            tracked.EstimatedLostParts++;
            tracked.LastSeenParts = Math.Max(0, CurrentPartCount(part.vessel));
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
            tracked.SnapshotCrew(vessel);
            bool counted = RegisterImpactEvent(tracked, "crash");

            RRLog.Verbose("[EAC] onCrash: vessel=" + SafeVesselName(vessel)
                + ", context=" + (string.IsNullOrEmpty(context) ? "<none>" : context)
                + ", counted=" + counted
                + ", impacts=" + tracked.ImpactEvents
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
                + ", impacts=" + tracked.ImpactEvents
                + ", pendingDelta=" + tracked.PendingSeparationDelta
                + ", detachedCreated=" + tracked.DetachedPartsCreatedDuringGrace
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
            tracked.LastSeenParts = currentParts;
            FinalizePendingSeparationLoss(tracked, "recovery");

            int totalCrew = Math.Max(tracked.MaxCrewObserved, tracked.CrewSnapshot.Count + tracked.KilledCrew.Count);
            int severityParts = Math.Max(Math.Max(tracked.EstimatedLostParts, tracked.ExplosionEvents), tracked.ImpactEvents);
            int fatalities = tracked.KilledCrew.Count;

            RRLog.Verbose("[EAC] Crash severity evaluation for vessel=" + SafeVesselName(vessel)
                + ", severityParts=" + severityParts
                + ", estimatedLostParts=" + tracked.EstimatedLostParts
                + ", explosions=" + tracked.ExplosionEvents
                + ", impacts=" + tracked.ImpactEvents
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
            int concreteImpactSignals = tracked != null
                ? Math.Max(Math.Max(tracked.EstimatedLostParts, tracked.ExplosionEvents), tracked.ImpactEvents) + tracked.KilledCrew.Count
                : 0;

            if (concreteImpactSignals <= 0 && noInjuryRoll <= noInjuryChance)
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
            int parts = tracked != null ? Math.Max(Math.Max(tracked.EstimatedLostParts, tracked.ExplosionEvents), tracked.ImpactEvents) : 0;
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
            int parts = tracked != null ? Math.Max(Math.Max(tracked.EstimatedLostParts, tracked.ExplosionEvents), tracked.ImpactEvents) : 0;
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
            GameEvents.onCollision.Add(OnCollision);
            GameEvents.onCrash.Add(OnCrash);
            GameEvents.onCrashSplashdown.Add(OnCrashSplashdown);
            GameEvents.onStageSeparation.Add(OnStageSeparation);
            GameEvents.onVesselCreate.Add(OnVesselCreate);
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
            GameEvents.onCollision.Remove(OnCollision);
            GameEvents.onCrash.Remove(OnCrash);
            GameEvents.onCrashSplashdown.Remove(OnCrashSplashdown);
            GameEvents.onStageSeparation.Remove(OnStageSeparation);
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
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

            Vessel vessel = ResolveExplosionVessel(reaction);
            RRLog.Verbose("[EAC] onPartExplode fired for vessel=" + (vessel != null ? vessel.vesselName : "<unresolved>"));
            if (vessel != null)
                CrashSeverityState.MarkExplosionEvent(vessel);
        }

        private static Vessel ResolveExplosionVessel(GameEvents.ExplosionReaction reaction)
        {
            object boxed = reaction;
            Type reactionType = boxed != null ? boxed.GetType() : typeof(GameEvents.ExplosionReaction);

            Part part = ReflectionUtils.GetMemberObject(boxed, reactionType, "CrashSeverity:ExplosionReactionPart",
                "part",
                "explodingPart",
                "explodedPart",
                "sourcePart",
                "originPart") as Part;
            if (part != null && part.vessel != null)
                return part.vessel;

            Vessel vessel = ReflectionUtils.GetMemberObject(boxed, reactionType, "CrashSeverity:ExplosionReactionVessel",
                "vessel",
                "sourceVessel",
                "originVessel") as Vessel;
            if (vessel != null)
                return vessel;

            object related = ReflectionUtils.GetMemberObject(boxed, reactionType, "CrashSeverity:ExplosionReactionRelated",
                "source",
                "origin",
                "host",
                "target",
                "from",
                "to");
            Part relatedPart = related as Part;
            if (relatedPart != null && relatedPart.vessel != null)
                return relatedPart.vessel;

            return related as Vessel;
        }


        private static HashSet<Vessel> ResolveEventReportVessels(EventReport report)
        {
            HashSet<Vessel> vessels = new HashSet<Vessel>();
            if (report == null) return vessels;

            if (report.origin != null && report.origin.vessel != null)
                vessels.Add(report.origin.vessel);

            object boxed = report;
            Type reportType = boxed.GetType();

            string[] memberNames = new[]
            {
                "sender", "source", "host", "target", "otherPart", "other", "part", "part1", "part2",
                "p", "p1", "p2", "vessel", "from", "to", "obj", "obj1", "obj2"
            };

            for (int i = 0; i < memberNames.Length; i++)
            {
                object value = ReflectionUtils.GetMemberObject(boxed, reportType, "CrashSeverity:EventReportMember:" + memberNames[i], memberNames[i]);
                Part part = value as Part;
                if (part != null)
                {
                    if (part.vessel != null)
                        vessels.Add(part.vessel);
                    continue;
                }

                Vessel vessel = value as Vessel;
                if (vessel != null)
                {
                    vessels.Add(vessel);
                    continue;
                }

                GameObject go = value as GameObject;
                if (go != null)
                {
                    Part goPart = go.GetComponent<Part>();
                    if (goPart != null && goPart.vessel != null)
                        vessels.Add(goPart.vessel);
                }
            }

            return vessels;
        }

        private static string DescribeEventReport(EventReport report)
        {
            if (report == null) return "<null>";
            string originName = "<null>";
            if (report.origin != null)
            {
                if (report.origin.partInfo != null && !string.IsNullOrEmpty(report.origin.partInfo.title))
                    originName = report.origin.partInfo.title;
                else if (!string.IsNullOrEmpty(report.origin.name))
                    originName = report.origin.name;
            }

            string other = !string.IsNullOrEmpty(report.other) ? report.other : "<null>";
            string msg = !string.IsNullOrEmpty(report.msg) ? report.msg : "<null>";
            return "origin=" + originName + ", other=" + other + ", msg=" + msg;
        }

        private static void OnCollision(EventReport report)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;

            HashSet<Vessel> vessels = ResolveEventReportVessels(report);
            if (vessels.Count == 0) return;

            string context = DescribeEventReport(report);
            RRLog.Verbose("[EAC] onCollision fired: " + context + ", vesselCount=" + vessels.Count);

            bool hasDistinctVessels = vessels.Count > 1;
            foreach (Vessel vessel in vessels)
            {
                if (vessel == null) continue;
                if (!hasDistinctVessels) continue;
                if (vessel.isEVA) continue;
                if (vessel.GetCrewCount() <= 0) continue;
                CrashSeverityState.MarkCollisionEvent(vessel, context);
            }
        }

        private static void OnCrashSplashdown(EventReport report)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;

            HashSet<Vessel> vessels = ResolveEventReportVessels(report);
            string context = DescribeEventReport(report);
            RRLog.Verbose("[EAC] onCrashSplashdown fired: " + context + ", vesselCount=" + vessels.Count);

            foreach (Vessel vessel in vessels)
            {
                if (vessel == null) continue;
                if (vessel.isEVA) continue;
                if (vessel.GetCrewCount() <= 0) continue;
                CrashSeverityState.MarkCrashEvent(vessel, context + " [splashdown]");
            }
        }

        private static void OnCrash(EventReport report)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;

            HashSet<Vessel> vessels = ResolveEventReportVessels(report);
            string context = DescribeEventReport(report);
            RRLog.Verbose("[EAC] onCrash fired: " + context + ", vesselCount=" + vessels.Count);

            foreach (Vessel vessel in vessels)
            {
                if (vessel == null) continue;
                if (vessel.isEVA) continue;
                if (vessel.GetCrewCount() <= 0) continue;
                CrashSeverityState.MarkCrashEvent(vessel, context);
            }
        }

        private static void OnStageSeparation(EventReport report)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;

            HashSet<Vessel> vessels = ResolveEventReportVessels(report);
            string context = DescribeEventReport(report);
            RRLog.Verbose("[EAC] onStageSeparation fired: " + context + ", vesselCount=" + vessels.Count);

            foreach (Vessel vessel in vessels)
            {
                if (vessel == null) continue;
                if (vessel.isEVA) continue;
                if (vessel.GetCrewCount() <= 0) continue;
                CrashSeverityState.MarkStageSeparation(vessel, context);
            }
        }

        private static void OnVesselCreate(Vessel vessel)
        {
            if (!RosterRotationState.CrashPenaltyEnabled) return;
            if (vessel == null) return;

            RRLog.Verbose("[EAC] onVesselCreate fired: vessel=" + (!string.IsNullOrEmpty(vessel.vesselName) ? vessel.vesselName : vessel.id.ToString())
                + ", parts=" + (vessel.parts != null ? vessel.parts.Count : 0)
                + ", crew=" + vessel.GetCrewCount());

            CrashSeverityState.RegisterCreatedDetachedVessel(vessel, "onVesselCreate");
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

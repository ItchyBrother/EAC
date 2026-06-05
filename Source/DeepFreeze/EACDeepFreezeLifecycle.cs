// EAC - DeepFreeze compatibility: lifecycle reconciliation and thaw risk.
//
// This file owns the rules for suspending EAC clocks during DeepFreeze and for
// applying the optional post-thaw fatality roll when DeepFreeze's own fatal
// difficulty option is enabled.

using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using RosterRotation.DeepFreeze;

namespace RosterRotation
{
    public partial class RosterRotationKSCUI
    {
        private const double DeepFreezeStatusDeathVerifySeconds = 3.0;
        private const double DeepFreezeStatusDeathMaxWaitSeconds = 20.0;
        private const int DeepFreezeSpecialRosterStatusValue = 9001;

        private struct DeepFreezeDeathCandidate
        {
            internal string Name;
            internal ProtoCrewMember.RosterStatus OldStatus;
            internal ProtoCrewMember.RosterStatus NewStatus;
            internal double EventUT;
            internal float EarliestResolveRT;
            internal float ExpireRT;
            internal string Reason;
        }

        private static readonly Dictionary<string, DeepFreezeDeathCandidate> DeepFreezeDeathCandidates =
            new Dictionary<string, DeepFreezeDeathCandidate>(StringComparer.Ordinal);

        private static readonly Dictionary<string, double> DeepFreezeFrozenNotificationUT =
            new Dictionary<string, double>(StringComparer.Ordinal);

        private static readonly Dictionary<string, double> DeepFreezeThawedNotificationUT =
            new Dictionary<string, double>(StringComparer.Ordinal);

        internal static bool ReconcileDeepFreezeForCurrentRoster()
        {
            EACDeepFreezeBridge.Update(force: true);

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            double nowUT = Planetarium.GetUniversalTime();
            bool anyDirty = false;

            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;

                var rec = RosterRotationState.GetOrCreate(k.name);
                if (ReconcileDeepFreezeState(k, rec, nowUT))
                    anyDirty = true;
            }

            // DeepFreeze stores frozen Kerbals outside roster.Crew, commonly as
            // Unowned/Dead while the frozen dictionary is authoritative.  Do a
            // second, tiny pass over DeepFreeze's frozen cache so EAC keeps the
            // cryosleep clock active even after scene loads or long time-warps
            // where the frozen Kerbal is not present in roster.Crew.
            foreach (var frozenInfo in EACDeepFreezeBridge.FrozenKerbals.Values)
            {
                if (ReconcileDeepFreezeFrozenSnapshot(frozenInfo, nowUT))
                    anyDirty = true;
            }

            if (ProcessPendingDeepFreezeDeathCandidates(nowUT))
                anyDirty = true;

            if (anyDirty)
                RequestDeepFreezeUiRefresh();

            return anyDirty;
        }

        private static bool ReconcileDeepFreezeFrozenSnapshot(
            EACDeepFreezeKerbalInfo frozenInfo,
            double nowUT)
        {
            if (frozenInfo == null || string.IsNullOrEmpty(frozenInfo.Name)) return false;

            var rec = RosterRotationState.GetOrCreate(frozenInfo.Name);
            if (rec == null) return false;

            bool anyDirty = false;

            double snapshotStartUT = frozenInfo.LastUpdateUT > 0 && frozenInfo.LastUpdateUT <= nowUT
                ? frozenInfo.LastUpdateUT
                : nowUT;

            if (!rec.DeepFreezeActive)
            {
                rec.DeepFreezeActive = true;
                rec.DeepFreezeStartUT = snapshotStartUT;
                if (MissionTimeTracker.CaptureCurrentMissionSegment(rec, snapshotStartUT, "DeepFreeze snapshot"))
                    anyDirty = true;
                rec.MissionStartUT = 0;
                rec.LastMissionDeathCheckUT = 0;
                anyDirty = true;

                RRLog.Verbose("[EAC] DeepFreeze snapshot entered: " + frozenInfo.Name
                    + ", startUT=" + rec.DeepFreezeStartUT.ToString("0.###")
                    + (string.IsNullOrEmpty(frozenInfo.VesselName) ? "" : ", vessel=" + frozenInfo.VesselName));
            }
            else if (rec.DeepFreezeStartUT <= 0 || rec.DeepFreezeStartUT > nowUT)
            {
                rec.DeepFreezeStartUT = snapshotStartUT;
                anyDirty = true;
            }

            if (!string.IsNullOrEmpty(frozenInfo.VesselName) &&
                !string.Equals(rec.DeepFreezeLastKnownVesselName, frozenInfo.VesselName, StringComparison.Ordinal))
            {
                rec.DeepFreezeLastKnownVesselName = frozenInfo.VesselName;
                anyDirty = true;
            }

            return anyDirty;
        }

        private static bool ReconcileDeepFreezeState(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            double nowUT)
        {
            if (k == null || rec == null) return false;

            bool frozen = EACDeepFreezeBridge.IsFrozen(k) ||
                          (rec.DeepFreezeActive && IsDeepFreezeSpecialRosterStatus(k.rosterStatus));

            if (rec.DeathUT > 0)
            {
                // A previously mis-recorded DeepFreeze transition can leave an
                // EAC death record behind.  Clear it only when DeepFreeze still
                // gives us a strong signal that this Kerbal is frozen-like.
                return TryClearFalseDeepFreezeDeathRecord(k, rec, nowUT, "DeepFreeze reconcile");
            }

            if (frozen && !rec.DeepFreezeActive)
            {
                rec.DeepFreezeActive = true;
                rec.DeepFreezeStartUT = EACDeepFreezeBridge.GetFrozenLastUpdateUT(k.name, nowUT);
                rec.DeepFreezeLastKnownVesselName = TryGetDeepFreezeVesselName(k.name, out var vesselName) ? vesselName : string.Empty;

                // Frozen time should not count, but awake assigned time before
                // freezing is still mission time for later recovery leave.
                MissionTimeTracker.CaptureCurrentMissionSegment(rec, rec.DeepFreezeStartUT, "DeepFreeze entered");
                rec.MissionStartUT = 0;
                rec.LastMissionDeathCheckUT = 0;

                RRLog.Verbose("[EAC] DeepFreeze entered: " + k.name
                    + ", startUT=" + rec.DeepFreezeStartUT.ToString("0.###")
                    + (string.IsNullOrEmpty(rec.DeepFreezeLastKnownVesselName) ? "" : ", vessel=" + rec.DeepFreezeLastKnownVesselName));
                return true;
            }

            if (!frozen && rec.DeepFreezeActive && IsPendingDeepFreezeDeathCandidate(k.name))
                return false;

            if (!frozen && rec.DeepFreezeActive)
            {
                double startUT = rec.DeepFreezeStartUT > 0 && rec.DeepFreezeStartUT <= nowUT
                    ? rec.DeepFreezeStartUT
                    : nowUT;
                double frozenSeconds = Math.Max(0, nowUT - startUT);

                rec.DeepFreezeActive = false;
                rec.DeepFreezeStartUT = 0;
                rec.DeepFreezeAccumulatedUT += frozenSeconds;

                SuspendEacClocksForCryosleep(k, rec, frozenSeconds);

                bool died = TryApplyDeepFreezeThawFatality(k, rec, frozenSeconds, nowUT);
                if (!died)
                {
                    PostDeepFreezeThawedNotification(k, rec, nowUT, frozenSeconds);

                    RRLog.Verbose("[EAC] DeepFreeze exited: " + k.name
                        + ", frozenSeconds=" + frozenSeconds.ToString("0.###")
                        + ", DeepFreezeFatalOption=" + EACDeepFreezeBridge.FatalOptionEnabled);
                }

                return true;
            }

            if (frozen)
            {
                // Keep the cached display location reasonably fresh while frozen.
                if (TryGetDeepFreezeVesselName(k.name, out var vesselName) &&
                    !string.Equals(rec.DeepFreezeLastKnownVesselName, vesselName, StringComparison.Ordinal))
                {
                    rec.DeepFreezeLastKnownVesselName = vesselName;
                    return true;
                }
            }

            return false;
        }

        internal static bool ShouldIgnoreDeathLikeStatusForDeepFreeze(ProtoCrewMember k)
        {
            if (k == null) return false;

            EACDeepFreezeBridge.Update(force: true);
            if (!EACDeepFreezeBridge.Installed) return false;

            RosterRotationState.Records.TryGetValue(k.name, out var rec);
            if (EACDeepFreezeBridge.IsFrozen(k)) return true;
            if (rec != null && rec.DeepFreezeActive) return true;
            if (IsDeepFreezeSpecialRosterStatus(k.rosterStatus)) return true;

            // During the freeze animation DeepFreeze emits a stock death-like
            // roster status before its frozen roster cache is populated.  When
            // the active vessel contains a DeepFreezer, treat that death-like
            // status as cryosleep for crash-severity purposes.
            return EACDeepFreezeBridge.HasDeepFreezerOnActiveVessel();
        }

        internal static bool TryDeferDeepFreezeStatusDeathCandidate(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            ProtoCrewMember.RosterStatus oldStatus,
            ProtoCrewMember.RosterStatus newStatus,
            double nowUT,
            string reason)
        {
            if (k == null || rec == null) return false;
            if (newStatus != ProtoCrewMember.RosterStatus.Dead &&
                newStatus != ProtoCrewMember.RosterStatus.Missing) return false;

            EACDeepFreezeBridge.Update(force: true);

            bool likelyDeepFreeze = EACDeepFreezeBridge.Installed &&
                (IsStrongDeepFreezeSignal(k, rec, allowSpecialStatus: true) ||
                 EACDeepFreezeBridge.HasDeepFreezerOnActiveVessel());

            if (!likelyDeepFreeze) return false;

            // DeepFreeze intentionally drives the Kerbal through a stock death-like
            // state while transferring them into cryosleep.  Treat this as a
            // provisional frozen transition immediately so the player sees an
            // EAC Frozen message instead of a false K.I.A. message.  If
            // DeepFreeze does not confirm the frozen state within the verification
            // window, EAC will roll this back and record the real death.
            AcceptDeepFreezeStatusTransition(
                k, rec, nowUT,
                reason + " (provisional DeepFreeze status transition)",
                postFrozenNotification: true);

            DeepFreezeDeathCandidates[k.name] = new DeepFreezeDeathCandidate
            {
                Name = k.name,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                EventUT = nowUT > 0 ? nowUT : Planetarium.GetUniversalTime(),
                EarliestResolveRT = UnityEngine.Time.realtimeSinceStartup + (float)DeepFreezeStatusDeathVerifySeconds,
                ExpireRT = UnityEngine.Time.realtimeSinceStartup + (float)DeepFreezeStatusDeathMaxWaitSeconds,
                Reason = reason
            };

            RRLog.Info("[EAC] DeepFreeze freeze transition detected for " + k.name
                + "; suppressed transient K.I.A. status " + oldStatus + " -> " + newStatus
                + ", activeFreezer=" + EACDeepFreezeBridge.HasDeepFreezerOnActiveVessel());

            SaveScheduler.RequestSave("DeepFreeze frozen status");
            RequestDeepFreezeUiRefresh();
            return true;
        }

        private static bool ProcessPendingDeepFreezeDeathCandidates(double nowUT)
        {
            if (DeepFreezeDeathCandidates.Count == 0) return false;

            EACDeepFreezeBridge.Update(force: true);

            bool anyDirty = false;
            var roster = HighLogic.CurrentGame?.CrewRoster;
            var namesToRemove = new List<string>();
            float rt = UnityEngine.Time.realtimeSinceStartup;

            foreach (var kvp in DeepFreezeDeathCandidates)
            {
                var candidate = kvp.Value;
                if (rt < candidate.EarliestResolveRT) continue;

                ProtoCrewMember k = FindCrewMemberByName(roster, candidate.Name);
                if (k == null)
                {
                    if (rt >= candidate.ExpireRT) namesToRemove.Add(candidate.Name);
                    continue;
                }

                var rec = RosterRotationState.GetOrCreate(k.name);

                if (IsConfirmedDeepFreezeSignal(k, rec, allowSpecialStatus: true))
                {
                    AcceptDeepFreezeStatusTransition(k, rec, candidate.EventUT, candidate.Reason + " (verified DeepFreeze transition)", postFrozenNotification: false);
                    namesToRemove.Add(candidate.Name);
                    anyDirty = true;
                    continue;
                }

                // Do not resolve a provisional DeepFreeze death-like status at the
                // first check.  DeepFreeze can take several seconds to populate its
                // frozen roster cache after KSP emits Assigned -> Dead/Missing.
                if (rt < candidate.ExpireRT)
                    continue;

                namesToRemove.Add(candidate.Name);

                bool stillDeathLike = k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                                      k.rosterStatus == ProtoCrewMember.RosterStatus.Missing;

                if (rec.DeepFreezeActive && !stillDeathLike && EACDeepFreezeBridge.HasDeepFreezerOnActiveVessel())
                {
                    // KSP restored Assigned/Available after the transient DeepFreeze
                    // death-like state.  Keep the provisional cryosleep record instead
                    // of turning this into K.I.A.
                    RRLog.Info("[EAC] DeepFreeze transition accepted after stock status recovered: "
                        + k.name + ", status=" + k.rosterStatus);
                    anyDirty = true;
                    continue;
                }

                if (rec.DeepFreezeActive && !EACDeepFreezeBridge.IsFrozen(k))
                {
                    ClearProvisionalDeepFreezeState(rec);
                    anyDirty = true;
                }

                if (rec.DeathUT <= 0 && stillDeathLike)
                {
                    RecordStatusDeathAndNotify(k, rec, candidate.EventUT, false,
                        candidate.Reason + " (verified after DeepFreeze check)");
                    anyDirty = true;
                }
            }

            foreach (var name in namesToRemove)
                DeepFreezeDeathCandidates.Remove(name);

            return anyDirty;
        }

        private static ProtoCrewMember FindCrewMemberByName(KerbalRoster roster, string name)
        {
            if (roster == null || string.IsNullOrEmpty(name)) return null;

            foreach (var k in roster.Crew)
            {
                if (k != null && string.Equals(k.name, name, StringComparison.Ordinal))
                    return k;
            }

            return null;
        }

        internal static bool TryClearFalseDeepFreezeDeathRecord(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            double nowUT,
            string reason)
        {
            if (k == null || rec == null || rec.DeathUT <= 0) return false;

            EACDeepFreezeBridge.Update(force: true);
            if (!EACDeepFreezeBridge.Installed) return false;

            // Be conservative.  Only clear a recorded death when DeepFreeze gives
            // us a positive frozen-like signal.  This avoids resurrecting real KIA
            // Kerbals just because DeepFreeze is installed.
            if (!IsConfirmedDeepFreezeSignal(k, rec, allowSpecialStatus: false) &&
                !rec.DeepFreezeActive) return false;

            double oldDeathUT = rec.DeathUT;
            rec.DeathUT = 0;
            rec.DiedOnMission = false;
            rec.PendingMissionDeath = false;
            MissionTimeTracker.CaptureCurrentMissionSegment(rec, nowUT, "clear false DeepFreeze KIA");
            rec.MissionStartUT = 0;
            rec.LastMissionDeathCheckUT = 0;
            rec.RetirementScheduled = false;

            AcceptDeepFreezeStatusTransition(k, rec, nowUT, reason + " (cleared false DeepFreeze KIA)");

            RRLog.Warn("[EAC] Cleared false KIA/Lost record for DeepFreeze Kerbal "
                + k.name + "; oldDeathUT=" + oldDeathUT.ToString("0.###")
                + ", status=" + k.rosterStatus
                + ", reason=" + (string.IsNullOrEmpty(reason) ? "<none>" : reason));

            SaveScheduler.RequestSave("clear false DeepFreeze death record");
            return true;
        }

        private static bool IsStrongDeepFreezeSignal(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, bool allowSpecialStatus)
        {
            if (k == null) return false;
            if (EACDeepFreezeBridge.IsFrozen(k)) return true;
            if (rec != null && rec.DeepFreezeActive) return true;

            // DeepFreeze and CrewRandR both use high custom roster statuses in
            // some installs.  Treat 9001 as a DeepFreeze signal only while the
            // DeepFreeze bridge is present; normal CrewRandR vacation handling is
            // still handled elsewhere and does not come through the death path.
            return allowSpecialStatus && EACDeepFreezeBridge.Installed && IsDeepFreezeSpecialRosterStatus(k.rosterStatus);
        }

        private static bool IsDeepFreezeSpecialRosterStatus(ProtoCrewMember.RosterStatus status)
        {
            return (int)status == DeepFreezeSpecialRosterStatusValue;
        }

        private static void AcceptDeepFreezeStatusTransition(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            double nowUT,
            string reason,
            bool postFrozenNotification = false)
        {
            if (k == null || rec == null) return;

            if (!rec.DeepFreezeActive)
            {
                rec.DeepFreezeActive = true;
                rec.DeepFreezeStartUT = EACDeepFreezeBridge.GetFrozenLastUpdateUT(k.name, nowUT > 0 ? nowUT : Planetarium.GetUniversalTime());
                if (rec.DeepFreezeStartUT <= 0)
                    rec.DeepFreezeStartUT = nowUT > 0 ? nowUT : Planetarium.GetUniversalTime();
            }

            if (TryGetDeepFreezeVesselName(k.name, out var vesselName))
                rec.DeepFreezeLastKnownVesselName = vesselName;
            else if (EACDeepFreezeBridge.HasDeepFreezerOnActiveVessel())
            {
                try
                {
                    if (FlightGlobals.ActiveVessel != null && !string.IsNullOrEmpty(FlightGlobals.ActiveVessel.vesselName))
                        rec.DeepFreezeLastKnownVesselName = FlightGlobals.ActiveVessel.vesselName;
                }
                catch { }
            }

            MissionTimeTracker.CaptureCurrentMissionSegment(rec, rec.DeepFreezeStartUT > 0 ? rec.DeepFreezeStartUT : nowUT, "DeepFreeze transition");
            rec.MissionStartUT = 0;
            rec.LastMissionDeathCheckUT = 0;

            if (postFrozenNotification)
                PostDeepFreezeFrozenNotification(k, rec, nowUT);

            RRLog.Verbose("[EAC] DeepFreeze transition accepted instead of KIA: "
                + k.name + ", status=" + k.rosterStatus
                + ", reason=" + (string.IsNullOrEmpty(reason) ? "<none>" : reason));
        }

        private static void RequestDeepFreezeUiRefresh()
        {
            RosterRotationKSCUI.RequestUiRefresh("DeepFreeze");
        }

        private static bool IsPendingDeepFreezeDeathCandidate(string kerbalName)
        {
            return !string.IsNullOrEmpty(kerbalName) && DeepFreezeDeathCandidates.ContainsKey(kerbalName);
        }

        private static bool IsConfirmedDeepFreezeSignal(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, bool allowSpecialStatus)
        {
            if (k == null) return false;
            if (EACDeepFreezeBridge.IsFrozen(k)) return true;
            return allowSpecialStatus && EACDeepFreezeBridge.Installed && IsDeepFreezeSpecialRosterStatus(k.rosterStatus);
        }

        private static void ClearProvisionalDeepFreezeState(RosterRotationState.KerbalRecord rec)
        {
            if (rec == null) return;
            rec.DeepFreezeActive = false;
            rec.DeepFreezeStartUT = 0;
            rec.DeepFreezeLastKnownVesselName = string.Empty;
            MissionTimeTracker.ClearMissionTracking(rec);
        }

        private static void PostDeepFreezeFrozenNotification(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            if (k == null) return;

            double eventUT = nowUT > 0 ? nowUT : Planetarium.GetUniversalTime();
            if (DeepFreezeFrozenNotificationUT.TryGetValue(k.name, out var lastUT) &&
                Math.Abs(eventUT - lastUT) < 1.0)
                return;

            DeepFreezeFrozenNotificationUT[k.name] = eventUT;

            string vesselName = null;
            if (rec != null && !string.IsNullOrEmpty(rec.DeepFreezeLastKnownVesselName))
                vesselName = rec.DeepFreezeLastKnownVesselName;
            else if (TryGetDeepFreezeVesselName(k.name, out var bridgeVesselName))
                vesselName = bridgeVesselName;
            else
            {
                try
                {
                    if (FlightGlobals.ActiveVessel != null && EACDeepFreezeBridge.HasDeepFreezerOnActiveVessel())
                        vesselName = FlightGlobals.ActiveVessel.vesselName;
                }
                catch { }
            }

            string vesselText = string.IsNullOrEmpty(vesselName) ? string.Empty : " aboard " + vesselName;
            string body = k.name + " has entered suspended animation" + vesselText + ". ("
                + RosterRotationState.FormatGameDate(eventUT) + ")";

            RosterRotationState.PostNotification(
                EACNotificationType.General,
                "DeepFreeze — " + k.name + " frozen",
                body,
                MessageSystemButton.MessageButtonColor.ORANGE,
                MessageSystemButton.ButtonIcons.MESSAGE,
                8f);
        }

        private static void PostDeepFreezeThawedNotification(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            double nowUT,
            double frozenSeconds)
        {
            if (k == null) return;

            double eventUT = nowUT > 0 ? nowUT : Planetarium.GetUniversalTime();
            if (DeepFreezeThawedNotificationUT.TryGetValue(k.name, out var lastUT) &&
                Math.Abs(eventUT - lastUT) < 1.0)
                return;

            DeepFreezeThawedNotificationUT[k.name] = eventUT;

            string vesselName = null;
            if (rec != null && !string.IsNullOrEmpty(rec.DeepFreezeLastKnownVesselName))
                vesselName = rec.DeepFreezeLastKnownVesselName;
            else
            {
                try
                {
                    if (FlightGlobals.ActiveVessel != null && !string.IsNullOrEmpty(FlightGlobals.ActiveVessel.vesselName))
                        vesselName = FlightGlobals.ActiveVessel.vesselName;
                }
                catch { }
            }

            string vesselText = string.IsNullOrEmpty(vesselName) ? string.Empty : " aboard " + vesselName;
            string durationText = frozenSeconds > 0
                ? " after " + RosterRotationState.FormatCountdown(frozenSeconds) + " in suspended animation"
                : string.Empty;

            string body = k.name + " has been thawed" + vesselText + durationText + ". ("
                + RosterRotationState.FormatGameDate(eventUT) + ")";

            RosterRotationState.PostNotification(
                EACNotificationType.General,
                "DeepFreeze — " + k.name + " thawed",
                body,
                MessageSystemButton.MessageButtonColor.GREEN,
                MessageSystemButton.ButtonIcons.MESSAGE,
                8f);
        }

        private static void SuspendEacClocksForCryosleep(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            double seconds)
        {
            if (rec == null || seconds <= 0) return;

            // Biological age should not advance while the Kerbal is in suspended
            // animation.  GetKerbalAge() uses DeepFreezeAccumulatedUT as the
            // biological clock offset; do not mutate BirthUT here because it can
            // legitimately be negative for Kerbals who were adults at campaign start.
            if (rec.NaturalRetirementUT > 0) rec.NaturalRetirementUT += seconds;
            if (rec.LastFlightUT > 0) rec.LastFlightUT += seconds;
            if (rec.RestUntilUT > 0) rec.RestUntilUT += seconds;
            if (rec.RetirementScheduledUT > 0) rec.RetirementScheduledUT += seconds;
            if (rec.TrainingEndUT > 0) rec.TrainingEndUT += seconds;
            if (rec.GraduationExamReadyUT > 0) rec.GraduationExamReadyUT += seconds;

            if (k != null && k.inactive && k.inactiveTimeEnd > 0)
                k.inactiveTimeEnd += seconds;
        }

        private static bool TryApplyDeepFreezeThawFatality(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            double frozenSeconds,
            double nowUT)
        {
            if (!ShouldRollDeepFreezeThawDeath(k, rec, frozenSeconds, nowUT, out var chance, out var roll))
                return false;

            RRLog.Verbose("[EAC] DeepFreeze thaw fatality roll for " + k.name
                + ": chance=" + chance.ToString("P4")
                + ", roll=" + roll.ToString("P4")
                + ", DeepFreezeFatalOption=" + EACDeepFreezeBridge.FatalOptionEnabled);

            if (roll >= chance) return false;
            return KillKerbalFromDeepFreezeThaw(k, rec, nowUT, frozenSeconds);
        }

        private static bool ShouldRollDeepFreezeThawDeath(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            double frozenSeconds,
            double nowUT,
            out double chance,
            out double roll)
        {
            chance = 0;
            roll = 1;

            if (k == null || rec == null) return false;
            if (rec.DeathUT > 0) return false;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return false;

            // This is the user's requested rule: EAC only adds thaw-death risk
            // when DeepFreeze itself is configured for fatal EC/heat failures.
            if (!EACDeepFreezeBridge.FatalOptionEnabled) return false;

            double baseChance = Clamp01(RosterRotationState.DeepFreezeThawDeathBaseChance);
            double maxChance = Clamp01(RosterRotationState.DeepFreezeThawDeathMaxChance);
            if (maxChance < baseChance) maxChance = baseChance;

            double yearsFrozen = RosterRotationState.YearSeconds > 0
                ? Math.Max(0, frozenSeconds / RosterRotationState.YearSeconds)
                : 0;
            double durationBonus = Math.Max(0, yearsFrozen) * Math.Max(0, RosterRotationState.DeepFreezeThawDeathBonusPerYear);

            chance = Math.Min(maxChance, baseChance + durationBonus);
            if (chance <= 0) return false;

            roll = UnityEngine.Random.value;
            return true;
        }

        private static bool KillKerbalFromDeepFreezeThaw(
            ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec,
            double nowUT,
            double frozenSeconds)
        {
            if (k == null || rec == null) return false;

            bool assigned = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
            string vesselName = rec.DeepFreezeLastKnownVesselName;

            if (assigned)
            {
                if (!TryDetachKerbalFromAssignedVessel(k, out var detachedVesselName))
                {
                    RRLog.Warn("[EAC] DeepFreeze thaw fatality aborted for " + k.name
                        + ": could not safely detach Kerbal from assigned vessel.");
                    return false;
                }

                if (!string.IsNullOrEmpty(detachedVesselName))
                    vesselName = detachedVesselName;
            }

            rec.DeathUT = nowUT;
            rec.DiedOnMission = assigned;
            rec.PendingMissionDeath = assigned;
            rec.RetirementScheduled = false;
            MissionTimeTracker.ClearMissionTracking(rec);
            ClearActiveDutyStateAfterDeath(rec);
            RetiredKerbalCleanupService.ResetAutoCleanupRequest(k.name);

            try
            {
                k.inactive = false;
                k.inactiveTimeEnd = 0;
                k.rosterStatus = ProtoCrewMember.RosterStatus.Dead;
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed to mark DeepFreeze thaw fatality dead for " + k.name + ": " + ex.Message);
                return false;
            }

            int age = RosterRotationState.GetKerbalAge(rec, nowUT);
            string ageText = age >= 0 ? " at age " + age : "";
            string frozenText = frozenSeconds > 0
                ? " after " + RosterRotationState.FormatCountdown(frozenSeconds) + " in suspended animation"
                : " after suspended animation";
            string vesselText = string.IsNullOrEmpty(vesselName) ? "" : " aboard " + vesselName;

            if (RosterRotationState.DeathNotificationsEnabled)
            {
                RosterRotationState.PostNotification(
                    EACNotificationType.Death,
                    "DeepFreeze thaw complication — " + k.name,
                    k.name + " died" + ageText + frozenText + vesselText + ". (" + RosterRotationState.FormatGameDate(nowUT) + ")",
                    MessageSystemButton.MessageButtonColor.RED,
                    MessageSystemButton.ButtonIcons.ALERT,
                    12f);
            }

            RRLog.Warn("[EAC] DeepFreeze thaw fatality: " + k.name
                + ", deathUT=" + nowUT.ToString("0.###")
                + ", frozenSeconds=" + frozenSeconds.ToString("0.###")
                + (string.IsNullOrEmpty(vesselName) ? "" : ", vessel=" + vesselName));

            SaveScheduler.RequestImmediateSave("DeepFreeze thaw fatality");
            return true;
        }

        internal static bool IsDeepFreezeFrozen(ProtoCrewMember k)
        {
            return EACDeepFreezeBridge.IsFrozen(k);
        }

        internal static bool TryGetDeepFreezeVesselName(string kerbalName, out string vesselName)
        {
            vesselName = null;
            if (!EACDeepFreezeBridge.TryGetFrozenInfo(kerbalName, out var info) || info == null)
                return false;

            vesselName = info.VesselName;
            return !string.IsNullOrEmpty(vesselName);
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }
}

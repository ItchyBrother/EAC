using System;
using System.Collections.Generic;
using System.Globalization;
using KSP.UI.Screens;

namespace RosterRotation
{
    /// <summary>
    /// Public facade for EAC-native veteran/suit/startup-crew support.  This
    /// intentionally remains the single call surface used by older EAC code,
    /// while implementation details live in smaller focused services.
    /// </summary>
    internal static class EACVeteranService
    {
        private static readonly HashSet<string> DefaultCrewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Jebediah Kerman",
            "Bill Kerman",
            "Bob Kerman",
            "Valentina Kerman"
        };

        internal static bool IsDelegatedToEarnYourStripes => EACExternalModDetector.IsEarnYourStripesInstalled();

        internal static bool EvaluateRoster(string reason, bool requestSave = true)
        {
            if (IsDelegatedToEarnYourStripes || !RosterRotationState.EACVeteranStatusEnabled)
                return false;

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            bool changed = false;
            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                var rec = RosterRotationState.GetOrCreate(k.name);
                if (EvaluateKerbal(k, rec, reason, requestSave: false))
                    changed = true;
            }

            if (changed && requestSave)
                SaveScheduler.RequestSave("EAC veteran evaluation");
            return changed;
        }

        internal static bool EvaluateKerbal(ProtoCrewMember kerbal, RosterRotationState.KerbalRecord rec, string reason, bool requestSave = true)
        {
            if (kerbal == null || rec == null) return false;
            if (IsDelegatedToEarnYourStripes || !RosterRotationState.EACVeteranStatusEnabled) return false;
            if (kerbal.type == ProtoCrewMember.KerbalType.Applicant) return false;
            if (kerbal.rosterStatus == ProtoCrewMember.RosterStatus.Dead || kerbal.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return false;
            if (rec.Retired || rec.DeathUT > 0) return false;

            bool changed = false;
            int flights = 0;
            double hours = 0d;
            string milestoneKey = null;
            bool qualifies = EACVeteranEligibility.IsAllowedVeteranClass(kerbal.trait)
                && EACVeteranEligibility.QualifiesForVeteran(kerbal, rec, out flights, out hours, out milestoneKey);
            bool currentVeteran = EACKerbalMemberAccess.ReadBool(kerbal, false, "veteran", "Veteran", "isVeteran", "IsVeteran");
            bool defaultCrew = IsDefaultCrew(kerbal.name);

            if (qualifies && !currentVeteran)
            {
                if (EACKerbalMemberAccess.WriteBool(kerbal, true, "veteran", "Veteran", "isVeteran", "IsVeteran"))
                {
                    changed = true;
                    string veteranBody = kerbal.name + " has earned Veteran status"
                        + " (" + flights.ToString(CultureInfo.InvariantCulture) + " flights, "
                        + hours.ToString("0.##", CultureInfo.InvariantCulture) + " flight hours)"
                        + (string.IsNullOrEmpty(milestoneKey) ? "." : " after milestone: " + milestoneKey + ".");
                    RRLog.Info("[EAC] Veteran status awarded: " + kerbal.name
                        + " flights=" + flights.ToString(CultureInfo.InvariantCulture)
                        + " hours=" + hours.ToString("0.##", CultureInfo.InvariantCulture)
                        + (string.IsNullOrEmpty(milestoneKey) ? "" : " milestone=" + milestoneKey)
                        + (string.IsNullOrEmpty(reason) ? "" : " reason=" + reason));
                    RosterRotationState.PostNotification(
                        EACNotificationType.Veteran,
                        "EAC Veteran Recognized",
                        veteranBody,
                        MessageSystemButton.MessageButtonColor.GREEN,
                        MessageSystemButton.ButtonIcons.COMPLETE,
                        8f);
                }
            }
            else if (!qualifies && currentVeteran && ShouldStripUnearnedVeteran(defaultCrew))
            {
                if (EACKerbalMemberAccess.WriteBool(kerbal, false, "veteran", "Veteran", "isVeteran", "IsVeteran"))
                {
                    changed = true;
                    RRLog.Info("[EAC] Unearned veteran status stripped: " + kerbal.name
                        + (defaultCrew ? " (default crew)" : "")
                        + (string.IsNullOrEmpty(reason) ? "" : " reason=" + reason));
                }
            }

            bool afterVeteran = EACKerbalMemberAccess.ReadBool(kerbal, false, "veteran", "Veteran", "isVeteran", "IsVeteran");
            if (RosterRotationState.EACApplySuits)
            {
                int wantedSuit = afterVeteran ? RosterRotationState.EACVeteranSuit : RosterRotationState.EACDefaultSuit;
                if (EACSuitService.TryApplySuit(kerbal, wantedSuit))
                    changed = true;
            }

            bool badassChanged;
            if (EACBadassProgressionService.TryEvaluateBadass(kerbal, rec, afterVeteran, milestoneKey, out badassChanged))
                changed = changed || badassChanged;

            if (changed && requestSave)
                SaveScheduler.RequestSave("EAC veteran evaluation");
            return changed;
        }

        internal static bool StripUnearnedVeteransFromRoster(string reason)
        {
            if (IsDelegatedToEarnYourStripes) return false;
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            bool changed = false;
            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                var rec = RosterRotationState.GetOrCreate(k.name);
                bool qualifies = EACVeteranEligibility.IsAllowedVeteranClass(k.trait)
                    && EACVeteranEligibility.QualifiesForVeteran(k, rec, out _, out _, out _);
                bool currentVeteran = EACKerbalMemberAccess.ReadBool(k, false, "veteran", "Veteran", "isVeteran", "IsVeteran");
                bool defaultCrew = IsDefaultCrew(k.name);
                if (!qualifies && currentVeteran && ShouldStripUnearnedVeteran(defaultCrew))
                {
                    if (EACKerbalMemberAccess.WriteBool(k, false, "veteran", "Veteran", "isVeteran", "IsVeteran"))
                    {
                        changed = true;
                        RRLog.Info("[EAC] Unearned veteran status stripped: " + k.name
                            + (defaultCrew ? " (default crew)" : "")
                            + (string.IsNullOrEmpty(reason) ? "" : " reason=" + reason));
                    }
                }

                if (RosterRotationState.EACApplySuits)
                {
                    bool nowVet = EACKerbalMemberAccess.ReadBool(k, false, "veteran", "Veteran", "isVeteran", "IsVeteran");
                    int wantedSuit = nowVet ? RosterRotationState.EACVeteranSuit : RosterRotationState.EACDefaultSuit;
                    if (EACSuitService.TryApplySuit(k, wantedSuit)) changed = true;
                }
            }

            if (changed) SaveScheduler.RequestSave("EAC strip unearned veterans");
            return changed;
        }

        internal static bool GenerateReplacementStartingCrew(int count)
        {
            return EACStartingCrewService.GenerateReplacementStartingCrew(count);
        }

        internal static bool ApplySuitToRoster(string reason)
        {
            return EACSuitService.ApplySuitToRoster(reason);
        }

        internal static bool IsDefaultCrew(string kerbalName)
        {
            return DefaultCrewNames.Contains(kerbalName ?? string.Empty);
        }

        private static bool ShouldStripUnearnedVeteran(bool defaultCrew)
        {
            if (defaultCrew)
            {
                return RosterRotationState.EACStripDefaultVeterans;
            }

            return RosterRotationState.EACStripOtherUnearnedVeterans;
        }
    }
}

using System;
using KSP.UI.Screens;
using UnityEngine;

namespace RosterRotation
{
    /// <summary>
    /// Handles optional EAC-native Badass progression and one-roll-per-milestone tracking.
    /// </summary>
    internal static class EACBadassProgressionService
    {
        internal static bool TryEvaluateBadass(ProtoCrewMember kerbal, RosterRotationState.KerbalRecord rec, bool isVeteran, string milestoneKey, out bool changed)
        {
            changed = false;
            if (!RosterRotationState.EACBadassProgressionEnabled) return false;
            if (kerbal == null || rec == null) return false;
            if (RosterRotationState.EACBadassRequireVeteran && !isVeteran) return false;
            if (RosterRotationState.EACBadassRequireMilestone && string.IsNullOrEmpty(milestoneKey)) return false;

            bool alreadyBadass = EACKerbalMemberAccess.ReadBool(kerbal, false, "isBadass", "IsBadass", "badass", "Badass");
            if (alreadyBadass) return false;

            string key = string.IsNullOrEmpty(milestoneKey) ? "veteran" : milestoneKey;
            if (string.Equals(rec.EACBadassRollKey, key, StringComparison.OrdinalIgnoreCase))
                return false;

            rec.EACBadassRollKey = key;
            int chance = Math.Max(0, Math.Min(100, RosterRotationState.EACBadassChancePercent));
            bool success = chance >= 100 || (chance > 0 && UnityEngine.Random.Range(0, 100) < chance);
            if (success && EACKerbalMemberAccess.WriteBool(kerbal, true, "isBadass", "IsBadass", "badass", "Badass"))
            {
                rec.EACBadassAwarded = true;
                changed = true;
                string badassBody = kerbal.name + " has become Badass"
                    + (string.IsNullOrEmpty(key) ? "." : " after milestone: " + key + ".");
                RRLog.Info("[EAC] Badass status awarded: " + kerbal.name + " milestone=" + key + " chance=" + chance + "%");
                RosterRotationState.PostNotification(
                    EACNotificationType.Badass,
                    "EAC Badass Recognized",
                    badassBody,
                    MessageSystemButton.MessageButtonColor.GREEN,
                    MessageSystemButton.ButtonIcons.COMPLETE,
                    8f);
            }
            else
            {
                RRLog.Verbose("[EAC] Badass roll completed without award: " + kerbal.name + " milestone=" + key + " chance=" + chance + "%");
            }

            return true;
        }
    }
}

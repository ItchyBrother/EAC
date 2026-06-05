using System;
using System.Globalization;

namespace RosterRotation
{
    /// <summary>
    /// Applies EAC-native suit presentation when Earn Your Stripes is not present.
    /// </summary>
    internal static class EACSuitService
    {
        internal static bool ApplySuitToRoster(string reason)
        {
            if (EACVeteranService.IsDelegatedToEarnYourStripes || !RosterRotationState.EACApplySuits) return false;
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            bool changed = false;
            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                bool vet = EACKerbalMemberAccess.ReadBool(k, false, "veteran", "Veteran", "isVeteran", "IsVeteran");
                int wantedSuit = vet ? RosterRotationState.EACVeteranSuit : RosterRotationState.EACDefaultSuit;
                if (TryApplySuit(k, wantedSuit)) changed = true;
            }

            if (changed)
                SaveScheduler.RequestSave("EAC suit presentation " + (reason ?? ""));
            return changed;
        }

        internal static bool TryApplySuit(ProtoCrewMember kerbal, int suitValue)
        {
            if (kerbal == null) return false;
            suitValue = Math.Max(0, Math.Min(2, suitValue));

            object current;
            if (EACKerbalMemberAccess.TryRead(kerbal, out current, "suit", "Suit", "kerbalSuit", "KerbalSuit"))
            {
                try
                {
                    int currentInt = current is Enum ? Convert.ToInt32(current) : Convert.ToInt32(current, CultureInfo.InvariantCulture);
                    if (currentInt == suitValue) return false;
                }
                catch { /* if unreadable, still try to write */ }
            }

            return EACKerbalMemberAccess.TryWriteIntLike(kerbal, suitValue, "suit", "Suit", "kerbalSuit", "KerbalSuit");
        }
    }
}

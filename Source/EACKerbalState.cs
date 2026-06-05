using System;

namespace RosterRotation
{
    internal static class EACKerbalState
    {
        internal static double GetTrainingEndUT(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec)
        {
            if (pcm == null || rec == null) return 0;
            if (rec.Training == TrainingType.None) return 0;
            if (rec.Retired || rec.DeathUT > 0 || rec.DiedOnMission || rec.PendingMissionDeath) return 0;
            if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return 0;
            return pcm.inactiveTimeEnd > 0 ? pcm.inactiveTimeEnd : 0;
        }

        internal static bool IsTrainingActive(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            double until = GetTrainingEndUT(pcm, rec);
            return until > nowUT;
        }

        internal static bool RehydrateTrainingInactiveFlagIfNeeded(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec, double nowUT, string reason)
        {
            if (pcm == null || rec == null) return false;
            double until = GetTrainingEndUT(pcm, rec);
            if (until <= nowUT) return false;
            if (pcm.inactive) return false;

            pcm.inactive = true;
            RRLog.Verbose("[EAC] Rehydrated EAC training lock for " + pcm.name
                + ": until=" + until.ToString("0.###")
                + (string.IsNullOrEmpty(reason) ? "" : ", reason=" + reason));
            return true;
        }
    }
}

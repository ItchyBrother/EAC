using System;

namespace RosterRotation
{
    internal static class HallOfHistoryRules
    {
        public static bool IsKilledInAction(bool retired, double retiredUT, double deathUT)
        {
            if (deathUT <= 0d) return false;
            if (retired && retiredUT > 0d) return false;
            return true;
        }

        public static bool IsRetiredDeath(bool retired, double retiredUT, double deathUT)
        {
            return deathUT > 0d && retired && retiredUT > 0d;
        }

        public static double ResolveRecordedUt(double deathUT, double lastSeenUT, double rosterUT, double liveUT)
        {
            if (deathUT > 0d) return deathUT;
            if (lastSeenUT > 0d) return lastSeenUT;
            if (rosterUT > 0d) return rosterUT;
            if (liveUT > 0d) return liveUT;
            return -1d;
        }

        public static int ResolveRetiredEffectiveExperienceLevel(int experienceAtRetire, double retiredUT, double whenUT, int activeFallbackLevel, double yearSeconds)
        {
            int starsAtRetire = experienceAtRetire >= 0 ? experienceAtRetire : activeFallbackLevel;
            if (starsAtRetire < 0) return -1;
            if (retiredUT <= 0d || whenUT <= 0d || yearSeconds <= 0d) return starsAtRetire;

            double elapsed = Math.Max(0d, whenUT - retiredUT);
            int starsLost = (int)Math.Floor(elapsed / yearSeconds);
            return Clamp(starsAtRetire - starsLost, 0, 5);
        }

        public static bool ShouldShowRetiredExperience(bool isRetiredDeath, int baselineExperienceLevel)
        {
            return isRetiredDeath && baselineExperienceLevel >= 0;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

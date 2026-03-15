using System;

namespace RosterRotation
{
    internal static class KspTimeMath
    {
        public const double KerbinSecondsPerDay = 6d * 60d * 60d;
        public const double EarthSecondsPerDay = 24d * 60d * 60d;
        public const double KerbinDaysPerYearPrecise = 426.08d;
        public const double EarthDaysPerYearPrecise = 365.25d;
        public const double KerbinDisplayDaysPerYear = 426d;
        public const double EarthDisplayDaysPerYear = 365d;

        public static double GetDaySeconds(bool useKerbinTime)
        {
            return useKerbinTime ? KerbinSecondsPerDay : EarthSecondsPerDay;
        }

        public static double GetYearSeconds(bool useKerbinTime)
        {
            return GetDaySeconds(useKerbinTime) * (useKerbinTime ? KerbinDaysPerYearPrecise : EarthDaysPerYearPrecise);
        }

        public static double GetDisplayYearSeconds(bool useKerbinTime)
        {
            return GetDaySeconds(useKerbinTime) * (useKerbinTime ? KerbinDisplayDaysPerYear : EarthDisplayDaysPerYear);
        }

        public static int CalculateAgeYears(double birthUT, double currentUT, double yearSeconds)
        {
            // BirthUT may legitimately be negative in this mod: kerbals who are already
            // adults at campaign start can have birthdays before the game epoch.
            if (currentUT < birthUT || yearSeconds <= 0d)
                return -1;

            return Math.Max(0, (int)Math.Floor((currentUT - birthUT) / yearSeconds));
        }

        public static void GetYearDayHourMinute(double ut, bool useKerbinTime, out int year, out int day, out int hour, out int minute)
        {
            double secondsPerDay = GetDaySeconds(useKerbinTime);
            double daysPerYear = useKerbinTime ? KerbinDisplayDaysPerYear : EarthDisplayDaysPerYear;

            double totalDays = ut / secondsPerDay;
            year = Math.Max(1, (int)Math.Floor(totalDays / daysPerYear) + 1);
            double dayOfYear = totalDays - Math.Floor((year - 1) * daysPerYear);
            day = Math.Max(1, (int)Math.Floor(dayOfYear) + 1);

            double daySeconds = ut - Math.Floor(totalDays) * secondsPerDay;
            hour = Clamp((int)Math.Floor(daySeconds / 3600d), 0, 23);
            minute = Clamp((int)Math.Floor((daySeconds - (hour * 3600d)) / 60d), 0, 59);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

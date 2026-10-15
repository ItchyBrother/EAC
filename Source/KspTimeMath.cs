using System;
using System.Reflection;

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

        private const double DefaultSecondsPerHour = 60d * 60d;
        private const double DefaultSecondsPerMinute = 60d;

        private static object _cachedFormatter;
        private static CalendarValues _cachedCustomCalendar;
        private static bool _cachedFormatterWasCustom;

        // Optional Kronometer DATE PRESENTATION only. Discovery is cached for
        // the active formatter object; no Kronometer compile-time dependency.
        private static object _cachedDateFormatter;
        private static MethodInfo _cachedPrintDateMethod;
        private static MethodInfo _cachedPrintDateCompactMethod;
        private static bool _cachedDateFormatterIsKronometer;

        internal struct CalendarValues
        {
            public readonly double SecondsPerDay;
            public readonly double SecondsPerYear;
            public readonly double SecondsPerHour;
            public readonly double SecondsPerMinute;
            public readonly bool IsCustom;

            public CalendarValues(
                double secondsPerDay,
                double secondsPerYear,
                double secondsPerHour,
                double secondsPerMinute,
                bool isCustom)
            {
                SecondsPerDay = secondsPerDay;
                SecondsPerYear = secondsPerYear;
                SecondsPerHour = secondsPerHour;
                SecondsPerMinute = secondsPerMinute;
                IsCustom = isCustom;
            }

            public double DaysPerYear => SecondsPerDay > 0d ? SecondsPerYear / SecondsPerDay : 0d;
        }

        public static double GetDaySeconds(bool useKerbinTime)
        {
            return GetCalendar(useKerbinTime).SecondsPerDay;
        }

        public static double GetYearSeconds(bool useKerbinTime)
        {
            return GetCalendar(useKerbinTime).SecondsPerYear;
        }

        public static double GetDisplayYearSeconds(bool useKerbinTime)
        {
            CalendarValues calendar = GetCalendar(useKerbinTime);
            if (calendar.IsCustom)
                return calendar.SecondsPerYear;

            return calendar.SecondsPerDay * (useKerbinTime ? KerbinDisplayDaysPerYear : EarthDisplayDaysPerYear);
        }

        public static double GetDisplayDaysPerYear(bool useKerbinTime)
        {
            CalendarValues calendar = GetCalendar(useKerbinTime);
            if (calendar.IsCustom)
                return calendar.DaysPerYear;

            return useKerbinTime ? KerbinDisplayDaysPerYear : EarthDisplayDaysPerYear;
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
            CalendarValues calendar = GetCalendar(useKerbinTime);
            double secondsPerDay = calendar.SecondsPerDay;
            double secondsPerYear = calendar.IsCustom
                ? calendar.SecondsPerYear
                : secondsPerDay * (useKerbinTime ? KerbinDisplayDaysPerYear : EarthDisplayDaysPerYear);

            if (secondsPerDay <= 0d || secondsPerYear <= 0d)
            {
                year = 1;
                day = 1;
                hour = 0;
                minute = 0;
                return;
            }

            // Kronometer starts each year at its exact configured year boundary.  A
            // fractional day at the end of a year therefore carries its clock time
            // into Year N+1, Day 1, matching ClockFormatter.GetDate().
            double safeUt = Math.Max(0d, ut);
            int zeroBasedYear = (int)Math.Floor(safeUt / secondsPerYear);
            double startOfYear = zeroBasedYear * secondsPerYear;
            year = zeroBasedYear + 1;
            day = SmartFloor(safeUt / secondsPerDay) - SmartFloor(startOfYear / secondsPerDay) + 1;
            day = Math.Max(1, day);

            double daySeconds = PositiveModulo(safeUt, secondsPerDay);
            double secondsPerHour = calendar.SecondsPerHour > 0d ? calendar.SecondsPerHour : DefaultSecondsPerHour;
            double secondsPerMinute = calendar.SecondsPerMinute > 0d ? calendar.SecondsPerMinute : DefaultSecondsPerMinute;

            hour = Math.Max(0, (int)Math.Floor(daySeconds / secondsPerHour));
            double hourRemainder = daySeconds - hour * secondsPerHour;
            minute = Math.Max(0, (int)Math.Floor(hourRemainder / secondsPerMinute));
        }

        private static CalendarValues GetCalendar(bool useKerbinTime)
        {
            if (TryGetKronometerCalendar(out CalendarValues custom))
                return custom;

            double secondsPerDay = useKerbinTime ? KerbinSecondsPerDay : EarthSecondsPerDay;
            double daysPerYear = useKerbinTime ? KerbinDaysPerYearPrecise : EarthDaysPerYearPrecise;
            return new CalendarValues(
                secondsPerDay,
                secondsPerDay * daysPerYear,
                DefaultSecondsPerHour,
                DefaultSecondsPerMinute,
                false);
        }

        private static bool TryGetKronometerCalendar(out CalendarValues calendar)
        {
            calendar = default(CalendarValues);

            object formatter;
            try
            {
                formatter = KSPUtil.dateTimeFormatter;
            }
            catch
            {
                return false;
            }

            if (formatter == null)
                return false;

            if (ReferenceEquals(formatter, _cachedFormatter))
            {
                calendar = _cachedCustomCalendar;
                return _cachedFormatterWasCustom;
            }

            _cachedFormatter = formatter;
            _cachedFormatterWasCustom = false;
            _cachedCustomCalendar = default(CalendarValues);

            Type formatterType = formatter.GetType();
            string formatterTypeName = formatterType.FullName ?? formatterType.Name ?? string.Empty;
            if (formatterTypeName.IndexOf("Kronometer", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            if (!TryReadPositiveNumber(formatter, formatterType, "Day", out double daySeconds)
                || !TryReadPositiveNumber(formatter, formatterType, "Year", out double yearSeconds))
            {
                RRLog.WarnOnce(
                    "KspTimeMath:KronometerCalendarUnavailable",
                    "KspTimeMath: Kronometer is active, but its Day/Year values could not be read. Falling back to EAC's selected calendar preset.");
                return false;
            }

            double hourSeconds = DefaultSecondsPerHour;
            double minuteSeconds = DefaultSecondsPerMinute;
            if (TryReadPositiveNumber(formatter, formatterType, "Hour", out double detectedHourSeconds))
                hourSeconds = detectedHourSeconds;
            if (TryReadPositiveNumber(formatter, formatterType, "Minute", out double detectedMinuteSeconds))
                minuteSeconds = detectedMinuteSeconds;

            _cachedCustomCalendar = new CalendarValues(
                daySeconds,
                yearSeconds,
                hourSeconds,
                minuteSeconds,
                true);
            _cachedFormatterWasCustom = true;
            calendar = _cachedCustomCalendar;

            RRLog.VerboseOnce(
                "KspTimeMath:KronometerCalendarDetected",
                "KspTimeMath: using Kronometer calendar (day=" + daySeconds.ToString("0.###")
                + "s, year=" + yearSeconds.ToString("0.###") + "s)." );
            return true;
        }

        /// <summary>
        /// Ask the ACTIVE Kronometer formatter to present an absolute UT.
        /// It alone applies its month definitions, offsets and leap-year rules.
        /// For other calendars, or any reflection/formatting failure, callers
        /// keep their original EAC year/day fallback. No lifecycle math changes.
        /// </summary>
        public static bool TryFormatKronometerDate(double ut, bool includeTime, bool compact, out string formatted)
        {
            formatted = null;
            object formatter;
            try
            {
                formatter = KSPUtil.dateTimeFormatter;
            }
            catch
            {
                return false;
            }

            if (formatter == null)
                return false;

            if (!ReferenceEquals(formatter, _cachedDateFormatter))
            {
                _cachedDateFormatter = formatter;
                _cachedPrintDateMethod = null;
                _cachedPrintDateCompactMethod = null;
                _cachedDateFormatterIsKronometer = false;

                Type type = formatter.GetType();
                string typeName = type.FullName ?? type.Name ?? string.Empty;
                if (typeName.IndexOf("Kronometer", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _cachedDateFormatterIsKronometer = true;
                    try
                    {
                        // Kronometer implements PrintDate and PrintDateCompact
                        // with (double, bool, [bool]); accept older variants.
                        foreach (MethodInfo candidate in type.GetMethods(
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (candidate.ReturnType != typeof(string))
                                continue;
                            bool full = candidate.Name == "PrintDate";
                            bool shortDate = candidate.Name == "PrintDateCompact";
                            if (!full && !shortDate)
                                continue;

                            ParameterInfo[] args = candidate.GetParameters();
                            if (args.Length < 1 || args.Length > 3 || args[0].ParameterType != typeof(double))
                                continue;
                            bool compatible = true;
                            for (int i = 1; i < args.Length; i++)
                                if (args[i].ParameterType != typeof(bool))
                                    compatible = false;
                            if (!compatible)
                                continue;

                            if (full && (_cachedPrintDateMethod == null || args.Length > _cachedPrintDateMethod.GetParameters().Length))
                                _cachedPrintDateMethod = candidate;
                            if (shortDate && (_cachedPrintDateCompactMethod == null || args.Length > _cachedPrintDateCompactMethod.GetParameters().Length))
                                _cachedPrintDateCompactMethod = candidate;
                        }
                    }
                    catch (Exception ex)
                    {
                        RRLog.WarnOnce("KspTimeMath:KronometerDateDiscoveryFailed",
                            "KspTimeMath: failed to discover Kronometer date-format methods; using EAC dates. " + ex.Message);
                    }
                    if (_cachedPrintDateMethod == null)
                        RRLog.WarnOnce("KspTimeMath:KronometerDateUnavailable",
                            "KspTimeMath: Kronometer PrintDate is unavailable; using EAC dates where needed.");
                }
            }

            if (!_cachedDateFormatterIsKronometer)
                return false;

            MethodInfo method = compact
                ? (_cachedPrintDateCompactMethod ?? _cachedPrintDateMethod)
                : _cachedPrintDateMethod;
            if (method == null)
                return false;

            try
            {
                int count = method.GetParameters().Length;
                double safeUT = Math.Max(0d, ut);
                object[] args = count == 3 ? new object[] { safeUT, includeTime, false }
                    : count == 2 ? new object[] { safeUT, includeTime }
                    : new object[] { safeUT };
                string value = method.Invoke(formatter, args) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    formatted = value.Trim();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Exception reason = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException : ex;
                RRLog.WarnOnce("KspTimeMath:KronometerDateFormatFailed",
                    "KspTimeMath: Kronometer date formatting failed; using EAC date display. " + reason.Message);
            }
            return false;
        }

        private static bool TryReadPositiveNumber(object instance, Type type, string propertyName, out double value)
        {
            value = 0d;
            try
            {
                PropertyInfo property = type.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || property.GetIndexParameters().Length != 0)
                    return false;

                object raw = property.GetValue(instance, null);
                if (raw == null)
                    return false;

                value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
            }
            catch
            {
                value = 0d;
                return false;
            }
        }

        private static int SmartFloor(double value)
        {
            int ceiling = (int)Math.Ceiling(value);
            if (ceiling - value < 0.0001d)
                return ceiling;

            return (int)Math.Floor(value);
        }

        private static double PositiveModulo(double value, double divisor)
        {
            if (divisor <= 0d)
                return 0d;

            double result = value % divisor;
            return result < 0d ? result + divisor : result;
        }
    }
}

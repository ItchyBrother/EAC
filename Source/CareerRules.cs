using System;

namespace RosterRotation
{
    internal struct AgeAssignmentResult
    {
        public double BirthUT;
        public int LastAgedYears;
        public double NaturalRetirementUT;
    }

    internal static class CareerRules
    {
        public static int CalculateKerbalAge(double birthUT, double nowUT, double yearSeconds)
        {
            if (birthUT == 0d) return -1;
            return KspTimeMath.CalculateAgeYears(birthUT, nowUT, yearSeconds);
        }

        public static int CalculateRetiredEffectiveStars(int currentExperienceLevel, bool retired, int experienceAtRetire, double retiredUT, double nowUT, double yearSeconds)
        {
            if (!retired) return currentExperienceLevel;

            int starsAtRetire = experienceAtRetire >= 0 ? experienceAtRetire : currentExperienceLevel;
            if (starsAtRetire <= 0 || yearSeconds <= 0d) return Math.Max(0, starsAtRetire);

            int starsLost = (int)((nowUT - retiredUT) / yearSeconds);
            return Math.Max(0, starsAtRetire - starsLost);
        }

        public static double CalculateTrainingDays(float stupidity01, int targetLevel, double randomness01)
        {
            double baseDays = targetLevel * 30.0;
            double stupidity = Clamp01(stupidity01);
            double extraFrac = randomness01 * stupidity * 0.5;
            return baseDays * (1.0 + extraFrac);
        }

        public static string GetTrainingLabel(TrainingType trainingType, int targetLevel)
        {
            if (trainingType == TrainingType.InitialHire) return "introductory training";
            if (trainingType == TrainingType.ExperienceUpgrade) return $"Level {targetLevel} training";
            if (trainingType == TrainingType.RecallRefresher) return "refresher training";
            return "training";
        }

        public static double CalculateTrainingFundsCost(double hireCost, double trainingFundsMultiplier, int targetLevel)
        {
            return hireCost * trainingFundsMultiplier * targetLevel;
        }

        public static double CalculateTrainingRDCost(double trainingRdPerStar, int targetLevel)
        {
            return trainingRdPerStar * targetLevel;
        }

        public static double CalculateMoraleRetireProbability(int stars, double inactiveYears, int displayedFlights)
        {
            double baseP;
            switch (stars)
            {
                case 0: baseP = 0.08; break;
                case 1: baseP = 0.05; break;
                case 2: baseP = 0.03; break;
                default: baseP = 0.015; break;
            }

            double inactivityMultiplier;
            if (inactiveYears >= 3.0) inactivityMultiplier = 4.0;
            else if (inactiveYears >= 2.0) inactivityMultiplier = 2.5;
            else if (inactiveYears >= 1.0) inactivityMultiplier = 1.5;
            else inactivityMultiplier = 1.0;

            double activityReduction = Math.Min(displayedFlights / 15.0, 0.75);
            return baseP * inactivityMultiplier * (1.0 - activityReduction);
        }

        public static AgeAssignmentResult CalculateAgeOnHire(double nowUT, double yearSeconds, int retirementAgeMin, int retirementAgeMax, double randomAgeA, double randomAgeB, double randomBirthdayOffset, double randomRetireAge)
        {
            double r1 = 25.0 + randomAgeA * 20.0;
            double r2 = 25.0 + randomAgeB * 20.0;
            double ageYears = (r1 + r2) * 0.5;
            return BuildAgeAssignment(nowUT, yearSeconds, retirementAgeMin, retirementAgeMax, ageYears, randomBirthdayOffset, randomRetireAge);
        }

        public static AgeAssignmentResult CalculateAgeByExperience(int stars, double nowUT, double yearSeconds, int retirementAgeMin, int retirementAgeMax, double randomAge, double randomBirthdayOffset, double randomRetireAge)
        {
            double ageMin, ageMax;
            if (stars >= 5) { ageMin = 45; ageMax = 51; }
            else if (stars == 4) { ageMin = 38; ageMax = 46; }
            else if (stars == 3) { ageMin = 35; ageMax = 42; }
            else { ageMin = 25; ageMax = 35; }

            double ageYears = ageMin + randomAge * (ageMax - ageMin);
            return BuildAgeAssignment(nowUT, yearSeconds, retirementAgeMin, retirementAgeMax, ageYears, randomBirthdayOffset, randomRetireAge);
        }

        private static AgeAssignmentResult BuildAgeAssignment(double nowUT, double yearSeconds, int retirementAgeMin, int retirementAgeMax, double ageYears, double randomBirthdayOffset, double randomRetireAge)
        {
            double birthdayOffset = (0.15 + randomBirthdayOffset * 0.70) * yearSeconds;
            double birthUT = nowUT - (ageYears * yearSeconds) - birthdayOffset;
            int lastAgedYears = yearSeconds > 0d ? (int)((nowUT - birthUT) / yearSeconds) : -1;
            int retireAge = retirementAgeMin + (int)(randomRetireAge * (retirementAgeMax - retirementAgeMin + 1));

            return new AgeAssignmentResult
            {
                BirthUT = birthUT,
                LastAgedYears = lastAgedYears,
                NaturalRetirementUT = birthUT + retireAge * yearSeconds
            };
        }

        private static double Clamp01(double value)
        {
            if (value < 0d) return 0d;
            if (value > 1d) return 1d;
            return value;
        }
    }
}

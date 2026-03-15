using System;
using System.Globalization;

namespace RosterRotation
{
    internal sealed class EacSettingsSnapshot
    {
        public double RestDays = 14;
        public bool UseKerbinDays = true;
        public int TrainingInitialDays = 30;
        public int TrainingStarDays = 30;
        public double TrainingFundsMultiplier = 1.0;
        public double TrainingRDPerStar = 10.0;
        public double TrainingBaseFundsCost = 62000;
        public double RecallFundsCostMultiplier = 1.0;
        public bool AgingEnabled = true;
        public bool DeathNotificationsEnabled = true;
        public bool HudNotificationsEnabled = true;
        public bool MessageAppNotificationsEnabled = true;
        public bool BirthdayNotificationsEnabled = true;
        public bool TrainingNotificationsEnabled = true;
        public bool RetirementNotificationsEnabled = true;
        public int RetirementAgeMin = 48;
        public int RetirementAgeMax = 55;
        public int RetiredDeathAgeMin = 55;
        public bool AutoCleanupUnreferencedKerbals = false;
        public bool VerboseLogging = false;
        public bool VerboseAgeLogging = false;
        public bool SyncFlightTrackerFromEacOnce = false;
        public bool TraitGrowthEnabled = false;
        public bool PortraitCaptureEnabled = true;
        public bool MissionDeathEnabled = false;
    }

    internal static class KerbalRecordPersistence
    {
        public static EacSettingsSnapshot CaptureSettingsFromState()
        {
            return new EacSettingsSnapshot
            {
                RestDays = RosterRotationState.RestDays,
                UseKerbinDays = RosterRotationState.UseKerbinDays,
                TrainingInitialDays = RosterRotationState.TrainingInitialDays,
                TrainingStarDays = RosterRotationState.TrainingStarDays,
                TrainingFundsMultiplier = RosterRotationState.TrainingFundsMultiplier,
                TrainingRDPerStar = RosterRotationState.TrainingRDPerStar,
                TrainingBaseFundsCost = RosterRotationState.TrainingBaseFundsCost,
                RecallFundsCostMultiplier = RosterRotationState.RecallFundsCostMultiplier,
                AgingEnabled = RosterRotationState.AgingEnabled,
                DeathNotificationsEnabled = RosterRotationState.DeathNotificationsEnabled,
                HudNotificationsEnabled = RosterRotationState.HudNotificationsEnabled,
                MessageAppNotificationsEnabled = RosterRotationState.MessageAppNotificationsEnabled,
                BirthdayNotificationsEnabled = RosterRotationState.BirthdayNotificationsEnabled,
                TrainingNotificationsEnabled = RosterRotationState.TrainingNotificationsEnabled,
                RetirementNotificationsEnabled = RosterRotationState.RetirementNotificationsEnabled,
                RetirementAgeMin = RosterRotationState.RetirementAgeMin,
                RetirementAgeMax = RosterRotationState.RetirementAgeMax,
                RetiredDeathAgeMin = RosterRotationState.RetiredDeathAgeMin,
                AutoCleanupUnreferencedKerbals = RosterRotationState.AutoCleanupUnreferencedKerbals,
                VerboseLogging = RosterRotationState.VerboseLogging,
                VerboseAgeLogging = RosterRotationState.VerboseAgeLogging,
                SyncFlightTrackerFromEacOnce = RosterRotationState.SyncFlightTrackerFromEacOnce,
                TraitGrowthEnabled = RosterRotationState.TraitGrowthEnabled,
                PortraitCaptureEnabled = RosterRotationState.PortraitCaptureEnabled,
                MissionDeathEnabled = RosterRotationState.MissionDeathEnabled
            };
        }

        public static EacSettingsSnapshot ReadSettings(ConfigNode settingsNode)
        {
            var settings = new EacSettingsSnapshot();
            if (settingsNode == null) return settings;

            settings.RestDays = PD(settingsNode.GetValue("restDays"), 14);
            settings.UseKerbinDays = PB(settingsNode.GetValue("useKerbinDays"), true);
            settings.TrainingInitialDays = PI(settingsNode.GetValue("trainingInitialDays"), 30);
            settings.TrainingStarDays = PI(settingsNode.GetValue("trainingStarDays"), 30);
            settings.TrainingFundsMultiplier = PD(settingsNode.GetValue("trainingFundsMultiplier"), 1.0);
            settings.TrainingRDPerStar = PD(settingsNode.GetValue("trainingRDPerStar"), 10.0);
            settings.TrainingBaseFundsCost = PD(settingsNode.GetValue("trainingBaseFundsCost"), 62000);
            settings.RecallFundsCostMultiplier = PD(settingsNode.GetValue("recallFundsCostMultiplier"), 1.0);
            settings.AgingEnabled = PB(settingsNode.GetValue("agingEnabled"), true);
            settings.DeathNotificationsEnabled = PB(settingsNode.GetValue("deathNotificationsEnabled"), true);
            settings.HudNotificationsEnabled = PB(settingsNode.GetValue("hudNotificationsEnabled"), true);
            settings.MessageAppNotificationsEnabled = PB(settingsNode.GetValue("messageAppNotificationsEnabled"), true);
            settings.BirthdayNotificationsEnabled = PB(settingsNode.GetValue("birthdayNotificationsEnabled"), true);
            settings.TrainingNotificationsEnabled = PB(settingsNode.GetValue("trainingNotificationsEnabled"), true);
            settings.RetirementNotificationsEnabled = PB(settingsNode.GetValue("retirementNotificationsEnabled"), true);
            settings.RetirementAgeMin = PI(settingsNode.GetValue("retirementAgeMin"), 48);
            settings.RetirementAgeMax = PI(settingsNode.GetValue("retirementAgeMax"), 55);
            settings.RetiredDeathAgeMin = PI(settingsNode.GetValue("retiredDeathAgeMin"), 55);
            settings.AutoCleanupUnreferencedKerbals = PB(settingsNode.GetValue("autoCleanupUnreferencedKerbals"), false);
            settings.VerboseLogging = PB(settingsNode.GetValue("verboseLogging"), false);
            settings.VerboseAgeLogging = PB(settingsNode.GetValue("verboseAgeLogging"), false);
            settings.SyncFlightTrackerFromEacOnce = PB(settingsNode.GetValue("syncFlightTrackerFromEacOnce"), false);
            settings.TraitGrowthEnabled = PB(settingsNode.GetValue("traitGrowthEnabled"), false);
            settings.PortraitCaptureEnabled = PB(settingsNode.GetValue("portraitCaptureEnabled"), true);
            settings.MissionDeathEnabled = PB(settingsNode.GetValue("missionDeathEnabled"), false);
            return settings;
        }

        public static void ApplySettingsToState(EacSettingsSnapshot settings, bool preserveVerboseSettings)
        {
            if (settings == null) return;

            RosterRotationState.RestDays = settings.RestDays;
            RosterRotationState.UseKerbinDays = settings.UseKerbinDays;
            RosterRotationState.TrainingInitialDays = settings.TrainingInitialDays;
            RosterRotationState.TrainingStarDays = settings.TrainingStarDays;
            RosterRotationState.TrainingFundsMultiplier = settings.TrainingFundsMultiplier;
            RosterRotationState.TrainingRDPerStar = settings.TrainingRDPerStar;
            RosterRotationState.TrainingBaseFundsCost = settings.TrainingBaseFundsCost;
            RosterRotationState.RecallFundsCostMultiplier = settings.RecallFundsCostMultiplier;
            RosterRotationState.AgingEnabled = settings.AgingEnabled;
            RosterRotationState.DeathNotificationsEnabled = settings.DeathNotificationsEnabled;
            RosterRotationState.HudNotificationsEnabled = settings.HudNotificationsEnabled;
            RosterRotationState.MessageAppNotificationsEnabled = settings.MessageAppNotificationsEnabled;
            RosterRotationState.BirthdayNotificationsEnabled = settings.BirthdayNotificationsEnabled;
            RosterRotationState.TrainingNotificationsEnabled = settings.TrainingNotificationsEnabled;
            RosterRotationState.RetirementNotificationsEnabled = settings.RetirementNotificationsEnabled;
            RosterRotationState.RetirementAgeMin = settings.RetirementAgeMin;
            RosterRotationState.RetirementAgeMax = settings.RetirementAgeMax;
            RosterRotationState.RetiredDeathAgeMin = settings.RetiredDeathAgeMin;
            RosterRotationState.AutoCleanupUnreferencedKerbals = settings.AutoCleanupUnreferencedKerbals;
            if (!preserveVerboseSettings)
            {
                RosterRotationState.VerboseLogging = settings.VerboseLogging;
                RosterRotationState.VerboseAgeLogging = settings.VerboseAgeLogging;
            }
            RosterRotationState.SyncFlightTrackerFromEacOnce = settings.SyncFlightTrackerFromEacOnce;
            RosterRotationState.TraitGrowthEnabled = settings.TraitGrowthEnabled;
            RosterRotationState.PortraitCaptureEnabled = settings.PortraitCaptureEnabled;
            RosterRotationState.MissionDeathEnabled = settings.MissionDeathEnabled;
            RosterRotationState.DebugForceMissionDeath = false;
        }

        public static void WriteSettingsNode(ConfigNode node, EacSettingsSnapshot settings, IFormatProvider formatProvider)
        {
            if (node == null || settings == null) return;
            var ci = formatProvider as CultureInfo ?? CultureInfo.InvariantCulture;

            node.AddValue("restDays", settings.RestDays.ToString(ci));
            node.AddValue("useKerbinDays", settings.UseKerbinDays.ToString(ci));
            node.AddValue("trainingInitialDays", settings.TrainingInitialDays.ToString(ci));
            node.AddValue("trainingStarDays", settings.TrainingStarDays.ToString(ci));
            node.AddValue("trainingFundsMultiplier", settings.TrainingFundsMultiplier.ToString(ci));
            node.AddValue("trainingRDPerStar", settings.TrainingRDPerStar.ToString(ci));
            node.AddValue("trainingBaseFundsCost", settings.TrainingBaseFundsCost.ToString(ci));
            node.AddValue("recallFundsCostMultiplier", settings.RecallFundsCostMultiplier.ToString(ci));
            node.AddValue("agingEnabled", settings.AgingEnabled.ToString(ci));
            node.AddValue("deathNotificationsEnabled", settings.DeathNotificationsEnabled.ToString(ci));
            node.AddValue("hudNotificationsEnabled", settings.HudNotificationsEnabled.ToString(ci));
            node.AddValue("messageAppNotificationsEnabled", settings.MessageAppNotificationsEnabled.ToString(ci));
            node.AddValue("birthdayNotificationsEnabled", settings.BirthdayNotificationsEnabled.ToString(ci));
            node.AddValue("trainingNotificationsEnabled", settings.TrainingNotificationsEnabled.ToString(ci));
            node.AddValue("retirementNotificationsEnabled", settings.RetirementNotificationsEnabled.ToString(ci));
            node.AddValue("retirementAgeMin", settings.RetirementAgeMin.ToString(ci));
            node.AddValue("retirementAgeMax", settings.RetirementAgeMax.ToString(ci));
            node.AddValue("retiredDeathAgeMin", settings.RetiredDeathAgeMin.ToString(ci));
            node.AddValue("autoCleanupUnreferencedKerbals", settings.AutoCleanupUnreferencedKerbals.ToString(ci));
            node.AddValue("verboseLogging", settings.VerboseLogging.ToString(ci));
            node.AddValue("verboseAgeLogging", settings.VerboseAgeLogging.ToString(ci));
            node.AddValue("syncFlightTrackerFromEacOnce", settings.SyncFlightTrackerFromEacOnce.ToString(ci));
            node.AddValue("traitGrowthEnabled", settings.TraitGrowthEnabled.ToString(ci));
            node.AddValue("portraitCaptureEnabled", settings.PortraitCaptureEnabled.ToString(ci));
            node.AddValue("missionDeathEnabled", settings.MissionDeathEnabled.ToString(ci));
        }

        public static bool TryReadRecord(ConfigNode recordNode, out string name, out RosterRotationState.KerbalRecord record)
        {
            name = recordNode != null ? recordNode.GetValue("name") : null;
            record = null;
            if (recordNode == null || string.IsNullOrEmpty(name))
                return false;

            record = new RosterRotationState.KerbalRecord
            {
                OriginalTrait = recordNode.GetValue("originalTrait"),
                OriginalType = ParseKerbalType(recordNode.GetValue("originalType"), ProtoCrewMember.KerbalType.Crew),
                Flights = PI(recordNode.GetValue("flights"), 0),
                LastFlightUT = PD(recordNode.GetValue("lastFlightUT"), 0),
                RestUntilUT = PD(recordNode.GetValue("restUntilUT"), 0),
                Retired = PB(recordNode.GetValue("retired"), false),
                RetiredUT = PD(recordNode.GetValue("retiredUT"), 0),
                ExperienceAtRetire = PI(recordNode.GetValue("experienceAtRetire"), -1),
                MissionStartUT = PD(recordNode.GetValue("missionStartUT"), 0),
                Training = (TrainingType)PI(recordNode.GetValue("trainingType"), 0),
                TrainingTargetLevel = PI(recordNode.GetValue("trainingTargetLevel"), 0),
                GrantedLevel = PI(recordNode.GetValue("grantedLevel"), -1),
                BirthUT = PD(recordNode.GetValue("birthUT"), 0),
                NaturalRetirementUT = PD(recordNode.GetValue("naturalRetirementUT"), 0),
                RetirementDelayYears = PI(recordNode.GetValue("retirementDelayYears"), 0),
                RetirementWarned = PB(recordNode.GetValue("retirementWarned"), false),
                RetirementScheduled = PB(recordNode.GetValue("retirementScheduled"), false),
                RetirementScheduledUT = PD(recordNode.GetValue("retirementScheduledUT"), 0),
                DeathUT = PD(recordNode.GetValue("deathUT"), 0),
                LastMissionDeathCheckUT = PD(recordNode.GetValue("lastMissionDeathCheckUT"), 0),
                DiedOnMission = PB(recordNode.GetValue("diedOnMission"), false),
                PendingMissionDeath = PB(recordNode.GetValue("pendingMissionDeath"), false),
                TrainingEndUT = PD(recordNode.GetValue("trainingEndUT"), 0),
                LastAgedYears = PI(recordNode.GetValue("lastAgedYears"), -1),
            };

            return true;
        }

        public static void WriteRecordNode(ConfigNode node, string name, RosterRotationState.KerbalRecord record, IFormatProvider formatProvider)
        {
            if (node == null || record == null || string.IsNullOrEmpty(name)) return;
            var ci = formatProvider as CultureInfo ?? CultureInfo.InvariantCulture;

            node.AddValue("name", name);
            if (!string.IsNullOrEmpty(record.OriginalTrait)) node.AddValue("originalTrait", record.OriginalTrait);
            node.AddValue("originalType", ((int)record.OriginalType).ToString(ci));
            node.AddValue("flights", record.Flights.ToString(ci));
            node.AddValue("lastFlightUT", record.LastFlightUT.ToString("R", ci));
            node.AddValue("restUntilUT", record.RestUntilUT.ToString("R", ci));
            node.AddValue("retired", record.Retired.ToString(ci));
            node.AddValue("retiredUT", record.RetiredUT.ToString("R", ci));
            if (record.ExperienceAtRetire >= 0) node.AddValue("experienceAtRetire", record.ExperienceAtRetire.ToString(ci));
            node.AddValue("missionStartUT", record.MissionStartUT.ToString("R", ci));
            node.AddValue("trainingType", ((int)record.Training).ToString(ci));
            node.AddValue("trainingTargetLevel", record.TrainingTargetLevel.ToString(ci));
            if (record.GrantedLevel >= 0) node.AddValue("grantedLevel", record.GrantedLevel.ToString(ci));
            if (record.TrainingEndUT > 0) node.AddValue("trainingEndUT", record.TrainingEndUT.ToString("R", ci));
            if (record.LastAgedYears >= 0)
            {
                node.AddValue("birthUT", record.BirthUT.ToString("R", ci));
                node.AddValue("naturalRetirementUT", record.NaturalRetirementUT.ToString("R", ci));
                node.AddValue("retirementDelayYears", record.RetirementDelayYears.ToString(ci));
                node.AddValue("retirementWarned", record.RetirementWarned.ToString(ci));
                node.AddValue("retirementScheduled", record.RetirementScheduled.ToString(ci));
                if (record.RetirementScheduledUT > 0) node.AddValue("retirementScheduledUT", record.RetirementScheduledUT.ToString("R", ci));
                if (record.DeathUT > 0) node.AddValue("deathUT", record.DeathUT.ToString("R", ci));
                if (record.LastMissionDeathCheckUT > 0) node.AddValue("lastMissionDeathCheckUT", record.LastMissionDeathCheckUT.ToString("R", ci));
                if (record.DiedOnMission) node.AddValue("diedOnMission", record.DiedOnMission.ToString(ci));
                if (record.PendingMissionDeath) node.AddValue("pendingMissionDeath", record.PendingMissionDeath.ToString(ci));
                node.AddValue("lastAgedYears", record.LastAgedYears.ToString(ci));
            }
        }

        private static int PI(string s, int fb)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fb;
        }

        private static double PD(string s, double fb)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fb;
        }

        private static bool PB(string s, bool fb)
        {
            return bool.TryParse(s, out bool v) ? v : fb;
        }

        private static ProtoCrewMember.KerbalType ParseKerbalType(string s, ProtoCrewMember.KerbalType fb)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? (ProtoCrewMember.KerbalType)i : fb;
        }
    }
}

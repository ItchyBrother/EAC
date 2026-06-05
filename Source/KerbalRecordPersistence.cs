using System;
using System.Globalization;

namespace RosterRotation
{
    internal sealed class EacSettingsSnapshot
    {
        public double RestDays = 14;
        public double RecoveryLeavePercent = 10;
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
        public bool VeteranNotificationsEnabled = true;
        public bool BadassNotificationsEnabled = true;
        public int RetirementAgeMin = 37;
        public int RetirementAgeMax = 47;
        public int RetiredDeathAgeMin = 50;
        public bool AutoCleanupUnreferencedKerbals = false;
        public bool VerboseLogging = false;
        public bool VerboseAgeLogging = false;
        public bool SyncFlightTrackerFromEacOnce = false;
        public bool TraitGrowthEnabled = false;
        public bool FinalExamContractsEnabled = false;
        public string GraduationExamHistory = "";
        public bool PortraitCaptureEnabled = true;
        public bool MissionDeathEnabled = false;
        public bool EACVeteranStatusEnabled = true;
        public int EACVeteranFlightsRequired = 5;
        public double EACVeteranHoursRequired = 12.0;
        public bool EACVeteranRequireMilestone = false;
        public bool EACStripDefaultVeterans = true;
        public bool EACStripOtherUnearnedVeterans = false;
        public bool EACAllowPilotVeterans = true;
        public bool EACAllowEngineerVeterans = true;
        public bool EACAllowScientistVeterans = true;
        public bool EACApplySuits = true;
        public int EACDefaultSuit = 0;
        public int EACVeteranSuit = 2;
        public bool EACBadassProgressionEnabled = false;
        public bool EACBadassRequireVeteran = true;
        public bool EACBadassRequireMilestone = true;
        public int EACBadassChancePercent = 10;
        public bool EACNewGameCrewSetupEnabled = true;
        public bool EACNewGameCrewSetupCompleted = false;
        public bool EACReplaceStartingCrewDefault = false;
        public int EACStartingCrewCount = 4;
        public bool EACStartingCrewAllowMales = true;
        public bool EACStartingCrewAllowFemales = true;
        public bool EACStartingCrewAllowPilots = true;
        public bool EACStartingCrewAllowEngineers = true;
        public bool EACStartingCrewAllowScientists = true;
        public bool CrewRotationAdvisorEnabled = true;
        public double DeepFreezeThawDeathBaseChance = 0.005;
        public double DeepFreezeThawDeathBonusPerYear = 0.001;
        public double DeepFreezeThawDeathMaxChance = 0.05;
    }

    internal static class KerbalRecordPersistence
    {
        public static EacSettingsSnapshot CaptureSettingsFromState()
        {
            return new EacSettingsSnapshot
            {
                RestDays = RosterRotationState.RestDays,
                RecoveryLeavePercent = RosterRotationState.RecoveryLeavePercent,
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
                VeteranNotificationsEnabled = RosterRotationState.VeteranNotificationsEnabled,
                BadassNotificationsEnabled = RosterRotationState.BadassNotificationsEnabled,
                RetirementAgeMin = RosterRotationState.RetirementAgeMin,
                RetirementAgeMax = RosterRotationState.RetirementAgeMax,
                RetiredDeathAgeMin = RosterRotationState.RetiredDeathAgeMin,
                AutoCleanupUnreferencedKerbals = RosterRotationState.AutoCleanupUnreferencedKerbals,
                VerboseLogging = RosterRotationState.VerboseLogging,
                VerboseAgeLogging = RosterRotationState.VerboseAgeLogging,
                SyncFlightTrackerFromEacOnce = RosterRotationState.SyncFlightTrackerFromEacOnce,
                TraitGrowthEnabled = RosterRotationState.TraitGrowthEnabled,
                FinalExamContractsEnabled = RosterRotationState.FinalExamContractsEnabled,
                GraduationExamHistory = RosterRotationState.GraduationExamHistory,
                PortraitCaptureEnabled = RosterRotationState.PortraitCaptureEnabled,
                MissionDeathEnabled = RosterRotationState.MissionDeathEnabled,
                EACVeteranStatusEnabled = RosterRotationState.EACVeteranStatusEnabled,
                EACVeteranFlightsRequired = RosterRotationState.EACVeteranFlightsRequired,
                EACVeteranHoursRequired = RosterRotationState.EACVeteranHoursRequired,
                EACVeteranRequireMilestone = RosterRotationState.EACVeteranRequireMilestone,
                EACStripDefaultVeterans = RosterRotationState.EACStripDefaultVeterans,
                EACStripOtherUnearnedVeterans = RosterRotationState.EACStripOtherUnearnedVeterans,
                EACAllowPilotVeterans = RosterRotationState.EACAllowPilotVeterans,
                EACAllowEngineerVeterans = RosterRotationState.EACAllowEngineerVeterans,
                EACAllowScientistVeterans = RosterRotationState.EACAllowScientistVeterans,
                EACApplySuits = RosterRotationState.EACApplySuits,
                EACDefaultSuit = RosterRotationState.EACDefaultSuit,
                EACVeteranSuit = RosterRotationState.EACVeteranSuit,
                EACBadassProgressionEnabled = RosterRotationState.EACBadassProgressionEnabled,
                EACBadassRequireVeteran = RosterRotationState.EACBadassRequireVeteran,
                EACBadassRequireMilestone = RosterRotationState.EACBadassRequireMilestone,
                EACBadassChancePercent = RosterRotationState.EACBadassChancePercent,
                EACNewGameCrewSetupEnabled = RosterRotationState.EACNewGameCrewSetupEnabled,
                EACNewGameCrewSetupCompleted = RosterRotationState.EACNewGameCrewSetupCompleted,
                EACReplaceStartingCrewDefault = RosterRotationState.EACReplaceStartingCrewDefault,
                EACStartingCrewCount = RosterRotationState.EACStartingCrewCount,
                EACStartingCrewAllowMales = RosterRotationState.EACStartingCrewAllowMales,
                EACStartingCrewAllowFemales = RosterRotationState.EACStartingCrewAllowFemales,
                EACStartingCrewAllowPilots = RosterRotationState.EACStartingCrewAllowPilots,
                EACStartingCrewAllowEngineers = RosterRotationState.EACStartingCrewAllowEngineers,
                EACStartingCrewAllowScientists = RosterRotationState.EACStartingCrewAllowScientists,
                CrewRotationAdvisorEnabled = RosterRotationState.CrewRotationAdvisorEnabled,
                DeepFreezeThawDeathBaseChance = RosterRotationState.DeepFreezeThawDeathBaseChance,
                DeepFreezeThawDeathBonusPerYear = RosterRotationState.DeepFreezeThawDeathBonusPerYear,
                DeepFreezeThawDeathMaxChance = RosterRotationState.DeepFreezeThawDeathMaxChance
            };
        }

        public static EacSettingsSnapshot ReadSettings(ConfigNode settingsNode)
        {
            var settings = new EacSettingsSnapshot();
            if (settingsNode == null) return settings;

            settings.RestDays = PD(settingsNode.GetValue("restDays"), 14);
            if (settingsNode.HasValue("recoveryLeavePercent"))
                settings.RecoveryLeavePercent = PD(settingsNode.GetValue("recoveryLeavePercent"), 10);
            else
                settings.RecoveryLeavePercent = settings.RestDays <= 0 ? 0 : 10;
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
            settings.VeteranNotificationsEnabled = PB(settingsNode.GetValue("veteranNotificationsEnabled"), true);
            settings.BadassNotificationsEnabled = PB(settingsNode.GetValue("badassNotificationsEnabled"), true);
            settings.RetirementAgeMin = PI(settingsNode.GetValue("retirementAgeMin"), 37);
            settings.RetirementAgeMax = PI(settingsNode.GetValue("retirementAgeMax"), 47);
            settings.RetiredDeathAgeMin = PI(settingsNode.GetValue("retiredDeathAgeMin"), 50);
            settings.AutoCleanupUnreferencedKerbals = PB(settingsNode.GetValue("autoCleanupUnreferencedKerbals"), false);
            settings.VerboseLogging = PB(settingsNode.GetValue("verboseLogging"), false);
            settings.VerboseAgeLogging = PB(settingsNode.GetValue("verboseAgeLogging"), false);
            settings.SyncFlightTrackerFromEacOnce = PB(settingsNode.GetValue("syncFlightTrackerFromEacOnce"), false);
            settings.TraitGrowthEnabled = PB(settingsNode.GetValue("traitGrowthEnabled"), false);
            settings.FinalExamContractsEnabled = PB(settingsNode.GetValue("finalExamContractsEnabled"), false);
            settings.GraduationExamHistory = settingsNode.GetValue("graduationExamHistory") ?? "";
            settings.PortraitCaptureEnabled = PB(settingsNode.GetValue("portraitCaptureEnabled"), true);
            settings.MissionDeathEnabled = PB(settingsNode.GetValue("missionDeathEnabled"), false);
            settings.EACVeteranStatusEnabled = PB(settingsNode.GetValue("eacVeteranStatusEnabled"), true);
            settings.EACVeteranFlightsRequired = PI(settingsNode.GetValue("eacVeteranFlightsRequired"), 5);
            settings.EACVeteranHoursRequired = PD(settingsNode.GetValue("eacVeteranHoursRequired"), 12.0);
            settings.EACVeteranRequireMilestone = PB(settingsNode.GetValue("eacVeteranRequireMilestone"), false);
            settings.EACStripDefaultVeterans = PB(settingsNode.GetValue("eacStripDefaultVeterans"), true);
            settings.EACStripOtherUnearnedVeterans = PB(settingsNode.GetValue("eacStripOtherUnearnedVeterans"), false);
            settings.EACAllowPilotVeterans = PB(settingsNode.GetValue("eacAllowPilotVeterans"), true);
            settings.EACAllowEngineerVeterans = PB(settingsNode.GetValue("eacAllowEngineerVeterans"), true);
            settings.EACAllowScientistVeterans = PB(settingsNode.GetValue("eacAllowScientistVeterans"), true);
            settings.EACApplySuits = PB(settingsNode.GetValue("eacApplySuits"), true);
            settings.EACDefaultSuit = PI(settingsNode.GetValue("eacDefaultSuit"), 0);
            settings.EACVeteranSuit = PI(settingsNode.GetValue("eacVeteranSuit"), 2);
            settings.EACBadassProgressionEnabled = PB(settingsNode.GetValue("eacBadassProgressionEnabled"), false);
            settings.EACBadassRequireVeteran = PB(settingsNode.GetValue("eacBadassRequireVeteran"), true);
            settings.EACBadassRequireMilestone = PB(settingsNode.GetValue("eacBadassRequireMilestone"), true);
            settings.EACBadassChancePercent = PI(settingsNode.GetValue("eacBadassChancePercent"), 10);
            settings.EACNewGameCrewSetupEnabled = PB(settingsNode.GetValue("eacNewGameCrewSetupEnabled"), true);
            settings.EACNewGameCrewSetupCompleted = PB(settingsNode.GetValue("eacNewGameCrewSetupCompleted"), false);
            settings.EACReplaceStartingCrewDefault = PB(settingsNode.GetValue("eacReplaceStartingCrewDefault"), false);
            settings.EACStartingCrewCount = PI(settingsNode.GetValue("eacStartingCrewCount"), 4);
            settings.EACStartingCrewAllowMales = PB(settingsNode.GetValue("eacStartingCrewAllowMales"), true);
            settings.EACStartingCrewAllowFemales = PB(settingsNode.GetValue("eacStartingCrewAllowFemales"), true);
            settings.EACStartingCrewAllowPilots = PB(settingsNode.GetValue("eacStartingCrewAllowPilots"), true);
            settings.EACStartingCrewAllowEngineers = PB(settingsNode.GetValue("eacStartingCrewAllowEngineers"), true);
            settings.EACStartingCrewAllowScientists = PB(settingsNode.GetValue("eacStartingCrewAllowScientists"), true);
            settings.CrewRotationAdvisorEnabled = PB(settingsNode.GetValue("crewRotationAdvisorEnabled"), true);
            settings.DeepFreezeThawDeathBaseChance = PD(settingsNode.GetValue("deepFreezeThawDeathBaseChance"), 0.005);
            settings.DeepFreezeThawDeathBonusPerYear = PD(settingsNode.GetValue("deepFreezeThawDeathBonusPerYear"), 0.001);
            settings.DeepFreezeThawDeathMaxChance = PD(settingsNode.GetValue("deepFreezeThawDeathMaxChance"), 0.05);
            return settings;
        }

        public static void ApplySettingsToState(EacSettingsSnapshot settings, bool preserveVerboseSettings)
        {
            if (settings == null) return;

            RosterRotationState.RestDays = settings.RestDays;
            RosterRotationState.RecoveryLeavePercent = settings.RecoveryLeavePercent;
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
            RosterRotationState.VeteranNotificationsEnabled = settings.VeteranNotificationsEnabled;
            RosterRotationState.BadassNotificationsEnabled = settings.BadassNotificationsEnabled;
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
            RosterRotationState.FinalExamContractsEnabled = settings.FinalExamContractsEnabled;
            RosterRotationState.GraduationExamHistory = settings.GraduationExamHistory ?? "";
            RosterRotationState.PortraitCaptureEnabled = settings.PortraitCaptureEnabled;
            RosterRotationState.MissionDeathEnabled = settings.MissionDeathEnabled;
            RosterRotationState.EACVeteranStatusEnabled = settings.EACVeteranStatusEnabled;
            RosterRotationState.EACVeteranFlightsRequired = settings.EACVeteranFlightsRequired;
            RosterRotationState.EACVeteranHoursRequired = settings.EACVeteranHoursRequired;
            RosterRotationState.EACVeteranRequireMilestone = settings.EACVeteranRequireMilestone;
            RosterRotationState.EACStripDefaultVeterans = settings.EACStripDefaultVeterans;
            RosterRotationState.EACStripOtherUnearnedVeterans = settings.EACStripOtherUnearnedVeterans;
            RosterRotationState.EACAllowPilotVeterans = settings.EACAllowPilotVeterans;
            RosterRotationState.EACAllowEngineerVeterans = settings.EACAllowEngineerVeterans;
            RosterRotationState.EACAllowScientistVeterans = settings.EACAllowScientistVeterans;
            RosterRotationState.EACApplySuits = settings.EACApplySuits;
            RosterRotationState.EACDefaultSuit = settings.EACDefaultSuit;
            RosterRotationState.EACVeteranSuit = settings.EACVeteranSuit;
            RosterRotationState.EACBadassProgressionEnabled = settings.EACBadassProgressionEnabled;
            RosterRotationState.EACBadassRequireVeteran = settings.EACBadassRequireVeteran;
            RosterRotationState.EACBadassRequireMilestone = settings.EACBadassRequireMilestone;
            RosterRotationState.EACBadassChancePercent = settings.EACBadassChancePercent;
            RosterRotationState.EACNewGameCrewSetupEnabled = settings.EACNewGameCrewSetupEnabled;
            RosterRotationState.EACNewGameCrewSetupCompleted = settings.EACNewGameCrewSetupCompleted;
            RosterRotationState.EACReplaceStartingCrewDefault = settings.EACReplaceStartingCrewDefault;
            RosterRotationState.EACStartingCrewCount = settings.EACStartingCrewCount;
            RosterRotationState.EACStartingCrewAllowMales = settings.EACStartingCrewAllowMales;
            RosterRotationState.EACStartingCrewAllowFemales = settings.EACStartingCrewAllowFemales;
            RosterRotationState.EACStartingCrewAllowPilots = settings.EACStartingCrewAllowPilots;
            RosterRotationState.EACStartingCrewAllowEngineers = settings.EACStartingCrewAllowEngineers;
            RosterRotationState.EACStartingCrewAllowScientists = settings.EACStartingCrewAllowScientists;
            RosterRotationState.CrewRotationAdvisorEnabled = settings.CrewRotationAdvisorEnabled;
            RosterRotationState.DeepFreezeThawDeathBaseChance = settings.DeepFreezeThawDeathBaseChance;
            RosterRotationState.DeepFreezeThawDeathBonusPerYear = settings.DeepFreezeThawDeathBonusPerYear;
            RosterRotationState.DeepFreezeThawDeathMaxChance = settings.DeepFreezeThawDeathMaxChance;
            RosterRotationState.DebugForceMissionDeath = false;
        }

        public static void WriteSettingsNode(ConfigNode node, EacSettingsSnapshot settings, IFormatProvider formatProvider)
        {
            if (node == null || settings == null) return;
            var ci = formatProvider as CultureInfo ?? CultureInfo.InvariantCulture;

            node.AddValue("restDays", settings.RestDays.ToString(ci));
            node.AddValue("recoveryLeavePercent", settings.RecoveryLeavePercent.ToString(ci));
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
            node.AddValue("veteranNotificationsEnabled", settings.VeteranNotificationsEnabled.ToString(ci));
            node.AddValue("badassNotificationsEnabled", settings.BadassNotificationsEnabled.ToString(ci));
            node.AddValue("retirementAgeMin", settings.RetirementAgeMin.ToString(ci));
            node.AddValue("retirementAgeMax", settings.RetirementAgeMax.ToString(ci));
            node.AddValue("retiredDeathAgeMin", settings.RetiredDeathAgeMin.ToString(ci));
            node.AddValue("autoCleanupUnreferencedKerbals", settings.AutoCleanupUnreferencedKerbals.ToString(ci));
            node.AddValue("verboseLogging", settings.VerboseLogging.ToString(ci));
            node.AddValue("verboseAgeLogging", settings.VerboseAgeLogging.ToString(ci));
            node.AddValue("syncFlightTrackerFromEacOnce", settings.SyncFlightTrackerFromEacOnce.ToString(ci));
            node.AddValue("traitGrowthEnabled", settings.TraitGrowthEnabled.ToString(ci));
            node.AddValue("finalExamContractsEnabled", settings.FinalExamContractsEnabled.ToString(ci));
            if (!string.IsNullOrEmpty(settings.GraduationExamHistory)) node.AddValue("graduationExamHistory", settings.GraduationExamHistory);
            node.AddValue("portraitCaptureEnabled", settings.PortraitCaptureEnabled.ToString(ci));
            node.AddValue("missionDeathEnabled", settings.MissionDeathEnabled.ToString(ci));
            node.AddValue("eacVeteranStatusEnabled", settings.EACVeteranStatusEnabled.ToString(ci));
            node.AddValue("eacVeteranFlightsRequired", settings.EACVeteranFlightsRequired.ToString(ci));
            node.AddValue("eacVeteranHoursRequired", settings.EACVeteranHoursRequired.ToString("R", ci));
            node.AddValue("eacVeteranRequireMilestone", settings.EACVeteranRequireMilestone.ToString(ci));
            node.AddValue("eacStripDefaultVeterans", settings.EACStripDefaultVeterans.ToString(ci));
            node.AddValue("eacStripOtherUnearnedVeterans", settings.EACStripOtherUnearnedVeterans.ToString(ci));
            node.AddValue("eacAllowPilotVeterans", settings.EACAllowPilotVeterans.ToString(ci));
            node.AddValue("eacAllowEngineerVeterans", settings.EACAllowEngineerVeterans.ToString(ci));
            node.AddValue("eacAllowScientistVeterans", settings.EACAllowScientistVeterans.ToString(ci));
            node.AddValue("eacApplySuits", settings.EACApplySuits.ToString(ci));
            node.AddValue("eacDefaultSuit", settings.EACDefaultSuit.ToString(ci));
            node.AddValue("eacVeteranSuit", settings.EACVeteranSuit.ToString(ci));
            node.AddValue("eacBadassProgressionEnabled", settings.EACBadassProgressionEnabled.ToString(ci));
            node.AddValue("eacBadassRequireVeteran", settings.EACBadassRequireVeteran.ToString(ci));
            node.AddValue("eacBadassRequireMilestone", settings.EACBadassRequireMilestone.ToString(ci));
            node.AddValue("eacBadassChancePercent", settings.EACBadassChancePercent.ToString(ci));
            node.AddValue("eacNewGameCrewSetupEnabled", settings.EACNewGameCrewSetupEnabled.ToString(ci));
            node.AddValue("eacNewGameCrewSetupCompleted", settings.EACNewGameCrewSetupCompleted.ToString(ci));
            node.AddValue("eacReplaceStartingCrewDefault", settings.EACReplaceStartingCrewDefault.ToString(ci));
            node.AddValue("eacStartingCrewCount", settings.EACStartingCrewCount.ToString(ci));
            node.AddValue("eacStartingCrewAllowMales", settings.EACStartingCrewAllowMales.ToString(ci));
            node.AddValue("eacStartingCrewAllowFemales", settings.EACStartingCrewAllowFemales.ToString(ci));
            node.AddValue("eacStartingCrewAllowPilots", settings.EACStartingCrewAllowPilots.ToString(ci));
            node.AddValue("eacStartingCrewAllowEngineers", settings.EACStartingCrewAllowEngineers.ToString(ci));
            node.AddValue("eacStartingCrewAllowScientists", settings.EACStartingCrewAllowScientists.ToString(ci));
            node.AddValue("crewRotationAdvisorEnabled", settings.CrewRotationAdvisorEnabled.ToString(ci));
            node.AddValue("deepFreezeThawDeathBaseChance", settings.DeepFreezeThawDeathBaseChance.ToString("R", ci));
            node.AddValue("deepFreezeThawDeathBonusPerYear", settings.DeepFreezeThawDeathBonusPerYear.ToString("R", ci));
            node.AddValue("deepFreezeThawDeathMaxChance", settings.DeepFreezeThawDeathMaxChance.ToString("R", ci));
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
                MissionAccumulatedUT = PD(recordNode.GetValue("missionAccumulatedUT"), 0),
                Training = (TrainingType)PI(recordNode.GetValue("trainingType"), 0),
                TrainingTargetLevel = PI(recordNode.GetValue("trainingTargetLevel"), 0),
                GrantedLevel = PI(recordNode.GetValue("grantedLevel"), -1),
                HighestLevelEverCertified = PI(recordNode.GetValue("highestLevelEverCertified"), PI(recordNode.GetValue("grantedLevel"), -1)),
                KerbalIdentityKey = recordNode.GetValue("kerbalIdentityKey") ?? recordNode.GetValue("eacKerbalId") ?? "",
                GraduationExamPending = PB(recordNode.GetValue("graduationExamPending"), false),
                GraduationExamActive = PB(recordNode.GetValue("graduationExamActive"), false),
                GraduationExamTargetLevel = PI(recordNode.GetValue("graduationExamTargetLevel"), 0),
                GraduationExamContractGuid = recordNode.GetValue("graduationExamContractGuid") ?? "",
                GraduationExamContractType = recordNode.GetValue("graduationExamContractType") ?? "",
                GraduationExamId = recordNode.GetValue("graduationExamId") ?? "",
                GraduationExamReadyUT = PD(recordNode.GetValue("graduationExamReadyUT"), 0),
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
                DeepFreezeActive = PB(recordNode.GetValue("deepFreezeActive"), false),
                DeepFreezeStartUT = PD(recordNode.GetValue("deepFreezeStartUT"), 0),
                DeepFreezeAccumulatedUT = PD(recordNode.GetValue("deepFreezeAccumulatedUT"), 0),
                DeepFreezeLastKnownVesselName = recordNode.GetValue("deepFreezeLastKnownVesselName") ?? "",
                EACBadassRollKey = recordNode.GetValue("eacBadassRollKey") ?? "",
                EACBadassAwarded = PB(recordNode.GetValue("eacBadassAwarded"), false),
            };

            RosterRotationState.EnsureKerbalIdentity(record);
            if (record.HighestLevelEverCertified < record.GrantedLevel)
                record.HighestLevelEverCertified = record.GrantedLevel;

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
            if (record.MissionAccumulatedUT > 0) node.AddValue("missionAccumulatedUT", record.MissionAccumulatedUT.ToString("R", ci));
            node.AddValue("trainingType", ((int)record.Training).ToString(ci));
            node.AddValue("trainingTargetLevel", record.TrainingTargetLevel.ToString(ci));
            if (record.GrantedLevel >= 0) node.AddValue("grantedLevel", record.GrantedLevel.ToString(ci));
            if (record.HighestLevelEverCertified >= 0) node.AddValue("highestLevelEverCertified", record.HighestLevelEverCertified.ToString(ci));
            if (!string.IsNullOrEmpty(record.KerbalIdentityKey)) node.AddValue("kerbalIdentityKey", record.KerbalIdentityKey);
            if (record.GraduationExamPending) node.AddValue("graduationExamPending", record.GraduationExamPending.ToString(ci));
            if (record.GraduationExamActive) node.AddValue("graduationExamActive", record.GraduationExamActive.ToString(ci));
            if (record.GraduationExamTargetLevel > 0) node.AddValue("graduationExamTargetLevel", record.GraduationExamTargetLevel.ToString(ci));
            if (!string.IsNullOrEmpty(record.GraduationExamContractGuid)) node.AddValue("graduationExamContractGuid", record.GraduationExamContractGuid);
            if (!string.IsNullOrEmpty(record.GraduationExamContractType)) node.AddValue("graduationExamContractType", record.GraduationExamContractType);
            if (!string.IsNullOrEmpty(record.GraduationExamId)) node.AddValue("graduationExamId", record.GraduationExamId);
            if (record.GraduationExamReadyUT > 0) node.AddValue("graduationExamReadyUT", record.GraduationExamReadyUT.ToString("R", ci));
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
                if (record.DeepFreezeActive) node.AddValue("deepFreezeActive", record.DeepFreezeActive.ToString(ci));
                if (record.DeepFreezeStartUT > 0) node.AddValue("deepFreezeStartUT", record.DeepFreezeStartUT.ToString("R", ci));
                if (record.DeepFreezeAccumulatedUT > 0) node.AddValue("deepFreezeAccumulatedUT", record.DeepFreezeAccumulatedUT.ToString("R", ci));
                if (!string.IsNullOrEmpty(record.DeepFreezeLastKnownVesselName)) node.AddValue("deepFreezeLastKnownVesselName", record.DeepFreezeLastKnownVesselName);
                node.AddValue("lastAgedYears", record.LastAgedYears.ToString(ci));
            }
            if (!string.IsNullOrEmpty(record.EACBadassRollKey)) node.AddValue("eacBadassRollKey", record.EACBadassRollKey);
            if (record.EACBadassAwarded) node.AddValue("eacBadassAwarded", record.EACBadassAwarded.ToString(ci));
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

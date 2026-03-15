using System;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    // EAC Difficulty Options layout:
    //   - One left-menu entry: "EAC"
    //   - Three panels across the top row: General, Training, Aging
    //   - Debug toggles remain in the General panel under a "Debug" heading
    // This avoids KSP splitting the section into "EAC (1)", "EAC (2)" pages when too many panels exist.

    internal static class EACStateBridge
    {
        private const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static bool GetBool(string field, bool fallback)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f == null || f.FieldType != typeof(bool)) return fallback;
            try { return (bool)f.GetValue(null); } catch { return fallback; }
        }

        public static int GetInt(string field, int fallback)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f == null || f.FieldType != typeof(int)) return fallback;
            try { return (int)f.GetValue(null); } catch { return fallback; }
        }

        public static double GetDouble(string field, double fallback)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f == null) return fallback;
            try
            {
                if (f.FieldType == typeof(double)) return (double)f.GetValue(null);
                if (f.FieldType == typeof(float)) return (float)f.GetValue(null);
                if (f.FieldType == typeof(int)) return (int)f.GetValue(null);
                return fallback;
            }
            catch { return fallback; }
        }

        public static void SetBool(string field, bool value)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f == null || f.FieldType != typeof(bool)) return;
            try { f.SetValue(null, value); } catch { /* ignore */ }
        }

        public static void SetInt(string field, int value)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f == null || f.FieldType != typeof(int)) return;
            try { f.SetValue(null, value); } catch { /* ignore */ }
        }

        public static void SetDouble(string field, double value)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f == null) return;
            try
            {
                if (f.FieldType == typeof(double)) f.SetValue(null, value);
                else if (f.FieldType == typeof(float)) f.SetValue(null, (float)value);
                else if (f.FieldType == typeof(int)) f.SetValue(null, (int)Math.Round(value));
            }
            catch { /* ignore */ }
        }
    }

    // Must be public because KSP instantiates CustomParameterNode types via reflection.
    public abstract class EACParamsBase : GameParameters.CustomParameterNode
    {
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;

        // Keep Section and DisplaySection identical across panels so we get ONE left-menu entry.
        public override string Section => "EAC";
        public override string DisplaySection => "EAC";
        public override int SectionOrder => 100;

        // KSP calls this on Accept.
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            PushToState();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            PullFromState();
        }

        protected abstract void PullFromState();
        protected abstract void PushToState();
    }

    // ---- Panel: General (includes Messages + Debug toggles) ----
    public class EACGameSettings : EACParamsBase
    {
        public override string Title => "General";

        // Persistence.cs calls these helpers; keep them on this type for compatibility.
        public static bool TryApplyToStateFromGameParams()
        {
            try
            {
                if (HighLogic.CurrentGame == null) return false;
                var gp = HighLogic.CurrentGame.Parameters;
                if (gp == null) return false;

                var gen = gp.CustomParams<EACGameSettings>();
                var trn = gp.CustomParams<EACGameSettings_Training>();
                var ag  = gp.CustomParams<EACGameSettings_Aging>();

                // General / timebase
                EACStateBridge.SetBool("UseKerbinDays", gen.UseKerbinTime);

                // Messages (channels)
                EACStateBridge.SetBool("NotifyHUD", gen.NotifyHUD);
                EACStateBridge.SetBool("NotifyMessageApp", gen.NotifyMessageApp);

                // Messages (categories)
                EACStateBridge.SetBool("BirthdayNotificationsEnabled", gen.NotifyBirthdays);
                EACStateBridge.SetBool("TrainingNotificationsEnabled", gen.NotifyTraining);
                EACStateBridge.SetBool("RetirementNotificationsEnabled", gen.NotifyRetirement);
                EACStateBridge.SetBool("DeathNotificationsEnabled", gen.NotifyDeaths);

                EACStateBridge.SetBool("PortraitCaptureEnabled", gen.PortraitCaptureEnabled);
                EACStateBridge.SetBool("CrashPenaltyEnabled", gen.CrashPenaltyEnabled);
                EACStateBridge.SetBool("MissionDeathEnabled", gen.MissionDeathEnabled);

                // Debug
                EACStateBridge.SetBool("VerboseLogging", gen.VerboseUILogs);
                EACStateBridge.SetBool("VerboseAgeLogging", gen.VerboseAgingLogs);

                // Training
                EACStateBridge.SetBool("TraitGrowthEnabled", trn.TraitGrowth);
                EACStateBridge.SetInt("TrainingInitialDays", trn.TrainingInitialDays);
                EACStateBridge.SetInt("TrainingStarDays", trn.TrainingStarDays);
                EACStateBridge.SetDouble("TrainingFundsMultiplier", trn.TrainingFundsMultiplier);
                EACStateBridge.SetDouble("TrainingRDPerStar", trn.TrainingRDPerStar);
                EACStateBridge.SetDouble("TrainingBaseFundsCost", trn.TrainingBaseFundsCost);
                EACStateBridge.SetDouble("RecallFundsCostMultiplier", trn.RecallFundsCostMultiplier);

                // Aging
                if (ag.RetirementAgeMax < ag.RetirementAgeMin) ag.RetirementAgeMax = ag.RetirementAgeMin;
                EACStateBridge.SetBool("AgingEnabled", ag.AgingEnabled);
                EACStateBridge.SetInt("RetirementAgeMin", ag.RetirementAgeMin);
                EACStateBridge.SetInt("RetirementAgeMax", ag.RetirementAgeMax);
                EACStateBridge.SetInt("RetiredDeathAgeMin", ag.RetiredDeathAgeMin);
                EACStateBridge.SetBool("AutoCleanupUnreferencedKerbals", ag.AutoCleanupUnreferencedKerbals);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySyncGameParamsFromState()
        {
            try
            {
                if (HighLogic.CurrentGame == null) return false;
                var gp = HighLogic.CurrentGame.Parameters;
                if (gp == null) return false;

                var gen = gp.CustomParams<EACGameSettings>();
                var trn = gp.CustomParams<EACGameSettings_Training>();
                var ag  = gp.CustomParams<EACGameSettings_Aging>();

                // General / timebase
                gen.UseKerbinTime = EACStateBridge.GetBool("UseKerbinDays", gen.UseKerbinTime);

                // Messages
                gen.NotifyHUD        = EACStateBridge.GetBool("NotifyHUD", gen.NotifyHUD);
                gen.NotifyMessageApp = EACStateBridge.GetBool("NotifyMessageApp", gen.NotifyMessageApp);

                gen.NotifyBirthdays  = EACStateBridge.GetBool("BirthdayNotificationsEnabled", gen.NotifyBirthdays);
                gen.NotifyTraining   = EACStateBridge.GetBool("TrainingNotificationsEnabled", gen.NotifyTraining);
                gen.NotifyRetirement = EACStateBridge.GetBool("RetirementNotificationsEnabled", gen.NotifyRetirement);
                gen.NotifyDeaths     = EACStateBridge.GetBool("DeathNotificationsEnabled", gen.NotifyDeaths);

                gen.PortraitCaptureEnabled = EACStateBridge.GetBool("PortraitCaptureEnabled", gen.PortraitCaptureEnabled);
                gen.CrashPenaltyEnabled = EACStateBridge.GetBool("CrashPenaltyEnabled", gen.CrashPenaltyEnabled);
                gen.MissionDeathEnabled = EACStateBridge.GetBool("MissionDeathEnabled", gen.MissionDeathEnabled);

                // Debug
                gen.VerboseUILogs    = EACStateBridge.GetBool("VerboseLogging", gen.VerboseUILogs);
                gen.VerboseAgingLogs = EACStateBridge.GetBool("VerboseAgeLogging", gen.VerboseAgingLogs);

                // Training
                trn.TraitGrowth            = EACStateBridge.GetBool("TraitGrowthEnabled", trn.TraitGrowth);
                trn.TrainingInitialDays     = EACStateBridge.GetInt("TrainingInitialDays", trn.TrainingInitialDays);
                trn.TrainingStarDays        = EACStateBridge.GetInt("TrainingStarDays", trn.TrainingStarDays);
                trn.TrainingFundsMultiplier = (float)EACStateBridge.GetDouble("TrainingFundsMultiplier", trn.TrainingFundsMultiplier);
                trn.TrainingRDPerStar       = (float)EACStateBridge.GetDouble("TrainingRDPerStar", trn.TrainingRDPerStar);
                trn.TrainingBaseFundsCost   = (float)EACStateBridge.GetDouble("TrainingBaseFundsCost", trn.TrainingBaseFundsCost);
                trn.RecallFundsCostMultiplier = (float)EACStateBridge.GetDouble("RecallFundsCostMultiplier", trn.RecallFundsCostMultiplier);

                // Aging
                ag.AgingEnabled       = EACStateBridge.GetBool("AgingEnabled", ag.AgingEnabled);
                ag.RetirementAgeMin   = EACStateBridge.GetInt("RetirementAgeMin", ag.RetirementAgeMin);
                ag.RetirementAgeMax   = EACStateBridge.GetInt("RetirementAgeMax", ag.RetirementAgeMax);
                ag.RetiredDeathAgeMin = EACStateBridge.GetInt("RetiredDeathAgeMin", ag.RetiredDeathAgeMin);
                ag.AutoCleanupUnreferencedKerbals = EACStateBridge.GetBool("AutoCleanupUnreferencedKerbals", ag.AutoCleanupUnreferencedKerbals);

                return true;
            }
            catch
            {
                return false;
            }
        }

        [GameParameters.CustomParameterUI(
            "Use Kerbin time",
            toolTip = "If enabled: 6-hour days, 426-day years. If disabled: 24-hour days, 365-day years.",
            autoPersistance = false)]
        public bool UseKerbinTime = true;

        // Messages: channels
        [GameParameters.CustomParameterUI(
            "HUD",
            toolTip = "Post notifications to the on-screen HUD ticker.",
            autoPersistance = false)]
        public bool NotifyHUD = true;

        [GameParameters.CustomParameterUI(
            "Message App",
            toolTip = "Post notifications to KSP's Message App (persistent messages).",
            autoPersistance = false)]
        public bool NotifyMessageApp = true;

        // Messages: categories
        [GameParameters.CustomParameterUI(
            "Birthdays",
            toolTip = "Enable birthday notifications.",
            autoPersistance = false)]
        public bool NotifyBirthdays = true;

        [GameParameters.CustomParameterUI(
            "Training",
            toolTip = "Enable training-related notifications.",
            autoPersistance = false)]
        public bool NotifyTraining = true;

        [GameParameters.CustomParameterUI(
            "Retirement",
            toolTip = "Enable retirement-related notifications.",
            autoPersistance = false)]
        public bool NotifyRetirement = true;

        [GameParameters.CustomParameterUI(
            "Deaths",
            toolTip = "Enable death notifications.",
            autoPersistance = false)]
        public bool NotifyDeaths = true;

        [GameParameters.CustomParameterUI(
            "Portrait capture",
            toolTip = "Capture visible in-flight kerbal portraits for Hall of History. Disable to stop the flight-scene portrait capture watcher from doing capture work.",
            autoPersistance = false)]
        public bool PortraitCaptureEnabled = true;


        [GameParameters.CustomParameterUI(
            "Crash penalty",
            toolTip = "Enable crash injury and medical-retirement penalties on vessel recovery.",
            autoPersistance = false)]
        public bool CrashPenaltyEnabled = true;

        [GameParameters.CustomParameterUI(
            "Mission old-age deaths",
            toolTip = "Allow assigned Kerbals past retirement age to die on mission from age and mission stress.",
            autoPersistance = false)]
        public bool MissionDeathEnabled = false;

        [GameParameters.CustomStringParameterUI(
            "",
            title = "",
            lines = 1,
            autoPersistance = false)]
        public string DebugSpacer = "";

        // Debug
        [GameParameters.CustomStringParameterUI(
            "",
            title = "Debug",
            lines = 1,
            autoPersistance = false)]
        public string DebugHeading = "";

        [GameParameters.CustomParameterUI(
            "Verbose UI logs",
            toolTip = "Enable extra UI logs (Retired tab, list rebuilds, etc).",
            autoPersistance = false)]
        public bool VerboseUILogs = false;

        [GameParameters.CustomParameterUI(
            "Verbose aging logs",
            toolTip = "Enable extra aging-related logs (birthdays/retirement checks).",
            autoPersistance = false)]
        public bool VerboseAgingLogs = false;

        protected override void PullFromState()
        {
            UseKerbinTime      = EACStateBridge.GetBool("UseKerbinDays", true);

            NotifyHUD          = EACStateBridge.GetBool("NotifyHUD", true);
            NotifyMessageApp   = EACStateBridge.GetBool("NotifyMessageApp", true);

            NotifyBirthdays    = EACStateBridge.GetBool("BirthdayNotificationsEnabled", true);
            NotifyTraining     = EACStateBridge.GetBool("TrainingNotificationsEnabled", true);
            NotifyRetirement   = EACStateBridge.GetBool("RetirementNotificationsEnabled", true);
            NotifyDeaths       = EACStateBridge.GetBool("DeathNotificationsEnabled", true);
            PortraitCaptureEnabled = EACStateBridge.GetBool("PortraitCaptureEnabled", true);
            CrashPenaltyEnabled = EACStateBridge.GetBool("CrashPenaltyEnabled", true);
            MissionDeathEnabled = EACStateBridge.GetBool("MissionDeathEnabled", false);

            VerboseUILogs      = EACStateBridge.GetBool("VerboseLogging", false);
            VerboseAgingLogs   = EACStateBridge.GetBool("VerboseAgeLogging", false);
        }

        protected override void PushToState()
        {
            bool oldVerbose = EACStateBridge.GetBool("VerboseLogging", false);
            bool oldAgeVerbose = EACStateBridge.GetBool("VerboseAgeLogging", false);

            EACStateBridge.SetBool("UseKerbinDays", UseKerbinTime);

            EACStateBridge.SetBool("NotifyHUD", NotifyHUD);
            EACStateBridge.SetBool("NotifyMessageApp", NotifyMessageApp);

            EACStateBridge.SetBool("BirthdayNotificationsEnabled", NotifyBirthdays);
            EACStateBridge.SetBool("TrainingNotificationsEnabled", NotifyTraining);
            EACStateBridge.SetBool("RetirementNotificationsEnabled", NotifyRetirement);
            EACStateBridge.SetBool("DeathNotificationsEnabled", NotifyDeaths);
            EACStateBridge.SetBool("PortraitCaptureEnabled", PortraitCaptureEnabled);
            EACStateBridge.SetBool("CrashPenaltyEnabled", CrashPenaltyEnabled);
            EACStateBridge.SetBool("MissionDeathEnabled", MissionDeathEnabled);

            EACStateBridge.SetBool("VerboseLogging", VerboseUILogs);
            EACStateBridge.SetBool("VerboseAgeLogging", VerboseAgingLogs);

            if (oldVerbose != VerboseUILogs || oldAgeVerbose != VerboseAgingLogs)
                RosterRotationState.VerboseSettingsDirty = true;
        }
    }

    // ---- Panel: Training ----
    public class EACGameSettings_Training : EACParamsBase
    {
        public override string Title => "Training";

        [GameParameters.CustomParameterUI(
            "Trait Growth",
            toolTip = "Allow level-training completions and veteran recoveries to slightly increase Courage and reduce Stupidity.",
            autoPersistance = false)]
        public bool TraitGrowth = false;

        [GameParameters.CustomIntParameterUI(
            "Init days",
            toolTip = "Initial onboarding training duration (days).",
            minValue = 0, maxValue = 365, stepSize = 1,
            autoPersistance = false)]
        public int TrainingInitialDays = 30;

        [GameParameters.CustomIntParameterUI(
            "Days / star",
            toolTip = "Training duration (days) per experience star gained.",
            minValue = 0, maxValue = 365, stepSize = 1,
            autoPersistance = false)]
        public int TrainingStarDays = 30;

        [GameParameters.CustomFloatParameterUI(
            "Funds mult.",
            toolTip = "Multiplier applied to computed training costs.",
            minValue = 0f, maxValue = 10f, stepCount = 101,
            autoPersistance = false)]
        public float TrainingFundsMultiplier = 1.0f;

        [GameParameters.CustomFloatParameterUI(
            "R&D / star",
            toolTip = "R&D points per star (if applicable in your career setup).",
            minValue = 0f, maxValue = 200f, stepCount = 201,
            autoPersistance = false)]
        public float TrainingRDPerStar = 10.0f;

        [GameParameters.CustomFloatParameterUI(
            "Base funds",
            toolTip = "Base training cost in Funds before modifiers.",
            minValue = 0f, maxValue = 500000f, stepCount = 201,
            autoPersistance = false)]
        public float TrainingBaseFundsCost = 62000f;

        [GameParameters.CustomFloatParameterUI(
            "Recall cost mult.",
            toolTip = "Multiplier applied to hire cost for recalling retired kerbals. 0 = free recall. No R&D cost.",
            minValue = 0f, maxValue = 5f, stepCount = 51,
            autoPersistance = false)]
        public float RecallFundsCostMultiplier = 1.0f;

        protected override void PullFromState()
        {
            TraitGrowth             = EACStateBridge.GetBool("TraitGrowthEnabled", false);
            TrainingInitialDays     = EACStateBridge.GetInt("TrainingInitialDays", 30);
            TrainingStarDays        = EACStateBridge.GetInt("TrainingStarDays", 30);
            TrainingFundsMultiplier = (float)EACStateBridge.GetDouble("TrainingFundsMultiplier", 1.0);
            TrainingRDPerStar       = (float)EACStateBridge.GetDouble("TrainingRDPerStar", 10.0);
            TrainingBaseFundsCost   = (float)EACStateBridge.GetDouble("TrainingBaseFundsCost", 62000);
            RecallFundsCostMultiplier = (float)EACStateBridge.GetDouble("RecallFundsCostMultiplier", 1.0);
        }

        protected override void PushToState()
        {
            EACStateBridge.SetBool("TraitGrowthEnabled", TraitGrowth);
            EACStateBridge.SetInt("TrainingInitialDays", TrainingInitialDays);
            EACStateBridge.SetInt("TrainingStarDays", TrainingStarDays);
            EACStateBridge.SetDouble("TrainingFundsMultiplier", TrainingFundsMultiplier);
            EACStateBridge.SetDouble("TrainingRDPerStar", TrainingRDPerStar);
            EACStateBridge.SetDouble("TrainingBaseFundsCost", TrainingBaseFundsCost);
            EACStateBridge.SetDouble("RecallFundsCostMultiplier", RecallFundsCostMultiplier);
        }
    }

    // ---- Panel: Aging ----
    public class EACGameSettings_Aging : EACParamsBase
    {
        public override string Title => "Aging";

        [GameParameters.CustomParameterUI(
            "Enable aging",
            toolTip = "If enabled, kerbals age, retire, and may die after retirement.",
            autoPersistance = false)]
        public bool AgingEnabled = true;

        [GameParameters.CustomIntParameterUI(
            "Retire min",
            toolTip = "Minimum retirement age (years).",
            minValue = 18, maxValue = 120, stepSize = 1,
            autoPersistance = false)]
        public int RetirementAgeMin = 48;

        [GameParameters.CustomIntParameterUI(
            "Retire max",
            toolTip = "Maximum retirement age (years). A random retirement age is chosen in [min,max].",
            minValue = 18, maxValue = 120, stepSize = 1,
            autoPersistance = false)]
        public int RetirementAgeMax = 55;

        [GameParameters.CustomIntParameterUI(
            "Retired death min",
            toolTip = "Minimum age at which retired kerbals may die (years).",
            minValue = 18, maxValue = 200, stepSize = 1,
            autoPersistance = false)]
        public int RetiredDeathAgeMin = 55;

        [GameParameters.CustomParameterUI(
            "Auto-clean unreferenced retired/dead kerbals",
            toolTip = "CAUTION! Backup your persistent file! If enabled retired/dead Kerbals not referenced will be removed.",
            autoPersistance = false)]
        public bool AutoCleanupUnreferencedKerbals = false;

        protected override void PullFromState()
        {
            AgingEnabled       = EACStateBridge.GetBool("AgingEnabled", true);
            RetirementAgeMin   = EACStateBridge.GetInt("RetirementAgeMin", 48);
            RetirementAgeMax   = EACStateBridge.GetInt("RetirementAgeMax", 55);
            RetiredDeathAgeMin = EACStateBridge.GetInt("RetiredDeathAgeMin", 55);
            AutoCleanupUnreferencedKerbals = EACStateBridge.GetBool("AutoCleanupUnreferencedKerbals", false);
        }

        protected override void PushToState()
        {
            if (RetirementAgeMax < RetirementAgeMin) RetirementAgeMax = RetirementAgeMin;

            EACStateBridge.SetBool("AgingEnabled", AgingEnabled);
            EACStateBridge.SetInt("RetirementAgeMin", RetirementAgeMin);
            EACStateBridge.SetInt("RetirementAgeMax", RetirementAgeMax);
            EACStateBridge.SetInt("RetiredDeathAgeMin", RetiredDeathAgeMin);
            EACStateBridge.SetBool("AutoCleanupUnreferencedKerbals", AutoCleanupUnreferencedKerbals);
        }
    }
}

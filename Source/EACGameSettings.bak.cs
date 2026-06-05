using System;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    // EAC Difficulty Options layout:
    //   - One left-menu entry: "EAC"
    //   - Three compact panels: General, Training, Aging
    //   - Detailed / low-frequency options live in the EAC toolbar Advanced Settings window.

    internal static class EACStateBridge
    {
        private const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static bool GetBool(string field, bool fallback)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f != null && f.FieldType == typeof(bool))
            {
                try { return (bool)f.GetValue(null); } catch { return fallback; }
            }
            var p = typeof(RosterRotationState).GetProperty(field, BF);
            if (p != null && p.PropertyType == typeof(bool) && p.CanRead)
            {
                try { return (bool)p.GetValue(null, null); } catch { return fallback; }
            }
            return fallback;
        }

        public static int GetInt(string field, int fallback)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f != null && f.FieldType == typeof(int))
            {
                try { return (int)f.GetValue(null); } catch { return fallback; }
            }
            var p = typeof(RosterRotationState).GetProperty(field, BF);
            if (p != null && p.PropertyType == typeof(int) && p.CanRead)
            {
                try { return (int)p.GetValue(null, null); } catch { return fallback; }
            }
            return fallback;
        }

        public static double GetDouble(string field, double fallback)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f != null)
            {
                try
                {
                    if (f.FieldType == typeof(double)) return (double)f.GetValue(null);
                    if (f.FieldType == typeof(float)) return (float)f.GetValue(null);
                    if (f.FieldType == typeof(int)) return (int)f.GetValue(null);
                }
                catch { return fallback; }
            }
            var p = typeof(RosterRotationState).GetProperty(field, BF);
            if (p != null && p.CanRead)
            {
                try
                {
                    if (p.PropertyType == typeof(double)) return (double)p.GetValue(null, null);
                    if (p.PropertyType == typeof(float)) return (float)p.GetValue(null, null);
                    if (p.PropertyType == typeof(int)) return (int)p.GetValue(null, null);
                }
                catch { return fallback; }
            }
            return fallback;
        }

        public static void SetBool(string field, bool value)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f != null && f.FieldType == typeof(bool))
            {
                try { f.SetValue(null, value); } catch { /* ignore */ }
                return;
            }
            var p = typeof(RosterRotationState).GetProperty(field, BF);
            if (p != null && p.PropertyType == typeof(bool) && p.CanWrite)
            {
                try { p.SetValue(null, value, null); } catch { /* ignore */ }
            }
        }

        public static void SetInt(string field, int value)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f != null && f.FieldType == typeof(int))
            {
                try { f.SetValue(null, value); } catch { /* ignore */ }
                return;
            }
            var p = typeof(RosterRotationState).GetProperty(field, BF);
            if (p != null && p.PropertyType == typeof(int) && p.CanWrite)
            {
                try { p.SetValue(null, value, null); } catch { /* ignore */ }
            }
        }

        public static void SetDouble(string field, double value)
        {
            var f = typeof(RosterRotationState).GetField(field, BF);
            if (f != null)
            {
                try
                {
                    if (f.FieldType == typeof(double)) f.SetValue(null, value);
                    else if (f.FieldType == typeof(float)) f.SetValue(null, (float)value);
                    else if (f.FieldType == typeof(int)) f.SetValue(null, (int)Math.Round(value));
                    return;
                }
                catch { /* ignore */ }
            }
            var p = typeof(RosterRotationState).GetProperty(field, BF);
            if (p != null && p.CanWrite)
            {
                try
                {
                    if (p.PropertyType == typeof(double)) p.SetValue(null, value, null);
                    else if (p.PropertyType == typeof(float)) p.SetValue(null, (float)value, null);
                    else if (p.PropertyType == typeof(int)) p.SetValue(null, (int)Math.Round(value), null);
                }
                catch { /* ignore */ }
            }
        }
    }

    // Must be public because KSP instantiates CustomParameterNode types via reflection.
    public abstract class EACParamsBase : GameParameters.CustomParameterNode
    {
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;

        public override string Section => "EAC";
        public override string DisplaySection => "EAC";
        public override int SectionOrder => 100;

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

    // ---- Panel: General ----
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

                EACStateBridge.SetBool("UseKerbinDays", gen.UseKerbinTime);

                EACStateBridge.SetBool("NotifyHUD", gen.NotifyHUD);
                bool wasMessageAppEnabled = EACStateBridge.GetBool("NotifyMessageApp", true);
                EACStateBridge.SetBool("NotifyMessageApp", gen.NotifyMessageApp);
                if (gen.NotifyMessageApp && !wasMessageAppEnabled)
                {
                    EACStateBridge.SetBool("BirthdayNotificationsEnabled", true);
                    EACStateBridge.SetBool("TrainingNotificationsEnabled", true);
                    EACStateBridge.SetBool("RetirementNotificationsEnabled", true);
                    EACStateBridge.SetBool("DeathNotificationsEnabled", true);
                }

                EACStateBridge.SetBool("PortraitCaptureEnabled", gen.PortraitCaptureEnabled);
                EACStateBridge.SetBool("CrashPenaltyEnabled", gen.CrashPenaltyEnabled);
                EACStateBridge.SetBool("MissionDeathEnabled", gen.MissionDeathEnabled);

                EACStateBridge.SetBool("VerboseLogging", gen.VerboseUILogs);
                EACStateBridge.SetBool("VerboseAgeLogging", gen.VerboseAgingLogs);

                EACStateBridge.SetBool("TraitGrowthEnabled", trn.TraitGrowth);
                bool finalExamContractsAvailable = EACContractConfiguratorBridge.IsAvailable;
                if (trn.FinalExamContracts && !finalExamContractsAvailable)
                {
                    RRLog.VerboseWarn("[EAC] Final exam contracts requested in settings but unavailable: " + EACContractConfiguratorBridge.AvailabilitySummary);
                    trn.FinalExamContracts = false;
                }
                EACStateBridge.SetBool("FinalExamContractsEnabled", trn.FinalExamContracts && finalExamContractsAvailable);
                EACStateBridge.SetInt("TrainingInitialDays", trn.TrainingInitialDays);
                EACStateBridge.SetInt("TrainingStarDays", trn.TrainingStarDays);
                EACStateBridge.SetDouble("TrainingFundsMultiplier", trn.TrainingFundsMultiplier);
                EACStateBridge.SetDouble("TrainingRDPerStar", trn.TrainingRDPerStar);
                EACStateBridge.SetDouble("TrainingBaseFundsCost", trn.TrainingBaseFundsCost);
                EACStateBridge.SetDouble("RecallFundsCostMultiplier", trn.RecallFundsCostMultiplier);

                if (ag.RetirementAgeMax < ag.RetirementAgeMin) ag.RetirementAgeMax = ag.RetirementAgeMin;
                EACStateBridge.SetBool("AgingEnabled", ag.AgingEnabled);
                EACStateBridge.SetInt("RetirementAgeMin", ag.RetirementAgeMin);
                EACStateBridge.SetInt("RetirementAgeMax", ag.RetirementAgeMax);
                EACStateBridge.SetInt("RetiredDeathAgeMin", ag.RetiredDeathAgeMin);
                if (!EACExternalModDetector.IsCrewRandRInstalled())
                {
                    EACStateBridge.SetDouble("RecoveryLeavePercent", ag.RecoveryLeavePercent);
                    EACStateBridge.SetDouble("RestDays", ag.RecoveryLeaveMaxDays);
                }

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

                gen.UseKerbinTime = EACStateBridge.GetBool("UseKerbinDays", gen.UseKerbinTime);
                gen.NotifyHUD        = EACStateBridge.GetBool("NotifyHUD", gen.NotifyHUD);
                gen.NotifyMessageApp = EACStateBridge.GetBool("NotifyMessageApp", gen.NotifyMessageApp);
                gen.PortraitCaptureEnabled = EACStateBridge.GetBool("PortraitCaptureEnabled", gen.PortraitCaptureEnabled);
                gen.CrashPenaltyEnabled = EACStateBridge.GetBool("CrashPenaltyEnabled", gen.CrashPenaltyEnabled);
                gen.MissionDeathEnabled = EACStateBridge.GetBool("MissionDeathEnabled", gen.MissionDeathEnabled);
                gen.VerboseUILogs    = EACStateBridge.GetBool("VerboseLogging", gen.VerboseUILogs);
                gen.VerboseAgingLogs = EACStateBridge.GetBool("VerboseAgeLogging", gen.VerboseAgingLogs);

                trn.TraitGrowth            = EACStateBridge.GetBool("TraitGrowthEnabled", trn.TraitGrowth);
                trn.FinalExamContractsStatus = EACContractConfiguratorBridge.AvailabilitySummary;
                trn.FinalExamContracts     = EACContractConfiguratorBridge.IsAvailable && EACStateBridge.GetBool("FinalExamContractsEnabled", trn.FinalExamContracts);
                trn.TrainingInitialDays     = EACStateBridge.GetInt("TrainingInitialDays", trn.TrainingInitialDays);
                trn.TrainingStarDays        = EACStateBridge.GetInt("TrainingStarDays", trn.TrainingStarDays);
                trn.TrainingFundsMultiplier = (float)EACStateBridge.GetDouble("TrainingFundsMultiplier", trn.TrainingFundsMultiplier);
                trn.TrainingRDPerStar       = (float)EACStateBridge.GetDouble("TrainingRDPerStar", trn.TrainingRDPerStar);
                trn.TrainingBaseFundsCost   = (float)EACStateBridge.GetDouble("TrainingBaseFundsCost", trn.TrainingBaseFundsCost);
                trn.RecallFundsCostMultiplier = (float)EACStateBridge.GetDouble("RecallFundsCostMultiplier", trn.RecallFundsCostMultiplier);

                ag.AgingEnabled       = EACStateBridge.GetBool("AgingEnabled", ag.AgingEnabled);
                ag.RetirementAgeMin   = EACStateBridge.GetInt("RetirementAgeMin", ag.RetirementAgeMin);
                ag.RetirementAgeMax   = EACStateBridge.GetInt("RetirementAgeMax", ag.RetirementAgeMax);
                ag.RetiredDeathAgeMin = EACStateBridge.GetInt("RetiredDeathAgeMin", ag.RetiredDeathAgeMin);
                ag.RecoveryIntegrationStatus = EACExternalModDetector.IsCrewRandRInstalled()
                    ? "Crew R&R detected: Crew R&R handles recovery leave; EAC recovery settings are hidden."
                    : "Crew R&R not detected: EAC handles recovery leave.";
                ag.RecoveryLeavePercent = (float)EACStateBridge.GetDouble("RecoveryLeavePercent", ag.RecoveryLeavePercent);
                ag.RecoveryLeaveMaxDays = (float)EACStateBridge.GetDouble("RestDays", ag.RecoveryLeaveMaxDays);

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

        [GameParameters.CustomParameterUI(
            "HUD",
            toolTip = "Post notifications to the on-screen HUD ticker.",
            autoPersistance = false)]
        public bool NotifyHUD = true;

        [GameParameters.CustomParameterUI(
            "Message App",
            toolTip = "Post notifications to KSP's Message App. When enabled from off, all message categories default on; individual categories are in EAC Advanced Settings.",
            autoPersistance = false)]
        public bool NotifyMessageApp = true;

        [GameParameters.CustomParameterUI(
            "Portrait capture",
            toolTip = "Capture visible in-flight Kerbal portraits for Hall of History.",
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

        [GameParameters.CustomStringParameterUI(
            "",
            title = "",
            lines = 2,
            autoPersistance = false)]
        public string AdvancedSettingsSpacer = "\n";

        [GameParameters.CustomStringParameterUI(
            "",
            title = "ADVANCED SETTINGS",
            lines = 5,
            autoPersistance = false)]
        public string AdvancedSettingsHint =
            "\n Open the EAC toolbar window and click Advanced Settings.\n" +
            "Includes message categories, cleanup, veterans, suits,\n" +
            "Badass progression, and starting crew setup.";

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            if (member != null && 
                (member.Name == nameof(AdvancedSettingsSpacer) ||
                member.Name == nameof(AdvancedSettingsHint) || 
                member.Name == nameof(DebugHeading)))
                return false;
            return true;
        }

        protected override void PullFromState()
        {
            UseKerbinTime      = EACStateBridge.GetBool("UseKerbinDays", true);
            NotifyHUD          = EACStateBridge.GetBool("NotifyHUD", true);
            NotifyMessageApp   = EACStateBridge.GetBool("NotifyMessageApp", true);
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
            bool oldMessageApp = EACStateBridge.GetBool("NotifyMessageApp", true);

            EACStateBridge.SetBool("UseKerbinDays", UseKerbinTime);
            EACStateBridge.SetBool("NotifyHUD", NotifyHUD);
            EACStateBridge.SetBool("NotifyMessageApp", NotifyMessageApp);
            if (NotifyMessageApp && !oldMessageApp)
            {
                EACStateBridge.SetBool("BirthdayNotificationsEnabled", true);
                EACStateBridge.SetBool("TrainingNotificationsEnabled", true);
                EACStateBridge.SetBool("RetirementNotificationsEnabled", true);
                EACStateBridge.SetBool("DeathNotificationsEnabled", true);
            }

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

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            if (member != null && member.Name == "FinalExamContracts")
                return EACContractConfiguratorBridge.IsAvailable;
            return true;
        }

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            if (member != null && member.Name == "FinalExamContracts")
                return EACContractConfiguratorBridge.IsAvailable;
            if (member != null && member.Name == nameof(FinalExamContractsStatus))
                return false;
            return true;
        }

        [GameParameters.CustomParameterUI(
            "Trait Growth",
            toolTip = "Allow level-training completions and veteran recoveries to slightly increase Courage and reduce Stupidity.",
            autoPersistance = false)]
        public bool TraitGrowth = false;

        [GameParameters.CustomStringParameterUI(
            "",
            title = "Final exam availability",
            lines = 2,
            autoPersistance = false)]
        public string FinalExamContractsStatus = "";

        [GameParameters.CustomParameterUI(
            "Final exam contracts",
            toolTip = "Require a Contract Configurator graduation exam before awarding trained experience levels.",
            autoPersistance = false)]
        public bool FinalExamContracts = false;

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
            FinalExamContractsStatus = EACContractConfiguratorBridge.AvailabilitySummary;
            FinalExamContracts      = EACContractConfiguratorBridge.IsAvailable && EACStateBridge.GetBool("FinalExamContractsEnabled", false);
            if (!EACContractConfiguratorBridge.IsAvailable)
                EACStateBridge.SetBool("FinalExamContractsEnabled", false);
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
            bool finalExamContractsAvailable = EACContractConfiguratorBridge.IsAvailable;
            FinalExamContractsStatus = EACContractConfiguratorBridge.AvailabilitySummary;
            if (FinalExamContracts && !finalExamContractsAvailable)
            {
                RRLog.VerboseWarn("[EAC] Final exam contracts cannot be enabled: " + EACContractConfiguratorBridge.AvailabilitySummary);
                FinalExamContracts = false;
            }
            EACStateBridge.SetBool("FinalExamContractsEnabled", FinalExamContracts && finalExamContractsAvailable);
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
            toolTip = "If enabled, Kerbals age, retire, and may die after retirement.",
            autoPersistance = false)]
        public bool AgingEnabled = true;

        [GameParameters.CustomIntParameterUI(
            "Retire min",
            toolTip = "Minimum retirement age (years).",
            minValue = 18, maxValue = 120, stepSize = 1,
            autoPersistance = false)]
        public int RetirementAgeMin = 37;

        [GameParameters.CustomIntParameterUI(
            "Retire max",
            toolTip = "Maximum retirement age (years). A random retirement age is chosen in [min,max].",
            minValue = 18, maxValue = 120, stepSize = 1,
            autoPersistance = false)]
        public int RetirementAgeMax = 47;

        [GameParameters.CustomIntParameterUI(
            "Retired death min",
            toolTip = "Minimum age at which retired Kerbals may die (years).",
            minValue = 18, maxValue = 200, stepSize = 1,
            autoPersistance = false)]
        public int RetiredDeathAgeMin = 50;

        [GameParameters.CustomStringParameterUI(
            "",
            title = "Recovery time",
            lines = 2,
            autoPersistance = false)]
        public string RecoveryIntegrationStatus = "";

        [GameParameters.CustomFloatParameterUI(
            "Recovery leave (%)",
            toolTip = "Base EAC recovery leave as a percent of the recovered mission's flight time. 0 disables EAC recovery leave. Ignored when CrewRandR is installed.",
            displayFormat = "N0",
            minValue = 0f, maxValue = 100f, stepCount = 101, asPercentage = false,
            autoPersistance = false)]
        public float RecoveryLeavePercent = 10f;

        [GameParameters.CustomFloatParameterUI(
            "RestDay Max",
            toolTip = "Maximum EAC recovery leave, in days. This cap is only used when Recovery leave (%) is above 0, and is ignored when CrewRandR is installed.",
            displayFormat = "N0",
            minValue = 0f, maxValue = 365f, stepCount = 366, asPercentage = false,
            autoPersistance = false)]
        public float RecoveryLeaveMaxDays = 14f;

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            if (member != null && EACExternalModDetector.IsCrewRandRInstalled())
            {
                if (member.Name == nameof(RecoveryLeavePercent) || member.Name == nameof(RecoveryLeaveMaxDays))
                    return false;
            }
            return true;
        }

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            if (member != null && member.Name == nameof(RecoveryIntegrationStatus))
                return false;
            if (member != null && member.Name == nameof(RecoveryLeaveMaxDays))
                return RecoveryLeavePercent > 0f && !EACExternalModDetector.IsCrewRandRInstalled();
            if (member != null && member.Name == nameof(RecoveryLeavePercent))
                return !EACExternalModDetector.IsCrewRandRInstalled();
            return true;
        }

        protected override void PullFromState()
        {
            AgingEnabled       = EACStateBridge.GetBool("AgingEnabled", true);
            RetirementAgeMin   = EACStateBridge.GetInt("RetirementAgeMin", 37);
            RetirementAgeMax   = EACStateBridge.GetInt("RetirementAgeMax", 47);
            RetiredDeathAgeMin = EACStateBridge.GetInt("RetiredDeathAgeMin", 50);
            RecoveryIntegrationStatus = EACExternalModDetector.IsCrewRandRInstalled()
                ? "Crew R&R detected: Crew R&R handles recovery leave; EAC recovery settings are hidden."
                : "Crew R&R not detected: EAC handles recovery leave.";
            RecoveryLeavePercent = (float)EACStateBridge.GetDouble("RecoveryLeavePercent", 10.0);
            RecoveryLeaveMaxDays = (float)EACStateBridge.GetDouble("RestDays", 14.0);
        }

        protected override void PushToState()
        {
            if (RetirementAgeMax < RetirementAgeMin) RetirementAgeMax = RetirementAgeMin;
            if (RecoveryLeavePercent < 0f) RecoveryLeavePercent = 0f;
            if (RecoveryLeavePercent > 100f) RecoveryLeavePercent = 100f;
            if (RecoveryLeaveMaxDays < 0f) RecoveryLeaveMaxDays = 0f;

            EACStateBridge.SetBool("AgingEnabled", AgingEnabled);
            EACStateBridge.SetInt("RetirementAgeMin", RetirementAgeMin);
            EACStateBridge.SetInt("RetirementAgeMax", RetirementAgeMax);
            EACStateBridge.SetInt("RetiredDeathAgeMin", RetiredDeathAgeMin);
            if (!EACExternalModDetector.IsCrewRandRInstalled())
            {
                EACStateBridge.SetDouble("RecoveryLeavePercent", RecoveryLeavePercent);
                EACStateBridge.SetDouble("RestDays", RecoveryLeaveMaxDays);
            }
        }
    }
}

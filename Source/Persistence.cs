using System;
using System.Globalization;
using UnityEngine;

namespace RosterRotation
{
    [KSPScenario(
        ScenarioCreationOptions.AddToAllGames,
        GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.EDITOR
    )]
    public class EACScenario : ScenarioModule
    {
        private const string NodeNameNew = "EAC";
        private const string NodeNameOld = "RosterRotation";

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                RosterRotationState.Records.Clear();
                if (node == null) return;

                ConfigNode root = null;
                if (node.HasNode(NodeNameNew)) root = node.GetNode(NodeNameNew);
                else if (node.HasNode(NodeNameOld)) root = node.GetNode(NodeNameOld);
                else root = node;

                // --- Settings ---
                // ALWAYS load from our Settings node first — it's the reliable source of truth.
                // GameParameters.CustomParams may not have loaded the saved values yet when
                // OnLoad fires (KSP load order: ScenarioModule.OnLoad runs before GameParameters
                // fully deserializes custom param nodes). Only use GameParams as primary source
                // if our Settings node doesn't exist (brand new game with no save data).
                if (root.HasNode("Settings"))
                {
                    var s = root.GetNode("Settings");
                    RosterRotationState.RestDays = PD(s.GetValue("restDays"), 14);
                    RosterRotationState.UseKerbinDays = PB(s.GetValue("useKerbinDays"), true);
                    RosterRotationState.TrainingInitialDays = PI(s.GetValue("trainingInitialDays"), 30);
                    RosterRotationState.TrainingStarDays = PI(s.GetValue("trainingStarDays"), 30);
                    RosterRotationState.TrainingFundsMultiplier = PD(s.GetValue("trainingFundsMultiplier"), 1.0);
                    RosterRotationState.TrainingRDPerStar = PD(s.GetValue("trainingRDPerStar"), 10.0);
                    RosterRotationState.TrainingBaseFundsCost = PD(s.GetValue("trainingBaseFundsCost"), 62000);
                    RosterRotationState.RecallFundsCostMultiplier = PD(s.GetValue("recallFundsCostMultiplier"), 1.0);
                    RosterRotationState.AgingEnabled = PB(s.GetValue("agingEnabled"), true);
                    RosterRotationState.DeathNotificationsEnabled = PB(s.GetValue("deathNotificationsEnabled"), true);
                    RosterRotationState.HudNotificationsEnabled = PB(s.GetValue("hudNotificationsEnabled"), true);
                    RosterRotationState.MessageAppNotificationsEnabled = PB(s.GetValue("messageAppNotificationsEnabled"), true);
                    RosterRotationState.BirthdayNotificationsEnabled = PB(s.GetValue("birthdayNotificationsEnabled"), true);
                    RosterRotationState.TrainingNotificationsEnabled = PB(s.GetValue("trainingNotificationsEnabled"), true);
                    RosterRotationState.RetirementNotificationsEnabled = PB(s.GetValue("retirementNotificationsEnabled"), true);
                    RosterRotationState.RetirementAgeMin = PI(s.GetValue("retirementAgeMin"), 48);
                    RosterRotationState.RetirementAgeMax = PI(s.GetValue("retirementAgeMax"), 55);
                    RosterRotationState.RetiredDeathAgeMin = PI(s.GetValue("retiredDeathAgeMin"), 55);
                    if (!RosterRotationState.VerboseSettingsDirty)
                    {
                        RosterRotationState.VerboseLogging = PB(s.GetValue("verboseLogging"), false);
                        RosterRotationState.VerboseAgeLogging = PB(s.GetValue("verboseAgeLogging"), false);
                    }

                    RRLog.Info($"[EAC] Settings loaded from save: VerboseLogging={RosterRotationState.VerboseLogging}");
                }

                // Push our loaded state into GameParameters so the Difficulty Options UI
                // shows the correct values when the player opens it.
                EACGameSettings.TrySyncGameParamsFromState();

                if (!root.HasNode("Record")) return;

                foreach (ConfigNode rNode in root.GetNodes("Record"))
                {
                    string name = rNode.GetValue("name");
                    if (string.IsNullOrEmpty(name)) continue;

                    var rec = new RosterRotationState.KerbalRecord
                    {
                        OriginalTrait = rNode.GetValue("originalTrait"),
                        OriginalType = ParseKerbalType(rNode.GetValue("originalType"), ProtoCrewMember.KerbalType.Crew),
                        Flights = PI(rNode.GetValue("flights"), 0),
                        LastFlightUT = PD(rNode.GetValue("lastFlightUT"), 0),
                        RestUntilUT = PD(rNode.GetValue("restUntilUT"), 0),
                        Retired = PB(rNode.GetValue("retired"), false),
                        RetiredUT = PD(rNode.GetValue("retiredUT"), 0),
                        ExperienceAtRetire = PI(rNode.GetValue("experienceAtRetire"), 0),
                        MissionStartUT = PD(rNode.GetValue("missionStartUT"), 0),
                        Training = (TrainingType)PI(rNode.GetValue("trainingType"), 0),
                        TrainingTargetLevel = PI(rNode.GetValue("trainingTargetLevel"), 0),
                        GrantedLevel = PI(rNode.GetValue("grantedLevel"), -1),
                        BirthUT = PD(rNode.GetValue("birthUT"), 0),
                        NaturalRetirementUT = PD(rNode.GetValue("naturalRetirementUT"), 0),
                        RetirementDelayYears = PI(rNode.GetValue("retirementDelayYears"), 0),
                        RetirementWarned = PB(rNode.GetValue("retirementWarned"), false),
                        RetirementScheduled = PB(rNode.GetValue("retirementScheduled"), false),
                        RetirementScheduledUT = PD(rNode.GetValue("retirementScheduledUT"), 0),
                        DeathUT = PD(rNode.GetValue("deathUT"), 0),
                        TrainingEndUT = PD(rNode.GetValue("trainingEndUT"), 0),
                        LastAgedYears = PI(rNode.GetValue("lastAgedYears"), -1),
                    };
                    RosterRotationState.Records[name] = rec;
                }
                RosterRotationState.InvalidateRetiredCache();
                RRLog.Info($"Loaded {RosterRotationState.Records.Count} kerbal records.");
            }
            catch (Exception ex) { RRLog.Error($"OnLoad failed: {ex}"); }
        }

        public override void OnSave(ConfigNode node)
        {
            try
            {
                if (node == null) return;
                ConfigNode root = node.HasNode(NodeNameNew) ? node.GetNode(NodeNameNew) : node.AddNode(NodeNameNew);
                root.RemoveNodes("Record");

                if (root.HasNode("Settings")) root.RemoveNode("Settings");
                var s = root.AddNode("Settings");
                var ci = CultureInfo.InvariantCulture;

                s.AddValue("restDays", RosterRotationState.RestDays.ToString(ci));
                s.AddValue("useKerbinDays", RosterRotationState.UseKerbinDays.ToString(ci));
                s.AddValue("trainingInitialDays", RosterRotationState.TrainingInitialDays.ToString(ci));
                s.AddValue("trainingStarDays", RosterRotationState.TrainingStarDays.ToString(ci));
                s.AddValue("trainingFundsMultiplier", RosterRotationState.TrainingFundsMultiplier.ToString(ci));
                s.AddValue("trainingRDPerStar", RosterRotationState.TrainingRDPerStar.ToString(ci));
                s.AddValue("trainingBaseFundsCost", RosterRotationState.TrainingBaseFundsCost.ToString(ci));
                s.AddValue("recallFundsCostMultiplier", RosterRotationState.RecallFundsCostMultiplier.ToString(ci));
                s.AddValue("agingEnabled", RosterRotationState.AgingEnabled.ToString(ci));
                s.AddValue("deathNotificationsEnabled", RosterRotationState.DeathNotificationsEnabled.ToString(ci));
                s.AddValue("hudNotificationsEnabled", RosterRotationState.HudNotificationsEnabled.ToString(ci));
                s.AddValue("messageAppNotificationsEnabled", RosterRotationState.MessageAppNotificationsEnabled.ToString(ci));
                s.AddValue("birthdayNotificationsEnabled", RosterRotationState.BirthdayNotificationsEnabled.ToString(ci));
                s.AddValue("trainingNotificationsEnabled", RosterRotationState.TrainingNotificationsEnabled.ToString(ci));
                s.AddValue("retirementNotificationsEnabled", RosterRotationState.RetirementNotificationsEnabled.ToString(ci));
                s.AddValue("retirementAgeMin", RosterRotationState.RetirementAgeMin.ToString(ci));
                s.AddValue("retirementAgeMax", RosterRotationState.RetirementAgeMax.ToString(ci));
                s.AddValue("retiredDeathAgeMin", RosterRotationState.RetiredDeathAgeMin.ToString(ci));
                s.AddValue("verboseLogging", RosterRotationState.VerboseLogging.ToString(ci));
                s.AddValue("verboseAgeLogging", RosterRotationState.VerboseAgeLogging.ToString(ci));
                RosterRotationState.VerboseSettingsDirty = false;

                foreach (var kvp in RosterRotationState.Records)
                {
                    var r = kvp.Value;
                    ConfigNode rNode = root.AddNode("Record");
                    rNode.AddValue("name", kvp.Key);
                    if (!string.IsNullOrEmpty(r.OriginalTrait)) rNode.AddValue("originalTrait", r.OriginalTrait);
                    rNode.AddValue("originalType", ((int)r.OriginalType).ToString(ci));
                    rNode.AddValue("flights", r.Flights.ToString(ci));
                    rNode.AddValue("lastFlightUT", r.LastFlightUT.ToString("R", ci));
                    rNode.AddValue("restUntilUT", r.RestUntilUT.ToString("R", ci));
                    rNode.AddValue("retired", r.Retired.ToString(ci));
                    rNode.AddValue("retiredUT", r.RetiredUT.ToString("R", ci));
                    rNode.AddValue("experienceAtRetire", r.ExperienceAtRetire.ToString(ci));
                    rNode.AddValue("missionStartUT", r.MissionStartUT.ToString("R", ci));
                    rNode.AddValue("trainingType", ((int)r.Training).ToString(ci));
                    rNode.AddValue("trainingTargetLevel", r.TrainingTargetLevel.ToString(ci));
                    if (r.GrantedLevel >= 0) rNode.AddValue("grantedLevel", r.GrantedLevel.ToString(ci));
                    if (r.TrainingEndUT > 0) rNode.AddValue("trainingEndUT", r.TrainingEndUT.ToString("R", ci));
                    if (r.LastAgedYears >= 0)
                    {
                        rNode.AddValue("birthUT", r.BirthUT.ToString("R", ci));
                        rNode.AddValue("naturalRetirementUT", r.NaturalRetirementUT.ToString("R", ci));
                        rNode.AddValue("retirementDelayYears", r.RetirementDelayYears.ToString(ci));
                        rNode.AddValue("retirementWarned", r.RetirementWarned.ToString(ci));
                        rNode.AddValue("retirementScheduled", r.RetirementScheduled.ToString(ci));
                        if (r.RetirementScheduledUT > 0) rNode.AddValue("retirementScheduledUT", r.RetirementScheduledUT.ToString("R", ci));
                        if (r.DeathUT > 0) rNode.AddValue("deathUT", r.DeathUT.ToString("R", ci));
                        rNode.AddValue("lastAgedYears", r.LastAgedYears.ToString(ci));
                    }
                }
            }
            catch (Exception ex) { RRLog.Error($"OnSave failed: {ex}"); }
        }

        private static int PI(string s, int fb) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fb;
        private static double PD(string s, double fb) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fb;
        private static bool PB(string s, bool fb) => bool.TryParse(s, out bool v) ? v : fb;
        private static ProtoCrewMember.KerbalType ParseKerbalType(string s, ProtoCrewMember.KerbalType fb)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? (ProtoCrewMember.KerbalType)i : fb;
    }

    /// <summary>
    /// Backward-compatible alias: old saves reference "RosterRotationScenario" by class name.
    /// KSP resolves SCENARIO { name = RosterRotationScenario } by searching for a type with
    /// that exact name. This subclass inherits all behavior from EACScenario so old saves load
    /// seamlessly. New games use EACScenario (via its [KSPScenario] attribute).
    /// </summary>
    public class RosterRotationScenario : EACScenario { }
}

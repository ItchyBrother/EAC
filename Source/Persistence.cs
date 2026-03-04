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

                // Prefer our dedicated sub-node if present.
                // Back-compat: also accept the old node name.
                ConfigNode root = null;
                if (node.HasNode(NodeNameNew)) root = node.GetNode(NodeNameNew);
                else if (node.HasNode(NodeNameOld)) root = node.GetNode(NodeNameOld);
                else root = node;

                // --- Settings (optional) ---
                // Prefer Difficulty Options (GameParameters). If not present (older saves), fall back to our Settings node.
                bool loadedFromParams = EACGameSettings.TryApplyToStateFromGameParams();
                if (!loadedFromParams)
                {
                    if (root.HasNode("Settings"))
                    {
                        var s = root.GetNode("Settings");
                        RosterRotationState.RestDays = ParseDouble(s.GetValue("restDays"), 14);
                        RosterRotationState.UseKerbinDays = ParseBool(s.GetValue("useKerbinDays"), true);
                        RosterRotationState.TrainingInitialDays = ParseInt(s.GetValue("trainingInitialDays"), 30);
                        RosterRotationState.TrainingStarDays = ParseInt(s.GetValue("trainingStarDays"), 30);
                        RosterRotationState.TrainingFundsMultiplier = ParseDouble(s.GetValue("trainingFundsMultiplier"), 1.0);
                        RosterRotationState.TrainingRDPerStar = ParseDouble(s.GetValue("trainingRDPerStar"), 10.0);
                        RosterRotationState.TrainingBaseFundsCost = ParseDouble(s.GetValue("trainingBaseFundsCost"), 62000);

                        RosterRotationState.AgingEnabled = ParseBool(s.GetValue("agingEnabled"), true);
                        RosterRotationState.DeathNotificationsEnabled = ParseBool(s.GetValue("deathNotificationsEnabled"), true);

                        // Notification routing
                        RosterRotationState.HudNotificationsEnabled = ParseBool(s.GetValue("hudNotificationsEnabled"), true);
                        RosterRotationState.MessageAppNotificationsEnabled = ParseBool(s.GetValue("messageAppNotificationsEnabled"), true);

                        // Notification categories
                        RosterRotationState.BirthdayNotificationsEnabled = ParseBool(s.GetValue("birthdayNotificationsEnabled"), true);
                        RosterRotationState.TrainingNotificationsEnabled = ParseBool(s.GetValue("trainingNotificationsEnabled"), true);
                        RosterRotationState.RetirementNotificationsEnabled = ParseBool(s.GetValue("retirementNotificationsEnabled"), true);

                        RosterRotationState.RetirementAgeMin = ParseInt(s.GetValue("retirementAgeMin"), 48);
                        RosterRotationState.RetirementAgeMax = ParseInt(s.GetValue("retirementAgeMax"), 55);
                        RosterRotationState.RetiredDeathAgeMin = ParseInt(s.GetValue("retiredDeathAgeMin"), 55);

                        RosterRotationState.VerboseLogging = ParseBool(s.GetValue("verboseLogging"), false);
                        RosterRotationState.VerboseAgeLogging = ParseBool(s.GetValue("verboseAgeLogging"), false);
                    }
                    else
                    {
                        // Defaults
                        RosterRotationState.RestDays = 14;
                        RosterRotationState.UseKerbinDays = true;
                        RosterRotationState.TrainingInitialDays = 30;
                        RosterRotationState.TrainingStarDays = 30;
                        RosterRotationState.TrainingFundsMultiplier = 1.0;
                        RosterRotationState.TrainingRDPerStar = 10.0;
                        RosterRotationState.TrainingBaseFundsCost = 62000;

                        RosterRotationState.AgingEnabled = true;
                        RosterRotationState.DeathNotificationsEnabled = true;
                        RosterRotationState.HudNotificationsEnabled = true;
                        RosterRotationState.MessageAppNotificationsEnabled = true;
                        RosterRotationState.BirthdayNotificationsEnabled = true;
                        RosterRotationState.TrainingNotificationsEnabled = true;
                        RosterRotationState.RetirementNotificationsEnabled = true;

                        RosterRotationState.RetirementAgeMin = 48;
                        RosterRotationState.RetirementAgeMax = 55;
                        RosterRotationState.RetiredDeathAgeMin = 55;

                        RosterRotationState.VerboseLogging = false;
                        RosterRotationState.VerboseAgeLogging = false;
                    }

                    // Keep the Difficulty Options UI in sync with older saves that only used our node.
                    EACGameSettings.TrySyncGameParamsFromState();
                }

                // --- Records ---
                if (!root.HasNode("Record")) return;

                foreach (ConfigNode rNode in root.GetNodes("Record"))
                {
                    string name = rNode.GetValue("name");
                    if (string.IsNullOrEmpty(name)) continue;

                    var rec = new RosterRotationState.KerbalRecord
                    {
                        // Optional: original role info
                        OriginalTrait = rNode.GetValue("originalTrait"),
                        OriginalType = ParseKerbalType(rNode.GetValue("originalType"), ProtoCrewMember.KerbalType.Crew),

                        Flights = ParseInt(rNode.GetValue("flights"), 0),
                        LastFlightUT = ParseDouble(rNode.GetValue("lastFlightUT"), 0),
                        RestUntilUT = ParseDouble(rNode.GetValue("restUntilUT"), 0),

                        // Retirement
                        Retired = ParseBool(rNode.GetValue("retired"), false),
                        RetiredUT = ParseDouble(rNode.GetValue("retiredUT"), 0),
                        ExperienceAtRetire = ParseInt(rNode.GetValue("experienceAtRetire"), 0),

                        // legacy / optional
                        MissionStartUT = ParseDouble(rNode.GetValue("missionStartUT"), 0),

                        // Training
                        Training = (TrainingType)ParseInt(rNode.GetValue("trainingType"), 0),
                        TrainingTargetLevel = ParseInt(rNode.GetValue("trainingTargetLevel"), 0),
                        GrantedLevel = ParseInt(rNode.GetValue("grantedLevel"), -1),

                        // Aging
                        BirthUT = ParseDouble(rNode.GetValue("birthUT"), 0),
                        NaturalRetirementUT = ParseDouble(rNode.GetValue("naturalRetirementUT"), 0),
                        RetirementDelayYears = ParseInt(rNode.GetValue("retirementDelayYears"), 0),
                        RetirementWarned = ParseBool(rNode.GetValue("retirementWarned"), false),
                        RetirementScheduled = ParseBool(rNode.GetValue("retirementScheduled"), false),
                        RetirementScheduledUT = ParseDouble(rNode.GetValue("retirementScheduledUT"), 0),
                        DeathUT = ParseDouble(rNode.GetValue("deathUT"), 0),
                        TrainingEndUT = ParseDouble(rNode.GetValue("trainingEndUT"), 0),
                        LastAgedYears = ParseInt(rNode.GetValue("lastAgedYears"), -1),
                    };

                    RosterRotationState.Records[name] = rec;
                }

                RRLog.Info($"Loaded {RosterRotationState.Records.Count} Kerbal records. RestDays={RosterRotationState.RestDays} UseKerbinDays={RosterRotationState.UseKerbinDays}");
            }
            catch (Exception ex)
            {
                RRLog.Error($"OnLoad failed: {ex}");
            }
        }

        public override void OnSave(ConfigNode node)
        {
            try
            {
                if (node == null) return;

                // Write under the new node name.
                ConfigNode root = node.HasNode(NodeNameNew) ? node.GetNode(NodeNameNew) : node.AddNode(NodeNameNew);

                // Clear old data
                root.RemoveNodes("Record");

                // Save settings
                if (root.HasNode("Settings")) root.RemoveNode("Settings");
                var s = root.AddNode("Settings");

                s.AddValue("restDays", RosterRotationState.RestDays.ToString(CultureInfo.InvariantCulture));
                s.AddValue("useKerbinDays", RosterRotationState.UseKerbinDays.ToString(CultureInfo.InvariantCulture));
                s.AddValue("trainingInitialDays", RosterRotationState.TrainingInitialDays.ToString(CultureInfo.InvariantCulture));
                s.AddValue("trainingStarDays", RosterRotationState.TrainingStarDays.ToString(CultureInfo.InvariantCulture));
                s.AddValue("trainingFundsMultiplier", RosterRotationState.TrainingFundsMultiplier.ToString(CultureInfo.InvariantCulture));
                s.AddValue("trainingRDPerStar", RosterRotationState.TrainingRDPerStar.ToString(CultureInfo.InvariantCulture));
                s.AddValue("trainingBaseFundsCost", RosterRotationState.TrainingBaseFundsCost.ToString(CultureInfo.InvariantCulture));

                s.AddValue("agingEnabled", RosterRotationState.AgingEnabled.ToString(CultureInfo.InvariantCulture));

                s.AddValue("deathNotificationsEnabled", RosterRotationState.DeathNotificationsEnabled.ToString(CultureInfo.InvariantCulture));
                s.AddValue("hudNotificationsEnabled", RosterRotationState.HudNotificationsEnabled.ToString(CultureInfo.InvariantCulture));
                s.AddValue("messageAppNotificationsEnabled", RosterRotationState.MessageAppNotificationsEnabled.ToString(CultureInfo.InvariantCulture));
                s.AddValue("birthdayNotificationsEnabled", RosterRotationState.BirthdayNotificationsEnabled.ToString(CultureInfo.InvariantCulture));
                s.AddValue("trainingNotificationsEnabled", RosterRotationState.TrainingNotificationsEnabled.ToString(CultureInfo.InvariantCulture));
                s.AddValue("retirementNotificationsEnabled", RosterRotationState.RetirementNotificationsEnabled.ToString(CultureInfo.InvariantCulture));

                s.AddValue("retirementAgeMin", RosterRotationState.RetirementAgeMin.ToString(CultureInfo.InvariantCulture));
                s.AddValue("retirementAgeMax", RosterRotationState.RetirementAgeMax.ToString(CultureInfo.InvariantCulture));
                s.AddValue("retiredDeathAgeMin", RosterRotationState.RetiredDeathAgeMin.ToString(CultureInfo.InvariantCulture));

                s.AddValue("verboseLogging", RosterRotationState.VerboseLogging.ToString(CultureInfo.InvariantCulture));
                s.AddValue("verboseAgeLogging", RosterRotationState.VerboseAgeLogging.ToString(CultureInfo.InvariantCulture));

                // Save records
                foreach (var kvp in RosterRotationState.Records)
                {
                    string name = kvp.Key;
                    var r = kvp.Value;

                    ConfigNode rNode = root.AddNode("Record");
                    rNode.AddValue("name", name);

                    if (!string.IsNullOrEmpty(r.OriginalTrait))
                        rNode.AddValue("originalTrait", r.OriginalTrait);
                    rNode.AddValue("originalType", ((int)r.OriginalType).ToString(CultureInfo.InvariantCulture));

                    rNode.AddValue("flights", r.Flights.ToString(CultureInfo.InvariantCulture));
                    rNode.AddValue("lastFlightUT", r.LastFlightUT.ToString("R", CultureInfo.InvariantCulture));
                    rNode.AddValue("restUntilUT", r.RestUntilUT.ToString("R", CultureInfo.InvariantCulture));

                    rNode.AddValue("retired", r.Retired.ToString(CultureInfo.InvariantCulture));
                    rNode.AddValue("retiredUT", r.RetiredUT.ToString("R", CultureInfo.InvariantCulture));
                    rNode.AddValue("experienceAtRetire", r.ExperienceAtRetire.ToString(CultureInfo.InvariantCulture));

                    rNode.AddValue("missionStartUT", r.MissionStartUT.ToString("R", CultureInfo.InvariantCulture));

                    rNode.AddValue("trainingType", ((int)r.Training).ToString(CultureInfo.InvariantCulture));
                    rNode.AddValue("trainingTargetLevel", r.TrainingTargetLevel.ToString(CultureInfo.InvariantCulture));
                    if (r.GrantedLevel >= 0)
                        rNode.AddValue("grantedLevel", r.GrantedLevel.ToString(CultureInfo.InvariantCulture));

                    if (r.TrainingEndUT > 0)
                        rNode.AddValue("trainingEndUT", r.TrainingEndUT.ToString("R", CultureInfo.InvariantCulture));

                    if (r.LastAgedYears >= 0)
                    {
                        rNode.AddValue("birthUT", r.BirthUT.ToString("R", CultureInfo.InvariantCulture));
                        rNode.AddValue("naturalRetirementUT", r.NaturalRetirementUT.ToString("R", CultureInfo.InvariantCulture));
                        rNode.AddValue("retirementDelayYears", r.RetirementDelayYears.ToString(CultureInfo.InvariantCulture));
                        rNode.AddValue("retirementWarned", r.RetirementWarned.ToString(CultureInfo.InvariantCulture));
                        rNode.AddValue("retirementScheduled", r.RetirementScheduled.ToString(CultureInfo.InvariantCulture));
                        if (r.RetirementScheduledUT > 0)
                            rNode.AddValue("retirementScheduledUT", r.RetirementScheduledUT.ToString("R", CultureInfo.InvariantCulture));
                        if (r.DeathUT > 0)
                            rNode.AddValue("deathUT", r.DeathUT.ToString("R", CultureInfo.InvariantCulture));
                        rNode.AddValue("lastAgedYears", r.LastAgedYears.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"OnSave failed: {ex}");
            }
        }

        private static int ParseInt(string s, int fallback)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        private static double ParseDouble(string s, double fallback)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        private static bool ParseBool(string s, bool fallback)
            => bool.TryParse(s, out bool v) ? v : fallback;

        private static ProtoCrewMember.KerbalType ParseKerbalType(string s, ProtoCrewMember.KerbalType fallback)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                return (ProtoCrewMember.KerbalType)i;
            return fallback;
        }
    }
}

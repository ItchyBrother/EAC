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
                    var settings = KerbalRecordPersistence.ReadSettings(root.GetNode("Settings"));
                    KerbalRecordPersistence.ApplySettingsToState(settings, RosterRotationState.VerboseSettingsDirty);

                    RRLog.Verbose($"[EAC] Settings loaded from save: VerboseLogging={RosterRotationState.VerboseLogging}, SyncFlightTrackerFromEacOnce={RosterRotationState.SyncFlightTrackerFromEacOnce}, TraitGrowthEnabled={RosterRotationState.TraitGrowthEnabled}");
                }

                RecoveryLeaveService.LoadPendingCrewRandRExtensions(root);

                // Push our loaded state into GameParameters so the Difficulty Options UI
                // shows the correct values when the player opens it.
                EACGameSettings.TrySyncGameParamsFromState();

                if (!root.HasNode("Record")) return;

                foreach (ConfigNode rNode in root.GetNodes("Record"))
                {
                    if (KerbalRecordPersistence.TryReadRecord(rNode, out string name, out var rec))
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

                KerbalRecordPersistence.WriteSettingsNode(s, KerbalRecordPersistence.CaptureSettingsFromState(), ci);
                RosterRotationState.VerboseSettingsDirty = false;

                RecoveryLeaveService.SavePendingCrewRandRExtensions(root);

                foreach (var kvp in RosterRotationState.Records)
                {
                    var r = kvp.Value;
                    ConfigNode rNode = root.AddNode("Record");
                    int recoveredFlights = GetRecoveredFlightCountFromRoster(kvp.Key, r.Flights);
                    r.Flights = recoveredFlights;
                    KerbalRecordPersistence.WriteRecordNode(rNode, kvp.Key, r, ci);
                }
            }
            catch (Exception ex) { RRLog.Error($"OnSave failed: {ex}"); }
        }

        private static int GetRecoveredFlightCountFromRoster(string kerbalName, int fallback)
        {
            if (string.IsNullOrEmpty(kerbalName) || HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null)
                return fallback;

            try
            {
                var roster = HighLogic.CurrentGame.CrewRoster;
                for (int i = 0; i < roster.Count; i++)
                {
                    ProtoCrewMember pcm;
                    try { pcm = roster[i]; }
                    catch { continue; }

                    if (pcm == null || !string.Equals(pcm.name, kerbalName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    ConfigNode kerbalNode = new ConfigNode("KERBAL");
                    try
                    {
                        pcm.Save(kerbalNode);
                    }
                    catch
                    {
                        return fallback;
                    }

                    int recoveredFlights = CountRecoveredFlightsFromKerbalNode(kerbalNode);
                    return recoveredFlights >= 0 ? recoveredFlights : fallback;
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Persistence.cs:229", "Suppressed exception in Persistence.cs:229", ex); }

            return fallback;
        }

        private static int CountRecoveredFlightsFromKerbalNode(ConfigNode kerbalNode)
        {
            if (kerbalNode == null)
                return -1;

            // Align with FlightTracker's practical behavior: only count flights that look like
            // completed vessel missions, not every stray Recover log record. A recovered flight
            // must have both a Flight,<Body> start entry and a Recover entry for the same flight id.
            var launchedFlights = new System.Collections.Generic.HashSet<int>();
            var recoveredFlights = new System.Collections.Generic.HashSet<int>();

            CollectCompletedCareerFlights(kerbalNode.GetNode("CAREER_LOG"), launchedFlights, recoveredFlights);
            CollectCompletedCareerFlights(kerbalNode.GetNode("careerLog"), launchedFlights, recoveredFlights);

            launchedFlights.IntersectWith(recoveredFlights);
            return launchedFlights.Count;
        }

        private static void CollectCompletedCareerFlights(
            ConfigNode logNode,
            System.Collections.Generic.HashSet<int> launchedFlights,
            System.Collections.Generic.HashSet<int> recoveredFlights)
        {
            if (logNode == null || launchedFlights == null || recoveredFlights == null)
                return;

            foreach (ConfigNode.Value value in logNode.values)
            {
                if (value == null || string.IsNullOrEmpty(value.name) || string.IsNullOrEmpty(value.value))
                    continue;
                if (string.Equals(value.name, "flight", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.name, "flights", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!int.TryParse(value.name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flightId))
                    continue;

                string entry = value.value.Trim();
                if (entry.StartsWith("Flight,", StringComparison.OrdinalIgnoreCase))
                {
                    launchedFlights.Add(flightId);
                    continue;
                }

                if (entry.StartsWith("Recover", StringComparison.OrdinalIgnoreCase))
                    recoveredFlights.Add(flightId);
            }
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

// EAC - Enhanced Astronaut Complex
// Contract Configurator behaviour: load a pre-settled vessel scenario from a saved ConfigNode.
// Build this into EAC_CCBridge.dll.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Contracts;
using ContractConfigurator;
using UnityEngine;
using RosterRotation;

namespace RosterRotation.ContractConfiguratorIntegration
{
    public sealed class EACLoadScenarioFactory : BehaviourFactory
    {
        private string scenarioFile = string.Empty;
        private string vesselName = string.Empty;
        private bool cleanupOnComplete = false;
        private bool cleanupOnFail = true;
        private bool cleanupOnWithdraw = true;

        public override bool Load(ConfigNode configNode)
        {
            bool valid = base.Load(configNode);
            valid &= ConfigNodeUtil.ParseValue(configNode, "scenarioFile", x => scenarioFile = x, this, string.Empty);
            ConfigNodeUtil.ParseValue(configNode, "vesselName", x => vesselName = x, this, string.Empty);
            ConfigNodeUtil.ParseValue(configNode, "cleanupOnComplete", x => cleanupOnComplete = x, this, cleanupOnComplete);
            ConfigNodeUtil.ParseValue(configNode, "cleanupOnFail", x => cleanupOnFail = x, this, cleanupOnFail);
            ConfigNodeUtil.ParseValue(configNode, "cleanupOnWithdraw", x => cleanupOnWithdraw = x, this, cleanupOnWithdraw);

            if (string.IsNullOrEmpty(scenarioFile))
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario requires scenarioFile.");
                valid = false;
            }
            return valid;
        }

        public override ContractBehaviour Generate(ConfiguredContract contract)
        {
            return new EACLoadScenario(scenarioFile, vesselName, cleanupOnComplete, cleanupOnFail, cleanupOnWithdraw);
        }
    }

    public sealed class EACLoadScenario : ContractBehaviour
    {
        private string scenarioFile = string.Empty;
        private string vesselName = string.Empty;
        private bool cleanupOnComplete = false;
        private bool cleanupOnFail = true;
        private bool cleanupOnWithdraw = true;
        private readonly List<string> spawnedVesselIds = new List<string>();
        private readonly HashSet<string> associatedVesselIds = new HashSet<string>();
        private bool spawned;
        private float nextAssociationAttemptRealtime;

        public EACLoadScenario() { }

        public EACLoadScenario(string scenarioFile, string vesselName, bool cleanupOnComplete, bool cleanupOnFail, bool cleanupOnWithdraw)
        {
            this.scenarioFile = scenarioFile ?? string.Empty;
            this.vesselName = vesselName ?? string.Empty;
            this.cleanupOnComplete = cleanupOnComplete;
            this.cleanupOnFail = cleanupOnFail;
            this.cleanupOnWithdraw = cleanupOnWithdraw;
        }

        protected override void OnAccepted()
        {
            LoadScenarioOnce();
        }

        protected override void OnUpdate()
        {
            if (!spawned || spawnedVesselIds.Count == 0) return;
            if (Time.realtimeSinceStartup < nextAssociationAttemptRealtime) return;
            nextAssociationAttemptRealtime = Time.realtimeSinceStartup + 1.0f;
            TryAssociatePendingScenarioVessels();
        }

        protected override void OnCompleted()
        {
            if (cleanupOnComplete)
                CleanupSpawnedVessels("contract completed");
        }

        protected override void OnFailed()
        {
            if (cleanupOnFail)
                CleanupSpawnedVessels("contract failed");
        }

        protected override void OnCancelled()
        {
            if (cleanupOnFail)
                CleanupSpawnedVessels("contract cancelled");
        }

        protected override void OnDeadlineExpired()
        {
            if (cleanupOnFail)
                CleanupSpawnedVessels("contract deadline expired");
        }

        protected override void OnWithdrawn()
        {
            if (cleanupOnWithdraw)
                CleanupSpawnedVessels("contract withdrawn");
        }

        protected override void OnSave(ConfigNode node)
        {
            if (!string.IsNullOrEmpty(scenarioFile)) node.AddValue("scenarioFile", scenarioFile);
            if (!string.IsNullOrEmpty(vesselName)) node.AddValue("vesselName", vesselName);
            node.AddValue("cleanupOnComplete", cleanupOnComplete);
            node.AddValue("cleanupOnFail", cleanupOnFail);
            node.AddValue("cleanupOnWithdraw", cleanupOnWithdraw);
            node.AddValue("spawned", spawned);
            foreach (string id in spawnedVesselIds.Where(x => !string.IsNullOrEmpty(x)))
                node.AddValue("spawnedVesselId", id);
        }

        protected override void OnLoad(ConfigNode node)
        {
            if (node == null) return;
            scenarioFile = node.GetValue("scenarioFile") ?? scenarioFile;
            vesselName = node.GetValue("vesselName") ?? vesselName;
            bool.TryParse(node.GetValue("cleanupOnComplete"), out cleanupOnComplete);
            bool.TryParse(node.GetValue("cleanupOnFail"), out cleanupOnFail);
            bool.TryParse(node.GetValue("cleanupOnWithdraw"), out cleanupOnWithdraw);
            bool.TryParse(node.GetValue("spawned"), out spawned);
            spawnedVesselIds.Clear();
            foreach (string id in node.GetValues("spawnedVesselId") ?? new string[0])
            {
                if (!string.IsNullOrEmpty(id)) spawnedVesselIds.Add(id);
            }
        }

        private void LoadScenarioOnce()
        {
            RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] EACLoadScenario OnAccepted; scenarioFile='" + scenarioFile + "'.");
            if (spawned)
            {
                RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] EACLoadScenario skipped because this contract already spawned its scenario.");
                return;
            }
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.flightState == null)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario could not load scenario because no current game is active.");
                return;
            }

            string fullPath = ResolveScenarioPath(scenarioFile);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario scenario file not found: " + scenarioFile + " resolved='" + (fullPath ?? string.Empty) + "'.");
                return;
            }

            ConfigNode root;
            try { root = ConfigNode.Load(fullPath); }
            catch (Exception ex)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario failed to read scenario file '" + scenarioFile + "': " + ex.Message);
                return;
            }

            if (root == null)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario scenario file loaded as null: " + scenarioFile);
                return;
            }

            ConfigNode[] vesselNodes = GetScenarioVesselNodes(root);
            if (vesselNodes == null || vesselNodes.Length == 0)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario found no VESSEL nodes in: " + scenarioFile);
                return;
            }

            int count = 0;
            foreach (ConfigNode vesselNode in vesselNodes)
            {
                if (vesselNode == null) continue;
                try
                {
                    PrepareVesselNode(vesselNode, count);

                    // Use KSP's game-level vessel insertion path instead of manually
                    // constructing a ProtoVessel and appending it to flightState.
                    // AddVessel updates the active game's vessel collection in the
                    // same way used by stock/KSP mod code and makes the scenario
                    // vessel visible without relying on a later save/reload cycle.
                    ProtoVessel protoVessel = HighLogic.CurrentGame.AddVessel(vesselNode);
                    if (protoVessel == null)
                        throw new InvalidOperationException("HighLogic.CurrentGame.AddVessel returned null.");

                    spawnedVesselIds.Add(protoVessel.vesselID.ToString());
                    RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] EACLoadScenario added vessel '" + protoVessel.vesselName + "' id=" + protoVessel.vesselID + ".");
                    TryAssociateScenarioVessel(protoVessel);
                    count++;
                }
                catch (Exception ex)
                {
                    RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario failed to add VESSEL from '" + scenarioFile + "': " + ex.Message);
                }
            }

            spawned = count > 0;
            if (spawned)
            {
                RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] EACLoadScenario loaded " + count + " vessel(s) from " + scenarioFile + ".");
                ScreenMessages.PostScreenMessage("EAC scenario loaded: " + count + " vessel(s)", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private static ConfigNode[] GetScenarioVesselNodes(ConfigNode root)
        {
            if (root == null) return new ConfigNode[0];
            if (string.Equals(root.name, "VESSEL", StringComparison.OrdinalIgnoreCase))
                return new[] { root };

            ConfigNode scenarioNode = root;
            if (!string.Equals(root.name, "EAC_SCENARIO", StringComparison.OrdinalIgnoreCase))
            {
                ConfigNode nestedScenario = root.GetNode("EAC_SCENARIO");
                if (nestedScenario != null) scenarioNode = nestedScenario;
            }

            return scenarioNode.GetNodes("VESSEL") ?? new ConfigNode[0];
        }

        private void PrepareVesselNode(ConfigNode vesselNode, int index)
        {
            string newPid = Guid.NewGuid().ToString("N");
            SetValue(vesselNode, "pid", newPid);
            SetValue(vesselNode, "persistentId", NewPersistentId().ToString());
            SetValue(vesselNode, "vesselSpawning", "False");
            SetValue(vesselNode, "landed", "True");
            SetValue(vesselNode, "sit", "LANDED");
            SetValue(vesselNode, "type", "Base");

            double now = Planetarium.GetUniversalTime();
            SetValue(vesselNode, "lct", now.ToString("R"));
            SetValue(vesselNode, "lastUT", now.ToString("R"));

            if (!string.IsNullOrEmpty(vesselName))
            {
                string resolvedName = vesselName;
                if (index > 0) resolvedName += " " + (index + 1).ToString();
                SetValue(vesselNode, "name", resolvedName);
            }
        }

        private static void SetValue(ConfigNode node, string key, string value)
        {
            if (node == null || string.IsNullOrEmpty(key)) return;
            if (node.HasValue(key)) node.SetValue(key, value, true);
            else node.AddValue(key, value);
        }

        private static uint NewPersistentId()
        {
            unchecked
            {
                byte[] bytes = Guid.NewGuid().ToByteArray();
                uint value = BitConverter.ToUInt32(bytes, 0);
                return value == 0 ? 1u : value;
            }
        }

        private static string ResolveScenarioPath(string configuredPath)
        {
            if (string.IsNullOrEmpty(configuredPath)) return string.Empty;
            string path = configuredPath.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(path)) return path;

            const string gameDataPrefix = "GameData/";
            if (path.StartsWith(gameDataPrefix, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(gameDataPrefix.Length);

            return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", path.Replace('/', Path.DirectorySeparatorChar));
        }


        private void TryAssociatePendingScenarioVessels()
        {
            for (int i = 0; i < spawnedVesselIds.Count; i++)
            {
                string id = spawnedVesselIds[i];
                Guid vesselGuid;
                if (!Guid.TryParse(id, out vesselGuid)) continue;
                if (associatedVesselIds.Contains(id)) continue;

                try
                {
                    Vessel vessel = FlightGlobals.FindVessel(vesselGuid);
                    if (vessel == null) continue;
                    if (TryAssociateScenarioVessel(vessel))
                        associatedVesselIds.Add(id);
                }
                catch (Exception ex)
                {
                    RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario delayed vessel association failed for id " + id + ": " + ex.Message);
                }
            }
        }

        private bool TryAssociateScenarioVessel(ProtoVessel protoVessel)
        {
            if (protoVessel == null) return false;
            Vessel vessel = null;
            try { vessel = protoVessel.vesselRef; } catch { vessel = null; }
            if (vessel == null)
            {
                try { vessel = FlightGlobals.FindVessel(protoVessel.vesselID); } catch { vessel = null; }
            }
            if (vessel == null) return false;
            bool result = TryAssociateScenarioVessel(vessel);
            if (result) associatedVesselIds.Add(protoVessel.vesselID.ToString());
            return result;
        }

        private bool TryAssociateScenarioVessel(Vessel vessel)
        {
            if (vessel == null) return false;

            bool associated = false;
            string configuredKey = vesselName ?? string.Empty;
            string liveName = vessel.vesselName ?? string.Empty;

            associated |= TryAssociateVesselKey(configuredKey, vessel);
            if (!string.Equals(configuredKey, liveName, StringComparison.Ordinal))
                associated |= TryAssociateVesselKey(liveName, vessel);

            // A stable generic key is useful for future contracts that do not care which
            // specific Level 3 exam spawned the shared field lab.
            associated |= TryAssociateVesselKey("EAC Level 3 Field Laboratory", vessel);
            return associated;
        }

        private static bool TryAssociateVesselKey(string key, Vessel vessel)
        {
            if (string.IsNullOrEmpty(key) || vessel == null) return false;
            try
            {
                ContractVesselTracker.Instance.AssociateVessel(key, vessel);
                RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] EACLoadScenario associated vessel '" + vessel.vesselName + "' with CC key '" + key + "'.");
                return true;
            }
            catch (Exception ex)
            {
                // This can happen immediately after AddVessel if KSP has not created a
                // live Vessel/root part yet.  OnUpdate will retry once the vessel loads.
                RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] EACLoadScenario could not yet associate vessel '" + (vessel.vesselName ?? "") + "' with CC key '" + key + "': " + ex.Message);
                return false;
            }
        }

        private void CleanupSpawnedVessels(string reason)
        {
            if (spawnedVesselIds.Count == 0) return;
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.flightState == null) return;

            int removed = 0;
            foreach (string id in spawnedVesselIds.ToList())
            {
                Guid vesselGuid;
                if (!Guid.TryParse(id, out vesselGuid)) continue;

                try
                {
                    Vessel live = FlightGlobals.FindVessel(vesselGuid);
                    if (live != null)
                    {
                        if (VesselHasCrew(live))
                        {
                            RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario cleanup skipped crewed vessel '" + live.vesselName + "' after " + reason + ". Recover or move crew before cleanup.");
                            continue;
                        }
                        live.Die();
                        removed++;
                        continue;
                    }

                    ProtoVessel proto = HighLogic.CurrentGame.flightState.protoVessels.FirstOrDefault(v => v != null && v.vesselID == vesselGuid);
                    if (proto != null)
                    {
                        if (ProtoVesselHasCrew(proto))
                        {
                            RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario cleanup skipped crewed proto-vessel '" + proto.vesselName + "' after " + reason + ". Recover or move crew before cleanup.");
                            continue;
                        }
                        HighLogic.CurrentGame.flightState.protoVessels.Remove(proto);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACLoadScenario cleanup failed for vessel id " + id + ": " + ex.Message);
                }
            }

            if (removed > 0)
                RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] EACLoadScenario removed " + removed + " scenario vessel(s) after " + reason + ".");
        }

        private static bool VesselHasCrew(Vessel vessel)
        {
            try
            {
                var crew = vessel?.GetVesselCrew();
                return crew != null && crew.Count > 0;
            }
            catch { return true; }
        }

        private static bool ProtoVesselHasCrew(ProtoVessel protoVessel)
        {
            try
            {
                var crew = protoVessel?.GetVesselCrew();
                return crew != null && crew.Count > 0;
            }
            catch { return true; }
        }
    }
}

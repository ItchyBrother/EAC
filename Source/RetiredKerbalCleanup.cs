using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEngine;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class RetiredKerbalCleanupService : MonoBehaviour
    {
        private static readonly HashSet<string> AutoCleanupSaveRequested =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly HashSet<string> StockScenarioNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ProgressTracking",
                "ContractSystem"
            };

        private static bool _postSaveSweepQueued;

        private void Awake()
        {
            DontDestroyOnLoad(this);
            GameEvents.onGameStateLoad.Add(OnGameStateLoad);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
            RRLog.Verbose($"[RetiredCleanup] Service initialized. Debug log: {RRLog.GeneralLogPath}; purge log: {RRLog.PurgeLogPath}");
        }

        private void OnDestroy()
        {
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
        }

        public static bool RequestAutoCleanupSave(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName)) return false;
            return AutoCleanupSaveRequested.Add(kerbalName);
        }

        public static void ResetAutoCleanupRequest(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName)) return;
            AutoCleanupSaveRequested.Remove(kerbalName);
        }

        public static bool IsRetiredPurgeDue(ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec, double nowUT)
        {
            if (rec == null || !rec.Retired) return false;
            if (rec.DeathUT > 0) return false;
            if (rec.RetiredUT <= 0) return false;

            if (k != null)
            {
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead) return false;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return false;
            }

            int starsAtRetire = rec.ExperienceAtRetire;
            if (starsAtRetire <= 0 && k != null)
                starsAtRetire = (int)k.experienceLevel;
            if (starsAtRetire < 0) starsAtRetire = 0;

            double elapsed = nowUT - rec.RetiredUT;
            if (elapsed < 0) return false;

            if (starsAtRetire <= 0)
                return elapsed >= RosterRotationState.YearSeconds;

            return elapsed >= (starsAtRetire * RosterRotationState.YearSeconds);
        }

        private void OnGameStateLoad(ConfigNode root)
        {
            TryPruneKerbals(root, "load");
        }

        private void OnGameStateSave(ConfigNode root)
        {
            bool autoCleanupEnabled = RosterRotationState.AutoCleanupUnreferencedKerbals;
            bool pendingMissionDeaths = HasPendingMissionDeaths();

            if (!autoCleanupEnabled && !pendingMissionDeaths)
            {
                RRLog.Verbose("[RetiredCleanup] Skipping save: auto-cleanup disabled and no pending mission-death patch work.");
                return;
            }

            if (SaveRootLooksComplete(root))
            {
                if (pendingMissionDeaths)
                    TryApplyPendingMissionDeaths(root, "save");
                if (autoCleanupEnabled)
                    TryPruneKerbals(root, "save");
                return;
            }

            RRLog.Verbose("[RetiredCleanup] Starting save sweep.");
            RRLog.Verbose("[RetiredCleanup] Save callback did not provide a complete persistent tree; deferring cleanup to a post-save persistent.sfs pass.");
            RRLog.AuditPurge("[RetiredCleanup] Deferring save sweep - save callback root incomplete; using post-save persistent.sfs pass.");
            QueuePostSaveSweep();
        }

        private static void TryPruneKerbals(ConfigNode root, string phase)
        {
            try
            {
                if (root == null)
                {
                    RRLog.Warn($"[RetiredCleanup] Skipping {phase}: root config node is null.");
                    return;
                }
                if (!RosterRotationState.AutoCleanupUnreferencedKerbals)
                {
                    RRLog.Verbose($"[RetiredCleanup] Skipping {phase}: auto-cleanup disabled.");
                    return;
                }

                RRLog.Verbose($"[RetiredCleanup] Starting {phase} sweep.");
                RRLog.AuditPurge($"[RetiredCleanup] Starting {phase} sweep.");

                if (string.Equals(phase, "save", StringComparison.OrdinalIgnoreCase) && !SaveRootLooksComplete(root))
                {
                    RRLog.Warn($"[RetiredCleanup] Skipping {phase} sweep because the save root does not appear to contain a complete persistent tree yet (missing ROSTER and/or SCENARIO nodes).");
                    RRLog.AuditPurge($"[RetiredCleanup] Skipping {phase} sweep - save root incomplete.");
                    return;
                }

                var candidates = CollectCandidates(root);
                if (candidates.Count == 0)
                {
                    RRLog.Verbose($"[RetiredCleanup] Completed {phase} sweep. Candidates=0, Removed=0, Kept=0.");
                    RRLog.AuditPurge($"[RetiredCleanup] Completed {phase} sweep. Candidates=0, Removed=0, Kept=0.");
                    return;
                }

                int removedCount = 0;
                int keptCount = 0;
                foreach (var candidate in candidates)
                {
                    string kerbalName = candidate.Key;
                    string reason = candidate.Value;

                    if (TryFindStockReference(root, kerbalName, out string stockReference))
                    {
                        keptCount++;
                        RRLog.Verbose($"[RetiredCleanup] Keeping {kerbalName} during {phase}: stock save reference found in {stockReference} ({reason}).");
                        RRLog.AuditPurge($"[RetiredCleanup] KEEP {kerbalName} during {phase} ({reason}) - stock save reference found in {stockReference}.");
                        continue;
                    }

                    bool removedRoster = RemoveKerbalFromRosterNode(root, kerbalName);
                    if (!removedRoster)
                    {
                        keptCount++;
                        RRLog.Warn($"[RetiredCleanup] Candidate {kerbalName} during {phase} was not removed because no matching roster node was found ({reason}). Live state was left unchanged.");
                        RRLog.AuditPurge($"[RetiredCleanup] KEEP {kerbalName} during {phase} ({reason}) - no matching roster node found; live state unchanged.");
                        continue;
                    }

                    bool removedRecord = RemoveKerbalRecordNode(root, kerbalName);
                    RemoveLiveKerbal(kerbalName);
                    RosterRotationState.Records.Remove(kerbalName);
                    RosterRotationState.InvalidateRetiredCache();
                    ResetAutoCleanupRequest(kerbalName);

                    removedCount++;
                    RRLog.Verbose($"[RetiredCleanup] Removed {reason} kerbal {kerbalName} during {phase}.");
                    RRLog.AuditPurge($"[RetiredCleanup] REMOVE {kerbalName} during {phase} ({reason}) - rosterRemoved={removedRoster}, recordRemoved={removedRecord}.");
                }

                RRLog.Verbose($"[RetiredCleanup] Completed {phase} sweep. Candidates={candidates.Count}, Removed={removedCount}, Kept={keptCount}.");
                RRLog.AuditPurge($"[RetiredCleanup] Completed {phase} sweep. Candidates={candidates.Count}, Removed={removedCount}, Kept={keptCount}.");
            }
            catch (Exception ex)
            {
                RRLog.Error("[RetiredCleanup] TryPruneKerbals failed: " + ex);
            }
        }


        private void QueuePostSaveSweep()
        {
            if (_postSaveSweepQueued)
            {
                RRLog.Verbose("[RetiredCleanup] Post-save sweep already queued.");
                return;
            }

            _postSaveSweepQueued = true;
            StartCoroutine(PostSaveSweepCoroutine());
        }

        private IEnumerator PostSaveSweepCoroutine()
        {
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.25f);

            try
            {
                RunPostSaveSweep();
            }
            catch (Exception ex)
            {
                RRLog.Error("[RetiredCleanup] PostSaveSweepCoroutine failed: " + ex);
            }

            _postSaveSweepQueued = false;
        }

        private void RunPostSaveSweep()
        {
            string path = ResolvePersistentPath();
            if (string.IsNullOrEmpty(path))
            {
                RRLog.Warn("[RetiredCleanup] Post-save sweep skipped: could not resolve persistent.sfs path.");
                RRLog.AuditPurge("[RetiredCleanup] Post-save sweep skipped - persistent.sfs path unavailable.");
                return;
            }

            if (!File.Exists(path))
            {
                RRLog.Warn($"[RetiredCleanup] Post-save sweep skipped: persistent.sfs was not found at {path}.");
                RRLog.AuditPurge($"[RetiredCleanup] Post-save sweep skipped - persistent.sfs not found at {path}.");
                return;
            }

            RRLog.Verbose($"[RetiredCleanup] Running post-save persistent sweep using {path}.");
            RRLog.AuditPurge($"[RetiredCleanup] Running post-save persistent sweep using {path}.");

            ConfigNode diskRoot = ConfigNode.Load(path);
            if (diskRoot == null)
            {
                RRLog.Warn($"[RetiredCleanup] Post-save sweep skipped: failed to load {path}.");
                RRLog.AuditPurge($"[RetiredCleanup] Post-save sweep skipped - failed to load {path}.");
                return;
            }

            if (!SaveRootLooksComplete(diskRoot))
            {
                RRLog.Warn("[RetiredCleanup] Post-save sweep skipped: persistent.sfs still does not contain a complete persistent tree.");
                RRLog.AuditPurge("[RetiredCleanup] Post-save sweep skipped - persistent.sfs incomplete.");
                return;
            }

            bool wroteChanges = false;
            if (HasPendingMissionDeaths())
                wroteChanges |= TryApplyPendingMissionDeaths(diskRoot, "post-save-file");
            if (RosterRotationState.AutoCleanupUnreferencedKerbals)
            {
                TryPruneKerbals(diskRoot, "post-save-file");
                wroteChanges = true;
            }

            if (wroteChanges)
            {
                diskRoot.Save(path);
                RRLog.Verbose($"[RetiredCleanup] Post-save persistent sweep finished and wrote {path}.");
                RRLog.AuditPurge($"[RetiredCleanup] Post-save persistent sweep finished and wrote {path}.");
            }
            else
            {
                RRLog.Verbose("[RetiredCleanup] Post-save persistent sweep found nothing to write.");
            }
        }

        private static string ResolvePersistentPath()
        {
            try
            {
                string saveFolder = HighLogic.SaveFolder;
                if (string.IsNullOrEmpty(saveFolder))
                    saveFolder = HighLogic.CurrentGame?.Title;
                if (string.IsNullOrEmpty(saveFolder))
                    return null;

                string root = KSPUtil.ApplicationRootPath;
                if (string.IsNullOrEmpty(root))
                    return null;

                return Path.Combine(root, "saves", saveFolder, "persistent.sfs");
            }
            catch
            {
                return null;
            }
        }

        private static bool HasPendingMissionDeaths()
        {
            foreach (var kvp in RosterRotationState.Records)
            {
                var rec = kvp.Value;
                if (rec == null) continue;
                if (!rec.PendingMissionDeath) continue;
                if (!rec.DiedOnMission) continue;
                if (rec.DeathUT <= 0) continue;
                return true;
            }
            return false;
        }

        private static bool TryApplyPendingMissionDeaths(ConfigNode root, string phase)
        {
            if (root == null) return false;

            bool changed = false;
            foreach (var kvp in new List<KeyValuePair<string, RosterRotationState.KerbalRecord>>(RosterRotationState.Records))
            {
                string kerbalName = kvp.Key;
                var rec = kvp.Value;
                if (rec == null) continue;
                if (!rec.PendingMissionDeath || !rec.DiedOnMission || rec.DeathUT <= 0) continue;

                bool rosterPatched = MarkKerbalDeadInRosterNode(root, kerbalName);
                int crewRefsRemoved = RemoveKerbalFromSavedVesselCrew(root, kerbalName);
                bool recordPatched = UpdateMissionDeathRecordNodes(root, kerbalName, rec);
                bool livePatched = MarkLiveKerbalDead(kerbalName);

                if (rosterPatched || crewRefsRemoved > 0 || recordPatched || livePatched)
                {
                    changed = true;
                    rec.PendingMissionDeath = false;
                    RRLog.Verbose($"[RetiredCleanup] Applied mission-death save patch for {kerbalName} during {phase}. rosterPatched={rosterPatched}, crewRefsRemoved={crewRefsRemoved}, recordPatched={recordPatched}, livePatched={livePatched}.");
                    RRLog.AuditPurge($"[RetiredCleanup] mission-death patch {kerbalName} during {phase}: rosterPatched={rosterPatched}, crewRefsRemoved={crewRefsRemoved}, recordPatched={recordPatched}, livePatched={livePatched}.");
                }
                else
                {
                    RRLog.Warn($"[RetiredCleanup] Mission-death save patch could not find writable save nodes for {kerbalName} during {phase}; leaving patch pending.");
                }
            }

            return changed;
        }

        private static bool MarkKerbalDeadInRosterNode(ConfigNode root, string kerbalName)
        {
            bool patched = false;
            foreach (ConfigNode roster in FindNodesRecursive(root, "ROSTER"))
            {
                foreach (ConfigNode kerbal in roster.GetNodes("KERBAL"))
                {
                    if (!string.Equals(kerbal.GetValue("name"), kerbalName, StringComparison.Ordinal)) continue;
                    SetOrAddValue(kerbal, "state", "Dead");
                    if (kerbal.HasValue("status"))
                        SetOrAddValue(kerbal, "status", "Dead");
                    if (kerbal.HasValue("seatIdx"))
                        SetOrAddValue(kerbal, "seatIdx", "-1");
                    patched = true;
                }
            }
            return patched;
        }

        private static bool MarkLiveKerbalDead(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName)) return false;

            try
            {
                var roster = HighLogic.CurrentGame?.CrewRoster;
                if (roster == null) return false;

                for (int i = 0; i < roster.Count; i++)
                {
                    ProtoCrewMember pcm;
                    try { pcm = roster[i]; } catch { continue; }
                    if (pcm == null) continue;
                    if (!string.Equals(pcm.name, kerbalName, StringComparison.Ordinal)) continue;
                    if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead) return false;

                    try { pcm.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch { return false; }
                    return true;
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RetiredCleanup] MarkLiveKerbalDead failed for " + kerbalName + ": " + ex.Message);
            }

            return false;
        }


        private static int RemoveKerbalFromSavedVesselCrew(ConfigNode root, string kerbalName)
        {
            int removed = 0;
            foreach (ConfigNode part in FindNodesRecursive(root, "PART"))
            {
                var keepCrew = new List<string>();
                bool touched = false;
                foreach (ConfigNode.Value value in part.values)
                {
                    if (value == null) continue;
                    if (!string.Equals(value.name, "crew", StringComparison.OrdinalIgnoreCase)) continue;
                    touched = true;
                    if (string.Equals(value.value, kerbalName, StringComparison.Ordinal))
                    {
                        removed++;
                        continue;
                    }
                    keepCrew.Add(value.value);
                }

                if (!touched) continue;
                part.RemoveValues("crew");
                foreach (string keep in keepCrew)
                    part.AddValue("crew", keep);
            }

            foreach (ConfigNode vessel in FindNodesRecursive(root, "VESSEL"))
                RecomputeSavedVesselCrewCounts(vessel);

            return removed;
        }

        private static void RecomputeSavedVesselCrewCounts(ConfigNode vessel)
        {
            if (vessel == null) return;

            int totalCrew = 0;
            int crewedParts = 0;
            foreach (ConfigNode part in FindNodesRecursive(vessel, "PART"))
            {
                int partCrew = 0;
                foreach (ConfigNode.Value value in part.values)
                {
                    if (value == null) continue;
                    if (!string.Equals(value.name, "crew", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(value.value)) continue;
                    partCrew++;
                }
                totalCrew += partCrew;
                if (partCrew > 0) crewedParts++;
            }

            TrySetNumericValueIfPresent(vessel, "crewCount", totalCrew);
            TrySetNumericValueIfPresent(vessel, "vesselCrew", totalCrew);
            TrySetNumericValueIfPresent(vessel, "crewedParts", crewedParts);
        }

        private static void TrySetNumericValueIfPresent(ConfigNode node, string valueName, int value)
        {
            if (node == null || string.IsNullOrEmpty(valueName) || !node.HasValue(valueName)) return;
            string existing = node.GetValue(valueName);
            if (!int.TryParse(existing, out _)) return;
            SetOrAddValue(node, valueName, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static bool UpdateMissionDeathRecordNodes(ConfigNode root, string kerbalName, RosterRotationState.KerbalRecord rec)
        {
            bool updated = false;
            foreach (ConfigNode scenario in FindNodesRecursive(root, "SCENARIO"))
            {
                updated |= UpdateRecordNodesInRoot(scenario, kerbalName, rec, "EAC");
                updated |= UpdateRecordNodesInRoot(scenario, kerbalName, rec, "RosterRotation");

                string scenarioName = scenario.GetValue("name");
                string moduleName = scenario.GetValue("module");
                bool looksLikeOwnLegacyScenario =
                    string.Equals(scenarioName, "EACScenario", StringComparison.Ordinal) ||
                    string.Equals(moduleName, "EACScenario", StringComparison.Ordinal) ||
                    string.Equals(scenarioName, "RosterRotation", StringComparison.Ordinal) ||
                    string.Equals(moduleName, "RosterRotation", StringComparison.Ordinal);
                if (looksLikeOwnLegacyScenario)
                    updated |= UpdateDirectRecordNodes(scenario, kerbalName, rec);
            }
            return updated;
        }

        private static bool UpdateRecordNodesInRoot(ConfigNode scenario, string kerbalName, RosterRotationState.KerbalRecord rec, string rootNodeName)
        {
            if (scenario == null || !scenario.HasNode(rootNodeName)) return false;
            return UpdateDirectRecordNodes(scenario.GetNode(rootNodeName), kerbalName, rec);
        }

        private static bool UpdateDirectRecordNodes(ConfigNode node, string kerbalName, RosterRotationState.KerbalRecord rec)
        {
            if (node == null || string.IsNullOrEmpty(kerbalName) || rec == null) return false;
            bool updated = false;
            foreach (ConfigNode recNode in node.GetNodes("Record"))
            {
                if (!string.Equals(recNode.GetValue("name"), kerbalName, StringComparison.Ordinal)) continue;
                SetOrAddValue(recNode, "deathUT", rec.DeathUT.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                SetOrAddValue(recNode, "diedOnMission", bool.TrueString);
                SetOrAddValue(recNode, "pendingMissionDeath", bool.FalseString);
                updated = true;
            }
            return updated;
        }

        private static void SetOrAddValue(ConfigNode node, string name, string value)
        {
            if (node == null || string.IsNullOrEmpty(name)) return;
            if (node.HasValue(name)) node.SetValue(name, value, true);
            else node.AddValue(name, value);
        }

        private static bool SaveRootLooksComplete(ConfigNode root)
        {
            if (root == null) return false;
            bool hasRoster = false;
            bool hasScenario = false;

            foreach (ConfigNode _ in FindNodesRecursive(root, "ROSTER")) { hasRoster = true; break; }
            foreach (ConfigNode _ in FindNodesRecursive(root, "SCENARIO")) { hasScenario = true; break; }

            return hasRoster && hasScenario;
        }

        private static Dictionary<string, string> CollectCandidates(ConfigNode root)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            double nowUT = Planetarium.GetUniversalTime();

            foreach (string name in CollectDueRetiredKerbalNames(nowUT))
                result[name] = "retired";

            foreach (string name in CollectDeadKerbalNamesFromRosterNode(root))
                if (!result.ContainsKey(name))
                    result[name] = "dead";

            return result;
        }

        private static IEnumerable<string> CollectDueRetiredKerbalNames(double nowUT)
        {
            var result = new List<string>();
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return result;

            foreach (ProtoCrewMember k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (!IsRetiredPurgeDue(k, rec, nowUT)) continue;
                result.Add(k.name);
            }
            return result;
        }

        private static IEnumerable<string> CollectDeadKerbalNamesFromRosterNode(ConfigNode root)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode roster in FindNodesRecursive(root, "ROSTER"))
            {
                foreach (ConfigNode kerbal in roster.GetNodes("KERBAL"))
                {
                    string state = kerbal.GetValue("state");
                    if (!string.Equals(state, "Dead", StringComparison.OrdinalIgnoreCase)) continue;

                    string name = kerbal.GetValue("name");
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!IsDeadPurgeEligible(root, name)) continue;
                    result.Add(name);
                }
            }
            return result;
        }

        private static bool IsDeadPurgeEligible(ConfigNode root, string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName)) return false;

            if (RosterRotationState.Records.TryGetValue(kerbalName, out var rec))
                return IsRetiredDeath(rec);

            if (TryGetSavedDeathState(root, kerbalName, out bool retired, out double retiredUT, out double deathUT))
                return retired && deathUT > 0 && retiredUT > 0 && deathUT >= retiredUT - 1.0;

            return false;
        }

        private static bool IsRetiredDeath(RosterRotationState.KerbalRecord rec)
        {
            if (rec == null) return false;
            return rec.Retired && rec.DeathUT > 0 && rec.RetiredUT > 0 && rec.DeathUT >= rec.RetiredUT - 1.0;
        }

        private static bool TryGetSavedDeathState(ConfigNode root, string kerbalName,
            out bool retired, out double retiredUT, out double deathUT)
        {
            retired = false;
            retiredUT = 0;
            deathUT = 0;
            if (root == null || string.IsNullOrEmpty(kerbalName)) return false;

            foreach (ConfigNode container in FindNodesRecursive(root, "EAC"))
            {
                if (TryReadDeathStateFromContainer(container, kerbalName, out retired, out retiredUT, out deathUT))
                    return true;
            }

            foreach (ConfigNode container in FindNodesRecursive(root, "RosterRotation"))
            {
                if (TryReadDeathStateFromContainer(container, kerbalName, out retired, out retiredUT, out deathUT))
                    return true;
            }

            return false;
        }

        private static bool TryReadDeathStateFromContainer(ConfigNode container, string kerbalName,
            out bool retired, out double retiredUT, out double deathUT)
        {
            retired = false;
            retiredUT = 0;
            deathUT = 0;
            if (container == null || string.IsNullOrEmpty(kerbalName)) return false;

            foreach (ConfigNode record in container.GetNodes("Record"))
            {
                if (!string.Equals(record.GetValue("name"), kerbalName, StringComparison.Ordinal)) continue;

                retired = ParseBool(record.GetValue("retired"));
                retiredUT = ParseDouble(record.GetValue("retiredUT"));
                deathUT = ParseDouble(record.GetValue("deathUT"));
                return true;
            }

            return false;
        }

        private static bool TryFindStockReference(ConfigNode root, string kerbalName, out string referenceSource)
        {
            referenceSource = null;
            if (string.IsNullOrEmpty(kerbalName)) return false;

            foreach (ConfigNode scenario in FindNodesRecursive(root, "SCENARIO"))
            {
                string scenarioName = scenario.GetValue("name");
                if (string.IsNullOrEmpty(scenarioName)) continue;
                if (!StockScenarioNames.Contains(scenarioName)) continue;
                if (NodeContainsKerbalName(scenario, kerbalName))
                {
                    referenceSource = $"SCENARIO/{scenarioName}";
                    return true;
                }
            }

            foreach (ConfigNode vessel in FindNodesRecursive(root, "VESSEL"))
            {
                if (!NodeContainsKerbalName(vessel, kerbalName)) continue;
                string vesselName = vessel.GetValue("name");
                referenceSource = string.IsNullOrEmpty(vesselName)
                    ? "VESSEL"
                    : $"VESSEL/{vesselName}";
                return true;
            }

            return false;
        }

        private static IEnumerable<ConfigNode> FindNodesRecursive(ConfigNode node, string nodeName)
        {
            if (node == null) yield break;
            if (string.Equals(node.name, nodeName, StringComparison.Ordinal))
                yield return node;

            foreach (ConfigNode child in node.nodes)
            {
                foreach (ConfigNode match in FindNodesRecursive(child, nodeName))
                    yield return match;
            }
        }

        private static bool NodeContainsKerbalName(ConfigNode node, string kerbalName)
        {
            if (node == null || string.IsNullOrEmpty(kerbalName)) return false;

            foreach (ConfigNode.Value value in node.values)
            {
                if (value == null) continue;
                if (ValueMatchesKerbalName(value.value, kerbalName)) return true;
            }

            foreach (ConfigNode child in node.nodes)
            {
                if (NodeContainsKerbalName(child, kerbalName)) return true;
            }

            return false;
        }

        private static bool ValueMatchesKerbalName(string value, string kerbalName)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(kerbalName)) return false;
            if (string.Equals(value, kerbalName, StringComparison.Ordinal)) return true;

            string[] tokens = value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (string.Equals(tokens[i].Trim(), kerbalName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out bool parsed) && parsed;
        }

        private static double ParseDouble(string value)
        {
            return double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                ? parsed : 0;
        }

        private static bool RemoveKerbalFromRosterNode(ConfigNode root, string kerbalName)
        {
            bool removed = false;
            foreach (ConfigNode roster in FindNodesRecursive(root, "ROSTER"))
            {
                var toRemove = new List<ConfigNode>();
                foreach (ConfigNode kerbal in roster.GetNodes("KERBAL"))
                {
                    if (string.Equals(kerbal.GetValue("name"), kerbalName, StringComparison.Ordinal))
                        toRemove.Add(kerbal);
                }
                foreach (ConfigNode kerbal in toRemove)
                {
                    roster.RemoveNode(kerbal);
                    removed = true;
                }
            }
            return removed;
        }

        private static bool RemoveKerbalRecordNode(ConfigNode root, string kerbalName)
        {
            bool removed = false;
            foreach (ConfigNode scenario in FindNodesRecursive(root, "SCENARIO"))
            {
                removed |= RemoveRecordNodesInRoot(scenario, kerbalName, "EAC");
                removed |= RemoveRecordNodesInRoot(scenario, kerbalName, "RosterRotation");

                string scenarioName = scenario.GetValue("name");
                string moduleName = scenario.GetValue("module");
                bool looksLikeOwnLegacyScenario =
                    string.Equals(scenarioName, "EACScenario", StringComparison.Ordinal) ||
                    string.Equals(moduleName, "EACScenario", StringComparison.Ordinal) ||
                    string.Equals(scenarioName, "RosterRotation", StringComparison.Ordinal) ||
                    string.Equals(moduleName, "RosterRotation", StringComparison.Ordinal);

                if (looksLikeOwnLegacyScenario)
                    removed |= RemoveDirectRecordNodes(scenario, kerbalName);
            }
            return removed;
        }

        private static bool RemoveRecordNodesInRoot(ConfigNode scenario, string kerbalName, string rootNodeName)
        {
            if (scenario == null || !scenario.HasNode(rootNodeName)) return false;
            return RemoveDirectRecordNodes(scenario.GetNode(rootNodeName), kerbalName);
        }

        private static bool RemoveDirectRecordNodes(ConfigNode node, string kerbalName)
        {
            if (node == null) return false;
            bool removed = false;
            var toRemove = new List<ConfigNode>();
            foreach (ConfigNode recNode in node.GetNodes("Record"))
            {
                if (string.Equals(recNode.GetValue("name"), kerbalName, StringComparison.Ordinal))
                    toRemove.Add(recNode);
            }
            foreach (ConfigNode recNode in toRemove)
            {
                node.RemoveNode(recNode);
                removed = true;
            }
            return removed;
        }

        private static void RemoveLiveKerbal(string kerbalName)
        {
            try
            {
                var roster = HighLogic.CurrentGame?.CrewRoster;
                if (roster == null || string.IsNullOrEmpty(kerbalName)) return;

                ProtoCrewMember target = null;
                foreach (ProtoCrewMember k in roster.Crew)
                {
                    if (k == null) continue;
                    if (!string.Equals(k.name, kerbalName, StringComparison.Ordinal)) continue;
                    target = k;
                    break;
                }
                if (target == null) return;

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo removePCM = roster.GetType().GetMethod("Remove", flags, null,
                    new[] { typeof(ProtoCrewMember) }, null);
                if (removePCM != null)
                {
                    removePCM.Invoke(roster, new object[] { target });
                    return;
                }

                MethodInfo removeString = roster.GetType().GetMethod("Remove", flags, null,
                    new[] { typeof(string) }, null);
                if (removeString != null)
                {
                    removeString.Invoke(roster, new object[] { kerbalName });
                    return;
                }

                FieldInfo crewField = roster.GetType().GetField("Crew", flags);
                if (crewField != null)
                {
                    var list = crewField.GetValue(roster) as IList;
                    if (list != null) list.Remove(target);
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RetiredCleanup] RemoveLiveKerbal failed for " + kerbalName + ": " + ex.Message);
            }
        }
    }
}

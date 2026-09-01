using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RosterRotation
{
    /// <summary>
    /// External storage for retired/lost Kerbals.
    ///
    /// Each KSP save snapshot carries only small ArchivedRosterRef nodes. The bulky
    /// KERBAL and EAC Record payloads live in roster-archive.cfg, keyed by a content
    /// hash. This lets persistent.sfs, quicksaves, and older named saves reference the
    /// exact roster versions they need without a newer archive snapshot bleeding into
    /// an older save.
    /// </summary>
    internal static class EACRosterArchive
    {
        private const string RootNodeName = "EAC_ROSTER_ARCHIVE";
        private const string EntryNodeName = "ArchivedKerbal";
        internal const string RefNodeName = "ArchivedRosterRef";
        private const string ArchiveVersion = "2";

        private static readonly HashSet<string> StockScenarioNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ProgressTracking",
                "ContractSystem"
            };

        // Active references are captured from the save being loaded. Never restore an
        // archive entry merely because it exists in the external file.
        private static readonly Dictionary<string, string> ActiveReferences =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private sealed class ArchivePlan
        {
            internal string Name;
            internal string Reason;
            internal string Id;
        }

        internal static string ArchivePath
        {
            get
            {
                try
                {
                    string saveFolder = HighLogic.SaveFolder;
                    if (string.IsNullOrEmpty(saveFolder))
                        saveFolder = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Title : null;
                    if (string.IsNullOrEmpty(saveFolder) || string.IsNullOrEmpty(KSPUtil.ApplicationRootPath))
                        return null;
                    return Path.Combine(KSPUtil.ApplicationRootPath, "saves", saveFolder, "EAC", "roster-archive.cfg");
                }
                catch
                {
                    return null;
                }
            }
        }

        internal static int CaptureActiveReferences(ConfigNode sourceNode)
        {
            ActiveReferences.Clear();
            if (sourceNode == null) return 0;

            foreach (ConfigNode reference in FindNodesRecursive(sourceNode, RefNodeName))
            {
                string id = reference.GetValue("id") ?? "";
                string name = reference.GetValue("name") ?? "";
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
                ActiveReferences[id] = name;
            }
            return ActiveReferences.Count;
        }

        internal static int MergeArchivedRecordsIntoState(ConfigNode referenceSource = null)
        {
            if (!RosterRotationState.ExternalRosterArchiveEnabled) return 0;
            if (referenceSource != null) CaptureActiveReferences(referenceSource);
            if (ActiveReferences.Count == 0) return 0;

            ConfigNode archiveRoot = LoadArchiveRoot();
            if (archiveRoot == null) return 0;

            int merged = 0;
            foreach (KeyValuePair<string, string> reference in ActiveReferences)
            {
                ConfigNode entry = FindArchiveEntryById(archiveRoot, reference.Key);
                if (entry == null)
                {
                    RRLog.Warn("[RosterArchive] Save references missing archive id " + reference.Key
                        + " for " + reference.Value + ".");
                    continue;
                }

                ConfigNode recordNode = entry.GetNode("Record");
                if (recordNode == null) continue;

                string name;
                RosterRotationState.KerbalRecord rec;
                if (!KerbalRecordPersistence.TryReadRecord(recordNode, out name, out rec)) continue;
                if (string.IsNullOrEmpty(name)) name = reference.Value;
                if (string.IsNullOrEmpty(name) || RosterRotationState.Records.ContainsKey(name)) continue;

                RosterRotationState.Records[name] = rec;
                merged++;
            }

            if (merged > 0)
            {
                RosterRotationState.InvalidateRetiredCache();
                RRLog.Info("[RosterArchive] Restored " + merged + " EAC record(s) referenced by this save.");
            }
            return merged;
        }

        internal static int RestoreArchivedKerbalsToRoster()
        {
            if (!RosterRotationState.ExternalRosterArchiveEnabled || ActiveReferences.Count == 0) return 0;

            Game game = HighLogic.CurrentGame;
            KerbalRoster roster = game != null ? game.CrewRoster : null;
            if (game == null || roster == null) return 0;

            ConfigNode archiveRoot = LoadArchiveRoot();
            if (archiveRoot == null) return 0;

            int restored = 0;
            foreach (KeyValuePair<string, string> reference in ActiveReferences)
            {
                if (FindRosterKerbal(roster, reference.Value) != null) continue;

                ConfigNode entry = FindArchiveEntryById(archiveRoot, reference.Key);
                ConfigNode kerbalNode = entry != null ? entry.GetNode("KERBAL") : null;
                if (kerbalNode == null)
                {
                    RRLog.Warn("[RosterArchive] Could not rehydrate " + reference.Value
                        + ": archive payload " + reference.Key + " is missing.");
                    continue;
                }

                try
                {
                    ProtoCrewMember pcm = new ProtoCrewMember(game.Mode, kerbalNode, ProtoCrewMember.KerbalType.Crew);
                    if (roster.AddCrewMember(pcm))
                    {
                        restored++;
                        RRLog.Verbose("[RosterArchive] Rehydrated " + reference.Value + " into the live roster.");
                    }
                    else
                    {
                        RRLog.Warn("[RosterArchive] Stock roster rejected archived Kerbal " + reference.Value + ".");
                    }
                }
                catch (Exception ex)
                {
                    RRLog.Warn("[RosterArchive] Could not rehydrate " + reference.Value + ": " + ex.Message);
                }
            }

            if (restored > 0)
            {
                RosterRotationState.InvalidateRetiredCache();
                RosterRotationKSCUI.RequestUiRefresh("roster archive restore");
                RRLog.Info("[RosterArchive] Rehydrated " + restored + " Kerbal(s) referenced by this save.");
            }
            return restored;
        }

        /// <summary>
        /// Archives eligible KERBAL/Record pairs, writes the external archive first,
        /// then replaces their save payload with small ArchivedRosterRef nodes.
        /// Returns true when the supplied save tree was changed.
        /// </summary>
        internal static bool ArchiveAndStrip(ConfigNode saveRoot, string phase, out int archivedCount)
        {
            archivedCount = 0;
            if (saveRoot == null || !RosterRotationState.ExternalRosterArchiveEnabled) return false;

            ConfigNode eacRoot = FindEacDataRoot(saveRoot);
            if (eacRoot == null)
            {
                RRLog.Warn("[RosterArchive] Could not locate the EAC scenario data during " + phase
                    + "; roster nodes were left in the save.");
                return false;
            }

            Dictionary<string, string> candidates = CollectCandidates(saveRoot);
            HashSet<string> presentRosterNames = CollectRosterNames(saveRoot);
            var plans = new List<ArchivePlan>();
            ConfigNode archiveRoot = LoadArchiveRoot() ?? new ConfigNode(RootNodeName);
            bool archiveDirty = !IsUsableArchiveFile(ArchivePath);
            if (!string.Equals(archiveRoot.GetValue("version"), ArchiveVersion, StringComparison.Ordinal))
            {
                SetOrAddValue(archiveRoot, "version", ArchiveVersion);
                archiveDirty = true;
            }

            foreach (KeyValuePair<string, string> candidate in candidates)
            {
                string name = candidate.Key;
                string reason = candidate.Value;

                string referenceSource;
                if (TryFindStockReference(saveRoot, name, out referenceSource))
                {
                    RRLog.Verbose("[RosterArchive] Keeping " + name + " in the save during " + phase
                        + ": referenced by " + referenceSource + ".");
                    continue;
                }

                ConfigNode savedKerbal = FindSavedKerbalNode(saveRoot, name);
                if (savedKerbal == null)
                {
                    RRLog.Warn("[RosterArchive] Could not archive " + name + " during " + phase
                        + ": no KERBAL node was found. The save was left unchanged for that Kerbal.");
                    continue;
                }

                ConfigNode archivedKerbal = CloneNode(savedKerbal);
                ConfigNode archivedRecord = BuildRecordNode(saveRoot, name);
                string id = ComputeArchiveId(name, archivedKerbal, archivedRecord);
                if (string.IsNullOrEmpty(id)) continue;

                if (UpsertArchiveEntryById(archiveRoot, id, name, reason, archivedKerbal, archivedRecord))
                    archiveDirty = true;
                plans.Add(new ArchivePlan
                {
                    Name = name,
                    Reason = reason,
                    Id = id
                });
            }

            // The external payload must be durable before any KERBAL nodes are stripped.
            // Existing content-addressed payloads do not need to be rewritten on every
            // KSP save; only write when a new payload/version is actually introduced.
            if (plans.Count > 0 && archiveDirty)
            {
                SetOrAddValue(archiveRoot, "lastWriteUT", Planetarium.GetUniversalTime().ToString("R", CultureInfo.InvariantCulture));
                if (!SaveArchiveAtomically(archiveRoot, ArchivePath))
                {
                    RRLog.Error("[RosterArchive] Archive write failed; no save roster nodes were stripped.");
                    return false;
                }
            }

            bool changed = false;

            // Any Kerbal present in the stock roster will either receive a fresh ref below
            // or is now stored normally (recalled, referenced by stock, etc.). Remove stale
            // refs for those names. Refs whose Kerbal is absent are preserved as a safety
            // path in case a previous load could not rehydrate that entry.
            foreach (string name in presentRosterNames)
                changed |= RemoveSaveReferences(eacRoot, name);

            for (int i = 0; i < plans.Count; i++)
            {
                ArchivePlan plan = plans[i];
                AddSaveReference(eacRoot, plan.Name, plan.Id, plan.Reason);
                changed = true;

                bool rosterRemoved = RemoveKerbalFromRosterNode(saveRoot, plan.Name);
                bool recordRemoved = RemoveKerbalRecordNode(saveRoot, plan.Name);
                if (rosterRemoved)
                {
                    archivedCount++;
                    RRLog.AuditPurge("[RosterArchive] ARCHIVE " + plan.Name + " during " + phase
                        + " - id=" + plan.Id + ", rosterRemoved=True, recordRemoved=" + recordRemoved + ".");
                }
            }

            if (archivedCount > 0)
                RRLog.Info("[RosterArchive] Archived " + archivedCount + " retired/lost Kerbal(s) during " + phase + ".");

            // Keep archive growth bounded. Avoid a full .sfs reference scan on routine
            // saves once the archive is already near the current-reference + safety size.
            // Any .sfs anywhere under this career that still references an older id will
            // protect that payload when a prune actually runs.
            int archiveEntries = archiveRoot.GetNodes(EntryNodeName).Length;
            int currentReferences = eacRoot.GetNodes(RefNodeName).Length;
            if (archiveDirty || archiveEntries > currentReferences + 3)
                PruneUnreferencedArchiveEntries(eacRoot);
            return changed;
        }

        internal static bool PreserveActiveReferences(ConfigNode eacRoot)
        {
            if (eacRoot == null || ActiveReferences.Count == 0) return false;
            bool changed = false;
            foreach (KeyValuePair<string, string> reference in ActiveReferences)
            {
                if (HasSaveReference(eacRoot, reference.Key)) continue;
                AddSaveReference(eacRoot, reference.Value, reference.Key, "loaded");
                changed = true;
            }
            return changed;
        }

        internal static bool ClearSaveReferences(ConfigNode saveRoot)
        {
            ConfigNode eacRoot = FindEacDataRoot(saveRoot);
            if (eacRoot == null) return false;
            bool changed = false;
            ConfigNode[] refs = eacRoot.GetNodes(RefNodeName);
            for (int i = 0; i < refs.Length; i++)
            {
                eacRoot.RemoveNode(refs[i]);
                changed = true;
            }
            return changed;
        }

        internal static bool SaveRootLooksComplete(ConfigNode root)
        {
            if (root == null || FindEacDataRoot(root) == null) return false;
            foreach (ConfigNode unused in FindNodesRecursive(root, "ROSTER")) return true;
            return false;
        }

        private static Dictionary<string, string> CollectCandidates(ConfigNode root)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ConfigNode roster in FindNodesRecursive(root, "ROSTER"))
            {
                foreach (ConfigNode kerbal in roster.GetNodes("KERBAL"))
                {
                    string name = kerbal.GetValue("name");
                    if (string.IsNullOrEmpty(name)) continue;
                    string state = kerbal.GetValue("state") ?? kerbal.GetValue("status") ?? "";

                    ConfigNode savedRecord = FindSavedRecordNode(root, name);
                    RosterRotationState.KerbalRecord liveRecord = null;
                    RosterRotationState.Records.TryGetValue(name, out liveRecord);

                    bool pendingMissionDeath = savedRecord != null
                        ? ReadBool(savedRecord.GetValue("pendingMissionDeath"), false)
                        : liveRecord != null && liveRecord.PendingMissionDeath;
                    if (pendingMissionDeath) continue;

                    if (state.Equals("Assigned", StringComparison.OrdinalIgnoreCase)) continue;

                    if (state.Equals("Dead", StringComparison.OrdinalIgnoreCase) ||
                        state.Equals("Missing", StringComparison.OrdinalIgnoreCase))
                    {
                        result[name] = "lost";
                        continue;
                    }

                    bool retired = savedRecord != null
                        ? ReadBool(savedRecord.GetValue("retired"), false)
                        : liveRecord != null && liveRecord.Retired;
                    if (retired) result[name] = "retired";
                }
            }
            return result;
        }

        private static HashSet<string> CollectRosterNames(ConfigNode root)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode roster in FindNodesRecursive(root, "ROSTER"))
            {
                foreach (ConfigNode kerbal in roster.GetNodes("KERBAL"))
                {
                    string name = kerbal.GetValue("name");
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
            }
            return names;
        }

        private static ConfigNode BuildRecordNode(ConfigNode saveRoot, string name)
        {
            ConfigNode saved = FindSavedRecordNode(saveRoot, name);
            if (saved != null) return CloneNode(saved);

            RosterRotationState.KerbalRecord rec;
            if (RosterRotationState.Records.TryGetValue(name, out rec) && rec != null)
            {
                ConfigNode node = new ConfigNode("Record");
                KerbalRecordPersistence.WriteRecordNode(node, name, rec, CultureInfo.InvariantCulture);
                return node;
            }
            return null;
        }

        private static bool UpsertArchiveEntryById(
            ConfigNode archiveRoot,
            string id,
            string name,
            string reason,
            ConfigNode kerbalNode,
            ConfigNode recordNode)
        {
            if (FindArchiveEntryById(archiveRoot, id) != null) return false;

            ConfigNode entry = archiveRoot.AddNode(EntryNodeName);
            entry.AddValue("id", id);
            entry.AddValue("name", name);
            entry.AddValue("reason", reason ?? "");
            entry.AddValue("archivedUT", Planetarium.GetUniversalTime().ToString("R", CultureInfo.InvariantCulture));
            CopyNodeContents(kerbalNode, entry.AddNode("KERBAL"));
            if (recordNode != null)
                CopyNodeContents(recordNode, entry.AddNode("Record"));
            return true;
        }

        private static void AddSaveReference(ConfigNode eacRoot, string name, string id, string reason)
        {
            ConfigNode reference = eacRoot.AddNode(RefNodeName);
            reference.AddValue("id", id);
            reference.AddValue("name", name);
            reference.AddValue("reason", reason ?? "");
        }

        private static bool HasSaveReference(ConfigNode eacRoot, string id)
        {
            if (eacRoot == null || string.IsNullOrEmpty(id)) return false;
            foreach (ConfigNode reference in eacRoot.GetNodes(RefNodeName))
                if (string.Equals(reference.GetValue("id"), id, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool RemoveSaveReferences(ConfigNode eacRoot, string name)
        {
            if (eacRoot == null || string.IsNullOrEmpty(name)) return false;
            var remove = new List<ConfigNode>();
            foreach (ConfigNode reference in eacRoot.GetNodes(RefNodeName))
                if (string.Equals(reference.GetValue("name"), name, StringComparison.Ordinal))
                    remove.Add(reference);
            for (int i = 0; i < remove.Count; i++) eacRoot.RemoveNode(remove[i]);
            return remove.Count > 0;
        }

        private static ConfigNode FindArchiveEntryById(ConfigNode archiveRoot, string id)
        {
            if (archiveRoot == null || string.IsNullOrEmpty(id)) return null;
            foreach (ConfigNode entry in archiveRoot.GetNodes(EntryNodeName))
                if (string.Equals(entry.GetValue("id"), id, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }

        private static string ComputeArchiveId(string name, ConfigNode kerbalNode, ConfigNode recordNode)
        {
            try
            {
                // Hash the serialized payload in its original order. Flight/career logs can
                // contain repeated keys where order is meaningful, so do not canonicalize
                // child/value ordering before identifying the snapshot.
                var snapshot = new StringBuilder(4096);
                snapshot.Append("name=").Append(name ?? "").Append('\n');
                if (kerbalNode != null) snapshot.Append(kerbalNode.ToString());
                snapshot.Append("\n--EAC-RECORD--\n");
                if (recordNode != null) snapshot.Append(recordNode.ToString());

                return EACHashing.ComputeSha256Hex(snapshot.ToString());
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Could not compute archive id for " + name + ": " + ex.Message);
                return null;
            }
        }

        internal static void PruneUnreferencedArchiveEntries(ConfigNode currentEacRoot)
        {
            string path = ArchivePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                HashSet<string> referenced;
                string failure;
                if (!TryCollectArchiveReferencesFromSaveFiles(out referenced, out failure))
                {
                    RRLog.Warn("[RosterArchive] Archive cleanup skipped because save references could not be scanned safely"
                        + (string.IsNullOrEmpty(failure) ? "." : ": " + failure));
                    return;
                }

                if (currentEacRoot != null)
                {
                    foreach (ConfigNode reference in currentEacRoot.GetNodes(RefNodeName))
                    {
                        string id = reference.GetValue("id");
                        if (!string.IsNullOrEmpty(id)) referenced.Add(id);
                    }
                }

                ConfigNode archiveRoot = LoadArchiveRoot();
                if (archiveRoot == null) return;

                ConfigNode[] entries = archiveRoot.GetNodes(EntryNodeName);
                const int safetyEntries = 3;
                int safetyKept = 0;
                int removed = 0;

                // Entries are append-only, so walk newest-to-oldest and preserve a
                // small unreferenced rollback cushion in addition to every referenced id.
                for (int i = entries.Length - 1; i >= 0; i--)
                {
                    ConfigNode entry = entries[i];
                    string id = entry != null ? entry.GetValue("id") : null;
                    if (!string.IsNullOrEmpty(id) && referenced.Contains(id)) continue;

                    if (safetyKept < safetyEntries)
                    {
                        safetyKept++;
                        continue;
                    }

                    archiveRoot.RemoveNode(entry);
                    removed++;
                }

                if (removed <= 0) return;

                SetOrAddValue(archiveRoot, "lastWriteUT", Planetarium.GetUniversalTime().ToString("R", CultureInfo.InvariantCulture));
                if (SaveArchiveAtomically(archiveRoot, path))
                    RRLog.Info("[RosterArchive] Archive cleanup removed " + removed
                        + " unreferenced payload(s); " + referenced.Count
                        + " referenced/current and " + safetyKept + " safety payload(s) retained.");
                else
                    RRLog.Warn("[RosterArchive] Archive cleanup prepared " + removed
                        + " removal(s), but the archive rewrite failed; the prior archive remains available.");
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Archive cleanup skipped after unexpected error: " + ex.Message);
            }
        }

        private static bool TryCollectArchiveReferencesFromSaveFiles(out HashSet<string> ids, out string failure)
        {
            ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string persistent = PersistentSavePath;
            string saveRoot = !string.IsNullOrEmpty(persistent) ? Path.GetDirectoryName(persistent) : null;
            return EACSaveFileScanner.TryCollectValues(saveRoot, RefNodeName, "id", ids, out failure);
        }

        private static ConfigNode LoadArchiveRoot()
        {
            string path = ArchivePath;
            if (string.IsNullOrEmpty(path)) return null;

            ConfigNode root = TryLoadArchiveFile(path);
            if (root != null) return root;

            ConfigNode backup = TryLoadArchiveFile(path + ".bak");
            if (backup != null)
                RRLog.Warn("[RosterArchive] Loaded backup roster archive because the primary archive was unavailable.");
            return backup;
        }

        private static ConfigNode TryLoadArchiveFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return ConfigNode.Load(path);
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Could not load " + path + ": " + ex.Message);
                return null;
            }
        }

        private static bool SaveArchiveAtomically(ConfigNode root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return false;
            string tempPath = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (!root.Save(tempPath)) return false;
                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length <= 0) return false;

                // Do not overwrite a known-good backup with a corrupt primary.
                if (File.Exists(path) && IsUsableArchiveFile(path))
                    File.Copy(path, path + ".bak", true);
                File.Copy(tempPath, path, true);
                File.Delete(tempPath);
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterArchive] Could not write " + path + ": " + ex);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
        }

        private static bool IsUsableArchiveFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                ConfigNode node = ConfigNode.Load(path);
                return node != null;
            }
            catch
            {
                return false;
            }
        }

        internal static string PersistentSavePath
        {
            get
            {
                try
                {
                    string saveFolder = HighLogic.SaveFolder;
                    if (string.IsNullOrEmpty(saveFolder))
                        saveFolder = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Title : null;
                    if (string.IsNullOrEmpty(saveFolder) || string.IsNullOrEmpty(KSPUtil.ApplicationRootPath))
                        return null;
                    return Path.Combine(KSPUtil.ApplicationRootPath, "saves", saveFolder, "persistent.sfs");
                }
                catch
                {
                    return null;
                }
            }
        }

        internal static bool SavePersistentTreeSafely(ConfigNode root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return false;
            string tempPath = path + ".eac-roster.tmp";
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (!root.Save(tempPath)) return false;
                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length <= 0) return false;

                string archiveDirectory = Path.GetDirectoryName(ArchivePath);
                if (!string.IsNullOrEmpty(archiveDirectory))
                {
                    Directory.CreateDirectory(archiveDirectory);
                    File.Copy(path, Path.Combine(archiveDirectory, "persistent-pre-roster-archive.bak.sfs"), true);
                }

                File.Copy(tempPath, path, true);
                File.Delete(tempPath);
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterArchive] Could not safely rewrite persistent.sfs after archival: " + ex);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
        }

        private static ProtoCrewMember FindRosterKerbal(KerbalRoster roster, string name)
        {
            if (roster == null || string.IsNullOrEmpty(name)) return null;
            try
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    ProtoCrewMember pcm;
                    try { pcm = roster[i]; } catch { continue; }
                    if (pcm != null && string.Equals(pcm.name, name, StringComparison.Ordinal))
                        return pcm;
                }
            }
            catch { }
            return null;
        }

        private static ConfigNode FindEacDataRoot(ConfigNode root)
        {
            if (root == null) return null;
            if (string.Equals(root.name, "EAC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(root.name, "RosterRotation", StringComparison.OrdinalIgnoreCase))
                return root;

            if (root.HasNode("EAC")) return root.GetNode("EAC");
            if (root.HasNode("RosterRotation")) return root.GetNode("RosterRotation");

            foreach (ConfigNode node in FindNodesRecursive(root, "EAC"))
                if (node.HasNode("Settings") || node.HasNode("Record") || node.HasNode(RefNodeName))
                    return node;
            foreach (ConfigNode node in FindNodesRecursive(root, "RosterRotation"))
                if (node.HasNode("Settings") || node.HasNode("Record") || node.HasNode(RefNodeName))
                    return node;
            return null;
        }

        private static ConfigNode FindSavedKerbalNode(ConfigNode root, string name)
        {
            foreach (ConfigNode roster in FindNodesRecursive(root, "ROSTER"))
                foreach (ConfigNode kerbal in roster.GetNodes("KERBAL"))
                    if (string.Equals(kerbal.GetValue("name"), name, StringComparison.Ordinal))
                        return kerbal;
            return null;
        }

        private static ConfigNode FindSavedRecordNode(ConfigNode root, string name)
        {
            foreach (ConfigNode container in FindNodesRecursive(root, "EAC"))
                foreach (ConfigNode record in container.GetNodes("Record"))
                    if (string.Equals(record.GetValue("name"), name, StringComparison.Ordinal))
                        return record;
            foreach (ConfigNode container in FindNodesRecursive(root, "RosterRotation"))
                foreach (ConfigNode record in container.GetNodes("Record"))
                    if (string.Equals(record.GetValue("name"), name, StringComparison.Ordinal))
                        return record;
            return null;
        }

        private static bool TryFindStockReference(ConfigNode root, string name, out string source)
        {
            source = null;
            foreach (ConfigNode scenario in FindNodesRecursive(root, "SCENARIO"))
            {
                string scenarioName = scenario.GetValue("name");
                if (string.IsNullOrEmpty(scenarioName) || !StockScenarioNames.Contains(scenarioName)) continue;
                if (!NodeContainsKerbalName(scenario, name)) continue;
                source = "SCENARIO/" + scenarioName;
                return true;
            }

            foreach (ConfigNode vessel in FindNodesRecursive(root, "VESSEL"))
            {
                if (!NodeContainsKerbalName(vessel, name)) continue;
                string vesselName = vessel.GetValue("name");
                source = string.IsNullOrEmpty(vesselName) ? "VESSEL" : "VESSEL/" + vesselName;
                return true;
            }
            return false;
        }

        private static bool NodeContainsKerbalName(ConfigNode node, string name)
        {
            if (node == null || string.IsNullOrEmpty(name)) return false;
            foreach (ConfigNode.Value value in node.values)
                if (value != null && string.Equals(value.value, name, StringComparison.Ordinal))
                    return true;
            foreach (ConfigNode child in node.nodes)
                if (NodeContainsKerbalName(child, name)) return true;
            return false;
        }

        private static bool RemoveKerbalFromRosterNode(ConfigNode root, string name)
        {
            bool removed = false;
            foreach (ConfigNode roster in FindNodesRecursive(root, "ROSTER"))
            {
                var remove = new List<ConfigNode>();
                foreach (ConfigNode kerbal in roster.GetNodes("KERBAL"))
                    if (string.Equals(kerbal.GetValue("name"), name, StringComparison.Ordinal))
                        remove.Add(kerbal);
                for (int i = 0; i < remove.Count; i++)
                {
                    roster.RemoveNode(remove[i]);
                    removed = true;
                }
            }
            return removed;
        }

        private static bool RemoveKerbalRecordNode(ConfigNode root, string name)
        {
            bool removed = false;
            foreach (ConfigNode container in FindNodesRecursive(root, "EAC"))
                removed |= RemoveDirectRecordNodes(container, name);
            foreach (ConfigNode container in FindNodesRecursive(root, "RosterRotation"))
                removed |= RemoveDirectRecordNodes(container, name);
            return removed;
        }

        private static bool RemoveDirectRecordNodes(ConfigNode node, string name)
        {
            bool removed = false;
            var remove = new List<ConfigNode>();
            foreach (ConfigNode record in node.GetNodes("Record"))
                if (string.Equals(record.GetValue("name"), name, StringComparison.Ordinal))
                    remove.Add(record);
            for (int i = 0; i < remove.Count; i++)
            {
                node.RemoveNode(remove[i]);
                removed = true;
            }
            return removed;
        }

        private static ConfigNode CloneNode(ConfigNode source)
        {
            if (source == null) return null;
            ConfigNode copy = new ConfigNode(source.name);
            CopyNodeContents(source, copy);
            return copy;
        }

        private static void CopyNodeContents(ConfigNode source, ConfigNode destination)
        {
            if (source == null || destination == null) return;
            foreach (ConfigNode.Value value in source.values)
                if (value != null) destination.AddValue(value.name, value.value);
            foreach (ConfigNode child in source.nodes)
            {
                ConfigNode childCopy = destination.AddNode(child.name);
                CopyNodeContents(child, childCopy);
            }
        }

        private static IEnumerable<ConfigNode> FindNodesRecursive(ConfigNode node, string nodeName)
        {
            if (node == null) yield break;
            if (string.Equals(node.name, nodeName, StringComparison.OrdinalIgnoreCase)) yield return node;
            foreach (ConfigNode child in node.nodes)
                foreach (ConfigNode match in FindNodesRecursive(child, nodeName))
                    yield return match;
        }

        private static void SetOrAddValue(ConfigNode node, string key, string value)
        {
            if (node.HasValue(key)) node.SetValue(key, value, true);
            else node.AddValue(key, value);
        }

        private static bool ReadBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal class EACRosterArchiveService : MonoBehaviour
    {
        private bool _postSavePersistentPassQueued;
        private string _postSavePersistentPath;
        private long _preSavePersistentTicks;
        private long _preSavePersistentLength;

        private void Awake()
        {
            DontDestroyOnLoad(this);
            GameEvents.onGameStateLoad.Add(OnGameStateLoad);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
        }

        private void OnDestroy()
        {
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
        }

        private void OnGameStateLoad(ConfigNode root)
        {
            EACRosterArchive.CaptureActiveReferences(root);
            StartCoroutine(RestoreAfterLoad());
        }

        private IEnumerator RestoreAfterLoad()
        {
            // CrewRoster can finish constructing after onGameStateLoad. A few short
            // frame retries avoid scene/load-order assumptions without polling forever.
            for (int i = 0; i < 6; i++)
            {
                yield return null;
                if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.CrewRoster != null)
                {
                    EACRosterArchive.MergeArchivedRecordsIntoState();
                    EACRosterArchive.RestoreArchivedKerbalsToRoster();
                    yield break;
                }
            }
        }

        private void OnGameStateSave(ConfigNode root)
        {
            if (root == null) return;

            // Reuse this already-established save callback for one-time migration of
            // pre-EACScenario saves.  A separate migration subscriber proved unreliable
            // in some KSP startup/scene-order combinations even though this service's
            // MainMenu subscription is stable.
            EACScenarioMigrationCleaner.OnGameStateSave(root);

            if (!RosterRotationState.ExternalRosterArchiveEnabled)
            {
                EACRosterArchive.ClearSaveReferences(root);
                return;
            }

            if (EACRosterArchive.SaveRootLooksComplete(root))
            {
                int archived;
                EACRosterArchive.ArchiveAndStrip(root, "save", out archived);
                return;
            }

            // KSP can fire onGameStateSave with a partial tree. The old cleanup code
            // worked around this by editing persistent.sfs after the save, but doing that
            // unconditionally can target the wrong file for quicksaves/named saves. Capture
            // the current persistent file stamp and only run the fallback if persistent.sfs
            // itself demonstrably changed after this callback.
            QueuePostSavePersistentPass();
            RRLog.Verbose("[RosterArchive] Save callback tree incomplete; queued a guarded persistent.sfs archive pass.");
        }

        private void QueuePostSavePersistentPass()
        {
            string path = EACRosterArchive.PersistentSavePath;
            if (string.IsNullOrEmpty(path)) return;

            _postSavePersistentPath = path;
            CaptureFileStamp(path, out _preSavePersistentTicks, out _preSavePersistentLength);
            if (_postSavePersistentPassQueued) return;

            _postSavePersistentPassQueued = true;
            StartCoroutine(PostSavePersistentPass());
        }

        private IEnumerator PostSavePersistentPass()
        {
            // Give KSP time to finish writing the target file. A persistent save normally
            // changes its mtime immediately; quicksaves/named saves leave persistent.sfs
            // untouched and therefore fail the guard below.
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.25f);

            try
            {
                RunPostSavePersistentPass();
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterArchive] Post-save persistent archive pass failed: " + ex);
            }

            _postSavePersistentPassQueued = false;
        }

        private void RunPostSavePersistentPass()
        {
            if (!RosterRotationState.ExternalRosterArchiveEnabled) return;
            string path = _postSavePersistentPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            long afterTicks;
            long afterLength;
            CaptureFileStamp(path, out afterTicks, out afterLength);
            if (afterTicks == _preSavePersistentTicks && afterLength == _preSavePersistentLength)
            {
                RRLog.Verbose("[RosterArchive] persistent.sfs did not change; post-save fallback skipped (likely quicksave/named save).");
                return;
            }

            ConfigNode diskRoot = ConfigNode.Load(path);
            if (diskRoot == null || !EACRosterArchive.SaveRootLooksComplete(diskRoot))
            {
                RRLog.Warn("[RosterArchive] Changed persistent.sfs could not be loaded as a complete save; post-save archival skipped.");
                return;
            }

            int archived;
            bool changed = EACRosterArchive.ArchiveAndStrip(diskRoot, "post-save-persistent", out archived);
            if (!changed) return;

            if (EACRosterArchive.SavePersistentTreeSafely(diskRoot, path))
                RRLog.Info("[RosterArchive] Post-save persistent.sfs archive pass wrote " + archived + " archived Kerbal(s).");
            else
                RRLog.Error("[RosterArchive] Post-save archival prepared changes but persistent.sfs rewrite failed; the original file was left in place when possible.");
        }

        private static void CaptureFileStamp(string path, out long ticks, out long length)
        {
            ticks = 0;
            length = -1;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                var info = new FileInfo(path);
                ticks = info.LastWriteTimeUtc.Ticks;
                length = info.Length;
            }
            catch
            {
                ticks = 0;
                length = -1;
            }
        }
    }
}

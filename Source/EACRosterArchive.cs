using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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

        // 1.6.3 hybrid cold archive. Unlike the legacy 1.6.0 archive, these entries
        // carry no save-side reference nodes and are not globally rehydrated during
        // normal gameplay. The payload is normally write-once, with one intentional
        // lifecycle mutation when a cold-retired Kerbal later dies of old age. Disabling
        // the opt-in feature deliberately restores entries to CrewRoster so the next
        // ordinary KSP save embeds them again.
        private const string ColdIndexRootNodeName = "EAC_COLD_ROSTER_INDEX";
        private const string ColdEntryRootNodeName = "EAC_COLD_ROSTER_ENTRY";
        private const string ColdIndexEntryNodeName = "ArchivedKerbal";
        private const string ColdArchiveVersion = "1";

        internal sealed class ColdRosterEntrySummary
        {
            internal string Id;
            internal string Name;
            internal string Reason;
            internal string Trait;
            internal int ExperienceLevel;
            internal float Courage;
            internal float Stupidity;
            internal double ArchivedUT;
            internal double RetiredUT;
            internal double DeathUT;
            internal bool DiedOnMission;
        }

        private sealed class ColdArchiveCandidate
        {
            internal ProtoCrewMember Live;
            internal ConfigNode KerbalNode;
        }

        private static string _coldIndexSaveKey;
        private static readonly List<ColdRosterEntrySummary> ColdIndexCache = new List<ColdRosterEntrySummary>();
        private static bool _coldIndexLoaded;
        private static string _lastColdScanSummarySignature;

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

        internal static int ActiveReferenceCount
        {
            get { return ActiveReferences.Count; }
        }

        internal static bool ActiveReferencesResolvedInLiveRoster()
        {
            if (ActiveReferences.Count == 0) return true;

            Game game = HighLogic.CurrentGame;
            KerbalRoster roster = game != null ? game.CrewRoster : null;
            if (roster == null) return false;

            foreach (KeyValuePair<string, string> reference in ActiveReferences)
                if (FindRosterKerbal(roster, reference.Value) == null)
                    return false;

            return true;
        }

        internal static void ClearActiveReferences()
        {
            ActiveReferences.Clear();
        }

        internal static int MergeArchivedRecordsIntoState(ConfigNode referenceSource = null)
        {
            // 1.6.1 hotfix: legacy archive references must still be readable so saves
            // created by 1.6.0 can migrate their retired/lost Kerbals back into the
            // stock roster. Do not gate restoration on the now-disabled archive option.
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
            // 1.6.1 hotfix: restore any legacy 1.6.0 archive references regardless of
            // the current setting. The archive feature is no longer used for new saves.
            if (ActiveReferences.Count == 0) return 0;

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

        internal static string ColdArchiveDirectory
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
                    return Path.Combine(KSPUtil.ApplicationRootPath, "saves", saveFolder, "EAC", "cold-roster");
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string ColdIndexPath
        {
            get
            {
                string directory = ColdArchiveDirectory;
                return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, "index.cfg");
            }
        }

        private static string ColdEntryPath(string id)
        {
            string directory = ColdArchiveDirectory;
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(id)) return null;
            return Path.Combine(directory, id + ".cfg");
        }

        internal static void InvalidateColdIndexCache()
        {
            _coldIndexSaveKey = null;
            _coldIndexLoaded = false;
            _lastColdScanSummarySignature = null;
            ColdIndexCache.Clear();
        }

        internal static bool EnsureColdArchiveInitialized(string phase)
        {
            string directory = ColdArchiveDirectory;
            string indexPath = ColdIndexPath;
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(indexPath))
            {
                RRLog.Warn("[RosterArchive] Cold archive could not be initialized during " + phase
                    + ": the current save path is unavailable.");
                return false;
            }

            try
            {
                Directory.CreateDirectory(directory);

                ConfigNode root = LoadColdIndexRoot();
                if (root != null
                    && File.Exists(indexPath)
                    && string.Equals(root.GetValue("version"), ColdArchiveVersion, StringComparison.Ordinal))
                {
                    EnsureColdIndexCache();
                    RRLog.Info("[RosterArchive] Cold archive enabled/available during " + phase
                        + ": " + indexPath);
                    return true;
                }

                if (root == null)
                    root = new ConfigNode(ColdIndexRootNodeName);

                if (!string.Equals(root.GetValue("version"), ColdArchiveVersion, StringComparison.Ordinal))
                    SetOrAddValue(root, "version", ColdArchiveVersion);

                if (!SaveColdIndexAtomically(root, indexPath))
                {
                    RRLog.Warn("[RosterArchive] Cold archive index could not be created during " + phase + ".");
                    return false;
                }

                RRLog.Info("[RosterArchive] Cold archive enabled/initialized during " + phase
                    + ": " + indexPath);
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Cold archive initialization failed during " + phase + ": " + ex.Message);
                return false;
            }
        }

        internal static void RemoveEmptyColdArchiveArtifacts(string phase)
        {
            EnsureColdIndexCache();
            if (ColdIndexCache.Count > 0) return;

            string indexPath = ColdIndexPath;
            string directory = ColdArchiveDirectory;
            try
            {
                if (!string.IsNullOrEmpty(indexPath))
                {
                    if (File.Exists(indexPath + ".bak")) File.Delete(indexPath + ".bak");
                    if (File.Exists(indexPath + ".tmp")) File.Delete(indexPath + ".tmp");
                    if (File.Exists(indexPath)) File.Delete(indexPath);
                }

                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)
                    && Directory.GetFileSystemEntries(directory).Length == 0)
                    Directory.Delete(directory);

                InvalidateColdIndexCache();
                RRLog.Verbose("[RosterArchive] Removed empty cold archive after " + phase + ".");
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Could not remove empty cold archive after " + phase + ": " + ex.Message);
            }
        }

        internal static List<ColdRosterEntrySummary> GetColdArchivedEntries(string reason = null, bool includeWhenDisabled = false)
        {
            var result = new List<ColdRosterEntrySummary>();
            if (!includeWhenDisabled && !RosterRotationState.ColdRosterArchiveEnabled)
                return result;

            EnsureColdIndexCache();
            for (int i = 0; i < ColdIndexCache.Count; i++)
            {
                ColdRosterEntrySummary entry = ColdIndexCache[i];
                if (entry == null) continue;
                if (!string.IsNullOrEmpty(reason) && !string.Equals(entry.Reason, reason, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(CloneColdSummary(entry));
            }
            return result;
        }

        internal static bool HasColdArchivedEntries()
        {
            EnsureColdIndexCache();
            return ColdIndexCache.Count > 0;
        }

        internal static bool AreAllColdArchivedKerbalsPresentInLiveRoster()
        {
            EnsureColdIndexCache();
            if (ColdIndexCache.Count == 0) return true;

            Game game = HighLogic.CurrentGame;
            KerbalRoster roster = game != null ? game.CrewRoster : null;
            if (roster == null) return false;

            for (int i = 0; i < ColdIndexCache.Count; i++)
            {
                ColdRosterEntrySummary entry = ColdIndexCache[i];
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                if (FindRosterKerbal(roster, entry.Name) == null)
                    return false;
            }
            return true;
        }

        internal static int MergeColdDeathLifecycleIntoState()
        {
            EnsureColdIndexCache();
            int changed = 0;

            for (int i = 0; i < ColdIndexCache.Count; i++)
            {
                ColdRosterEntrySummary entry = ColdIndexCache[i];
                if (entry == null || string.IsNullOrEmpty(entry.Name) || entry.DeathUT <= 0) continue;

                ConfigNode kerbalNode;
                ConfigNode recordNode;
                if (!TryLoadColdArchivedEntry(entry.Id, out kerbalNode, out recordNode) || recordNode == null)
                    continue;

                string recordName;
                RosterRotationState.KerbalRecord archivedRec;
                if (!KerbalRecordPersistence.TryReadRecord(recordNode, out recordName, out archivedRec)
                    || archivedRec == null)
                    continue;

                string targetName = string.IsNullOrEmpty(recordName) ? entry.Name : recordName;
                RosterRotationState.KerbalRecord liveRec;
                if (!RosterRotationState.Records.TryGetValue(targetName, out liveRec) || liveRec == null)
                {
                    RosterRotationState.Records[targetName] = archivedRec;
                    changed++;
                    continue;
                }

                bool rowChanged = false;
                if (archivedRec.DeathUT > 0 && liveRec.DeathUT <= 0)
                {
                    liveRec.DeathUT = archivedRec.DeathUT;
                    liveRec.DiedOnMission = archivedRec.DiedOnMission;
                    liveRec.PendingMissionDeath = archivedRec.PendingMissionDeath;
                    rowChanged = true;
                }
                if (archivedRec.Retired && !liveRec.Retired)
                {
                    liveRec.Retired = true;
                    rowChanged = true;
                }
                if (liveRec.RetiredUT <= 0 && archivedRec.RetiredUT > 0)
                {
                    liveRec.RetiredUT = archivedRec.RetiredUT;
                    rowChanged = true;
                }
                if (archivedRec.LastAgedYears > liveRec.LastAgedYears)
                {
                    liveRec.LastAgedYears = archivedRec.LastAgedYears;
                    rowChanged = true;
                }

                if (rowChanged) changed++;
            }

            if (changed > 0)
            {
                RosterRotationState.InvalidateRetiredCache();
                RRLog.Info("[RosterArchive] Reconciled " + changed
                    + " cold-archive death lifecycle record(s) into EAC state after load.");
            }
            return changed;
        }

        internal static int RestoreColdArchivedKerbalsToRoster(string phase)
        {
            EnsureColdIndexCache();
            if (ColdIndexCache.Count == 0) return 0;

            Game game = HighLogic.CurrentGame;
            KerbalRoster roster = game != null ? game.CrewRoster : null;
            if (game == null || roster == null) return 0;

            int restored = 0;
            int failed = 0;

            for (int i = 0; i < ColdIndexCache.Count; i++)
            {
                ColdRosterEntrySummary entry = ColdIndexCache[i];
                if (entry == null || string.IsNullOrEmpty(entry.Name) || string.IsNullOrEmpty(entry.Id))
                    continue;

                if (FindRosterKerbal(roster, entry.Name) != null)
                    continue;

                ConfigNode kerbalNode;
                ConfigNode recordNode;
                if (!TryLoadColdArchivedEntry(entry.Id, out kerbalNode, out recordNode) || kerbalNode == null)
                {
                    failed++;
                    RRLog.Warn("[RosterArchive] Could not restore cold-archived " + entry.Name
                        + " during " + phase + ": archived KERBAL payload is missing or unreadable.");
                    continue;
                }

                try
                {
                    ProtoCrewMember pcm = new ProtoCrewMember(game.Mode, kerbalNode, ProtoCrewMember.KerbalType.Crew);
                    if (!roster.AddCrewMember(pcm))
                    {
                        failed++;
                        RRLog.Warn("[RosterArchive] Stock roster rejected cold-archived Kerbal "
                            + entry.Name + " during " + phase + ".");
                        continue;
                    }

                    if (recordNode != null)
                    {
                        string recordName;
                        RosterRotationState.KerbalRecord archivedRec;
                        if (KerbalRecordPersistence.TryReadRecord(recordNode, out recordName, out archivedRec)
                            && archivedRec != null)
                        {
                            string targetName = string.IsNullOrEmpty(recordName) ? entry.Name : recordName;
                            RosterRotationState.KerbalRecord liveRec;
                            if (!RosterRotationState.Records.TryGetValue(targetName, out liveRec) || liveRec == null)
                            {
                                RosterRotationState.Records[targetName] = archivedRec;
                            }
                            else if (archivedRec.DeathUT > 0 && liveRec.DeathUT <= 0)
                            {
                                // A cold-retiree death is written to the archive before
                                // the coalesced stock/EAC save. Preserve that death if the
                                // previous session ended before the normal save flushed.
                                liveRec.DeathUT = archivedRec.DeathUT;
                                liveRec.DiedOnMission = archivedRec.DiedOnMission;
                                liveRec.PendingMissionDeath = archivedRec.PendingMissionDeath;
                                liveRec.Retired = liveRec.Retired || archivedRec.Retired;
                                if (liveRec.RetiredUT <= 0 && archivedRec.RetiredUT > 0)
                                    liveRec.RetiredUT = archivedRec.RetiredUT;
                                if (archivedRec.LastAgedYears > liveRec.LastAgedYears)
                                    liveRec.LastAgedYears = archivedRec.LastAgedYears;
                            }
                        }
                    }

                    restored++;
                    RRLog.Info("[RosterArchive] Restored cold-archived " + entry.Name
                        + " to the live stock roster during " + phase
                        + "; the cold payload is retained until a later load verifies the stock save.");
                }
                catch (Exception ex)
                {
                    failed++;
                    RRLog.Warn("[RosterArchive] Could not restore cold-archived " + entry.Name
                        + " during " + phase + ": " + ex.Message);
                }
            }

            if (restored > 0)
            {
                RosterRotationState.InvalidateRetiredCache();
                RosterRotationKSCUI.RequestUiRefresh("cold roster restore");
            }

            if (failed > 0)
            {
                RRLog.Warn("[RosterArchive] Cold archive restore during " + phase + " left "
                    + failed + " entr" + (failed == 1 ? "y" : "ies")
                    + " external for safety.");
            }

            return restored;
        }

        internal static bool PurgeColdArchiveAfterVerifiedRestore(string phase)
        {
            if (RosterRotationState.ColdRosterArchiveEnabled) return false;

            EnsureColdIndexCache();
            if (ColdIndexCache.Count == 0) return true;
            if (!AreAllColdArchivedKerbalsPresentInLiveRoster()) return false;

            string indexPath = ColdIndexPath;
            bool payloadsDeleted = true;

            for (int i = 0; i < ColdIndexCache.Count; i++)
            {
                ColdRosterEntrySummary entry = ColdIndexCache[i];
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                string path = ColdEntryPath(entry.Id);
                try
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    payloadsDeleted = false;
                    RRLog.Warn("[RosterArchive] Could not remove verified cold payload for "
                        + entry.Name + ": " + ex.Message);
                }
            }

            // Keep the index if any payload could not be removed. That preserves a
            // complete retryable manifest instead of leaving unindexed external data.
            if (!payloadsDeleted) return false;

            try
            {
                string backup = string.IsNullOrEmpty(indexPath) ? null : indexPath + ".bak";
                string temp = string.IsNullOrEmpty(indexPath) ? null : indexPath + ".tmp";

                // Remove backup/temp first; delete the primary index last.
                if (!string.IsNullOrEmpty(backup) && File.Exists(backup)) File.Delete(backup);
                if (!string.IsNullOrEmpty(temp) && File.Exists(temp)) File.Delete(temp);
                if (!string.IsNullOrEmpty(indexPath) && File.Exists(indexPath)) File.Delete(indexPath);

                string directory = ColdArchiveDirectory;
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)
                    && Directory.GetFileSystemEntries(directory).Length == 0)
                    Directory.Delete(directory);

                int count = ColdIndexCache.Count;
                InvalidateColdIndexCache();
                RosterRotationKSCUI.RequestUiRefresh("cold roster verified restore cleanup");
                RRLog.Info("[RosterArchive] Removed " + count
                    + " cold-archive entr" + (count == 1 ? "y" : "ies")
                    + " after a later game load verified they were restored in the stock save (" + phase + ").");
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Restored Kerbals are present in the stock save, but cold-archive cleanup failed: "
                    + ex.Message);
                return false;
            }
        }

        internal static bool TryLoadColdArchivedKerbalNode(string id, out ConfigNode kerbalNode)
        {
            ConfigNode recordNode;
            return TryLoadColdArchivedEntry(id, out kerbalNode, out recordNode);
        }

        private static bool TryLoadColdArchivedEntry(string id, out ConfigNode kerbalNode, out ConfigNode recordNode)
        {
            kerbalNode = null;
            recordNode = null;
            string path = ColdEntryPath(id);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                ConfigNode root = ConfigNode.Load(path);
                if (root == null) return false;

                ConfigNode node = root.GetNode("KERBAL");
                if (node == null) return false;

                kerbalNode = CloneNode(node);
                ConfigNode record = root.GetNode("Record");
                if (record != null) recordNode = CloneNode(record);
                return kerbalNode != null;
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Could not read cold roster payload " + id + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Commits the one allowed lifecycle mutation of a cold-retired entry: old-age
        /// death. The archived stock KERBAL is made Dead before the in-memory death is
        /// considered durable, then the index moves the entry from Retired to Lost.
        /// </summary>
        internal static bool SyncColdArchivedRetireeDeath(
            string name,
            RosterRotationState.KerbalRecord rec,
            double deathUT)
        {
            if (string.IsNullOrEmpty(name) || rec == null || deathUT <= 0) return false;

            EnsureColdIndexCache();
            ColdRosterEntrySummary summary = null;
            for (int i = 0; i < ColdIndexCache.Count; i++)
            {
                ColdRosterEntrySummary entry = ColdIndexCache[i];
                if (entry == null) continue;
                if (!string.Equals(entry.Name, name, StringComparison.Ordinal)) continue;
                summary = CloneColdSummary(entry);
                break;
            }
            if (summary == null) return false;

            string entryPath = ColdEntryPath(summary.Id);
            if (string.IsNullOrEmpty(entryPath) || !File.Exists(entryPath))
            {
                RRLog.Warn("[RosterArchive] Cannot mark cold-retired " + name
                    + " deceased: archived payload is missing.");
                return false;
            }

            ConfigNode entryRoot;
            try { entryRoot = ConfigNode.Load(entryPath); }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Cannot read cold-retired payload for " + name
                    + " during death transition: " + ex.Message);
                return false;
            }
            if (entryRoot == null)
            {
                RRLog.Warn("[RosterArchive] Cannot mark cold-retired " + name
                    + " deceased: archived payload is unreadable.");
                return false;
            }

            ConfigNode kerbalNode = entryRoot.GetNode("KERBAL");
            if (kerbalNode == null)
            {
                RRLog.Warn("[RosterArchive] Cannot mark cold-retired " + name
                    + " deceased: archived KERBAL node is missing.");
                return false;
            }

            // Payload first: reversal safety requires the archived stock KERBAL to be
            // Dead before the in-memory record is allowed to remain deceased.
            SetOrAddValue(entryRoot, "reason", "lost");
            SetOrAddValue(entryRoot, "deathUT", deathUT.ToString("R", CultureInfo.InvariantCulture));
            SetOrAddValue(kerbalNode, "state", "Dead");

            ConfigNode oldRecord = entryRoot.GetNode("Record");
            if (oldRecord != null) entryRoot.RemoveNode(oldRecord);
            ConfigNode newRecord = entryRoot.AddNode("Record");
            KerbalRecordPersistence.WriteRecordNode(newRecord, name, rec, CultureInfo.InvariantCulture);

            if (!SaveColdEntryAtomically(entryRoot, entryPath, summary.Id))
            {
                RRLog.Warn("[RosterArchive] Could not make the cold death transition durable for " + name
                    + "; the retired state will be kept and retried later.");
                return false;
            }

            summary.Reason = "lost";
            summary.DeathUT = deathUT;
            summary.DiedOnMission = false;

            string indexPath = ColdIndexPath;
            ConfigNode indexRoot = LoadColdIndexRoot() ?? new ConfigNode(ColdIndexRootNodeName);
            if (!string.Equals(indexRoot.GetValue("version"), ColdArchiveVersion, StringComparison.Ordinal))
                SetOrAddValue(indexRoot, "version", ColdArchiveVersion);

            ConfigNode existing = FindColdIndexEntry(indexRoot, summary.Id, summary.Name);
            if (existing != null) indexRoot.RemoveNode(existing);
            WriteColdIndexEntry(indexRoot.AddNode(ColdIndexEntryNodeName), summary);

            if (!SaveColdIndexAtomically(indexRoot, indexPath))
            {
                // The payload already says Dead, which is the important reversal-safety
                // invariant. Keep the death committed and retry index reconciliation on
                // a later aging pass/load instead of resurrecting the Kerbal in memory.
                RRLog.Warn("[RosterArchive] Cold payload for " + name
                    + " is safely Dead, but the cold index could not be updated to Lost; retry will occur later.");
                return true;
            }

            UpsertColdCache(summary);
            RosterRotationKSCUI.RequestUiRefresh("cold retiree death");
            RRLog.Info("[RosterArchive] Cold-retired " + name
                + " died of old age and was moved from Retired to Lost at UT="
                + deathUT.ToString("0.###", CultureInfo.InvariantCulture) + ".");
            return true;
        }

        private static bool SaveColdEntryAtomically(ConfigNode root, string path, string expectedId)
        {
            if (root == null || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(expectedId)) return false;
            string temp = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                if (!root.Save(temp) || !File.Exists(temp) || new FileInfo(temp).Length <= 0) return false;
                if (!IsUsableColdEntryFile(temp, expectedId)) return false;

                ConfigNode verifyTemp = ConfigNode.Load(temp);
                ConfigNode verifyTempKerbal = verifyTemp != null ? verifyTemp.GetNode("KERBAL") : null;
                string tempState = verifyTempKerbal != null
                    ? (verifyTempKerbal.GetValue("state") ?? verifyTempKerbal.GetValue("status") ?? string.Empty)
                    : string.Empty;
                if (!string.Equals(tempState, "Dead", StringComparison.OrdinalIgnoreCase)) return false;

                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                File.Copy(temp, path, true);
                File.Delete(temp);
                return IsUsableColdEntryFile(path, expectedId);
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterArchive] Could not atomically update cold roster payload: " + ex);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }

        /// <summary>
        /// 1.6.3 hybrid cold archive. Recallable retired Kerbals stay in the live stock
        /// roster. Only permanently Dead Kerbals and retired Kerbals whose effective
        /// recall stars have reached zero are candidates. The external payload/index is
        /// made durable first, then the live/save KERBAL node is removed through the
        /// normal KSP save flow. No persistent.sfs reload/rewrite pass is used.
        /// </summary>
        internal static bool ArchiveEligibleColdRoster(ConfigNode saveRoot, string phase, out int archivedCount)
        {
            archivedCount = 0;
            if (saveRoot == null || !RosterRotationState.ColdRosterArchiveEnabled) return false;

            // Make the feature visible and diagnosable even when this particular save
            // contains no eligible Kerbals. Enabling the option should create an empty
            // index immediately rather than waiting for the first archive candidate.
            string coldIndexPath = ColdIndexPath;
            if (string.IsNullOrEmpty(coldIndexPath) || !File.Exists(coldIndexPath))
                EnsureColdArchiveInitialized(phase);

            Game game = HighLogic.CurrentGame;
            KerbalRoster roster = game != null ? game.CrewRoster : null;
            double nowUT = Planetarium.GetUniversalTime();
            bool changed = false;

            int stockCount = 0;
            int retiredCount = 0;
            int permanentDeadCount = 0;
            int eligibleRetiredCount = 0;
            int deferredReferenceCount = 0;
            int skippedNoRecordCount = 0;
            int skippedProtectedCount = 0;
            int skippedAssignedCount = 0;
            int serializationFailureCount = 0;
            int writeFailureCount = 0;
            int liveRemovalFailureCount = 0;
            int saveRemovalFailureCount = 0;

            bool saveRootHasRoster;
            string candidateSource;
            List<ColdArchiveCandidate> candidates =
                BuildColdArchiveCandidates(saveRoot, roster, out saveRootHasRoster, out candidateSource, out serializationFailureCount);
            stockCount = candidates.Count;

            for (int i = 0; i < candidates.Count; i++)
            {
                ColdArchiveCandidate candidate = candidates[i];
                if (candidate == null || candidate.KerbalNode == null) continue;

                ProtoCrewMember live = candidate.Live;
                ConfigNode savedKerbal = candidate.KerbalNode;

                string name = live != null ? live.name : savedKerbal.GetValue("name");
                if (string.IsNullOrEmpty(name)) continue;

                string state = savedKerbal.GetValue("state") ?? savedKerbal.GetValue("status") ?? string.Empty;
                bool permanentDead =
                    (live != null && live.rosterStatus == ProtoCrewMember.RosterStatus.Dead)
                    || string.Equals(state, "Dead", StringComparison.OrdinalIgnoreCase);
                if (permanentDead) permanentDeadCount++;

                RosterRotationState.KerbalRecord rec;
                if (!RosterRotationState.Records.TryGetValue(name, out rec) || rec == null)
                {
                    skippedNoRecordCount++;
                    continue;
                }

                if (rec.Retired) retiredCount++;
                if (rec.PendingMissionDeath || rec.DeepFreezeActive)
                {
                    skippedProtectedCount++;
                    continue;
                }

                if (live != null)
                {
                    if (live.type == ProtoCrewMember.KerbalType.Applicant)
                    {
                        skippedProtectedCount++;
                        continue;
                    }
                    if (RosterRotationKSCUI.IsDeepFreezeFrozen(live))
                    {
                        skippedProtectedCount++;
                        continue;
                    }
                    try
                    {
                        if (CrewRandRAdapter.IsOnVacation(live))
                        {
                            skippedProtectedCount++;
                            continue;
                        }
                    }
                    catch { }
                }

                string reason = null;

                // Missing is intentionally not archived. Stock KSP can use Missing for
                // respawnable crew. A stock Dead state is itself sufficient evidence
                // that this is the permanent Lost side of the cold archive; an EAC
                // DeathUT is useful metadata when present, but is not an eligibility gate.
                if (permanentDead)
                {
                    reason = "lost";
                }
                else if (rec.Retired && rec.DeathUT <= 0)
                {
                    int effectiveStars = RosterRotationState.GetRetiredEffectiveStars(live, rec, nowUT);
                    if (effectiveStars <= 0)
                    {
                        reason = "retired";
                        eligibleRetiredCount++;
                    }
                }

                if (string.IsNullOrEmpty(reason)) continue;

                if (live != null && live.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                {
                    skippedAssignedCount++;
                    continue;
                }

                // Belt-and-suspenders vessel check. An assigned roster status normally
                // catches this, but proto-vessel snapshots can lag status transitions.
                string runtimeVessel;
                if (TryFindRuntimeVesselReference(name, out runtimeVessel))
                {
                    deferredReferenceCount++;
                    RRLog.Verbose("[RosterArchive] Cold archive deferred for " + name + " during " + phase
                        + ": live vessel reference found in " + runtimeVessel + ".");
                    continue;
                }

                // If KSP supplied useful save-tree content, retain the old conservative
                // active-vessel/active-contract reference check. The normal save callback
                // frequently fires before ROSTER exists, so this is supplemental rather
                // than the source of archive candidates.
                string referenceSource;
                if (TryFindColdArchiveLiveReference(saveRoot, name, out referenceSource))
                {
                    deferredReferenceCount++;
                    RRLog.Verbose("[RosterArchive] Cold archive deferred for " + name + " during " + phase
                        + ": live reference found in " + referenceSource + ".");
                    continue;
                }

                ConfigNode archivedKerbal = CloneNode(savedKerbal);
                ConfigNode archivedRecord = BuildRecordNode(saveRoot, name);
                ColdRosterEntrySummary summary = BuildColdSummary(name, reason, live, savedKerbal, rec, nowUT);
                if (summary == null || !TryPersistColdEntry(summary, archivedKerbal, archivedRecord))
                {
                    writeFailureCount++;
                    RRLog.Warn("[RosterArchive] Cold archive write failed for " + name + " during " + phase
                        + "; stock roster data was left unchanged.");
                    continue;
                }

                // The live CrewRoster is the authoritative source for the next stock save.
                // Remove from it after the external payload is durable. This is the key
                // difference from the old 1.6.0 post-save persistent.sfs rewrite cycle.
                if (live != null && !TryRemoveLiveKerbal(roster, live))
                {
                    liveRemovalFailureCount++;
                    RRLog.Warn("[RosterArchive] Cold archive payload is safe for " + name
                        + ", but CrewRoster removal failed; the stock roster was kept for retry.");
                    continue;
                }

                // Some KSP save callbacks provide a complete tree and some do not. If a
                // ROSTER node is present, strip it too. If it is absent, do not treat that
                // as an error: the live-roster removal plus one follow-up normal save is
                // what makes the change durable.
                if (saveRootHasRoster && !RemoveKerbalFromRosterNode(saveRoot, name))
                {
                    saveRemovalFailureCount++;
                    RRLog.Warn("[RosterArchive] Cold archive payload is safe for " + name
                        + ", but the complete save tree's KERBAL node could not be removed; "
                        + "a follow-up normal save will retry from the updated live roster.");
                }

                archivedCount++;
                changed = true;
                RosterRotationState.InvalidateRetiredCache();
                RRLog.Info("[RosterArchive] Cold-archived " + name + " (" + reason + ") during " + phase
                    + " from " + candidateSource + "; no post-save persistent.sfs rewrite is required.");
            }

            string summarySignature = candidateSource + "|" + saveRootHasRoster + "|" + stockCount + "|"
                + retiredCount + "|" + permanentDeadCount + "|" + eligibleRetiredCount + "|"
                + deferredReferenceCount + "|" + skippedNoRecordCount + "|" + skippedProtectedCount + "|"
                + skippedAssignedCount + "|" + serializationFailureCount + "|" + writeFailureCount + "|"
                + liveRemovalFailureCount + "|" + saveRemovalFailureCount + "|" + archivedCount;
            string summaryMessage = "[RosterArchive] Cold scan (" + phase + "): source=" + candidateSource
                + ", saveRootRoster=" + saveRootHasRoster
                + ", stock=" + stockCount
                + ", retired=" + retiredCount
                + ", eligibleRetired=" + eligibleRetiredCount
                + ", permanentDead=" + permanentDeadCount
                + ", deferredRefs=" + deferredReferenceCount
                + ", noEacRecord=" + skippedNoRecordCount
                + ", protected=" + skippedProtectedCount
                + ", assigned=" + skippedAssignedCount
                + ", serializeFailures=" + serializationFailureCount
                + ", writeFailures=" + writeFailureCount
                + ", liveRemovalFailures=" + liveRemovalFailureCount
                + ", saveRemovalFailures=" + saveRemovalFailureCount
                + ", archived=" + archivedCount + ".";

            if (!string.Equals(_lastColdScanSummarySignature, summarySignature, StringComparison.Ordinal))
            {
                RRLog.Info(summaryMessage);
                _lastColdScanSummarySignature = summarySignature;
            }
            else
            {
                RRLog.Verbose(summaryMessage);
            }

            if (archivedCount > 0)
                RosterRotationKSCUI.RequestUiRefresh("cold roster archive");
            return changed;
        }

        private static List<ColdArchiveCandidate> BuildColdArchiveCandidates(
            ConfigNode saveRoot,
            KerbalRoster roster,
            out bool saveRootHasRoster,
            out string candidateSource,
            out int serializationFailureCount)
        {
            saveRootHasRoster = false;
            serializationFailureCount = 0;

            var savedByName = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
            if (saveRoot != null)
            {
                foreach (ConfigNode rosterNode in FindNodesRecursive(saveRoot, "ROSTER"))
                {
                    saveRootHasRoster = true;
                    foreach (ConfigNode kerbalNode in rosterNode.GetNodes("KERBAL"))
                    {
                        if (kerbalNode == null) continue;
                        string name = kerbalNode.GetValue("name");
                        if (string.IsNullOrEmpty(name) || savedByName.ContainsKey(name)) continue;
                        savedByName[name] = kerbalNode;
                    }
                }
            }

            var result = new List<ColdArchiveCandidate>();

            // Prefer the live CrewRoster. KSP's onGameStateSave callback is observed to
            // fire before the full persistent tree contains ROSTER, so using the callback
            // ConfigNode as the candidate source incorrectly produced stock=0.
            if (roster != null)
            {
                // Match EAC's Astronaut Complex roster enumeration: Crew plus indexed
                // roster entries, deduplicated by name. Some KSP roster states are not
                // reliably exposed by only one of those collections.
                var snapshot = new List<ProtoCrewMember>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                try
                {
                    if (roster.Crew != null)
                    {
                        foreach (ProtoCrewMember pcm in roster.Crew)
                        {
                            if (pcm == null || string.IsNullOrEmpty(pcm.name)) continue;
                            if (seen.Add(pcm.name)) snapshot.Add(pcm);
                        }
                    }
                }
                catch { }

                try
                {
                    for (int i = 0; i < roster.Count; i++)
                    {
                        ProtoCrewMember pcm;
                        try { pcm = roster[i]; }
                        catch { continue; }
                        if (pcm == null || string.IsNullOrEmpty(pcm.name)) continue;
                        if (seen.Add(pcm.name)) snapshot.Add(pcm);
                    }
                }
                catch { }

                for (int i = 0; i < snapshot.Count; i++)
                {
                    ProtoCrewMember pcm = snapshot[i];
                    if (pcm == null) continue;

                    ConfigNode kerbalNode;
                    if (!string.IsNullOrEmpty(pcm.name) && savedByName.TryGetValue(pcm.name, out kerbalNode))
                    {
                        kerbalNode = CloneNode(kerbalNode);
                    }
                    else
                    {
                        kerbalNode = new ConfigNode("KERBAL");
                        try
                        {
                            pcm.Save(kerbalNode);
                        }
                        catch (Exception ex)
                        {
                            serializationFailureCount++;
                            RRLog.Warn("[RosterArchive] Could not serialize live Kerbal " + (pcm.name ?? "<unknown>")
                                + " for cold-archive eligibility: " + ex.Message);
                            continue;
                        }
                    }

                    result.Add(new ColdArchiveCandidate
                    {
                        Live = pcm,
                        KerbalNode = kerbalNode
                    });
                }

                candidateSource = saveRootHasRoster ? "live-roster+save-tree" : "live-roster";
                return result;
            }

            // Defensive fallback for unusual load/save states where CrewRoster is not
            // available but KSP did provide a complete ROSTER tree.
            foreach (KeyValuePair<string, ConfigNode> kvp in savedByName)
            {
                result.Add(new ColdArchiveCandidate
                {
                    Live = null,
                    KerbalNode = CloneNode(kvp.Value)
                });
            }
            candidateSource = "save-tree";
            return result;
        }

        private static bool TryFindRuntimeVesselReference(string name, out string source)
        {
            source = null;
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                var protoVessels = HighLogic.CurrentGame != null
                    && HighLogic.CurrentGame.flightState != null
                    ? HighLogic.CurrentGame.flightState.protoVessels as IEnumerable
                    : null;
                if (protoVessels == null) return false;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (object protoVessel in protoVessels)
                {
                    if (protoVessel == null) continue;
                    try
                    {
                        Type type = protoVessel.GetType();
                        MethodInfo getCrew = type.GetMethod("GetVesselCrew", flags);
                        IEnumerable crew = getCrew != null ? getCrew.Invoke(protoVessel, null) as IEnumerable : null;
                        if (crew == null) continue;

                        bool found = false;
                        foreach (object crewObj in crew)
                        {
                            ProtoCrewMember pcm = crewObj as ProtoCrewMember;
                            if (pcm != null && string.Equals(pcm.name, name, StringComparison.Ordinal))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found) continue;

                        string vesselName = null;
                        FieldInfo vesselNameField = type.GetField("vesselName", flags);
                        if (vesselNameField != null) vesselName = vesselNameField.GetValue(protoVessel) as string;
                        if (string.IsNullOrEmpty(vesselName))
                        {
                            PropertyInfo vesselNameProperty = type.GetProperty("vesselName", flags);
                            if (vesselNameProperty != null)
                                vesselName = vesselNameProperty.GetValue(protoVessel, null) as string;
                        }

                        source = string.IsNullOrEmpty(vesselName) ? "ProtoVessel" : "ProtoVessel/" + vesselName;
                        return true;
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        private static ColdRosterEntrySummary BuildColdSummary(
            string name,
            string reason,
            ProtoCrewMember live,
            ConfigNode savedKerbal,
            RosterRotationState.KerbalRecord rec,
            double nowUT)
        {
            if (string.IsNullOrEmpty(name) || rec == null || savedKerbal == null) return null;

            string identity = RosterRotationState.EnsureKerbalIdentity(rec);
            string idSource = (!string.IsNullOrEmpty(identity) ? identity : name) + "|" + (reason ?? string.Empty);
            string id = EACHashing.ComputeSha256Hex(idSource);
            if (string.IsNullOrEmpty(id)) return null;

            int experienceLevel = 0;
            float courage = ParseFloat(savedKerbal.GetValue("courage"), 0f);
            float stupidity = ParseFloat(savedKerbal.GetValue("stupidity"), 0f);
            string trait = savedKerbal.GetValue("trait") ?? rec.OriginalTrait ?? string.Empty;

            if (live != null)
            {
                try { experienceLevel = Math.Max(0, (int)live.experienceLevel); } catch { }
                try { courage = live.courage; } catch { }
                try { stupidity = live.stupidity; } catch { }
                try { if (!string.IsNullOrEmpty(live.trait)) trait = live.trait; } catch { }
            }
            else if (rec.ExperienceAtRetire >= 0)
            {
                experienceLevel = rec.ExperienceAtRetire;
            }
            else if (rec.HighestLevelEverCertified >= 0)
            {
                experienceLevel = rec.HighestLevelEverCertified;
            }
            else if (rec.GrantedLevel >= 0)
            {
                experienceLevel = rec.GrantedLevel;
            }

            return new ColdRosterEntrySummary
            {
                Id = id,
                Name = name,
                Reason = reason ?? string.Empty,
                Trait = trait ?? string.Empty,
                ExperienceLevel = Math.Max(0, experienceLevel),
                Courage = Mathf.Clamp01(courage),
                Stupidity = Mathf.Clamp01(stupidity),
                ArchivedUT = nowUT,
                RetiredUT = rec.RetiredUT,
                DeathUT = rec.DeathUT,
                DiedOnMission = rec.DiedOnMission
            };
        }

        private static bool TryPersistColdEntry(
            ColdRosterEntrySummary summary,
            ConfigNode kerbalNode,
            ConfigNode recordNode)
        {
            if (summary == null || kerbalNode == null || string.IsNullOrEmpty(summary.Id)) return false;
            string directory = ColdArchiveDirectory;
            string entryPath = ColdEntryPath(summary.Id);
            string indexPath = ColdIndexPath;
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(entryPath) || string.IsNullOrEmpty(indexPath))
                return false;

            try
            {
                Directory.CreateDirectory(directory);

                // Immutable per-Kerbal payload. If a verified payload already exists, reuse it.
                if (!IsUsableColdEntryFile(entryPath, summary.Id))
                {
                    ConfigNode entryRoot = new ConfigNode(ColdEntryRootNodeName);
                    entryRoot.AddValue("version", ColdArchiveVersion);
                    entryRoot.AddValue("id", summary.Id);
                    entryRoot.AddValue("name", summary.Name ?? string.Empty);
                    entryRoot.AddValue("reason", summary.Reason ?? string.Empty);
                    entryRoot.AddValue("archivedUT", summary.ArchivedUT.ToString("R", CultureInfo.InvariantCulture));
                    CopyNodeContents(kerbalNode, entryRoot.AddNode("KERBAL"));
                    if (recordNode != null)
                        CopyNodeContents(recordNode, entryRoot.AddNode("Record"));

                    string tempEntry = entryPath + ".tmp";
                    try { if (File.Exists(tempEntry)) File.Delete(tempEntry); } catch { }
                    if (!entryRoot.Save(tempEntry) || !File.Exists(tempEntry) || new FileInfo(tempEntry).Length <= 0)
                        return false;
                    File.Copy(tempEntry, entryPath, true);
                    File.Delete(tempEntry);
                    if (!IsUsableColdEntryFile(entryPath, summary.Id)) return false;
                }

                ConfigNode indexRoot = LoadColdIndexRoot() ?? new ConfigNode(ColdIndexRootNodeName);
                if (!string.Equals(indexRoot.GetValue("version"), ColdArchiveVersion, StringComparison.Ordinal))
                    SetOrAddValue(indexRoot, "version", ColdArchiveVersion);

                ConfigNode existing = FindColdIndexEntry(indexRoot, summary.Id, summary.Name);
                if (existing == null)
                {
                    WriteColdIndexEntry(indexRoot.AddNode(ColdIndexEntryNodeName), summary);
                    if (!SaveColdIndexAtomically(indexRoot, indexPath)) return false;
                }

                UpsertColdCache(summary);
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterArchive] Could not persist cold roster entry for " + summary.Name + ": " + ex);
                return false;
            }
        }

        private static bool TryFindColdArchiveLiveReference(ConfigNode root, string name, out string source)
        {
            source = null;
            if (root == null || string.IsNullOrEmpty(name)) return false;

            // Any vessel reference is a hard stop. The Kerbal may still be attached to a
            // craft snapshot even if roster status has already changed.
            foreach (ConfigNode vessel in FindNodesRecursive(root, "VESSEL"))
            {
                if (!NodeContainsKerbalName(vessel, name)) continue;
                string vesselName = vessel.GetValue("name");
                source = string.IsNullOrEmpty(vesselName) ? "VESSEL" : "VESSEL/" + vesselName;
                return true;
            }

            // Only ACTIVE stock contracts count as a live contract dependency. Completed
            // ContractSystem/ProgressTracking history can contain names and must not pin a
            // dead/retired Kerbal in the stock roster forever.
            foreach (ConfigNode scenario in FindNodesRecursive(root, "SCENARIO"))
            {
                string scenarioName = scenario.GetValue("name");
                if (!string.Equals(scenarioName, "ContractSystem", StringComparison.Ordinal)) continue;

                foreach (ConfigNode contract in FindNodesRecursive(scenario, "CONTRACT"))
                {
                    if (!NodeContainsKerbalName(contract, name)) continue;
                    string state = contract.GetValue("state") ?? contract.GetValue("State") ?? string.Empty;
                    if (string.IsNullOrEmpty(state) || string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase))
                    {
                        source = "SCENARIO/ContractSystem/" + (string.IsNullOrEmpty(state) ? "unknown-state" : "Active");
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryRemoveLiveKerbal(KerbalRoster roster, ProtoCrewMember target)
        {
            if (roster == null || target == null) return true;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo removePcm = roster.GetType().GetMethod("Remove", flags, null, new[] { typeof(ProtoCrewMember) }, null);
                if (removePcm != null)
                {
                    object result = removePcm.Invoke(roster, new object[] { target });
                    if (result is bool) return (bool)result;
                    return FindRosterKerbal(roster, target.name) == null;
                }

                MethodInfo removeName = roster.GetType().GetMethod("Remove", flags, null, new[] { typeof(string) }, null);
                if (removeName != null)
                {
                    object result = removeName.Invoke(roster, new object[] { target.name });
                    if (result is bool) return (bool)result;
                    return FindRosterKerbal(roster, target.name) == null;
                }

                // Last-resort list-field removal for KSP builds/mods that do not expose Remove.
                foreach (FieldInfo field in roster.GetType().GetFields(flags))
                {
                    if (!typeof(IList).IsAssignableFrom(field.FieldType)) continue;
                    IList list = field.GetValue(roster) as IList;
                    if (list == null || !list.Contains(target)) continue;
                    list.Remove(target);
                    if (FindRosterKerbal(roster, target.name) == null) return true;
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterArchive] Could not remove " + target.name + " from live CrewRoster: " + ex.Message);
            }
            return FindRosterKerbal(roster, target.name) == null;
        }

        private static void EnsureColdIndexCache()
        {
            string key = ColdIndexPath ?? string.Empty;
            if (_coldIndexLoaded && string.Equals(_coldIndexSaveKey, key, StringComparison.Ordinal)) return;

            _coldIndexLoaded = true;
            _coldIndexSaveKey = key;
            ColdIndexCache.Clear();

            ConfigNode root = LoadColdIndexRoot();
            if (root == null) return;
            foreach (ConfigNode node in root.GetNodes(ColdIndexEntryNodeName))
            {
                ColdRosterEntrySummary summary = ReadColdIndexEntry(node);
                if (summary != null) ColdIndexCache.Add(summary);
            }
        }

        private static ConfigNode LoadColdIndexRoot()
        {
            string path = ColdIndexPath;
            ConfigNode root = TryLoadColdIndexFile(path);
            if (root != null) return root;
            root = TryLoadColdIndexFile(string.IsNullOrEmpty(path) ? null : path + ".bak");
            if (root != null)
                RRLog.Warn("[RosterArchive] Loaded backup cold-roster index because the primary index was unavailable.");
            return root;
        }

        private static ConfigNode TryLoadColdIndexFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                ConfigNode root = ConfigNode.Load(path);
                if (root == null) return null;
                return root;
            }
            catch
            {
                return null;
            }
        }

        private static bool SaveColdIndexAtomically(ConfigNode root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return false;
            string temp = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                if (!root.Save(temp) || !File.Exists(temp) || new FileInfo(temp).Length <= 0) return false;
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                File.Copy(temp, path, true);
                File.Delete(temp);
                _coldIndexLoaded = false;
                EnsureColdIndexCache();
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterArchive] Could not write cold-roster index: " + ex);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }

        private static bool IsUsableColdEntryFile(string path, string expectedId)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || new FileInfo(path).Length <= 0) return false;
                ConfigNode root = ConfigNode.Load(path);
                return root != null
                    && root.GetNode("KERBAL") != null
                    && string.Equals(root.GetValue("id"), expectedId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static ConfigNode FindColdIndexEntry(ConfigNode indexRoot, string id, string name)
        {
            if (indexRoot == null) return null;
            foreach (ConfigNode node in indexRoot.GetNodes(ColdIndexEntryNodeName))
            {
                if (!string.IsNullOrEmpty(id) && string.Equals(node.GetValue("id"), id, StringComparison.OrdinalIgnoreCase))
                    return node;
                if (!string.IsNullOrEmpty(name) && string.Equals(node.GetValue("name"), name, StringComparison.Ordinal))
                    return node;
            }
            return null;
        }

        private static void WriteColdIndexEntry(ConfigNode node, ColdRosterEntrySummary summary)
        {
            if (node == null || summary == null) return;
            node.AddValue("id", summary.Id ?? string.Empty);
            node.AddValue("name", summary.Name ?? string.Empty);
            node.AddValue("reason", summary.Reason ?? string.Empty);
            node.AddValue("trait", summary.Trait ?? string.Empty);
            node.AddValue("experienceLevel", summary.ExperienceLevel.ToString(CultureInfo.InvariantCulture));
            node.AddValue("courage", summary.Courage.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("stupidity", summary.Stupidity.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("archivedUT", summary.ArchivedUT.ToString("R", CultureInfo.InvariantCulture));
            if (summary.RetiredUT > 0) node.AddValue("retiredUT", summary.RetiredUT.ToString("R", CultureInfo.InvariantCulture));
            if (summary.DeathUT > 0) node.AddValue("deathUT", summary.DeathUT.ToString("R", CultureInfo.InvariantCulture));
            if (summary.DiedOnMission) node.AddValue("diedOnMission", bool.TrueString);
        }

        private static ColdRosterEntrySummary ReadColdIndexEntry(ConfigNode node)
        {
            if (node == null) return null;
            string id = node.GetValue("id");
            string name = node.GetValue("name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) return null;
            return new ColdRosterEntrySummary
            {
                Id = id,
                Name = name,
                Reason = node.GetValue("reason") ?? string.Empty,
                Trait = node.GetValue("trait") ?? string.Empty,
                ExperienceLevel = ParseInt(node.GetValue("experienceLevel"), 0),
                Courage = Mathf.Clamp01(ParseFloat(node.GetValue("courage"), 0f)),
                Stupidity = Mathf.Clamp01(ParseFloat(node.GetValue("stupidity"), 0f)),
                ArchivedUT = ParseDouble(node.GetValue("archivedUT"), 0d),
                RetiredUT = ParseDouble(node.GetValue("retiredUT"), 0d),
                DeathUT = ParseDouble(node.GetValue("deathUT"), 0d),
                DiedOnMission = ReadBool(node.GetValue("diedOnMission"), false)
            };
        }

        private static void UpsertColdCache(ColdRosterEntrySummary summary)
        {
            if (summary == null) return;
            EnsureColdIndexCache();
            for (int i = 0; i < ColdIndexCache.Count; i++)
            {
                ColdRosterEntrySummary existing = ColdIndexCache[i];
                if (existing == null) continue;
                if (string.Equals(existing.Id, summary.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.Name, summary.Name, StringComparison.Ordinal))
                {
                    ColdIndexCache[i] = CloneColdSummary(summary);
                    return;
                }
            }
            ColdIndexCache.Add(CloneColdSummary(summary));
        }

        private static ColdRosterEntrySummary CloneColdSummary(ColdRosterEntrySummary source)
        {
            if (source == null) return null;
            return new ColdRosterEntrySummary
            {
                Id = source.Id,
                Name = source.Name,
                Reason = source.Reason,
                Trait = source.Trait,
                ExperienceLevel = source.ExperienceLevel,
                Courage = source.Courage,
                Stupidity = source.Stupidity,
                ArchivedUT = source.ArchivedUT,
                RetiredUT = source.RetiredUT,
                DeathUT = source.DeathUT,
                DiedOnMission = source.DiedOnMission
            };
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static double ParseDouble(string value, double fallback)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
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

        internal static bool DisableArchiveSettingInSaveTree(ConfigNode saveRoot)
        {
            ConfigNode eacRoot = FindEacDataRoot(saveRoot);
            ConfigNode settings = eacRoot != null ? eacRoot.GetNode("Settings") : null;
            if (settings == null) return false;

            SetOrAddValue(settings, "externalRosterArchiveEnabled", bool.FalseString);
            return true;
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
        private bool _legacyArchiveMigrationReady;

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
            EACRosterArchive.InvalidateColdIndexCache();
            int references = EACRosterArchive.CaptureActiveReferences(root);
            _legacyArchiveMigrationReady = references == 0;
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
                    // Capture this before any compatibility restoration occurs. If every
                    // cold entry is already present at this point, those Kerbals came from
                    // the stock save we just loaded, which safely proves a previous restore
                    // was persisted and the external safety copies can be removed.
                    bool coldArchiveDisabled = !RosterRotationState.ColdRosterArchiveEnabled;
                    bool coldEntriesExist = EACRosterArchive.HasColdArchivedEntries();
                    if (coldEntriesExist)
                        EACRosterArchive.MergeColdDeathLifecycleIntoState();

                    bool coldRestoreVerifiedByLoadedSave = coldArchiveDisabled
                        && coldEntriesExist
                        && EACRosterArchive.AreAllColdArchivedKerbalsPresentInLiveRoster();

                    int references = EACRosterArchive.ActiveReferenceCount;
                    int merged = EACRosterArchive.MergeArchivedRecordsIntoState();
                    int restored = EACRosterArchive.RestoreArchivedKerbalsToRoster();
                    _legacyArchiveMigrationReady = EACRosterArchive.ActiveReferencesResolvedInLiveRoster();

                    if (references > 0)
                    {
                        if (_legacyArchiveMigrationReady)
                        {
                            // Once every referenced Kerbal is back in the stock roster,
                            // normal KSP saves can carry them permanently. The next save
                            // removes the tiny archive reference nodes and turns the old
                            // option off without touching persistent.sfs a second time.
                            RosterRotationState.ExternalRosterArchiveEnabled = false;
                            RRLog.Info("[RosterArchive] 1.6.1 migration restored legacy archive references to the stock roster. "
                                + "MergedRecords=" + merged + ", RehydratedKerbals=" + restored
                                + ". Retired/lost external roster storage is now disabled.");
                        }
                        else
                        {
                            RRLog.Warn("[RosterArchive] 1.6.1 could not fully restore all legacy archive references. "
                                + "Archive references will be preserved for safety; no persistent.sfs rewrite will run.");
                        }
                    }
                    else
                    {
                        RosterRotationState.ExternalRosterArchiveEnabled = false;
                    }

                    if (coldArchiveDisabled && coldEntriesExist)
                    {
                        if (coldRestoreVerifiedByLoadedSave)
                        {
                            EACRosterArchive.PurgeColdArchiveAfterVerifiedRestore("archive disabled");
                        }
                        else
                        {
                            int coldRestored = EACRosterArchive.RestoreColdArchivedKerbalsToRoster("archive disabled on load");
                            if (coldRestored > 0)
                            {
                                // Keep the external payloads until a later load proves
                                // this normal save embedded the restored stock KERBAL nodes.
                                SaveScheduler.RequestSave("restore disabled cold roster archive");
                            }

                            if (!EACRosterArchive.AreAllColdArchivedKerbalsPresentInLiveRoster())
                            {
                                RRLog.Warn("[RosterArchive] Cold archive is disabled, but one or more archived Kerbals "
                                    + "could not be restored. Their external payloads were retained for safety.");
                            }
                        }
                    }

                    yield break;
                }
            }

            if (EACRosterArchive.ActiveReferenceCount > 0)
            {
                RRLog.Warn("[RosterArchive] 1.6.1 migration could not access CrewRoster during load. "
                    + "Legacy archive references will be preserved for safety.");
            }
        }

        private void OnGameStateSave(ConfigNode root)
        {
            if (root == null) return;

            // Reuse this already-established save callback for one-time migration of
            // pre-EACScenario saves.
            EACScenarioMigrationCleaner.OnGameStateSave(root);

            int references = EACRosterArchive.ActiveReferenceCount;
            bool safeToEmbed = references == 0
                || _legacyArchiveMigrationReady
                || EACRosterArchive.ActiveReferencesResolvedInLiveRoster();

            if (!safeToEmbed)
            {
                // Do not strip, reload, or rewrite persistent.sfs. Keep the legacy refs
                // until a later load can successfully rehydrate every archived Kerbal.
                RRLog.Warn("[RosterArchive] 1.6.1 preserved legacy archive references because not all archived Kerbals "
                    + "are present in the live stock roster. No post-save persistent.sfs pass was performed.");
                return;
            }

            EACRosterArchive.ClearSaveReferences(root);
            EACRosterArchive.DisableArchiveSettingInSaveTree(root);
            RosterRotationState.ExternalRosterArchiveEnabled = false;

            if (references > 0)
            {
                RRLog.Info("[RosterArchive] 1.6.1 migrated " + references
                    + " legacy roster archive reference(s) back to persistent.sfs; future saves no longer use the legacy rehydrating archive.");
                EACRosterArchive.ClearActiveReferences();
            }

            int coldArchived;
            EACRosterArchive.ArchiveEligibleColdRoster(root, "save", out coldArchived);
            if (coldArchived > 0)
            {
                // KSP commonly raises onGameStateSave before ROSTER has been added to
                // the callback tree. The cold payload is already durable and the live
                // CrewRoster has already been pruned; one ordinary follow-up save makes
                // that live-roster change durable without ever rewriting persistent.sfs
                // behind KSP's back.
                SaveScheduler.RequestSave("cold roster archive finalize");
            }
        }
    }
}

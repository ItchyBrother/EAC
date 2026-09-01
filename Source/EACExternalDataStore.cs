using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using KSP.UI.Screens;

namespace RosterRotation
{
    /// <summary>
    /// Versioned external persistence for EAC-owned Kerbal state. KSP save files keep
    /// only a revision pointer; each revision is immutable so quicksaves/named saves
    /// can safely rewind EAC history together with the stock game state.
    ///
    /// New careers opt in to this datastore. Existing test-build saves that already
    /// reference a revision are migrated forward by EACScenario.
    /// </summary>
    internal static class EACExternalDataStore
    {
        internal const int SchemaVersion = 2;
        private const string VersionValue = "externalDataVersion";
        private const string RevisionValue = "externalDataRevision";
        private const string ContentHashValue = "contentHash";
        private const int UnreferencedSafetyRevisions = 3;

        // ScenarioModule save nodes are sometimes reconstructed without carrying the
        // previous pointer. Keep the loaded/written revision in memory as a dedupe hint;
        // TryLoadReferencedSnapshot replaces it whenever the player loads another save.
        private static long _runtimeRevision;
        private static string _runtimeContentHash;
        private static string _runtimeSaveKey;

        private static string SaveDirectory
        {
            get
            {
                string saveFolder = HighLogic.SaveFolder;
                if (string.IsNullOrEmpty(saveFolder) || string.IsNullOrEmpty(KSPUtil.ApplicationRootPath)) return null;
                return Path.Combine(KSPUtil.ApplicationRootPath, "saves", saveFolder);
            }
        }

        private static string BaseDirectory
        {
            get
            {
                string saveRoot = SaveDirectory;
                return string.IsNullOrEmpty(saveRoot) ? null : Path.Combine(saveRoot, "EAC", "data");
            }
        }

        private static string RevisionDirectory
        {
            get
            {
                string root = BaseDirectory;
                return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "revisions");
            }
        }

        internal static bool TryLoadReferencedSnapshot(ConfigNode scenarioRoot, bool replaceRecords, out long revision)
        {
            revision = 0;
            if (scenarioRoot == null) return false;
            if (!long.TryParse(scenarioRoot.GetValue(RevisionValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out revision) || revision <= 0)
                return false;

            string path = GetRevisionPath(revision);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                RRLog.Error("[EAC] External data revision " + revision + " is referenced by this save but is missing: " + path);
                return false;
            }

            try
            {
                ConfigNode disk = ConfigNode.Load(path);
                ConfigNode root = disk != null && disk.HasNode("EAC_DATA") ? disk.GetNode("EAC_DATA") : disk;
                if (root == null) return false;

                int version = ParseInt(root.GetValue("schemaVersion"), 0);
                long fileRevision = ParseLong(root.GetValue("revision"), 0);
                if (version < 1 || fileRevision != revision)
                {
                    RRLog.Error("[EAC] External data validation failed for revision " + revision + ".");
                    return false;
                }

                EnsureRuntimeSaveKey();
                _runtimeRevision = revision;
                _runtimeContentHash = root.GetValue(ContentHashValue) ?? "";

                if (replaceRecords) RosterRotationState.Records.Clear();
                int loaded = 0;
                foreach (ConfigNode recordNode in root.GetNodes("Record"))
                {
                    string name;
                    RosterRotationState.KerbalRecord record;
                    if (!KerbalRecordPersistence.TryReadRecord(recordNode, out name, out record)) continue;
                    RosterRotationState.Records[name] = record;
                    loaded++;
                }

                RRLog.Info("[EAC] Loaded external data revision " + revision + " with " + loaded + " Kerbal records.");
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Error("[EAC] Failed loading external data revision " + revision + ": " + ex);
                return false;
            }
        }

        internal static bool TryWriteSnapshot(ConfigNode scenarioRoot, out long revision)
        {
            revision = 0;
            if (scenarioRoot == null || !RosterRotationState.ExternalDataStorageEnabled) return false;

            string dir = RevisionDirectory;
            if (string.IsNullOrEmpty(dir)) return false;

            try
            {
                EnsureRuntimeSaveKey();
                Directory.CreateDirectory(dir);

                string contentHash;
                ConfigNode fileRoot = BuildSnapshotPayload(out contentHash);

                // KSP save events are much more frequent than meaningful EAC changes.
                // Reuse the current immutable revision when the sorted record payload is
                // identical instead of creating a file for every scene/autosave callback.
                long priorRevision = ParseLong(scenarioRoot.GetValue(RevisionValue), 0);
                if (priorRevision <= 0) priorRevision = _runtimeRevision;
                bool priorMatches =
                    priorRevision > 0 &&
                    ((priorRevision == _runtimeRevision &&
                      !string.IsNullOrEmpty(_runtimeContentHash) &&
                      string.Equals(_runtimeContentHash, contentHash, StringComparison.OrdinalIgnoreCase) &&
                      File.Exists(GetRevisionPath(priorRevision))) ||
                     SnapshotHashMatches(priorRevision, contentHash));

                if (priorMatches)
                {
                    revision = priorRevision;
                    _runtimeRevision = revision;
                    _runtimeContentHash = contentHash;
                    scenarioRoot.SetValue(VersionValue, SchemaVersion.ToString(CultureInfo.InvariantCulture), true);
                    scenarioRoot.SetValue(RevisionValue, revision.ToString(CultureInfo.InvariantCulture), true);
                    RRLog.Verbose("[EAC] Reused external data revision " + revision + " (record content unchanged).");
                    return true;
                }

                revision = DateTime.UtcNow.Ticks;
                string finalPath = GetRevisionPath(revision);
                while (File.Exists(finalPath))
                {
                    revision++;
                    finalPath = GetRevisionPath(revision);
                }

                SetOrAddValue(fileRoot, "schemaVersion", SchemaVersion.ToString(CultureInfo.InvariantCulture));
                SetOrAddValue(fileRoot, "revision", revision.ToString(CultureInfo.InvariantCulture));
                SetOrAddValue(fileRoot, "savedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                SetOrAddValue(fileRoot, ContentHashValue, contentHash);

                string tempPath = finalPath + ".tmp";
                fileRoot.Save(tempPath);

                ConfigNode verifyDisk = ConfigNode.Load(tempPath);
                ConfigNode verifyRoot = verifyDisk != null && verifyDisk.HasNode("EAC_DATA") ? verifyDisk.GetNode("EAC_DATA") : verifyDisk;
                if (verifyRoot == null ||
                    ParseLong(verifyRoot.GetValue("revision"), 0) != revision ||
                    !string.Equals(verifyRoot.GetValue(ContentHashValue), contentHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Snapshot verification failed.");

                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(tempPath, finalPath);

                scenarioRoot.SetValue(VersionValue, SchemaVersion.ToString(CultureInfo.InvariantCulture), true);
                scenarioRoot.SetValue(RevisionValue, revision.ToString(CultureInfo.InvariantCulture), true);
                _runtimeRevision = revision;
                _runtimeContentHash = contentHash;
                RRLog.Info("[EAC] Wrote external data revision " + revision + " (" + RosterRotationState.Records.Count + " Kerbal records).");

                CleanupUnreferencedRevisions(revision);
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Error("[EAC] Failed writing external data snapshot; falling back to embedded EAC records for this save: " + ex);
                revision = 0;
                scenarioRoot.RemoveValue(VersionValue);
                scenarioRoot.RemoveValue(RevisionValue);
                return false;
            }
        }

        internal static void RemoveReference(ConfigNode scenarioRoot)
        {
            if (scenarioRoot == null) return;
            scenarioRoot.RemoveValue(VersionValue);
            scenarioRoot.RemoveValue(RevisionValue);
        }

        /// <summary>
        /// Deletes only revisions that are not referenced by any .sfs currently stored
        /// beneath the active save folder. The newest few unreferenced revisions are
        /// retained as a rollback cushion. If the reference scan cannot be completed,
        /// no deletion is attempted.
        /// </summary>
        internal static void CleanupUnreferencedRevisions(long currentRevision)
        {
            string dir = RevisionDirectory;
            string saveRoot = SaveDirectory;
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(saveRoot) || !Directory.Exists(dir)) return;

            try
            {
                HashSet<long> referenced;
                string failure;
                if (!TryCollectReferencedRevisions(saveRoot, out referenced, out failure))
                {
                    RRLog.Warn("[EAC] External revision cleanup skipped because save references could not be scanned safely"
                        + (string.IsNullOrEmpty(failure) ? "." : ": " + failure));
                    return;
                }

                if (currentRevision > 0) referenced.Add(currentRevision);

                var revisionFiles = new List<KeyValuePair<long, string>>();
                foreach (string file in Directory.GetFiles(dir, "rev-*.cfg", SearchOption.TopDirectoryOnly))
                {
                    long fileRevision;
                    if (!TryParseRevisionFileName(file, out fileRevision)) continue;
                    revisionFiles.Add(new KeyValuePair<long, string>(fileRevision, file));
                }

                revisionFiles.Sort((a, b) => b.Key.CompareTo(a.Key));

                int safetyKept = 0;
                int deleted = 0;
                for (int i = 0; i < revisionFiles.Count; i++)
                {
                    long fileRevision = revisionFiles[i].Key;
                    string file = revisionFiles[i].Value;
                    if (referenced.Contains(fileRevision)) continue;

                    if (safetyKept < UnreferencedSafetyRevisions)
                    {
                        safetyKept++;
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        RRLog.Warn("[EAC] Could not delete unreferenced external revision " + fileRevision + ": " + ex.Message);
                    }
                }

                if (deleted > 0)
                    RRLog.Info("[EAC] External revision cleanup deleted " + deleted + " unreferenced revision(s); "
                        + referenced.Count + " referenced/current and " + safetyKept + " safety revision(s) retained.");
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] External revision cleanup skipped after unexpected error: " + ex.Message);
            }
        }

        private static ConfigNode BuildSnapshotPayload(out string contentHash)
        {
            ConfigNode fileRoot = new ConfigNode("EAC_DATA");
            var canonical = new StringBuilder(16384);
            var names = new List<string>(RosterRotationState.Records.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);

            var ci = CultureInfo.InvariantCulture;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                RosterRotationState.KerbalRecord rec;
                if (string.IsNullOrEmpty(name) || !RosterRotationState.Records.TryGetValue(name, out rec) || rec == null)
                    continue;

                ConfigNode rNode = fileRoot.AddNode("Record");
                KerbalRecordPersistence.WriteRecordNode(rNode, name, rec, ci);
                canonical.Append(rNode.ToString()).Append('\n');
            }

            contentHash = ComputeSha256(canonical.ToString());
            return fileRoot;
        }

        private static bool SnapshotHashMatches(long revision, string expectedHash)
        {
            if (revision <= 0 || string.IsNullOrEmpty(expectedHash)) return false;
            string path = GetRevisionPath(revision);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            try
            {
                ConfigNode disk = ConfigNode.Load(path);
                ConfigNode root = disk != null && disk.HasNode("EAC_DATA") ? disk.GetNode("EAC_DATA") : disk;
                if (root == null) return false;
                return string.Equals(root.GetValue(ContentHashValue), expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeSha256(string text)
        {
            return EACHashing.ComputeSha256Hex(text);
        }

        private static bool TryCollectReferencedRevisions(string saveRoot, out HashSet<long> revisions, out string failure)
        {
            revisions = new HashSet<long>();
            var rawValues = new HashSet<string>(StringComparer.Ordinal);
            if (!EACSaveFileScanner.TryCollectValues(saveRoot, null, RevisionValue, rawValues, out failure))
                return false;

            foreach (string value in rawValues)
            {
                long parsed;
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                    revisions.Add(parsed);
            }
            return true;
        }

        private static bool TryParseRevisionFileName(string path, out long revision)
        {
            revision = 0;
            if (string.IsNullOrEmpty(path)) return false;
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name) || !name.StartsWith("rev-", StringComparison.OrdinalIgnoreCase)) return false;
            return long.TryParse(name.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out revision) && revision > 0;
        }

        private static void EnsureRuntimeSaveKey()
        {
            string key = (HighLogic.SaveFolder ?? string.Empty) + "|"
                + (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Title : string.Empty);
            if (string.Equals(_runtimeSaveKey, key, StringComparison.Ordinal)) return;
            _runtimeSaveKey = key;
            _runtimeRevision = 0;
            _runtimeContentHash = null;
        }

        private static string GetRevisionPath(long revision)
        {
            string dir = RevisionDirectory;
            if (string.IsNullOrEmpty(dir) || revision <= 0) return null;
            return Path.Combine(dir, "rev-" + revision.ToString(CultureInfo.InvariantCulture) + ".cfg");
        }

        private static void SetOrAddValue(ConfigNode node, string key, string value)
        {
            if (node.HasValue(key)) node.SetValue(key, value, true);
            else node.AddValue(key, value);
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static long ParseLong(string value, long fallback)
        {
            long parsed;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }
    }

    /// <summary>
    /// One-time, per-save opt-in reminder. The datastore remains OFF unless the user
    /// enables it in Advanced Settings; this message only explains the long-career option.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    internal sealed class EACExternalStoragePrompt : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(1.0f);

            if (HighLogic.CurrentGame == null) yield break;
            if (RosterRotationState.ExternalDataStorageEnabled || RosterRotationState.ExternalStoragePromptShown) yield break;

            RosterRotationState.ExternalStoragePromptShown = true;
            RosterRotationState.PostNotification(
                "EAC External Data Storage",
                "External EAC history storage is available but disabled. For long careers, enable it in EAC Advanced Settings > Data storage to reduce persistent.sfs growth.",
                MessageSystemButton.MessageButtonColor.YELLOW,
                MessageSystemButton.ButtonIcons.MESSAGE,
                10f);
            SaveScheduler.RequestSave("external storage opt-in notice");
        }
    }
}

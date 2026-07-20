using System;
using System.Globalization;
using System.IO;
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

        private static string _scenarioLoadKey;
        private static bool _canonicalScenarioLoadedForSave;
        private static bool _legacyScenarioLoadedForSave;
        private static bool _settingsNodeLoadedForSave;
        private static bool _startingCrewSetupCompletedLoadedForSave;
        private static bool _recordNodesLoadedForSave;

        internal static bool CanonicalScenarioLoadedForCurrentSave
        {
            get
            {
                ResetScenarioLoadFlagsIfSaveChanged();
                return _canonicalScenarioLoadedForSave;
            }
        }

        internal static bool StartingCrewSetupCompletedLoadedForCurrentSave
        {
            get
            {
                ResetScenarioLoadFlagsIfSaveChanged();
                return _settingsNodeLoadedForSave && _startingCrewSetupCompletedLoadedForSave;
            }
        }

        internal static bool PersistedKerbalRecordsLoadedForCurrentSave
        {
            get
            {
                ResetScenarioLoadFlagsIfSaveChanged();
                return _recordNodesLoadedForSave;
            }
        }

        internal static void ResetScenarioLoadFlagsIfSaveChanged()
        {
            string key = (HighLogic.SaveFolder ?? string.Empty) + "|" + (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Title : string.Empty);
            if (string.Equals(_scenarioLoadKey, key, StringComparison.Ordinal)) return;

            _scenarioLoadKey = key;
            _canonicalScenarioLoadedForSave = false;
            _legacyScenarioLoadedForSave = false;
            _settingsNodeLoadedForSave = false;
            _startingCrewSetupCompletedLoadedForSave = false;
            _recordNodesLoadedForSave = false;
        }

        internal static void MarkLegacyScenarioLoadedForCurrentSave()
        {
            ResetScenarioLoadFlagsIfSaveChanged();
            _legacyScenarioLoadedForSave = true;
        }

        private static void MarkCanonicalScenarioLoadedForCurrentSave()
        {
            ResetScenarioLoadFlagsIfSaveChanged();
            _canonicalScenarioLoadedForSave = true;
        }

        private static bool ScenarioNodeHasEacData(ConfigNode node)
        {
            if (node == null) return false;
            if (node.HasNode(NodeNameNew) || node.HasNode(NodeNameOld)) return true;
            if (node.HasNode("Settings") || node.HasNode("Record") || node.HasNode("CrashPending")) return true;
            return false;
        }

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                ResetScenarioLoadFlagsIfSaveChanged();

                if (!ScenarioNodeHasEacData(node) && _legacyScenarioLoadedForSave)
                {
                    // Old saves can briefly contain only RosterRotationScenario data plus a new
                    // empty EACScenario shell.  If the legacy scenario already imported data for
                    // this save, do not clear it just because the canonical node is empty.
                    MarkCanonicalScenarioLoadedForCurrentSave();
                    RRLog.Verbose("[EAC] Empty EACScenario loaded after legacy migration; preserving imported legacy state.");
                    return;
                }

                LoadFromScenarioNode(node, true, "EACScenario");
                MarkCanonicalScenarioLoadedForCurrentSave();
            }
            catch (Exception ex) { RRLog.Error($"OnLoad failed: {ex}"); }
        }

        internal static void LoadFromScenarioNode(ConfigNode node, bool clearRecords, string source)
        {
            try
            {
                if (clearRecords) RosterRotationState.Records.Clear();
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
                    _settingsNodeLoadedForSave = true;
                    _startingCrewSetupCompletedLoadedForSave = settings.EACNewGameCrewSetupCompleted;
                    KerbalRecordPersistence.ApplySettingsToState(settings, RosterRotationState.VerboseSettingsDirty);

                    RRLog.Verbose($"[EAC] Settings loaded from save: VerboseLogging={RosterRotationState.VerboseLogging}, SyncFlightTrackerFromEacOnce={RosterRotationState.SyncFlightTrackerFromEacOnce}, TraitGrowthEnabled={RosterRotationState.TraitGrowthEnabled}");
                }
                else
                {
                    _settingsNodeLoadedForSave = false;
                    _startingCrewSetupCompletedLoadedForSave = false;
                    EACGameSettings.TryApplyToStateFromGameParams();
                    RRLog.Verbose($"[EAC] Settings node missing; applied GameParameters defaults: VerboseLogging={RosterRotationState.VerboseLogging}, SyncFlightTrackerFromEacOnce={RosterRotationState.SyncFlightTrackerFromEacOnce}, TraitGrowthEnabled={RosterRotationState.TraitGrowthEnabled}");
                }

                _recordNodesLoadedForSave = root.HasNode("Record");

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
            catch (Exception ex) { RRLog.Error($"LoadFromScenarioNode failed ({source}): {ex}"); }
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
                    int liveFlights = r.Flights;
                    int savedFlights = GetRecoveredFlightCountFromRoster(kvp.Key, liveFlights);
                    r.Flights = savedFlights;
                    KerbalRecordPersistence.WriteRecordNode(rNode, kvp.Key, r, ci);
                    r.Flights = liveFlights;
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
    /// Backward-compatible load-only alias: old saves may still contain
    /// SCENARIO { name = RosterRotationScenario }.  Keep the type so old saves can migrate,
    /// but do not save EAC data through this legacy scenario.
    /// </summary>
    public class RosterRotationScenario : EACScenario
    {
        public override void OnLoad(ConfigNode node)
        {
            try
            {
                ResetScenarioLoadFlagsIfSaveChanged();

                if (CanonicalScenarioLoadedForCurrentSave)
                {
                    RRLog.Verbose("[EAC] Ignoring legacy RosterRotationScenario because EACScenario already loaded for this save.");
                    return;
                }

                LoadFromScenarioNode(node, true, "legacy RosterRotationScenario");
                MarkLegacyScenarioLoadedForCurrentSave();
                EACScenarioMigrationCleaner.QueueLegacyMigrationNoticeFromLegacyLoad();
                RRLog.Verbose("[EAC] Loaded legacy RosterRotationScenario for migration; future saves will use EACScenario only.");
            }
            catch (Exception ex) { RRLog.Error($"Legacy RosterRotationScenario.OnLoad failed: {ex}"); }
        }

        public override void OnSave(ConfigNode node)
        {
            // Intentionally empty.  The canonical EACScenario writes all current data.
            // The save-tree cleaner below removes this legacy SCENARIO node before the
            // persistent file is written, so it does not duplicate EACScenario data.
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class EACScenarioMigrationCleaner : MonoBehaviour
    {
        private const string MigrationNoticeMarkerFileName = "EAC.legacy-migration-notice.txt";
        private const string MigrationNoticeAckFileName = "EAC.legacy-migration-notice.ack";

        private static bool _pendingMigrationNotice;
        private static bool _migrationNoticeShown;
        private static string _pendingMigrationNoticeSaveKey;
        private static string _lastMigrationBackupPath;
        private static string _migrationBackupCreatedForSaveKey;

        private bool _gameStateSaveEventSubscribed;
        private bool _gameStateSaveEventUnavailableLogged;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            TrySubscribeToGameStateSave();
        }

        private void OnDestroy()
        {
            if (!_gameStateSaveEventSubscribed) return;

            try
            {
                if (GameEvents.onGameStateSave != null)
                    GameEvents.onGameStateSave.Remove(OnGameStateSave);
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed to unsubscribe scenario migration cleaner from onGameStateSave: " + ex.Message);
            }
            finally
            {
                _gameStateSaveEventSubscribed = false;
            }
        }

        private void Update()
        {
            // Some heavily modded installs initialize or replace this GameEvent after
            // MainMenu addons are created. Retry harmlessly until it is available.
            if (!_gameStateSaveEventSubscribed)
                TrySubscribeToGameStateSave();

            TryShowPendingMigrationNotice("main-menu persistent monitor");
        }

        private void TrySubscribeToGameStateSave()
        {
            if (_gameStateSaveEventSubscribed) return;

            try
            {
                if (GameEvents.onGameStateSave == null)
                {
                    if (!_gameStateSaveEventUnavailableLogged)
                    {
                        RRLog.Warn("[EAC] GameEvents.onGameStateSave is not available yet; scenario migration cleaner will retry.");
                        _gameStateSaveEventUnavailableLogged = true;
                    }
                    return;
                }

                GameEvents.onGameStateSave.Add(OnGameStateSave);
                _gameStateSaveEventSubscribed = true;

                if (_gameStateSaveEventUnavailableLogged)
                    RRLog.Info("[EAC] Scenario migration cleaner successfully subscribed to onGameStateSave after a delayed initialization.");
            }
            catch (Exception ex)
            {
                if (!_gameStateSaveEventUnavailableLogged)
                {
                    RRLog.Warn("[EAC] Unable to subscribe scenario migration cleaner to onGameStateSave; will retry: " + ex.Message);
                    _gameStateSaveEventUnavailableLogged = true;
                }
            }
        }

        internal static void QueueLegacyMigrationNoticeFromLegacyLoad()
        {
            try
            {
                string saveKey = GetCurrentSaveKey();
                if (string.IsNullOrEmpty(saveKey))
                {
                    RRLog.Warn("[EAC] Legacy RosterRotationScenario detected, but current save folder is not available yet; Space Center notice will retry from backup marker.");
                    return;
                }

                RRLog.Info("[EAC] Legacy RosterRotationScenario detected during load; preparing save backup and user notice.");

                string backupPath;
                if (EnsureMigrationBackupForCurrentSave(saveKey, out backupPath))
                {
                    _lastMigrationBackupPath = backupPath;
                    WriteMigrationNoticeMarker(backupPath);
                    QueueMigrationNotice(saveKey);
                    RRLog.Info("[EAC] Backed up persistent file after detecting legacy RosterRotationScenario: " + backupPath);
                    RRLog.Info("[EAC] Legacy RosterRotationScenario detected; user migration notice queued for Space Center.");
                }
                else
                {
                    RRLog.Warn("[EAC] Legacy RosterRotationScenario detected, but persistent.sfs backup could not be created; migration notice will not be shown yet.");
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed to queue legacy scenario migration notice: " + ex.Message);
            }
        }

        internal static bool TryShowPendingMigrationNotice(string reason)
        {
            try
            {
                if (_migrationNoticeShown) return false;
                if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return false;

                string saveKey = GetCurrentSaveKey();
                if (string.IsNullOrEmpty(saveKey)) return false;

                ArmNoticeFromMarkerOrExistingBackup(saveKey);

                if (!_pendingMigrationNotice) return false;
                if (!string.IsNullOrEmpty(_pendingMigrationNoticeSaveKey) &&
                    !string.Equals(_pendingMigrationNoticeSaveKey, saveKey, StringComparison.Ordinal))
                {
                    return false;
                }

                _migrationNoticeShown = true;
                _pendingMigrationNotice = false;
                ShowMigrationNotice();
                WriteMigrationNoticeAck();
                DeleteMigrationNoticeMarker();
                RRLog.Info("[EAC] Displayed legacy scenario migration notice (" + reason + ").");
                return true;
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed while trying to show legacy scenario migration notice: " + ex.Message);
                return false;
            }
        }

        private static void QueueMigrationNotice(string saveKey)
        {
            _pendingMigrationNoticeSaveKey = saveKey;
            _pendingMigrationNotice = true;
            _migrationNoticeShown = false;
        }

        private static void ArmNoticeFromMarkerOrExistingBackup(string saveKey)
        {
            if (_pendingMigrationNotice) return;
            if (MigrationNoticeAckExists()) return;

            string markerPath = GetMigrationNoticeMarkerPath();
            if (!string.IsNullOrEmpty(markerPath) && File.Exists(markerPath))
            {
                string backupPath = null;
                try { backupPath = File.ReadAllText(markerPath).Trim(); }
                catch { }

                if (!string.IsNullOrEmpty(backupPath))
                    _lastMigrationBackupPath = backupPath;

                QueueMigrationNotice(saveKey);
                RRLog.Info("[EAC] Legacy scenario migration notice armed from marker file: " + markerPath);
                return;
            }

            // Covers the case from earlier builds where the legacy node was already cleaned
            // and a backup exists, but no notice marker was written yet.
            string existingBackup = FindLatestMigrationBackupPath();
            if (!string.IsNullOrEmpty(existingBackup))
            {
                _lastMigrationBackupPath = existingBackup;
                WriteMigrationNoticeMarker(existingBackup);
                QueueMigrationNotice(saveKey);
                RRLog.Info("[EAC] Legacy scenario migration notice armed from existing backup: " + existingBackup);
            }
        }

        private static bool EnsureMigrationBackupForCurrentSave(string saveKey, out string backupPath)
        {
            backupPath = _lastMigrationBackupPath;

            if (!string.IsNullOrEmpty(saveKey) &&
                string.Equals(_migrationBackupCreatedForSaveKey, saveKey, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(_lastMigrationBackupPath) &&
                File.Exists(_lastMigrationBackupPath))
            {
                backupPath = _lastMigrationBackupPath;
                return true;
            }

            if (!TryBackupPersistentSave(out backupPath))
                return false;

            _migrationBackupCreatedForSaveKey = saveKey;
            _lastMigrationBackupPath = backupPath;
            return true;
        }

        private static void OnGameStateSave(ConfigNode root)
        {
            try
            {
                int legacyCount = CountLegacyScenarioNodes(root);
                if (legacyCount <= 0) return;

                string saveKey = GetCurrentSaveKey();
                string backupPath;
                if (!EnsureMigrationBackupForCurrentSave(saveKey, out backupPath))
                {
                    RRLog.Warn("[EAC] Legacy RosterRotationScenario node found, but persistent.sfs backup failed; leaving legacy node untouched for safety.");
                    return;
                }

                int removed = RemoveLegacyScenarioNodes(root);
                if (removed > 0)
                {
                    WriteMigrationNoticeMarker(backupPath);
                    QueueMigrationNotice(saveKey);

                    RRLog.Info("[EAC] Backed up persistent file before legacy scenario cleanup: " + backupPath);
                    RRLog.Info("[EAC] Removed legacy RosterRotationScenario node(s) from save: " + removed);
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed to remove legacy RosterRotationScenario node from save: " + ex.Message);
            }
        }

        private static bool TryBackupPersistentSave(out string backupPath)
        {
            backupPath = null;

            try
            {
                string sourcePath = GetPersistentSavePath();
                if (string.IsNullOrEmpty(sourcePath)) return false;

                if (!File.Exists(sourcePath))
                {
                    RRLog.Warn("[EAC] Cannot back up persistent.sfs before legacy scenario cleanup because the file was not found: " + sourcePath);
                    return false;
                }

                string directory = Path.GetDirectoryName(sourcePath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string candidate = Path.Combine(directory, "persistent.EAC-legacy-backup-" + timestamp + ".sfs");

                int suffix = 1;
                backupPath = candidate;
                while (File.Exists(backupPath))
                {
                    backupPath = Path.Combine(directory, "persistent.EAC-legacy-backup-" + timestamp + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".sfs");
                    suffix++;
                }

                File.Copy(sourcePath, backupPath, false);
                return File.Exists(backupPath);
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed to back up persistent.sfs before legacy scenario cleanup: " + ex.Message);
                backupPath = null;
                return false;
            }
        }

        private static string GetSaveDirectory()
        {
            try
            {
                if (string.IsNullOrEmpty(HighLogic.SaveFolder)) return null;

                return Path.Combine(
                    KSPUtil.ApplicationRootPath,
                    "saves",
                    HighLogic.SaveFolder);
            }
            catch
            {
                return null;
            }
        }

        private static string GetPersistentSavePath()
        {
            try
            {
                string directory = GetSaveDirectory();
                if (string.IsNullOrEmpty(directory)) return null;
                return Path.Combine(directory, "persistent.sfs");
            }
            catch
            {
                return null;
            }
        }

        private static string GetCurrentSaveKey()
        {
            // Save title can change during scenario load -> Space Center transition; the
            // folder is the stable identity for this one-shot migration notice.
            return HighLogic.SaveFolder ?? string.Empty;
        }

        private static string GetMigrationNoticeMarkerPath()
        {
            string directory = GetSaveDirectory();
            return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, MigrationNoticeMarkerFileName);
        }

        private static string GetMigrationNoticeAckPath()
        {
            string directory = GetSaveDirectory();
            return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, MigrationNoticeAckFileName);
        }

        private static bool MigrationNoticeAckExists()
        {
            string path = GetMigrationNoticeAckPath();
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        private static void WriteMigrationNoticeMarker(string backupPath)
        {
            try
            {
                string path = GetMigrationNoticeMarkerPath();
                if (string.IsNullOrEmpty(path)) return;
                File.WriteAllText(path, backupPath ?? string.Empty);
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed to write legacy scenario migration notice marker: " + ex.Message);
            }
        }

        private static void DeleteMigrationNoticeMarker()
        {
            try
            {
                string path = GetMigrationNoticeMarkerPath();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                RRLog.Verbose("[EAC] Could not delete legacy scenario migration notice marker: " + ex.Message);
            }
        }

        private static void WriteMigrationNoticeAck()
        {
            try
            {
                string path = GetMigrationNoticeAckPath();
                if (string.IsNullOrEmpty(path)) return;
                File.WriteAllText(path, DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                RRLog.Verbose("[EAC] Could not write legacy scenario migration notice acknowledgement: " + ex.Message);
            }
        }

        private static string FindLatestMigrationBackupPath()
        {
            try
            {
                string directory = GetSaveDirectory();
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

                string[] files = Directory.GetFiles(directory, "persistent.EAC-legacy-backup-*.sfs");
                if (files == null || files.Length == 0) return null;

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files[files.Length - 1];
            }
            catch
            {
                return null;
            }
        }

        private static void ShowMigrationNotice()
        {
            const string title = "Enhanced Astronaut Complex";
            string message = "Your persistent file contained outdated EAC save information.\n\n" +
                             "EAC backed up your persistent file and saved the current EAC save information in the new format.";

            if (!string.IsNullOrEmpty(_lastMigrationBackupPath))
                message += "\n\nBackup created:\n" + _lastMigrationBackupPath;

            try
            {
                PopupDialog.SpawnPopupDialog(
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    "EACLegacySaveMigrationNotice",
                    title,
                    message,
                    "OK",
                    false,
                    HighLogic.UISkin);
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed to show legacy scenario migration popup: " + ex.Message);
                ScreenMessages.PostScreenMessage(message, 10f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private static int CountLegacyScenarioNodes(ConfigNode node)
        {
            if (node == null) return 0;

            int count = 0;
            foreach (ConfigNode child in node.GetNodes())
            {
                if (child == null) continue;

                if (string.Equals(child.name, "SCENARIO", StringComparison.Ordinal) &&
                    string.Equals(child.GetValue("name"), "RosterRotationScenario", StringComparison.Ordinal))
                {
                    count++;
                    continue;
                }

                count += CountLegacyScenarioNodes(child);
            }

            return count;
        }

        private static int RemoveLegacyScenarioNodes(ConfigNode node)
        {
            if (node == null) return 0;

            int removed = 0;
            var toRemove = new System.Collections.Generic.List<ConfigNode>();

            foreach (ConfigNode child in node.GetNodes())
            {
                if (child == null) continue;

                if (string.Equals(child.name, "SCENARIO", StringComparison.Ordinal) &&
                    string.Equals(child.GetValue("name"), "RosterRotationScenario", StringComparison.Ordinal))
                {
                    toRemove.Add(child);
                    continue;
                }

                removed += RemoveLegacyScenarioNodes(child);
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                node.RemoveNode(toRemove[i]);
                removed++;
            }

            return removed;
        }
    }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class EACScenarioMigrationSpaceCenterNotice : MonoBehaviour
    {
        private int _attempts;
        private float _nextAttemptTime;

        private void Start()
        {
            TryShowOrFinish("space-center Start");
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextAttemptTime) return;
            _nextAttemptTime = Time.realtimeSinceStartup + 1f;

            if (TryShowOrFinish("space-center retry")) return;

            _attempts++;
            if (_attempts > 30)
                Destroy(this);
        }

        private bool TryShowOrFinish(string reason)
        {
            if (EACScenarioMigrationCleaner.TryShowPendingMigrationNotice(reason))
            {
                Destroy(this);
                return true;
            }

            return false;
        }
    }
}

// EAC - Enhanced Astronaut Complex - Mod.cs
// Core shared state, flight tracker, and KSC UI.

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP;
using KSP.UI.Screens;

namespace RosterRotation
{
    public enum TrainingType
    {
        None              = 0,
        InitialHire       = 1,
        ExperienceUpgrade = 2,
        RecallRefresher   = 3
    }

    public enum EACNotificationType
    {
        General    = 0,
        Birthday   = 1,
        Training   = 2,
        Retirement = 3,
        Death      = 4
    }

    // ── Shared state ───────────────────────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RosterRotationFlightTracker : MonoBehaviour
    {
        private void Start()
        {
            GameEvents.OnVesselRecoveryRequested.Add(OnRecover);
            GameEvents.onKerbalStatusChange.Add(OnKerbalStatusChange);
        }

        private void OnDestroy()
        {
            GameEvents.OnVesselRecoveryRequested.Remove(OnRecover);
            GameEvents.onKerbalStatusChange.Remove(OnKerbalStatusChange);
        }

        private void OnRecover(Vessel v)
        {
            if (v == null) return;
            double now = Planetarium.GetUniversalTime();
            foreach (var pcm in v.GetVesselCrew())
            {
                if (pcm == null) continue;
                var r = RosterRotationState.GetOrCreate(pcm.name);
                r.Flights++;
                r.LastFlightUT = now;
                r.MissionStartUT = 0;
                r.LastMissionDeathCheckUT = 0;
                RosterRotationKSCUI.TryApplyVeteranTraitGrowthOnRecovery(pcm, r, v, now);

                bool killedInFlight = pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead
                                   || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing
                                   || r.DeathUT > 0;
                if (killedInFlight)
                {
                    r.RetirementScheduled = false;
                }
                else if (r.RetirementScheduled && !r.Retired)
                {
                    r.RetirementScheduled    = false;
                    r.Retired                = true;
                    r.RetiredUT              = now;
                    r.ExperienceAtRetire     = (int)pcm.experienceLevel;
                    r.RetirementWarned       = false;
                    if (string.IsNullOrEmpty(r.OriginalTrait)) r.OriginalTrait = pcm.trait;
                    r.OriginalType = pcm.type;
                    pcm.inactive        = true;
                    pcm.inactiveTimeEnd = now + RosterRotationState.YearSeconds * 1000.0;
                    RosterRotationState.InvalidateRetiredCache();
                    RetiredKerbalCleanupService.ResetAutoCleanupRequest(pcm.name);

                    RosterRotationState.PostNotification(
                        EACNotificationType.Retirement, $"Retired — {pcm.name}",
                        $"{pcm.name} has retired following mission recovery. ({RosterRotationState.FormatGameDate(now)})",
                        MessageSystemButton.MessageButtonColor.ORANGE,
                        MessageSystemButton.ButtonIcons.MESSAGE);
                }
            }

            CrashSeverityState.HandleRecovery(v, now);
        }

        private void OnKerbalStatusChange(
            ProtoCrewMember pcm,
            ProtoCrewMember.RosterStatus oldStatus,
            ProtoCrewMember.RosterStatus newStatus)
        {
            if (pcm == null) return;
            var rec = RosterRotationState.GetOrCreate(pcm.name);
            double now = Planetarium.GetUniversalTime();

            if (newStatus == ProtoCrewMember.RosterStatus.Available && rec.DeathUT > 0)
            {
                rec.DeathUT = 0;
                rec.DiedOnMission = false;
                rec.PendingMissionDeath = false;
                rec.MissionStartUT = 0;
                rec.LastMissionDeathCheckUT = 0;
                RetiredKerbalCleanupService.ResetAutoCleanupRequest(pcm.name);
                SaveScheduler.RequestSave("kerbal status change");
                return;
            }

            bool isDeath = newStatus == ProtoCrewMember.RosterStatus.Dead
                        || newStatus == ProtoCrewMember.RosterStatus.Missing;
            if (!isDeath || rec.DeathUT > 0) return;

            rec.DeathUT = now;
            rec.DiedOnMission = false;
            rec.PendingMissionDeath = false;
            rec.MissionStartUT = 0;
            rec.LastMissionDeathCheckUT = 0;
            RetiredKerbalCleanupService.ResetAutoCleanupRequest(pcm.name);
            int kiaAge = RosterRotationState.GetKerbalAge(rec, now);

            if (RosterRotationState.DeathNotificationsEnabled)
            {
                string kiaAge2 = kiaAge >= 0 ? "Age " + kiaAge + ", " : "";
                string kiaDate = RosterRotationState.FormatGameDate(now);
                RosterRotationState.PostNotification(
                    EACNotificationType.Death, "K.I.A. — " + pcm.name,
                    pcm.name + " was killed in action. " + kiaAge2 + kiaDate + ".",
                    MessageSystemButton.MessageButtonColor.RED,
                    MessageSystemButton.ButtonIcons.ALERT, 12f);
            }
            SaveScheduler.RequestSave("kerbal death state");
        }
    }

    // ── KSC UI ─────────────────────────────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RosterRotationKSCUI : MonoBehaviour
    {
        private const string ModVersion = "1.1.2";
        private const string WindowTitle = "Enhanced Astronaut Complex v" + ModVersion;

        public static bool RetiredTabSelected;

        private Texture2D _iconTex;
        private ApplicationLauncherButton _btn;
        private bool    _show;
        private Rect    _window = new Rect(300, 100, 940, 620);
        private Vector2 _scroll;

        private enum AcOverlay { None, Applicants, Training, ForceRetire }
        private AcOverlay _acOverlay      = AcOverlay.None;
        private bool      _prevACOpen;
        private Rect      _overlayWindow  = new Rect(80, 120, 820, 500);
        private Vector2   _overlayScroll;
        private GUIStyle  _windowStyle;
        private GUISkin   _windowStyleSourceSkin;
        private bool      _windowStyleReady;

        private enum Tab { Applicants, Active, Assigned, Training, RandR, Retired, Lost }
        private Tab _tab = Tab.Active;

        private float _nextCheckRT  = 0f;
        private const float CHECK_INTERVAL = 5f;

        private ProtoCrewMember _pendingTrainKerbal;
        private bool            _showTrainConfirm;
        private bool _pendingForceRefresh = false;

        private const float UiCacheSeconds = 0.25f;
        private static float _lastCrewCapacityCacheRT = -10f;
        private static int _cachedActiveNonRetiredCount;
        private static int _cachedMaxCrew = int.MaxValue;
        private static float _lastHireCostCacheRT = -10f;
        private static double _cachedHireCost = RosterRotationState.TrainingBaseFundsCost;
        private static int _cachedHireCostActiveCount = int.MinValue;
        private static float _cachedHireCostFacilityLevel = float.NaN;
        private static MethodInfo _cachedRecruitCostCountMethod;
        private static MethodInfo _cachedRecruitCostFacilityMethod;
        private static bool _searchedFlightTrackerFlightsMethod;
        private static MethodInfo _cachedFlightTrackerFlightsMethod;
        private static Type _cachedFlightTrackerApiType;
        private static FieldInfo _cachedFlightTrackerApiInstanceField;
        private static PropertyInfo _cachedFlightTrackerApiInstanceProperty;
        private static bool _searchedFlightTrackerStore;
        private static Type _cachedFlightTrackerStoreType;
        private static FieldInfo _cachedFlightTrackerStoreInstanceField;
        private static PropertyInfo _cachedFlightTrackerStoreInstanceProperty;
        private static FieldInfo _cachedFlightTrackerFlightsField;
        private static bool _searchedFlightTrackerMissionHoursMethod;
        private static MethodInfo _cachedFlightTrackerMissionHoursMethod;

        private bool _flightTrackerSyncExecutedThisSession;

        private float _lastRosterRowsCacheRT = -10f;
        private Tab _cachedRosterRowsTab = (Tab)(-1);
        private List<RosterRowData> _cachedRosterRows = new List<RosterRowData>();

        private float _lastApplicantsCacheRT = -10f;
        private List<ProtoCrewMember> _cachedApplicants = new List<ProtoCrewMember>();

        private float _lastTrainingCandidatesCacheRT = -10f;
        private List<ProtoCrewMember> _cachedTrainingCandidates = new List<ProtoCrewMember>();

        private float _lastRetireRowsCacheRT = -10f;
        private List<RosterRowData> _cachedRetireRows = new List<RosterRowData>();

        private sealed class RosterRowData
        {
            public ProtoCrewMember Kerbal;
            public RosterRotationState.KerbalRecord Record;
            public bool Retired;
            public bool HasFlown;
            public bool IsLost;
            public bool IsAssigned;
            public string Status;
            public string AgeText;
            public int DisplayFlights;
            public int EffectiveStars;
            public bool InTrainingLockout;
        }

        private void Start()
        {
            _iconTex = GameDatabase.Instance.GetTexture("EAC/Icons/icon", false);
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
            GameEvents.onKerbalTypeChange.Add(OnKerbalTypeChange);
            StartCoroutine(StartupDelayed());
            StartCoroutine(PerfProfilerCoroutine());
        }

        /// <summary>
        /// Periodic performance logger — dumps EAC overhead stats every 30s when verbose logging is on.
        /// Helps identify which subsystems are consuming frame time.
        /// </summary>
        private System.Collections.IEnumerator PerfProfilerCoroutine()
        {
            RRLog.Info("[EAC] PerfProfiler started (reports every 10s when verbose logging is on)");
            yield return new WaitForSeconds(5f);
            var wait = new WaitForSeconds(10f);
            while (true)
            {
                if (RRLog.VerboseEnabled)
                {
                    var sb = new System.Text.StringBuilder("[EAC] PerfReport: ");
                    sb.Append("ACOpenPolls=").Append(ACOpenCache.PollCount);
                    sb.Append(" CacheHits=").Append(ACOpenCache.CacheHitCount);
                    sb.Append(" ExpensiveScans=").Append(ACOpenCache.ExpensiveScanCount);
                    sb.Append(" ScanMs=").Append(ACOpenCache.TotalScanMs.ToString("F1"));
                    sb.Append(" FPS=").Append((1f / Time.unscaledDeltaTime).ToString("F1"));

                    ACOpenCache.PollCount = 0;
                    ACOpenCache.CacheHitCount = 0;
                    ACOpenCache.ExpensiveScanCount = 0;
                    ACOpenCache.TotalScanMs = 0;

                    RRLog.Info(sb.ToString());
                }
                yield return wait;
            }
        }

        private System.Collections.IEnumerator StartupDelayed()
        {
            yield return null;
            yield return null;
            ApplyGrantedLevels();
            HealRespawnedKerbals();
            if (RosterRotationState.AgingEnabled)
                InitializeExistingKerbalAges();
            _pendingForceRefresh = true;
        }

        private static void HealRespawnedKerbals()
        {
            try
            {
                var roster = HighLogic.CurrentGame?.CrewRoster;
                if (roster == null) return;
                int healed = 0;
                foreach (var k in roster.Crew)
                {
                    if (k == null) continue;
                    if (k.rosterStatus != ProtoCrewMember.RosterStatus.Available &&
                        k.rosterStatus != ProtoCrewMember.RosterStatus.Assigned) continue;
                    if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                    if (rec.DeathUT <= 0) continue;
                    if (rec.DiedOnMission || rec.PendingMissionDeath) continue;
                    rec.DeathUT = 0;
                    rec.DiedOnMission = false;
                    rec.PendingMissionDeath = false;
                    rec.MissionStartUT = 0;
                    rec.LastMissionDeathCheckUT = 0;
                    healed++;
                }
                if (healed > 0)
                    SaveScheduler.RequestSave("heal respawned kerbals");
            }
            catch (Exception ex) { RRLog.Warn($"HealRespawnedKerbals: {ex.Message}"); }
        }

        private static void InitializeExistingKerbalAges()
        {
            try
            {
                var roster = HighLogic.CurrentGame?.CrewRoster;
                if (roster == null) return;
                double nowUT = Planetarium.GetUniversalTime();
                int newCount = 0;
                foreach (var k in roster.Crew)
                {
                    if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                    var rec = RosterRotationState.GetOrCreate(k.name);
                    if (rec.LastAgedYears >= 0) continue;
                    AssignAgeByExperience(k, rec, nowUT);
                    newCount++;
                }
                if (newCount > 0)
                    SaveScheduler.RequestSave("initialize existing kerbal ages");
            }
            catch (Exception ex) { RRLog.Warn($"InitializeExistingKerbalAges: {ex.Message}"); }
        }

        private void ApplyGrantedLevels()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;
            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (rec.GrantedLevel <= 0 || (int)k.experienceLevel >= rec.GrantedLevel) continue;
                try { k.experienceLevel = rec.GrantedLevel; }
                catch (Exception ex) { RRLog.Warn($"ApplyGrantedLevels: {k.name} failed: {ex.Message}"); }
            }
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
            GameEvents.onKerbalTypeChange.Remove(OnKerbalTypeChange);
            RemoveButton();
        }

        private void Update()
        {
            int pending = RosterRotationKSCUIBridge.ConsumeOverlay();
            if (pending != RosterRotationKSCUIBridge.AcOverlayNone)
            {
                _acOverlay = (_acOverlay == AcOverlay.None) ? AcOverlay.Applicants : AcOverlay.None;
                InvalidateUICaches();
            }

            if (_pendingForceRefresh)
            {
                _pendingForceRefresh = false;
                ACPatches.ForceRefresh();
                InvalidateUICaches();
            }

            MaybeRunPendingFlightTrackerSync();

            if (Time.realtimeSinceStartup < _nextCheckRT) return;
            _nextCheckRT = Time.realtimeSinceStartup + CHECK_INTERVAL;
            CheckTrainingCompletion();
            if (RosterRotationState.AgingEnabled)
                CheckAgingAndRetirement();
        }

        private void OnKerbalTypeChange(ProtoCrewMember pcm,
                                        ProtoCrewMember.KerbalType oldType,
                                        ProtoCrewMember.KerbalType newType)
        {
            if (pcm == null) return;
            if (oldType != ProtoCrewMember.KerbalType.Applicant) return;
            if (newType != ProtoCrewMember.KerbalType.Crew) return;

            double nowUT = Planetarium.GetUniversalTime();
            var rec = RosterRotationState.GetOrCreate(pcm.name);

            if (rec.Training == TrainingType.None)
            {
                double sec = RosterRotationState.TrainingInitialDays * RosterRotationState.DaySeconds;
                pcm.inactive        = true;
                pcm.inactiveTimeEnd = nowUT + sec;
                rec.Training            = TrainingType.InitialHire;
                rec.TrainingTargetLevel = 0;
            }

            if (RosterRotationState.AgingEnabled && rec.LastAgedYears < 0)
                AssignAgeOnHire(pcm, rec, nowUT);

            InvalidateUICaches();
            _pendingForceRefresh = true;
        }

        private void CheckTrainingCompletion()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;
            double now = Planetarium.GetUniversalTime();
            bool anyDone = false;

            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (rec.Training == TrainingType.None) continue;

                bool done = !k.inactive || (k.inactiveTimeEnd > 0 && now >= k.inactiveTimeEnd);
                if (!done) continue;

                if (rec.Training == TrainingType.ExperienceUpgrade)
                {
                    int target = rec.TrainingTargetLevel;
                    if (target >= 1 && target <= 3)
                    {
                        try { k.experienceLevel = target; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:422", "Suppressed exception in Mod.cs:422", ex); }
                        rec.GrantedLevel = target;
                        try { k.careerLog.AddEntry("Training" + target, "Kerbin"); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:424", "Suppressed exception in Mod.cs:424", ex); }

                        if (RosterRotationState.AgingEnabled && rec.NaturalRetirementUT > 0)
                        {
                            rec.RetirementDelayYears += target;
                            rec.RetirementWarned = false;
                        }

                        RosterRotationKSCUI.TryApplyTrainingTraitGrowth(k, rec, target, now);

                        RosterRotationState.PostNotification(
                            EACNotificationType.Training, $"Training Complete — {k.name}",
                            $"{k.name} has completed Level {target} training and is ready for duty. ({RosterRotationState.FormatGameDate(now)})",
                            MessageSystemButton.MessageButtonColor.GREEN,
                            MessageSystemButton.ButtonIcons.COMPLETE, 6f);
                    }
                }
                else if (rec.Training == TrainingType.InitialHire)
                {
                    RosterRotationState.PostNotification(
                        EACNotificationType.Training, "Training Complete — " + k.name,
                        k.name + " has completed initial training and is ready for active duty. (" + RosterRotationState.FormatGameDate(now) + ")",
                        MessageSystemButton.MessageButtonColor.GREEN,
                        MessageSystemButton.ButtonIcons.COMPLETE, 8f);
                    rec.TrainingEndUT = now;
                }
                else if (rec.Training == TrainingType.RecallRefresher)
                {
                    RosterRotationState.PostNotification(
                        EACNotificationType.Training, "Refresher Complete — " + k.name,
                        k.name + " has completed refresher training and is cleared for missions. (" + RosterRotationState.FormatGameDate(now) + ")",
                        MessageSystemButton.MessageButtonColor.GREEN,
                        MessageSystemButton.ButtonIcons.COMPLETE, 8f);
                }

                rec.Training = TrainingType.None;
                rec.TrainingTargetLevel = 0;
                if (k.inactive && now >= k.inactiveTimeEnd) k.inactive = false;
                anyDone = true;
            }

            if (anyDone)
            {
                InvalidateUICaches();
                SaveScheduler.RequestSave("training completion");
            }
        }

        // ── App Launcher ───────────────────────────────────────────────────────
        private void OnAppLauncherReady()
        {
            if (_btn != null || ApplicationLauncher.Instance == null) return;
            _btn = ApplicationLauncher.Instance.AddModApplication(
                () => _show = true, () => _show = false,
                null, null, null, null,
                ApplicationLauncher.AppScenes.SPACECENTER, _iconTex);
        }

        private void OnAppLauncherDestroyed() { _btn = null; _show = false; }
        private void RemoveButton()
        {
            if (_btn != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_btn);
            _btn = null;
        }

        private void DrawHallButton()
        {
            bool hallAvailable = HallOfHistoryWindow.IsAvailable;
            bool hallOpen = HallOfHistoryWindow.IsOpen;

            GUI.enabled = hallAvailable;
            string label = hallOpen ? "Close Hall" : "Open Hall";
            if (GUILayout.Button(label, GUILayout.Width(110), GUILayout.Height(24)))
            {
                if (hallOpen)
                    HallOfHistoryWindow.HideWindow();
                else
                    HallOfHistoryWindow.ShowWindow(true);
            }
            GUI.enabled = true;
        }

        private void DrawHallStatusHint()
        {
            if (HallOfHistoryWindow.IsAvailable)
                return;

            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.65f);
            GUILayout.Label("Hall of History is still initializing.");
            GUI.color = prev;
        }

        // ── OnGUI ──────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            GUISkin previousSkin = GUI.skin;
            GUISkin kspSkin = KspGuiSkin.Current;
            if (kspSkin != null)
                GUI.skin = kspSkin;

            try
            {
                if (!_windowStyleReady || !ReferenceEquals(_windowStyleSourceSkin, kspSkin))
                {
                    _windowStyleReady = true;
                    _windowStyleSourceSkin = kspSkin;
                    _windowStyle = new GUIStyle(KspGuiSkin.Window);
                }

                if (_show)
                    _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, WindowTitle, _windowStyle);

                bool acOpen = ACOpenCache.IsOpen;
                if (!acOpen && _prevACOpen) _acOverlay = AcOverlay.None;
                _prevACOpen = acOpen;
                if (!acOpen) RetiredTabSelected = false;

                if (acOpen && _acOverlay != AcOverlay.None)
                {
                    string title = GetOverlayTitle(_acOverlay);
                    _overlayWindow = GUILayout.Window(
                        GetInstanceID() + 55555, _overlayWindow, DrawACOverlay, title, _windowStyle);
                }
            }
            finally
            {
                GUI.skin = previousSkin;
            }
        }

        private string GetOverlayTitle(AcOverlay ov)
        {
            if (ov == AcOverlay.Applicants)  return $"EAC v{ModVersion}: Applicants";
            if (ov == AcOverlay.Training)    return $"EAC v{ModVersion}: Send to Training";
            if (ov == AcOverlay.ForceRetire) return $"EAC v{ModVersion}: Force Retire";
            return WindowTitle;
        }

        // ── Main toolbar window ────────────────────────────────────────────────
        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == Tab.Applicants, "Applicants", "Button", GUILayout.Width(110))) _tab = Tab.Applicants;
            if (GUILayout.Toggle(_tab == Tab.Active,     "Active",     "Button", GUILayout.Width(80)))  _tab = Tab.Active;
            if (GUILayout.Toggle(_tab == Tab.Assigned,   "Assigned",   "Button", GUILayout.Width(90)))  _tab = Tab.Assigned;
            if (GUILayout.Toggle(_tab == Tab.Training,   "Training",   "Button", GUILayout.Width(90)))  _tab = Tab.Training;
            if (GUILayout.Toggle(_tab == Tab.RandR,      "R&R",        "Button", GUILayout.Width(60)))  _tab = Tab.RandR;
            if (GUILayout.Toggle(_tab == Tab.Retired,    "Retired",    "Button", GUILayout.Width(90)))  _tab = Tab.Retired;
            if (GUILayout.Toggle(_tab == Tab.Lost,       "Lost",       "Button", GUILayout.Width(70)))  _tab = Tab.Lost;
            GUILayout.FlexibleSpace();
            DrawHallButton();
            GUILayout.EndHorizontal();
            DrawHallStatusHint();
            GUILayout.Space(6);

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) { GUILayout.Label("Crew roster not available."); GUILayout.EndVertical(); GUI.DragWindow(); return; }

            double now = Planetarium.GetUniversalTime();
            if (_tab == Tab.Applicants) DrawApplicantsTab(roster);
            else if (_tab == Tab.Training) DrawTrainingTab(roster, now);
            else DrawRosterTab(roster, now);

            GUILayout.Space(8);
            if (GUILayout.Button("Close")) _show = false;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawApplicantsTab(KerbalRoster roster)
        {
            int activeCount = GetActiveNonRetiredCount();
            int maxCrew = GetMaxCrew();
            bool atCap = activeCount >= maxCrew;
            var applicants = GetApplicantsCached(roster);

            GUILayout.Label($"Applicants: {applicants.Count}");
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(260));
            GUILayout.Label("Skill", GUILayout.Width(110));
            GUILayout.Label("Courage", GUILayout.Width(90));
            GUILayout.Label("Stupidity", GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            GUILayout.Label("", GUILayout.Width(150));
            GUILayout.EndHorizontal();
            DrawHRule();

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var k in applicants)
            {
                if (k == null) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name, GUILayout.Width(260));
                GUILayout.Label(k.trait, GUILayout.Width(110));
                GUILayout.Label($"{k.courage:P0}", GUILayout.Width(90));
                GUILayout.Label($"{k.stupidity:P0}", GUILayout.Width(90));
                GUILayout.FlexibleSpace();
                GUI.enabled = !atCap;
                if (GUILayout.Button("Hire", GUILayout.Width(70)))
                {
                    k.type = ProtoCrewMember.KerbalType.Crew;
                    k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                    SaveScheduler.RequestSave("hire applicant");
                    InvalidateUICaches();
                    ACPatches.ForceRefresh();
                }
                GUI.enabled = true;
                if (GUILayout.Button("Reject", GUILayout.Width(70)))
                {
                    RejectApplicant(roster, k);
                    InvalidateUICaches();
                    ACPatches.ForceRefresh();
                    ACPatches.ForceRefreshApplicants();
                }
                GUILayout.EndHorizontal();
            }
            if (applicants.Count == 0) GUILayout.Label("No applicants available.");
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            DrawHRule();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Slots: {activeCount} / {maxCrew}{(atCap ? " FULL" : "")}", GUILayout.Width(260));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reject All Applicants", GUILayout.Width(180)))
            {
                var all = roster.Applicants.ToList();
                foreach (var k in all) RejectApplicant(roster, k);
                InvalidateUICaches();
                ACPatches.ForceRefresh();
                ACPatches.ForceRefreshApplicants();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawRosterTab(KerbalRoster roster, double now)
        {
            var rows = GetRosterRowsCached(roster, now);
            int activeCount = GetActiveNonRetiredCount();
            int maxCrew = GetMaxCrew();
            bool atCap = activeCount >= maxCrew;
            const float nameWidth = 300f;
            const float flightsWidth = 85f;
            const float ageWidth = 65f;
            const float statusWidth = 260f;
            const float actionButtonWidth = 80f;
            const float actionAreaWidth = actionButtonWidth * 2f;

            GUILayout.Label($"Shown: {rows.Count}");
            GUILayout.Space(8);
            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (var row in rows)
            {
                var k = row.Kerbal;
                var r = row.Record;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{k.name} — {k.trait} — L{(int)k.experienceLevel}", GUILayout.Width(nameWidth));
                GUILayout.Label($"Flights:{row.DisplayFlights}", GUILayout.Width(flightsWidth));
                GUILayout.Label(row.AgeText, GUILayout.Width(ageWidth));
                GUILayout.Label(row.Status, GUILayout.Width(statusWidth));
                GUILayout.FlexibleSpace();

                if (row.IsLost || row.IsAssigned)
                {
                    GUILayout.Space(actionAreaWidth);
                }
                else if (!row.Retired)
                {
                    bool inTraining = k.inactive;
                    bool onMission = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                    bool maxLevel = k.experienceLevel >= 3f;
                    GUI.enabled = !inTraining && !onMission && !maxLevel;
                    if (GUILayout.Button("Train", GUILayout.Width(actionButtonWidth)))
                    {
                        _pendingTrainKerbal = k;
                        _showTrainConfirm = true;
                    }
                    GUI.enabled = true;
                    GUI.enabled = !inTraining && !onMission && !row.InTrainingLockout;
                    if (row.InTrainingLockout)
                        GUILayout.Button("Committed", GUILayout.Width(actionButtonWidth));
                    else if (GUILayout.Button("Retire", GUILayout.Width(actionButtonWidth)))
                    {
                        DoRetire(k, r);
                        InvalidateUICaches();
                    }
                    GUI.enabled = true;
                }
                else
                {
                    bool noStar = row.EffectiveStars <= 0;
                    double recallCost = GetRecallFundsCost();
                    double curFunds = Funding.Instance?.Funds ?? 0;
                    bool cantAfford = recallCost > 0 && curFunds < recallCost;
                    GUI.enabled = !atCap && !noStar && !cantAfford;
                    string btnLabel = noStar ? "No Stars" : (recallCost > 0 ? $"Recall √{recallCost:N0}" : "Recall");
                    if (GUILayout.Button(btnLabel, GUILayout.Width(noStar ? actionButtonWidth : 130f)))
                    {
                        DoRecall(k, r, row.EffectiveStars);
                        InvalidateUICaches();
                    }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            if (_showTrainConfirm && _pendingTrainKerbal != null)
                DrawTrainConfirm();
        }

        private void DrawTrainConfirm()
        {
            var trainK = _pendingTrainKerbal;
            double hire = GetNextHireCost();
            int tgt = (int)trainK.experienceLevel + 1;
            double fCost = TrainingFundsCost(hire, tgt);
            double rCost = TrainingRDCost(tgt);
            double funds = Funding.Instance?.Funds ?? 0;
            double rd = ResearchAndDevelopment.Instance?.Science ?? 0;
            bool afford = funds >= fCost && rd >= rCost;

            GUILayout.Space(6);
            GUILayout.BeginVertical(KspGuiSkin.Box);
            GUILayout.Label($"Train {trainK.name}  L{(int)trainK.experienceLevel} → L{tgt}");
            int cBase = tgt * 30;
            int cMax = (int)(cBase * 1.5);
            string cDur = trainK.stupidity < 0.01f ? $"{cBase}d" : $"{cBase}–{cMax}d";
            GUILayout.Label($"Cost: √{fCost:N0}  |  {rCost:N0} R&D  |  {cDur}");
            if (!afford) GUILayout.Label("⚠ Insufficient funds or R&D!");
            GUILayout.BeginHorizontal();
            GUI.enabled = afford;
            if (GUILayout.Button("Confirm", GUILayout.Width(100)))
            {
                ExecuteTraining(trainK, tgt, fCost, rCost);
                _showTrainConfirm = false; _pendingTrainKerbal = null;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Cancel", GUILayout.Width(100)))
            { _showTrainConfirm = false; _pendingTrainKerbal = null; }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawTrainingTab(KerbalRoster roster, double now)
        {
            double hire = GetNextHireCost();
            double funds = Funding.Instance?.Funds ?? 0;
            double rd = ResearchAndDevelopment.Instance?.Science ?? 0;

            GUILayout.Label($"Training costs based on next hire cost: √{hire:N0}");
            GUILayout.Label(
                $"  L1: √{TrainingFundsCost(hire,1):N0} + {TrainingRDCost(1):N0} R&D   " +
                $"L2: √{TrainingFundsCost(hire,2):N0} + {TrainingRDCost(2):N0} R&D   " +
                $"L3: √{TrainingFundsCost(hire,3):N0} + {TrainingRDCost(3):N0} R&D   " +
                $"({RosterRotationState.TrainingStarDays}d base per level)");
            GUILayout.Space(6);

            GUILayout.Label("▶ Currently In Training");
            bool anyT = false;
            foreach (var k in roster.Crew)
            {
                if (k == null || !k.inactive || k.inactiveTimeEnd <= now) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r == null || r.Training == TrainingType.None || r.DeathUT > 0) continue;
                string lbl = TrainingLabel(r.Training, r.TrainingTargetLevel);
                double rem = Math.Max(0, k.inactiveTimeEnd - now);

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name, GUILayout.Width(150));
                GUILayout.Label($"L{(int)k.experienceLevel}", GUILayout.Width(35));
                GUILayout.Label(lbl, GUILayout.Width(200));
                GUILayout.Label(RosterRotationState.FormatCountdown(rem), GUILayout.Width(110));
                GUILayout.EndHorizontal();
                anyT = true;
            }
            if (!anyT) GUILayout.Label("  None.");
            GUILayout.Space(8);

            GUILayout.Label("▶ Send to Training");
            var candidates = GetTrainingCandidatesCached(roster);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(200));
            if (candidates.Count == 0) GUILayout.Label("  No eligible kerbals.");
            foreach (var k in candidates)
            {
                int tgtT = (int)k.experienceLevel + 1;
                double fc = TrainingFundsCost(hire, tgtT);
                double rc = TrainingRDCost(tgtT);
                bool afford = funds >= fc && rd >= rc;

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name, GUILayout.Width(140));
                GUILayout.Label($"L{(int)k.experienceLevel}→L{tgtT}", GUILayout.Width(70));
                int baseD = tgtT * 30; int maxD = (int)(baseD * 1.5);
                GUILayout.Label($"√{fc:N0} + {rc:N0}R  {(k.stupidity < 0.01f ? $"{baseD}d" : $"{baseD}–{maxD}d")}", GUILayout.Width(200));
                GUILayout.FlexibleSpace();
                GUI.enabled = afford;
                if (GUILayout.Button("Send", GUILayout.Width(70)))
                    ExecuteTraining(k, tgtT, fc, rc);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (RosterRotationState.AgingEnabled)
            {
                DrawHRule();
                GUILayout.Label("⚙ Aging Settings");
                GUILayout.Label("Aging, retirement, and notification settings are configured in Difficulty Options → EAC.");
                GUILayout.Label(
                    $"  Retirement Age: {RosterRotationState.RetirementAgeMin}–{RosterRotationState.RetirementAgeMax}    " +
                    $"Retired Death Age Min: {RosterRotationState.RetiredDeathAgeMin}");
            }
        }

        // ── AC Overlay ─────────────────────────────────────────────────────────
        private void DrawACOverlay(int id)
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            double now = Planetarium.GetUniversalTime();

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_acOverlay == AcOverlay.Applicants,  "📋 Applicants",    "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.Applicants;
            if (GUILayout.Toggle(_acOverlay == AcOverlay.Training,    "🎓 Send Training", "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.Training;
            if (GUILayout.Toggle(_acOverlay == AcOverlay.ForceRetire, "🚪 Force Retire",  "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.ForceRetire;

            bool hallAvailable = HallOfHistoryWindow.IsAvailable;
            bool hallOpen = HallOfHistoryWindow.IsOpen;
            GUI.enabled = hallAvailable;
            string hallLabel = hallOpen ? "🏛 Close Hall" : "🏛 Open Hall";
            if (GUILayout.Button(hallLabel, GUILayout.Width(160)))
            {
                if (hallOpen)
                    HallOfHistoryWindow.HideWindow();
                else
                    HallOfHistoryWindow.ShowWindow(true);
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕ Close", GUILayout.Width(80))) _acOverlay = AcOverlay.None;
            GUILayout.EndHorizontal();

            if (!hallAvailable)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.95f, 0.65f);
                GUILayout.Label("Hall of History is still initializing.");
                GUI.color = prev;
            }

            GUILayout.Space(6);

            if (roster == null) { GUILayout.Label("Roster unavailable."); GUI.DragWindow(); return; }

            if (_acOverlay == AcOverlay.Applicants)       DrawApplicantsOverlay(roster);
            else if (_acOverlay == AcOverlay.Training)    DrawTrainingOverlay(roster, now);
            else if (_acOverlay == AcOverlay.ForceRetire) DrawRetireOverlay(roster, now);

            GUI.DragWindow();
        }

        private void DrawApplicantsOverlay(KerbalRoster roster)
        {
            int activeCount = GetActiveNonRetiredCount();
            int maxCrew = GetMaxCrew();
            bool atCap = activeCount >= maxCrew;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(200));
            GUILayout.Label("Skill", GUILayout.Width(100));
            GUILayout.Label("Courage", GUILayout.Width(80));
            GUILayout.Label("Stupidity", GUILayout.Width(80));
            GUILayout.Label("", GUILayout.Width(160));
            GUILayout.EndHorizontal();
            DrawHRule();

            var applicants = GetApplicantsCached(roster);
            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(300));
            foreach (var k in applicants)
            {
                if (k == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name, GUILayout.Width(200));
                GUILayout.Label(k.trait, GUILayout.Width(100));
                GUILayout.Label($"{k.courage:P0}", GUILayout.Width(80));
                GUILayout.Label($"{k.stupidity:P0}", GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                GUI.enabled = !atCap;
                if (GUILayout.Button("Hire", GUILayout.Width(70)))
                {
                    k.type = ProtoCrewMember.KerbalType.Crew;
                    k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                    SaveScheduler.RequestSave("hire applicant");
                    InvalidateUICaches();
                    ACPatches.ForceRefresh();
                }
                GUI.enabled = true;
                if (GUILayout.Button("Reject", GUILayout.Width(70)))
                {
                    RejectApplicant(roster, k);
                    InvalidateUICaches();
                    ACPatches.ForceRefresh();
                    ACPatches.ForceRefreshApplicants();
                }
                GUILayout.EndHorizontal();
            }
            if (applicants.Count == 0) GUILayout.Label("No applicants available.");
            GUILayout.EndScrollView();

            GUILayout.Space(6); DrawHRule();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Slots: {activeCount} / {maxCrew}{(atCap ? " FULL" : "")}", GUILayout.Width(260));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reject All Applicants", GUILayout.Width(180)))
            {
                var all = roster.Applicants.ToList();
                foreach (var k in all) RejectApplicant(roster, k);
                InvalidateUICaches();
                ACPatches.ForceRefresh();
                ACPatches.ForceRefreshApplicants();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawTrainingOverlay(KerbalRoster roster, double now)
        {
            double hire = GetNextHireCost();
            double funds = Funding.Instance?.Funds ?? 0;
            double rd = ResearchAndDevelopment.Instance?.Science ?? 0;

            GUILayout.Label($"Funds: √{funds:N0}   R&D: {rd:N0}   Next Hire Base: √{hire:N0}");
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(180));
            GUILayout.Label("Skill", GUILayout.Width(90));
            GUILayout.Label("Level", GUILayout.Width(50));
            GUILayout.Label("Funds Cost", GUILayout.Width(110));
            GUILayout.Label("R&D Cost", GUILayout.Width(80));
            GUILayout.Label("Duration", GUILayout.Width(80));
            GUILayout.Label("", GUILayout.Width(140));
            GUILayout.EndHorizontal();
            DrawHRule();

            foreach (var k in roster.Crew)
            {
                if (k == null || !k.inactive || k.inactiveTimeEnd <= now) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r == null || r.Training == TrainingType.None || r.DeathUT > 0) continue;
                double rem = Math.Max(0, k.inactiveTimeEnd - now);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"⏳ {k.name}", GUILayout.Width(180));
                GUILayout.Label(k.trait, GUILayout.Width(90));
                GUILayout.Label($"L{(int)k.experienceLevel}", GUILayout.Width(50));
                GUILayout.Label(TrainingLabel(r.Training, r.TrainingTargetLevel), GUILayout.Width(110));
                GUILayout.Label("", GUILayout.Width(80));
                GUILayout.Label(RosterRotationState.FormatCountdown(rem), GUILayout.Width(80));
                GUILayout.Label("In Training", GUILayout.Width(140));
                GUILayout.EndHorizontal();
            }

            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(250));
            var candidates = GetTrainingCandidatesCached(roster);
            foreach (var k in candidates)
            {
                int tgt = (int)k.experienceLevel + 1;
                double fc = TrainingFundsCost(hire, tgt);
                double rc = TrainingRDCost(tgt);
                bool afford = funds >= fc && rd >= rc;
                int baseDays = tgt * 30;
                int maxDays = (int)(baseDays * 1.5);

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name, GUILayout.Width(180));
                GUILayout.Label(k.trait, GUILayout.Width(90));
                GUILayout.Label($"L{(int)k.experienceLevel}→L{tgt}", GUILayout.Width(50));
                GUILayout.Label($"√{fc:N0}", GUILayout.Width(110));
                GUILayout.Label($"{rc:N0}", GUILayout.Width(80));
                GUILayout.Label(k.stupidity < 0.01f ? $"{baseDays}d" : $"{baseDays}–{maxDays}d", GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                GUI.enabled = afford;
                if (GUILayout.Button("Send to Training", GUILayout.Width(130)))
                    ExecuteTraining(k, tgt, fc, rc);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void DrawRetireOverlay(KerbalRoster roster, double now)
        {
            GUILayout.Label("Retire active kerbals. Retired kerbals are hidden from missions.");
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(200));
            GUILayout.Label("Skill", GUILayout.Width(100));
            GUILayout.Label("Level", GUILayout.Width(60));
            GUILayout.Label("Flights", GUILayout.Width(70));
            GUILayout.Label("Status", GUILayout.Width(200));
            GUILayout.Label("", GUILayout.Width(90));
            GUILayout.EndHorizontal();
            DrawHRule();

            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(300));
            var crew = GetRetireRowsCached(roster, now);

            foreach (var row in crew)
            {
                var k = row.Kerbal;
                var r = row.Record;
                bool onMission = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                bool inTraining = r?.Training != TrainingType.None && k.inactive;

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name, GUILayout.Width(200));
                GUILayout.Label(k.trait, GUILayout.Width(100));
                GUILayout.Label($"L{(int)k.experienceLevel}", GUILayout.Width(60));
                GUILayout.Label($"{row.DisplayFlights}", GUILayout.Width(50));
                GUILayout.Label(row.AgeText, GUILayout.Width(55));
                GUILayout.Label(row.Status, GUILayout.Width(170));
                GUILayout.FlexibleSpace();

                GUI.enabled = !onMission && !inTraining && !row.InTrainingLockout;
                if (row.InTrainingLockout)
                    GUILayout.Button("Committed", GUILayout.Width(80));
                else if (GUILayout.Button("Retire", GUILayout.Width(80)))
                {
                    DoRetire(k, r);
                    InvalidateUICaches();
                    _pendingForceRefresh = true;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (crew.Count == 0) GUILayout.Label("No active kerbals.");
            GUILayout.EndScrollView();
        }

        // ── Retire / Recall helpers ────────────────────────────────────────────
        private void DoRetire(ProtoCrewMember k, RosterRotationState.KerbalRecord r)
        {
            if (r == null) { r = new RosterRotationState.KerbalRecord(); RosterRotationState.Records[k.name] = r; }
            if (string.IsNullOrEmpty(r.OriginalTrait)) r.OriginalTrait = k.trait;
            r.OriginalType       = k.type;
            r.Retired            = true;
            r.RetiredUT          = Planetarium.GetUniversalTime();
            r.ExperienceAtRetire = (int)k.experienceLevel;
            k.inactive        = true;
            k.inactiveTimeEnd = Planetarium.GetUniversalTime() + RosterRotationState.YearSeconds * 1000.0;
            RosterRotationState.InvalidateRetiredCache();
        }

        private void DoRecall(ProtoCrewMember k, RosterRotationState.KerbalRecord r, int effStars)
        {
            // Charge recall cost (base hire cost × multiplier, funds only)
            double recallCost = GetRecallFundsCost();
            if (recallCost > 0)
            {
                double funds = Funding.Instance?.Funds ?? 0;
                if (funds < recallCost)
                {
                    ScreenMessages.PostScreenMessage(
                        $"Cannot recall {k.name} — insufficient funds (need √{recallCost:N0}).",
                        4f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }
                try { Funding.Instance?.AddFunds(-recallCost, TransactionReasons.CrewRecruited); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1081", "Suppressed exception in Mod.cs:1081", ex); }
            }

            r.Retired = false;
            if (k.type == ProtoCrewMember.KerbalType.Tourist || k.type == ProtoCrewMember.KerbalType.Unowned)
            {
                k.type = r.OriginalType;
                if (k.type == 0) k.type = ProtoCrewMember.KerbalType.Crew;
            }
            k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
            if (!string.IsNullOrEmpty(r.OriginalTrait)) k.trait = r.OriginalTrait;
            try { k.experienceLevel = effStars; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1092", "Suppressed exception in Mod.cs:1092", ex); }
            double sec = 30.0 * RosterRotationState.DaySeconds;
            k.inactive        = true;
            k.inactiveTimeEnd = Planetarium.GetUniversalTime() + sec;
            RetiredKerbalCleanupService.ResetAutoCleanupRequest(k.name);
            r.Training            = TrainingType.RecallRefresher;
            r.TrainingTargetLevel = 0;
            RosterRotationState.InvalidateRetiredCache();

            string costMsg = recallCost > 0 ? $" (√{recallCost:N0})" : "";
            ScreenMessages.PostScreenMessage(
                $"{k.name} recalled — 30-day refresher training begins.{costMsg}",
                4f, ScreenMessageStyle.UPPER_CENTER);

            _pendingForceRefresh = true;
        }

        /// <summary>Recall cost = next hire cost × RecallFundsCostMultiplier.</summary>
        internal static double GetRecallFundsCost()
        {
            return GetNextHireCost() * RosterRotationState.RecallFundsCostMultiplier;
        }

        // ── Training execution ─────────────────────────────────────────────────
        private static double CalcTrainingDays(ProtoCrewMember k, int targetLevel)
        {
            return CareerRules.CalculateTrainingDays(k != null ? k.stupidity : 0f, targetLevel, UnityEngine.Random.value);
        }

        private void ExecuteTraining(ProtoCrewMember k, int targetLevel, double fCost, double rCost)
        {
            if (k == null) return;
            try
            {
                Funding.Instance?.AddFunds(-fCost, TransactionReasons.CrewRecruited);
                ResearchAndDevelopment.Instance?.AddScience((float)-rCost, TransactionReasons.CrewRecruited);
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1132", "Suppressed exception in Mod.cs:1132", ex); }

            double trainingDays = CalcTrainingDays(k, targetLevel);
            double sec = trainingDays * RosterRotationState.DaySeconds;
            k.inactive        = true;
            k.inactiveTimeEnd = Planetarium.GetUniversalTime() + sec;

            var rec = RosterRotationState.GetOrCreate(k.name);
            rec.Training            = TrainingType.ExperienceUpgrade;
            rec.TrainingTargetLevel = targetLevel;

            ScreenMessages.PostScreenMessage(
                $"{k.name} sent to training → L{targetLevel}   √{fCost:N0}  {rCost:N0} R&D  {trainingDays:F0}d",
                5f, ScreenMessageStyle.UPPER_CENTER);

            InvalidateUICaches();
            SaveScheduler.RequestSave("start training");
            ACPatches.ForceRefresh();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private List<ProtoCrewMember> BuildTrainingCandidates(KerbalRoster roster)
        {
            var list = new List<ProtoCrewMember>();
            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (k.inactive || k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                if (k.experienceLevel >= 3f) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r?.Retired == true || r?.DeathUT > 0) continue;
                list.Add(k);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return list;
        }

        private void InvalidateUICaches()
        {
            _lastRosterRowsCacheRT = -10f;
            _lastApplicantsCacheRT = -10f;
            _lastTrainingCandidatesCacheRT = -10f;
            _lastRetireRowsCacheRT = -10f;
            _lastCrewCapacityCacheRT = -10f;  // force crew count refresh
            InvalidateCrewCapacityCache();
            CrewRandRAdapter.InvalidateVacationCache();
        }

        internal static void InvalidateCrewCapacityCache()
        {
            _lastCrewCapacityCacheRT = -10f;
            _lastHireCostCacheRT = -10f;
        }

        private List<RosterRowData> GetRosterRowsCached(KerbalRoster roster, double now)
        {
            float rt = Time.realtimeSinceStartup;
            if (_cachedRosterRowsTab == _tab && rt - _lastRosterRowsCacheRT < UiCacheSeconds)
                return _cachedRosterRows;

            _lastRosterRowsCacheRT = rt;
            _cachedRosterRowsTab = _tab;
            _cachedRosterRows = BuildRosterRows(roster, now, _tab);
            return _cachedRosterRows;
        }

        private List<RosterRowData> BuildRosterRows(KerbalRoster roster, double now, Tab tab)
        {
            var rows = new List<RosterRowData>();
            foreach (var k in roster.Kerbals())
            {
                if (k == null) continue;
                bool applicant = k.type == ProtoCrewMember.KerbalType.Applicant;
                if (tab == Tab.Applicants)
                {
                    if (!applicant) continue;
                }
                else if (applicant)
                {
                    continue;
                }

                RosterRotationState.Records.TryGetValue(k.name, out var r);
                bool retired = r != null && r.Retired;
                bool hasFlown = r != null && r.LastFlightUT > 0;
                bool onVacation = CrewRandRAdapter.IsOnVacationByName(k.name, now);
                bool onMission = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                bool inTraining = r != null && r.Training != TrainingType.None && k.inactive && k.inactiveTimeEnd > now;
                bool isLost = k.rosterStatus == ProtoCrewMember.RosterStatus.Dead
                           || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing
                           || (r != null && r.DeathUT > 0);

                switch (tab)
                {
                    case Tab.Active:   if (isLost || retired || onVacation || onMission || inTraining) continue; break;
                    case Tab.Assigned: if (isLost || retired || !onMission) continue; break;
                    case Tab.RandR:    if (isLost || retired || !onVacation) continue; break;
                    case Tab.Retired:  if (!retired || isLost) continue; break;
                    case Tab.Lost:     if (!isLost) continue; break;
                }

                int displayFlights = GetDisplayedFlights(k, r);

                var row = new RosterRowData();
                row.Kerbal = k;
                row.Record = r;
                row.Retired = retired;
                row.HasFlown = hasFlown || displayFlights > 0;
                row.IsLost = isLost;
                row.IsAssigned = onMission;
                row.Status = BuildStatusString(k, r, now, hasFlown, retired);
                row.AgeText = GetAgeDisplay(r, now);
                row.DisplayFlights = displayFlights;
                row.EffectiveStars = retired ? RosterRotationState.GetRetiredEffectiveStars(k, r, now) : 0;
                row.InTrainingLockout = r != null && r.TrainingEndUT > 0
                    && (now - r.TrainingEndUT) < RosterRotationState.YearSeconds;
                rows.Add(row);
            }
            rows.Sort((a, b) => string.Compare(a.Kerbal.name, b.Kerbal.name, StringComparison.Ordinal));
            return rows;
        }

        private List<ProtoCrewMember> GetApplicantsCached(KerbalRoster roster)
        {
            float rt = Time.realtimeSinceStartup;
            if (rt - _lastApplicantsCacheRT < UiCacheSeconds)
                return _cachedApplicants;

            _lastApplicantsCacheRT = rt;
            _cachedApplicants = roster.Applicants.Where(k => k != null).ToList();
            _cachedApplicants.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return _cachedApplicants;
        }

        private List<ProtoCrewMember> GetTrainingCandidatesCached(KerbalRoster roster)
        {
            float rt = Time.realtimeSinceStartup;
            if (rt - _lastTrainingCandidatesCacheRT < UiCacheSeconds)
                return _cachedTrainingCandidates;

            _lastTrainingCandidatesCacheRT = rt;
            _cachedTrainingCandidates = BuildTrainingCandidates(roster);
            return _cachedTrainingCandidates;
        }

        private List<RosterRowData> GetRetireRowsCached(KerbalRoster roster, double now)
        {
            float rt = Time.realtimeSinceStartup;
            if (rt - _lastRetireRowsCacheRT < UiCacheSeconds)
                return _cachedRetireRows;

            _lastRetireRowsCacheRT = rt;
            _cachedRetireRows = new List<RosterRowData>();
            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r?.Retired == true || r?.DeathUT > 0) continue;

                var row = new RosterRowData();
                row.Kerbal = k;
                row.Record = r;
                row.Status = BuildStatusString(k, r, now, r != null && r.LastFlightUT > 0, false);
                row.AgeText = GetAgeDisplay(r, now);
                row.DisplayFlights = GetDisplayedFlights(k, r);
                row.InTrainingLockout = r != null && r.TrainingEndUT > 0
                    && (now - r.TrainingEndUT) < RosterRotationState.YearSeconds;
                _cachedRetireRows.Add(row);
            }
            _cachedRetireRows.Sort((a, b) => string.Compare(a.Kerbal.name, b.Kerbal.name, StringComparison.Ordinal));
            return _cachedRetireRows;
        }

        private static string GetAgeDisplay(RosterRotationState.KerbalRecord record, double now)
        {
            if (!RosterRotationState.AgingEnabled || record == null || record.LastAgedYears < 0)
                return "";

            int age = RosterRotationState.GetKerbalAge(record, now);
            return age >= 0 ? $"Age {age}" : "";
        }

        private string BuildStatusString(ProtoCrewMember k, RosterRotationState.KerbalRecord r,
                                         double now, bool hasFlown, bool retired)
        {
            if (r != null && r.DeathUT > 0)
            {
                int age = RosterRotationState.GetKerbalAge(r, r.DeathUT);
                string ageStr = age >= 0 ? $"Age {age}, " : "";
                string dateStr = RosterRotationState.FormatGameDateYD(r.DeathUT);
                bool retiredDeath = (r.RetiredUT > 0) && (r.DeathUT >= r.RetiredUT - 1);
                if (r.DiedOnMission)
                    return $"Died on mission {ageStr}{dateStr}";
                return retiredDeath ? $"Died {ageStr}{dateStr}" : $"K.I.A. {ageStr}{dateStr}";
            }
            if (retired)
            {
                int eff = RosterRotationState.GetRetiredEffectiveStars(k, r, now);
                return $"RETIRED L{eff} ({RosterRotationState.FormatTimeAgo(r.RetiredUT, now)})";
            }
            if (r != null && r.Training != TrainingType.None && k.inactive && k.inactiveTimeEnd > now)
            {
                double rem = k.inactiveTimeEnd - now;
                return $"In {TrainingLabel(r.Training, r.TrainingTargetLevel)}  {RosterRotationState.FormatCountdown(rem)}";
            }
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
            {
                if (TryGetAssignedVesselName(k, out var vesselName))
                    return "ASSIGNED: " + vesselName;
                return "ASSIGNED";
            }
            if (k.inactive && k.inactiveTimeEnd > now)
                return $"INACTIVE ({RosterRotationState.FormatCountdown(k.inactiveTimeEnd - now)})";
            if (CrewRandRAdapter.TryGetVacationUntilByName(k.name, out var vacUntil) && vacUntil > now)
                return $"R&R ({FormatTime(vacUntil - now)})";
            return "AVAILABLE";
        }

        private static bool TryGetAssignedVesselName(ProtoCrewMember k, out string vesselName)
        {
            vesselName = null;
            if (k == null || string.IsNullOrEmpty(k.name)) return false;

            try
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null) continue;
                    try
                    {
                        var crew = vessel.GetVesselCrew();
                        if (crew == null) continue;
                        if (crew.Any(c => c != null && string.Equals(c.name, k.name, StringComparison.Ordinal)))
                        {
                            vesselName = vessel.vesselName;
                            return !string.IsNullOrEmpty(vesselName);
                        }
                    }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1372", "Suppressed exception in Mod.cs:1372", ex); }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1375", "Suppressed exception in Mod.cs:1375", ex); }

            try
            {
                var protoVessels = HighLogic.CurrentGame?.flightState?.protoVessels as System.Collections.IEnumerable;
                if (protoVessels == null) return false;
                foreach (var pv in protoVessels)
                {
                    if (pv == null) continue;
                    try
                    {
                        var pvType = pv.GetType();
                        var getCrew = pvType.GetMethod("GetVesselCrew", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var crewRaw = getCrew?.Invoke(pv, null) as System.Collections.IEnumerable;
                        if (crewRaw == null) continue;

                        bool found = false;
                        foreach (var crewObj in crewRaw)
                        {
                            var crew = crewObj as ProtoCrewMember;
                            if (crew == null) continue;
                            if (!string.Equals(crew.name, k.name, StringComparison.Ordinal)) continue;
                            found = true;
                            break;
                        }
                        if (!found) continue;

                        var vesselNameField = pvType.GetField("vesselName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        vesselName = vesselNameField?.GetValue(pv) as string;
                        if (string.IsNullOrEmpty(vesselName))
                        {
                            var vesselNameProp = pvType.GetProperty("vesselName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            vesselName = vesselNameProp?.GetValue(pv, null) as string;
                        }
                        return !string.IsNullOrEmpty(vesselName);
                    }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1411", "Suppressed exception in Mod.cs:1411", ex); }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1414", "Suppressed exception in Mod.cs:1414", ex); }

            return false;
        }

        private static MethodInfo GetFlightTrackerFlightsMethod()
        {
            if (_searchedFlightTrackerFlightsMethod) return _cachedFlightTrackerFlightsMethod;
            _searchedFlightTrackerFlightsMethod = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        MethodInfo mi = null;
                        try
                        {
                            mi = t.GetMethod("GetNumberOfFlights",
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new[] { typeof(string) },
                                null);
                            if (mi != null)
                            {
                                _cachedFlightTrackerFlightsMethod = mi;
                                _cachedFlightTrackerApiType = t;
                                _cachedFlightTrackerApiInstanceProperty = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                _cachedFlightTrackerApiInstanceField = t.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                RRLog.Info($"[EAC] FlightTracker API detected: {t.FullName}.GetNumberOfFlights(string) [instance]");
                                return _cachedFlightTrackerFlightsMethod;
                            }

                            mi = t.GetMethod("GetNumberOfFlights",
                                BindingFlags.Public | BindingFlags.Static,
                                null,
                                new[] { typeof(string) },
                                null);
                            if (mi != null)
                            {
                                _cachedFlightTrackerFlightsMethod = mi;
                                _cachedFlightTrackerApiType = t;
                                RRLog.Info($"[EAC] FlightTracker API detected: {t.FullName}.GetNumberOfFlights(string) [static]");
                                return _cachedFlightTrackerFlightsMethod;
                            }
                        }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1468", "Suppressed exception in Mod.cs:1468", ex); }
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1472", "Suppressed exception in Mod.cs:1472", ex); }

            return null;
        }

        private static object GetFlightTrackerApiInstance()
        {
            try
            {
                if (_cachedFlightTrackerApiInstanceProperty != null)
                    return _cachedFlightTrackerApiInstanceProperty.GetValue(null, null);
                if (_cachedFlightTrackerApiInstanceField != null)
                    return _cachedFlightTrackerApiInstanceField.GetValue(null);
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1486", "Suppressed exception in Mod.cs:1486", ex); }
            return null;
        }

        private static bool ResolveFlightTrackerStore()
        {
            if (_searchedFlightTrackerStore)
                return _cachedFlightTrackerStoreType != null && _cachedFlightTrackerFlightsField != null;

            _searchedFlightTrackerStore = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;

                        FieldInfo flightsField = null;
                        try { flightsField = t.GetField("Flights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1513", "Suppressed exception in Mod.cs:1513", ex); }
                        if (flightsField == null) continue;

                        FieldInfo instanceField = null;
                        PropertyInfo instanceProp = null;
                        try { instanceField = t.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1519", "Suppressed exception in Mod.cs:1519", ex); }
                        if (instanceField == null)
                        {
                            try { instanceProp = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1523", "Suppressed exception in Mod.cs:1523", ex); }
                        }

                        if (instanceField == null && instanceProp == null) continue;

                        _cachedFlightTrackerStoreType = t;
                        _cachedFlightTrackerStoreInstanceField = instanceField;
                        _cachedFlightTrackerStoreInstanceProperty = instanceProp;
                        _cachedFlightTrackerFlightsField = flightsField;
                        RRLog.Info($"[EAC] FlightTracker store detected: {t.FullName}.Flights");
                        return true;
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1537", "Suppressed exception in Mod.cs:1537", ex); }

            return false;
        }

        private static bool TryGetFlightTrackerFlightsDictionary(out IDictionary dict, out string reason)
        {
            dict = null;
            reason = null;

            try
            {
                if (!ResolveFlightTrackerStore())
                {
                    reason = "store type not found";
                    return false;
                }

                object instance = null;
                if (_cachedFlightTrackerStoreInstanceProperty != null)
                    instance = _cachedFlightTrackerStoreInstanceProperty.GetValue(null, null);
                else if (_cachedFlightTrackerStoreInstanceField != null)
                    instance = _cachedFlightTrackerStoreInstanceField.GetValue(null);

                if (instance == null)
                {
                    reason = "store instance unavailable";
                    return false;
                }

                dict = _cachedFlightTrackerFlightsField.GetValue(instance) as IDictionary;
                if (dict == null)
                {
                    reason = "Flights dictionary unavailable";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool TryGetFlightTrackerFlights(string kerbalName, out int flights, out string reason)
        {
            flights = 0;
            reason = null;
            if (string.IsNullOrEmpty(kerbalName))
            {
                reason = "kerbal name missing";
                return false;
            }

            try
            {
                var api = GetFlightTrackerFlightsMethod();
                if (api != null)
                {
                    object target = null;
                    if (!api.IsStatic)
                    {
                        target = GetFlightTrackerApiInstance();
                        if (target == null)
                        {
                            reason = "FlightTracker API instance unavailable";
                        }
                        else
                        {
                            object raw = api.Invoke(target, new object[] { kerbalName });
                            if (raw != null)
                            {
                                flights = Math.Max(0, Convert.ToInt32(raw));
                                reason = "api";
                                return true;
                            }

                            reason = "FlightTracker API returned null";
                        }
                    }
                    else
                    {
                        object raw = api.Invoke(null, new object[] { kerbalName });
                        if (raw != null)
                        {
                            flights = Math.Max(0, Convert.ToInt32(raw));
                            reason = "api";
                            return true;
                        }

                        reason = "FlightTracker API returned null";
                    }
                }
                else
                {
                    reason = "FlightTracker API not found";
                }
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
            }

            IDictionary dict;
            string dictReason;
            if (TryGetFlightTrackerFlightsDictionary(out dict, out dictReason))
            {
                try
                {
                    object raw = dict.Contains(kerbalName) ? dict[kerbalName] : 0;
                    flights = Math.Max(0, Convert.ToInt32(raw));
                    reason = "store";
                    return true;
                }
                catch (Exception ex)
                {
                    reason = ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(dictReason))
                reason = string.IsNullOrEmpty(reason) ? dictReason : (reason + "; " + dictReason);
            return false;
        }

        private static bool TrySetFlightTrackerFlights(string kerbalName, int newValue, out int previousValue, out string reason)
        {
            previousValue = 0;
            reason = null;
            IDictionary dict;
            if (!TryGetFlightTrackerFlightsDictionary(out dict, out reason))
                return false;

            try
            {
                previousValue = dict.Contains(kerbalName) ? Math.Max(0, Convert.ToInt32(dict[kerbalName])) : 0;
                dict[kerbalName] = Math.Max(0, newValue);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static void LogDisplayedFlightsChoice(ProtoCrewMember k, int eacFlights, bool ftAvailable, int ftFlights, string ftReason, string usingSource)
        {
            if (!RRLog.VerboseEnabled || k == null || string.IsNullOrEmpty(k.name)) return;

            string ftText = ftAvailable ? ftFlights.ToString() : ("unavailable: " + (string.IsNullOrEmpty(ftReason) ? "unknown" : ftReason));
            RRLog.VerboseOnce(
                "DisplayedFlights:" + k.name + ":" + eacFlights + ":" + ftText + ":" + usingSource,
                $"[EAC] Flight comparison for {k.name}: EAC={eacFlights}, FlightTracker={ftText}; using={usingSource}");
        }

        private static int GetDisplayedFlights(ProtoCrewMember k, RosterRotationState.KerbalRecord r)
        {
            int fallback = Math.Max(0, r?.Flights ?? 0);
            if (k == null || string.IsNullOrEmpty(k.name)) return fallback;

            try
            {
                int ftFlights;
                string ftReason;
                if (TryGetFlightTrackerFlights(k.name, out ftFlights, out ftReason))
                {
                    LogDisplayedFlightsChoice(k, fallback, true, ftFlights, ftReason, "FlightTracker");
                    return Math.Max(0, ftFlights);
                }

                LogDisplayedFlightsChoice(k, fallback, false, 0, ftReason, "EAC");
                return fallback;
            }
            catch (Exception ex)
            {
                RRLog.Warn($"FlightTracker lookup failed for {k?.name}: {ex.Message}");
                LogDisplayedFlightsChoice(k, fallback, false, 0, ex.Message, "EAC");
                return fallback;
            }
        }

        private void MaybeRunPendingFlightTrackerSync()
        {
            if (_flightTrackerSyncExecutedThisSession) return;
            if (!RosterRotationState.SyncFlightTrackerFromEacOnce) return;
            if (!RRLog.VerboseEnabled) return;

            _flightTrackerSyncExecutedThisSession = true;
            RunFlightTrackerSyncFromEacOnce();
        }

        private void RunFlightTrackerSyncFromEacOnce()
        {
            int compared = 0;
            int updated = 0;
            int skipped = 0;

            RRLog.Verbose("[EAC] One-time FlightTracker sync requested by save flag. Starting EAC -> FlightTracker comparison.");

            try
            {
                foreach (var kvp in RosterRotationState.Records)
                {
                    string kerbalName = kvp.Key;
                    var rec = kvp.Value;
                    if (string.IsNullOrEmpty(kerbalName) || rec == null) continue;

                    int eacFlights = Math.Max(0, rec.Flights);
                    int ftFlights;
                    string ftReason;
                    bool ftAvailable = TryGetFlightTrackerFlights(kerbalName, out ftFlights, out ftReason);

                    compared++;

                    if (!ftAvailable)
                    {
                        RRLog.Verbose($"[EAC] FlightTracker sync skipped for {kerbalName}: EAC={eacFlights}, FlightTracker unavailable ({ftReason ?? "unknown"}).");
                        skipped++;
                        continue;
                    }

                    RRLog.Verbose($"[EAC] FlightTracker sync compare for {kerbalName}: EAC={eacFlights}, FlightTracker={ftFlights}.");

                    if (eacFlights <= ftFlights)
                        continue;

                    int previousValue;
                    string setReason;
                    if (TrySetFlightTrackerFlights(kerbalName, eacFlights, out previousValue, out setReason))
                    {
                        updated++;
                        RRLog.Verbose($"[EAC] Synced FlightTracker flights for {kerbalName}: FlightTracker={previousValue} -> EAC={eacFlights}.");
                    }
                    else
                    {
                        skipped++;
                        RRLog.Verbose($"[EAC] Failed to sync FlightTracker flights for {kerbalName}: EAC={eacFlights}, FlightTracker={ftFlights}, reason={setReason ?? "unknown"}.");
                    }
                }
            }
            finally
            {
                RosterRotationState.SyncFlightTrackerFromEacOnce = false;
                RRLog.Verbose($"[EAC] One-time FlightTracker sync finished. Compared={compared}, Updated={updated}, Skipped={skipped}. Save flag reset to False.");
                SaveScheduler.RequestImmediateSave("FlightTracker sync");
                RRLog.Verbose("[EAC] Saved persistent.sfs after one-time FlightTracker sync.");
            }
        }

        private const float TraitGrowthCourageCap = 0.90f;
        private const float TraitGrowthStupidityFloor = 0.10f;
        private const float TraitGrowthVeteranBaseChance = 0.25f;
        private const float TraitGrowthVeteranFlightBonusPerFlight = 0.01f;
        private const float TraitGrowthVeteranFlightBonusCap = 0.10f;
        private const float TraitGrowthVeteranHourBonusPerTenHours = 0.01f;
        private const float TraitGrowthVeteranHourBonusCap = 0.10f;
        private const float TraitGrowthVeteranTotalBonusCap = 0.15f;
        private const float TraitGrowthVeteranDelta = 0.02f;

        public static void TryApplyVeteranTraitGrowthOnRecovery(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, Vessel vessel, double nowUT)
        {
            if (!RosterRotationState.TraitGrowthEnabled) return;
            if (k == null || rec == null) return;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return;
            if (rec.Retired) return;
            if (k.type != ProtoCrewMember.KerbalType.Crew) return;
            if (k.experienceLevel < 3)
            {
                if (RRLog.VerboseEnabled)
                    RRLog.Verbose($"[EAC] Trait growth (veteran) skipped for {k.name}: experienceLevel={k.experienceLevel} (< 3).");
                return;
            }

            int eacFlights = Math.Max(0, rec.Flights);
            int ftFlights;
            string ftFlightsReason;
            bool ftFlightsAvailable = TryGetFlightTrackerFlights(k.name, out ftFlights, out ftFlightsReason);
            int flights = ftFlightsAvailable ? Math.Max(eacFlights, Math.Max(0, ftFlights)) : eacFlights;

            double ftHours;
            string ftHoursReason;
            bool ftHoursAvailable = TryGetFlightTrackerRecordedHours(k.name, out ftHours, out ftHoursReason);
            double currentMissionHours = vessel != null ? Math.Max(0.0, vessel.missionTime / 3600.0) : 0.0;
            double totalHours = (ftHoursAvailable ? Math.Max(0.0, ftHours) : 0.0) + currentMissionHours;

            float flightBonus = Mathf.Min(TraitGrowthVeteranFlightBonusCap, flights * TraitGrowthVeteranFlightBonusPerFlight);
            float hourBonus = Mathf.Min(TraitGrowthVeteranHourBonusCap, (float)(Math.Floor(totalHours / 10.0) * TraitGrowthVeteranHourBonusPerTenHours));
            float serviceBonus = Mathf.Min(TraitGrowthVeteranTotalBonusCap, flightBonus + hourBonus);
            float courageChance = Mathf.Clamp01((TraitGrowthVeteranBaseChance + serviceBonus) * Mathf.Clamp01(1f - k.courage));
            float stupidityChance = Mathf.Clamp01((TraitGrowthVeteranBaseChance + serviceBonus) * Mathf.Clamp01(k.stupidity));

            ApplyTraitGrowthRolls(
                k,
                courageChance,
                stupidityChance,
                TraitGrowthVeteranDelta,
                TraitGrowthVeteranDelta,
                "Veteran service",
                $"[EAC] Trait growth (veteran) for {k.name}: EACFlights={eacFlights}, FTFlights={(ftFlightsAvailable ? ftFlights.ToString() : "unavailable:" + (ftFlightsReason ?? "unknown"))}, FTHours={(ftHoursAvailable ? ftHours.ToString("F2") : "unavailable:" + (ftHoursReason ?? "unknown"))}, CurrentMissionHours={currentMissionHours:F2}, TotalHours={totalHours:F2}, CourageChance={courageChance:P1}, StupidityChance={stupidityChance:P1}",
                nowUT);
        }

        public static void TryApplyTrainingTraitGrowth(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int targetLevel, double nowUT)
        {
            if (!RosterRotationState.TraitGrowthEnabled) return;
            if (k == null || rec == null) return;
            if (targetLevel < 1 || targetLevel > 3) return;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return;

            float baseChance;
            float delta;
            switch (targetLevel)
            {
                case 1: baseChance = 0.15f; delta = 0.01f; break;
                case 2: baseChance = 0.25f; delta = 0.015f; break;
                default: baseChance = 0.35f; delta = 0.02f; break;
            }

            float courageChance = Mathf.Clamp01(baseChance * Mathf.Clamp01(1f - k.courage));
            float stupidityChance = Mathf.Clamp01(baseChance * Mathf.Clamp01(k.stupidity));

            ApplyTraitGrowthRolls(
                k,
                courageChance,
                stupidityChance,
                delta,
                delta,
                $"Level {targetLevel} training",
                $"[EAC] Trait growth (training) for {k.name}: TargetLevel={targetLevel}, CourageChance={courageChance:P1}, StupidityChance={stupidityChance:P1}",
                nowUT);
        }

        private static void ApplyTraitGrowthRolls(ProtoCrewMember k, float courageChance, float stupidityChance, float courageDelta, float stupidityDelta, string sourceLabel, string verbosePrefix, double nowUT)
        {
            if (k == null) return;

            float oldCourage = Mathf.Clamp01(k.courage);
            float oldStupidity = Mathf.Clamp01(k.stupidity);
            float courageRoll = UnityEngine.Random.value;
            float stupidityRoll = UnityEngine.Random.value;

            bool courageSuccess = courageRoll < courageChance && oldCourage < TraitGrowthCourageCap;
            bool stupiditySuccess = stupidityRoll < stupidityChance && oldStupidity > TraitGrowthStupidityFloor;

            float newCourage = courageSuccess ? Mathf.Min(TraitGrowthCourageCap, oldCourage + courageDelta) : oldCourage;
            float newStupidity = stupiditySuccess ? Mathf.Max(TraitGrowthStupidityFloor, oldStupidity - stupidityDelta) : oldStupidity;

            if (RRLog.VerboseEnabled)
            {
                RRLog.Verbose(verbosePrefix +
                    $", CourageRoll={courageRoll:F3}, StupidityRoll={stupidityRoll:F3}, CourageSuccess={courageSuccess}, StupiditySuccess={stupiditySuccess}, OldCourage={oldCourage:P0}, NewCourage={newCourage:P0}, OldStupidity={oldStupidity:P0}, NewStupidity={newStupidity:P0}");
            }

            if (!courageSuccess && !stupiditySuccess) return;

            try { k.courage = newCourage; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1896", "Suppressed exception in Mod.cs:1896", ex); }
            try { k.stupidity = newStupidity; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1897", "Suppressed exception in Mod.cs:1897", ex); }

            var changes = new List<string>();
            if (courageSuccess) changes.Add($"Courage {oldCourage:P0} to {newCourage:P0}");
            if (stupiditySuccess) changes.Add($"Stupidity {oldStupidity:P0} to {newStupidity:P0}");

            string body = $"{k.name} showed growth from {sourceLabel}: {string.Join(", ", changes.ToArray())}. ({RosterRotationState.FormatGameDate(nowUT)})";
            RosterRotationState.PostNotification(
                EACNotificationType.Training,
                $"Trait Growth — {k.name}",
                body,
                MessageSystemButton.MessageButtonColor.GREEN,
                MessageSystemButton.ButtonIcons.COMPLETE,
                6f);
        }

        private static MethodInfo GetFlightTrackerMissionHoursMethod()
        {
            if (_searchedFlightTrackerMissionHoursMethod) return _cachedFlightTrackerMissionHoursMethod;
            _searchedFlightTrackerMissionHoursMethod = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        try
                        {
                            var mi = t.GetMethod("GetRecordedMissionTimeHours", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                                  ?? t.GetMethod("GetRecordedMissionTimeHours", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)
                                  ?? t.GetMethod("GetRecordedMissionTimeSeconds", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                                  ?? t.GetMethod("GetRecordedMissionTimeSeconds", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                            if (mi == null) continue;

                            _cachedFlightTrackerMissionHoursMethod = mi;
                            if (_cachedFlightTrackerApiType == null)
                            {
                                _cachedFlightTrackerApiType = t;
                                _cachedFlightTrackerApiInstanceProperty = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                _cachedFlightTrackerApiInstanceField = t.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            }
                            RRLog.Info($"[EAC] FlightTracker time API detected: {t.FullName}.{mi.Name}(string) [{(mi.IsStatic ? "static" : "instance")}]");
                            return _cachedFlightTrackerMissionHoursMethod;
                        }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1949", "Suppressed exception in Mod.cs:1949", ex); }
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:1953", "Suppressed exception in Mod.cs:1953", ex); }

            return null;
        }

        private static bool TryGetFlightTrackerRecordedHours(string kerbalName, out double hours, out string reason)
        {
            hours = 0.0;
            reason = null;
            if (string.IsNullOrEmpty(kerbalName))
            {
                reason = "kerbal name missing";
                return false;
            }

            try
            {
                var api = GetFlightTrackerMissionHoursMethod();
                if (api == null)
                {
                    reason = "FlightTracker time API not found";
                    return false;
                }

                object target = null;
                if (!api.IsStatic)
                {
                    target = GetFlightTrackerApiInstance();
                    if (target == null)
                    {
                        reason = "FlightTracker API instance unavailable";
                        return false;
                    }
                }

                object raw = api.Invoke(target, new object[] { kerbalName });
                if (raw == null)
                {
                    reason = "FlightTracker time API returned null";
                    return false;
                }

                double value = Convert.ToDouble(raw);
                if ((api.Name ?? string.Empty).IndexOf("Seconds", StringComparison.OrdinalIgnoreCase) >= 0)
                    value /= 3600.0;

                hours = Math.Max(0.0, value);
                reason = "api";
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static string TrainingLabel(TrainingType t, int targetLevel)
        {
            return CareerRules.GetTrainingLabel(t, targetLevel);
        }

        private static double TrainingFundsCost(double hireCost, int targetLevel)
            => CareerRules.CalculateTrainingFundsCost(hireCost, RosterRotationState.TrainingFundsMultiplier, targetLevel);
        private static double TrainingRDCost(int targetLevel)
            => CareerRules.CalculateTrainingRDCost(RosterRotationState.TrainingRDPerStar, targetLevel);

        private void DrawHRule()
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, KspGuiSkin.HorizontalSlider,
                GUILayout.Height(2), GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        private static void RejectApplicant(KerbalRoster roster, ProtoCrewMember applicant)
        {
            try
            {
                if (roster == null || applicant == null) return;
                MethodInfo rm = null;
                foreach (var m in typeof(KerbalRoster).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m == null) continue;
                    string n = (m.Name ?? "").ToLowerInvariant();
                    if (!(n.Contains("remove") || n.Contains("delete") || n.Contains("reject"))) continue;
                    var ps = m.GetParameters();
                    if (ps?.Length == 1 && ps[0].ParameterType == typeof(ProtoCrewMember)) { rm = m; break; }
                }
                if (rm != null) rm.Invoke(roster, new object[] { applicant });
                else
                {
                    var f = typeof(KerbalRoster)
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(fi => fi != null && typeof(List<ProtoCrewMember>).IsAssignableFrom(fi.FieldType));
                    var list = f?.GetValue(roster) as List<ProtoCrewMember>;
                    list?.Remove(applicant);
                }
                SaveScheduler.RequestSave("reject applicant");
            }
            catch (Exception e) { RRLog.Error("RejectApplicant failed: " + e); }
        }

        internal static double GetNextHireCost()
        {
            try
            {
                var gv = GameVariables.Instance;
                if (gv == null) return RosterRotationState.TrainingBaseFundsCost;

                float facLevel = (float)ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex);
                int recruitCount = GetRecruitCostInputCount();
                float nowRT = Time.realtimeSinceStartup;

                if (nowRT - _lastHireCostCacheRT < UiCacheSeconds
                    && _cachedHireCostActiveCount == recruitCount
                    && Math.Abs(_cachedHireCostFacilityLevel - facLevel) < 0.001f)
                {
                    return _cachedHireCost;
                }

                if (_cachedRecruitCostCountMethod == null)
                {
                    _cachedRecruitCostCountMethod = typeof(GameVariables).GetMethod("GetRecruitCost",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(int), typeof(float) }, null);
                }

                if (_cachedRecruitCostFacilityMethod == null)
                {
                    _cachedRecruitCostFacilityMethod = typeof(GameVariables).GetMethod("GetRecruitCost",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(float) }, null);
                }

                if (_cachedRecruitCostCountMethod != null)
                {
                    var res = _cachedRecruitCostCountMethod.Invoke(gv, new object[] { recruitCount, facLevel });
                    if (res is float f2) return CacheHireCost(f2, recruitCount, facLevel, nowRT);
                    if (res is double d2) return CacheHireCost(d2, recruitCount, facLevel, nowRT);
                }

                if (_cachedRecruitCostFacilityMethod != null)
                {
                    var res = _cachedRecruitCostFacilityMethod.Invoke(gv, new object[] { facLevel });
                    if (res is float f) return CacheHireCost(f, recruitCount, facLevel, nowRT);
                    if (res is double d) return CacheHireCost(d, recruitCount, facLevel, nowRT);
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:2104", "Suppressed exception in Mod.cs:2104", ex); }
            return CacheHireCost(RosterRotationState.TrainingBaseFundsCost, GetRecruitCostInputCount(),
                (float)ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex), Time.realtimeSinceStartup);
        }

        private static int GetRecruitCostInputCount()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return 0;

            int count = 0;
            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                count++;
            }
            return count;
        }

        private static double CacheHireCost(double value, int recruitCount, float facilityLevel, float nowRT)
        {
            _cachedHireCost = value;
            _cachedHireCostActiveCount = recruitCount;
            _cachedHireCostFacilityLevel = facilityLevel;
            _lastHireCostCacheRT = nowRT;
            return value;
        }

        private static void RefreshCrewCapacityCacheIfNeeded()
        {
            float nowRT = Time.realtimeSinceStartup;
            if (nowRT - _lastCrewCapacityCacheRT < UiCacheSeconds) return;
            _lastCrewCapacityCacheRT = nowRT;

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null)
            {
                _cachedActiveNonRetiredCount = 0;
                _cachedMaxCrew = int.MaxValue;
                return;
            }

            int n = 0;
            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                    k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                if (RosterRotationState.Records.TryGetValue(k.name, out var r) && r?.Retired == true) continue;
                n++;
            }
            _cachedActiveNonRetiredCount = n;

            int cached = ACPatches.GetCachedMaxCrew();
            _cachedMaxCrew = cached < int.MaxValue ? cached : int.MaxValue;
        }

        private static int GetMaxCrew()
        {
            RefreshCrewCapacityCacheIfNeeded();
            return _cachedMaxCrew;
        }

        private static int GetActiveNonRetiredCount()
        {
            RefreshCrewCapacityCacheIfNeeded();
            return _cachedActiveNonRetiredCount;
        }

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            double ds = RosterRotationState.DaySeconds;
            if (seconds / ds >= 1.0) return $"{seconds / ds:0.0}d";
            if (seconds / 3600 >= 1.0) return $"{seconds / 3600:0.0}h";
            return $"{seconds / 60:0}m";
        }

        // ── Age assignment ─────────────────────────────────────────────────────
        internal static void AssignAgeOnHire(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            double yearSec = RosterRotationState.YearSeconds;
            AgeAssignmentResult result = CareerRules.CalculateAgeOnHire(
                nowUT,
                yearSec,
                RosterRotationState.RetirementAgeMin,
                RosterRotationState.RetirementAgeMax,
                UnityEngine.Random.value,
                UnityEngine.Random.value,
                UnityEngine.Random.value,
                UnityEngine.Random.value);
            rec.BirthUT = result.BirthUT;
            rec.LastAgedYears = result.LastAgedYears;
            rec.NaturalRetirementUT = result.NaturalRetirementUT;
        }

        internal static void AssignAgeByExperience(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            double yearSec = RosterRotationState.YearSeconds;
            AgeAssignmentResult result = CareerRules.CalculateAgeByExperience(
                k != null ? (int)k.experienceLevel : 0,
                nowUT,
                yearSec,
                RosterRotationState.RetirementAgeMin,
                RosterRotationState.RetirementAgeMax,
                UnityEngine.Random.value,
                UnityEngine.Random.value,
                UnityEngine.Random.value);
            rec.BirthUT = result.BirthUT;
            rec.LastAgedYears = result.LastAgedYears;
            rec.NaturalRetirementUT = result.NaturalRetirementUT;
        }

        // ── Aging checks ───────────────────────────────────────────────────────
        private void CheckAgingAndRetirement()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;
            double nowUT = Planetarium.GetUniversalTime();
            double yearSec = RosterRotationState.YearSeconds;
            bool anyDirty = false;

            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (rec.LastAgedYears < 0) continue;
                if (rec.DeathUT > 0 && !rec.Retired) continue;

                int currentAge = RosterRotationState.GetKerbalAge(rec, nowUT);
                if (currentAge < 0) continue;

                bool onMission = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                if (onMission)
                {
                    if (rec.MissionStartUT <= 0 || rec.MissionStartUT > nowUT)
                    {
                        rec.MissionStartUT = nowUT;
                        anyDirty = true;
                    }
                }
                else if (rec.MissionStartUT > 0 || rec.LastMissionDeathCheckUT > 0)
                {
                    rec.MissionStartUT = 0;
                    rec.LastMissionDeathCheckUT = 0;
                    anyDirty = true;
                }

                double effectiveRetireUT = rec.NaturalRetirementUT + rec.RetirementDelayYears * yearSec;
                if (!rec.Retired)
                {
                    if (CheckAssignedMissionDeath(k, rec, nowUT, currentAge, effectiveRetireUT))
                        anyDirty = true;

                    if (rec.DeathUT > 0)
                        continue;
                }

                if (rec.Retired)
                {
                    if (rec.DeathUT <= 0)
                    {
                        if (CheckRetiredDeath(k, rec, nowUT, currentAge))
                            anyDirty = true;

                        if (rec.DeathUT <= 0
                        && RetiredKerbalCleanupService.IsRetiredPurgeDue(k, rec, nowUT))
                        {
                            if (RetiredKerbalCleanupService.RequestAutoCleanupSave(k.name))
                                anyDirty = true;
                        }
                    }

                    continue;
                }
                if (currentAge <= rec.LastAgedYears) continue;
                rec.LastAgedYears = currentAge;
                anyDirty = true;

                if (k.rosterStatus != ProtoCrewMember.RosterStatus.Dead
                 && k.rosterStatus != ProtoCrewMember.RosterStatus.Missing)
                {
                    RosterRotationState.PostNotification(
                        EACNotificationType.Birthday, "Birthday — " + k.name,
                        k.name + " turns " + currentAge + " today! (" + RosterRotationState.FormatGameDate(nowUT) + ")",
                        MessageSystemButton.MessageButtonColor.GREEN,
                        MessageSystemButton.ButtonIcons.MESSAGE, 5f);
                }

                if (nowUT >= effectiveRetireUT)
                {
                    if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                    {
                        rec.RetirementScheduled = true;
                        rec.RetirementScheduledUT = nowUT;
                        RosterRotationState.PostNotification(EACNotificationType.Retirement,
                            $"Retirement Pending — {k.name}",
                            $"{k.name} has reached retirement age but will serve until mission end.",
                            MessageSystemButton.MessageButtonColor.YELLOW, MessageSystemButton.ButtonIcons.ALERT);
                    }
                    else
                        FireRetirement(k, rec, nowUT, "reached retirement age");
                    continue;
                }

                if (!rec.RetirementWarned && (effectiveRetireUT - nowUT) < yearSec)
                {
                    rec.RetirementWarned = true;
                    RosterRotationState.PostNotification(EACNotificationType.Retirement,
                        $"Retirement Warning — {k.name}",
                        $"{k.name} is approaching retirement age and may retire within the year.",
                        MessageSystemButton.MessageButtonColor.YELLOW, MessageSystemButton.ButtonIcons.ALERT);
                }

                int stars = (int)k.experienceLevel;
                if (stars >= 4) continue;
                double pRetire = MoraleRetireProbability(k, rec, nowUT, stars);
                if (pRetire <= 0) continue;
                if (UnityEngine.Random.value < pRetire)
                {
                    if (!rec.RetirementWarned)
                    {
                        rec.RetirementWarned = true;
                        RosterRotationState.PostNotification(EACNotificationType.Retirement,
                            $"Retirement Warning — {k.name}",
                            $"{k.name} is considering retirement.",
                            MessageSystemButton.MessageButtonColor.YELLOW, MessageSystemButton.ButtonIcons.ALERT);
                    }
                    else
                    {
                        if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                        {
                            rec.RetirementScheduled = true;
                            rec.RetirementScheduledUT = nowUT;
                        }
                        else
                            FireRetirement(k, rec, nowUT, "decided to retire");
                    }
                }
            }

            if (anyDirty)
                SaveScheduler.RequestSave("aging and retirement");
        }

        private static double MoraleRetireProbability(ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec, double nowUT, int stars)
        {
            double yearSec = RosterRotationState.YearSeconds;
            double inactiveYears = rec != null && rec.LastFlightUT > 0 ? (nowUT - rec.LastFlightUT) / yearSec : 999;
            int displayedFlights = GetDisplayedFlights(k, rec);
            return CareerRules.CalculateMoraleRetireProbability(stars, inactiveYears, displayedFlights);
        }

        private void FireRetirement(ProtoCrewMember k, RosterRotationState.KerbalRecord rec,
            double nowUT, string reason)
        {
            if (string.IsNullOrEmpty(rec.OriginalTrait)) rec.OriginalTrait = k.trait;
            rec.OriginalType = k.type;
            rec.Retired = true;
            rec.RetiredUT = nowUT;
            rec.ExperienceAtRetire = (int)k.experienceLevel;
            rec.RetirementWarned = false;
            rec.RetirementScheduled = false;
            k.inactive        = true;
            k.inactiveTimeEnd = nowUT + RosterRotationState.YearSeconds * 1000.0;
            RosterRotationState.InvalidateRetiredCache();
            RetiredKerbalCleanupService.ResetAutoCleanupRequest(k.name);

            int frAge = RosterRotationState.GetKerbalAge(rec, nowUT);
            string frAgeS = frAge >= 0 ? $" at age {frAge}" : "";
            RosterRotationState.PostNotification(EACNotificationType.Retirement,
                $"Retired — {k.name}",
                $"{k.name} has {reason} and entered retirement{frAgeS}. ({RosterRotationState.FormatGameDate(nowUT)})",
                MessageSystemButton.MessageButtonColor.ORANGE, MessageSystemButton.ButtonIcons.MESSAGE);
            InvalidateUICaches();
            _pendingForceRefresh = true;
        }

        private bool CheckRetiredDeath(ProtoCrewMember k, RosterRotationState.KerbalRecord rec,
            double nowUT, int currentAge)
        {
            if (currentAge <= rec.LastAgedYears) return false;
            rec.LastAgedYears = currentAge;
            int minAge = RosterRotationState.RetiredDeathAgeMin;
            double pDeath;
            if      (currentAge >= minAge + 30) pDeath = 0.30;
            else if (currentAge >= minAge + 20) pDeath = 0.14;
            else if (currentAge >= minAge + 10) pDeath = 0.06;
            else if (currentAge >= minAge)      pDeath = 0.02;
            else                                return true;

            if (UnityEngine.Random.value >= pDeath) return true;

            rec.DeathUT = nowUT;
            rec.DiedOnMission = false;
            rec.PendingMissionDeath = false;
            try { k.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:2412", "Suppressed exception in Mod.cs:2412", ex); }

            if (RosterRotationState.DeathNotificationsEnabled)
                RosterRotationState.PostNotification(EACNotificationType.Death,
                    $"Deceased — {k.name}",
                    $"{k.name} has passed away at age {currentAge}. ({RosterRotationState.FormatGameDate(nowUT)})",
                    MessageSystemButton.MessageButtonColor.RED, MessageSystemButton.ButtonIcons.ALERT, 12f);

            InvalidateUICaches();
            _pendingForceRefresh = true;
            return true;
        }

        private static bool TryDetachKerbalFromAssignedVessel(ProtoCrewMember k, out string vesselName)
        {
            vesselName = null;
            if (k == null || string.IsNullOrEmpty(k.name)) return false;

            try
            {
                var protoVessels = HighLogic.CurrentGame?.flightState?.protoVessels as System.Collections.IEnumerable;
                if (protoVessels == null) return false;

                foreach (var pv in protoVessels)
                {
                    if (pv == null) continue;

                    bool foundCrew = false;
                    try
                    {
                        var pvType = pv.GetType();
                        var getCrew = pvType.GetMethod("GetVesselCrew", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var crewRaw = getCrew?.Invoke(pv, null) as System.Collections.IEnumerable;
                        if (crewRaw == null) continue;

                        foreach (var crewObj in crewRaw)
                        {
                            var crew = crewObj as ProtoCrewMember;
                            string crewName = crew != null ? crew.name : null;
                            if (string.IsNullOrEmpty(crewName) && crewObj != null)
                            {
                                var ct = crewObj.GetType();
                                crewName = ct.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(crewObj) as string;
                                if (string.IsNullOrEmpty(crewName))
                                    crewName = ct.GetProperty("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(crewObj, null) as string;
                            }
                            if (!string.Equals(crewName, k.name, StringComparison.Ordinal)) continue;
                            foundCrew = true;
                            break;
                        }
                        if (!foundCrew) continue;

                        vesselName = pvType.GetField("vesselName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pv) as string;
                        if (string.IsNullOrEmpty(vesselName))
                            vesselName = pvType.GetProperty("vesselName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pv, null) as string;

                        object partsObj = pvType.GetField("protoPartSnapshots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pv);
                        if (partsObj == null)
                            partsObj = pvType.GetProperty("protoPartSnapshots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pv, null);

                        var parts = partsObj as System.Collections.IEnumerable;
                        if (parts == null) return false;

                        bool removed = false;
                        int removedConfigCrew = 0;
                        int removedCachedCrew = 0;
                        foreach (var partObj in parts)
                        {
                            if (partObj == null) continue;
                            var pt = partObj.GetType();
                            object crewListObj = pt.GetField("protoModuleCrew", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(partObj);
                            if (crewListObj == null)
                                crewListObj = pt.GetProperty("protoModuleCrew", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(partObj, null);

                            var list = crewListObj as System.Collections.IList;
                            if (list != null && list.Count > 0)
                            {
                                for (int i = list.Count - 1; i >= 0; --i)
                                {
                                    var crewEntry = list[i];
                                    var crew = crewEntry as ProtoCrewMember;
                                    string crewName = crew != null ? crew.name : null;
                                    if (string.IsNullOrEmpty(crewName) && crewEntry != null)
                                    {
                                        var ct = crewEntry.GetType();
                                        crewName = ct.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(crewEntry) as string;
                                        if (string.IsNullOrEmpty(crewName))
                                        {
                                            var prop = ct.GetProperty("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                                                crewName = prop.GetValue(crewEntry, null) as string;
                                        }
                                    }
                                    if (!string.Equals(crewName, k.name, StringComparison.Ordinal)) continue;

                                    list.RemoveAt(i);
                                    removed = true;
                                }
                            }

                            removedCachedCrew += RemoveKerbalFromCrewLikeLists(partObj, k.name);

                            foreach (var field in pt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                if (field == null || field.FieldType != typeof(ConfigNode)) continue;
                                var node = field.GetValue(partObj) as ConfigNode;
                                if (node == null) continue;
                                removedConfigCrew += RemoveKerbalFromConfigNodeCrewValues(node, k.name);
                            }
                            foreach (var prop in pt.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                if (prop == null || prop.PropertyType != typeof(ConfigNode) || !prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                                ConfigNode node = null;
                                try { node = prop.GetValue(partObj, null) as ConfigNode; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:2525", "Suppressed exception in Mod.cs:2525", ex); }
                                if (node == null) continue;
                                removedConfigCrew += RemoveKerbalFromConfigNodeCrewValues(node, k.name);
                            }
                        }

                        removedCachedCrew += RemoveKerbalFromCrewLikeLists(pv, k.name);
                        TryRefreshProtoVesselCrewCaches(pv);

                        if (removed || removedConfigCrew > 0 || removedCachedCrew > 0)
                        {
                            RRLog.Verbose("[EAC] Detached " + k.name + " from vessel " + (string.IsNullOrEmpty(vesselName) ? "<unknown>" : vesselName)
                                + " for mission death processing. protoModuleCrewRemoved=" + removed
                                + ", configCrewValuesRemoved=" + removedConfigCrew
                                + ", cachedCrewEntriesRemoved=" + removedCachedCrew + ".");
                            return true;
                        }

                        return false;
                    }
                    catch (Exception ex)
                    {
                        RRLog.Error("[EAC] Failed detaching " + k.name + " from assigned vessel: " + ex);
                        if (foundCrew)
                            return false;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error("[EAC] Exception while scanning assigned vessels for " + k.name + ": " + ex);
            }

            return false;
        }

        private static int RemoveKerbalFromConfigNodeCrewValues(ConfigNode node, string kerbalName)
        {
            if (node == null || string.IsNullOrEmpty(kerbalName)) return 0;

            int removed = 0;
            var keepCrew = new System.Collections.Generic.List<string>();
            bool touchedCrewValues = false;

            foreach (ConfigNode.Value value in node.values)
            {
                if (value == null) continue;
                if (!string.Equals(value.name, "crew", StringComparison.OrdinalIgnoreCase)) continue;
                touchedCrewValues = true;
                if (string.Equals(value.value, kerbalName, StringComparison.Ordinal))
                {
                    removed++;
                    continue;
                }
                keepCrew.Add(value.value);
            }

            if (touchedCrewValues)
            {
                node.RemoveValues("crew");
                for (int i = 0; i < keepCrew.Count; i++)
                    node.AddValue("crew", keepCrew[i]);
            }

            foreach (ConfigNode child in node.nodes)
                removed += RemoveKerbalFromConfigNodeCrewValues(child, kerbalName);

            return removed;
        }

        private static int RemoveKerbalFromCrewLikeLists(object owner, string kerbalName)
        {
            if (owner == null || string.IsNullOrEmpty(kerbalName)) return 0;

            int removed = 0;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = owner.GetType();

            foreach (var field in type.GetFields(flags))
            {
                if (field == null) continue;
                if (field.FieldType == typeof(string)) continue;
                if (!typeof(System.Collections.IList).IsAssignableFrom(field.FieldType)) continue;
                if (field.Name.IndexOf("crew", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var list = field.GetValue(owner) as System.Collections.IList;
                if (list == null || list.Count == 0) continue;

                for (int i = list.Count - 1; i >= 0; --i)
                {
                    var entry = list[i];
                    if (entry == null) continue;

                    string entryName = entry as string;
                    var pcm = entry as ProtoCrewMember;
                    if (string.IsNullOrEmpty(entryName) && pcm != null)
                        entryName = pcm.name;
                    if (string.IsNullOrEmpty(entryName))
                    {
                        var et = entry.GetType();
                        entryName = et.GetField("name", flags)?.GetValue(entry) as string;
                        if (string.IsNullOrEmpty(entryName))
                        {
                            var prop = et.GetProperty("name", flags);
                            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                                entryName = prop.GetValue(entry, null) as string;
                        }
                    }

                    if (!string.Equals(entryName, kerbalName, StringComparison.Ordinal)) continue;
                    list.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        private static void TryRefreshProtoVesselCrewCaches(object protoVessel)
        {
            if (protoVessel == null) return;

            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var pvType = protoVessel.GetType();

                int totalCrew = 0;
                int crewedParts = 0;
                object partsObj = pvType.GetField("protoPartSnapshots", flags)?.GetValue(protoVessel)
                               ?? pvType.GetProperty("protoPartSnapshots", flags)?.GetValue(protoVessel, null);
                var parts = partsObj as System.Collections.IEnumerable;
                if (parts != null)
                {
                    foreach (var partObj in parts)
                    {
                        if (partObj == null) continue;
                        var pt = partObj.GetType();
                        object crewListObj = pt.GetField("protoModuleCrew", flags)?.GetValue(partObj)
                                          ?? pt.GetProperty("protoModuleCrew", flags)?.GetValue(partObj, null);
                        var list = crewListObj as System.Collections.IList;
                        int count = list != null ? list.Count : 0;
                        if (count > 0)
                        {
                            totalCrew += count;
                            crewedParts++;
                        }
                    }
                }

                var vesselCrewField = pvType.GetField("vesselCrew", flags);
                if (vesselCrewField != null && vesselCrewField.FieldType == typeof(int))
                    vesselCrewField.SetValue(protoVessel, totalCrew);
                var vesselCrewProp = pvType.GetProperty("vesselCrew", flags);
                if (vesselCrewProp != null && vesselCrewProp.CanWrite && vesselCrewProp.PropertyType == typeof(int))
                    vesselCrewProp.SetValue(protoVessel, totalCrew, null);

                var crewedPartsField = pvType.GetField("crewedParts", flags);
                if (crewedPartsField != null && crewedPartsField.FieldType == typeof(int))
                    crewedPartsField.SetValue(protoVessel, crewedParts);
                var crewedPartsProp = pvType.GetProperty("crewedParts", flags);
                if (crewedPartsProp != null && crewedPartsProp.CanWrite && crewedPartsProp.PropertyType == typeof(int))
                    crewedPartsProp.SetValue(protoVessel, crewedParts, null);

                object vesselRef = pvType.GetField("vesselRef", flags)?.GetValue(protoVessel)
                               ?? pvType.GetProperty("vesselRef", flags)?.GetValue(protoVessel, null);
                if (vesselRef != null)
                {
                    var vt = vesselRef.GetType();
                    vt.GetMethod("CrewListSetDirty", flags)?.Invoke(vesselRef, null);
                    var crewWasModified = vt.GetMethod("CrewWasModified", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                        new[] { vt }, null);
                    if (crewWasModified != null)
                        crewWasModified.Invoke(null, new[] { vesselRef });
                }
            }
            catch (Exception ex)
            {
                RRLog.Verbose("[EAC] Failed refreshing proto vessel crew caches: " + ex.Message);
            }
        }

        private bool CheckAssignedMissionDeath(ProtoCrewMember k, RosterRotationState.KerbalRecord rec,
            double nowUT, int currentAge, double effectiveRetireUT)
        {
            if (k == null || rec == null) return false;
            if (k.rosterStatus != ProtoCrewMember.RosterStatus.Assigned) return false;
            if (rec.DeathUT > 0) return false;

            bool forceTest = RosterRotationState.DebugForceMissionDeath; // TEMP TEST HOOK
            if (!forceTest && !RosterRotationState.MissionDeathEnabled) return false;
            if (!forceTest && nowUT < effectiveRetireUT) return false;

            if (rec.MissionStartUT <= 0 || rec.MissionStartUT > nowUT)
                rec.MissionStartUT = nowUT;

            double daySec = RosterRotationState.DaySeconds;
            double lastCheckUT = rec.LastMissionDeathCheckUT > 0 ? rec.LastMissionDeathCheckUT : rec.MissionStartUT;
            double riskStartUT = forceTest ? lastCheckUT : Math.Max(lastCheckUT, effectiveRetireUT);
            double eligibleElapsedUT = nowUT - riskStartUT;
            if (!forceTest && eligibleElapsedUT < daySec) return false;

            int elapsedDays = forceTest ? 1 : Math.Max(1, (int)Math.Floor(eligibleElapsedUT / daySec));
            double missionDays = rec.MissionStartUT > 0 ? Math.Max(0.0, (nowUT - rec.MissionStartUT) / daySec) : 0.0;
            double yearsPastRetirement = forceTest ? 0.0 : Math.Max(0.0, (nowUT - effectiveRetireUT) / RosterRotationState.YearSeconds);
            double ageFactor = 1.0 + (yearsPastRetirement * yearsPastRetirement * 0.5);
            double stressFactor = 1.0 + Math.Min(3.0, missionDays / 120.0);
            double dailyChance = Math.Min(0.25, 0.000015 * ageFactor * stressFactor);
            double rollChance = forceTest ? 1.0 : 1.0 - Math.Pow(Math.Max(0.0, 1.0 - dailyChance), elapsedDays);
            float roll = UnityEngine.Random.value;

            rec.LastMissionDeathCheckUT = nowUT;

            if (RRLog.VerboseEnabled)
            {
                RRLog.Verbose(
                    $"[EAC] Mission death check for {k.name}: ForceTest={forceTest}, Age={currentAge}, YearsPastRetirement={yearsPastRetirement:F2}, MissionDays={missionDays:F1}, ElapsedDays={elapsedDays}, DailyChance={dailyChance:P4}, RollChance={rollChance:P4}, Roll={roll:F4}");
            }

            if (!forceTest && roll >= rollChance)
                return true;

            if (forceTest)
                RosterRotationState.DebugForceMissionDeath = false;

            string vesselName;
            if (!TryDetachKerbalFromAssignedVessel(k, out vesselName))
            {
                RRLog.Error("[EAC] Mission death could not detach " + (k != null ? k.name : "<null>") + " from assigned vessel; aborting death transition.");
                return false;
            }

            rec.DeathUT = nowUT;
            rec.DiedOnMission = true;
            rec.PendingMissionDeath = true;
            rec.RetirementScheduled = false;
            rec.MissionStartUT = 0;
            rec.LastMissionDeathCheckUT = 0;
            RetiredKerbalCleanupService.ResetAutoCleanupRequest(k.name);

            // Reassert a dead roster state in memory now that the vessel crew link has been severed.
            // Stock may briefly flip the kerbal to Missing during save capture; we set Dead again
            // immediately after the save call so later saves do not drift back to Missing.
            try { k.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:2768", "Suppressed exception in Mod.cs:2768", ex); }

            // Keep stock's fragile inactive-vessel roster bookkeeping out of the critical path.
            // We persist the authoritative death state and let the save-time patch finalize the
            // stock roster + vessel nodes if KSP tries to repair the assigned status during save.
            SaveScheduler.RequestImmediateSave("assigned mission death");
            try { if (k.rosterStatus != ProtoCrewMember.RosterStatus.Dead) k.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:2774", "Suppressed exception in Mod.cs:2774", ex); }

            if (RosterRotationState.DeathNotificationsEnabled)
            {
                string causeText = forceTest
                    ? $"[TEST] {k.name} was marked dead on mission."
                    : $"{k.name} has died on mission at age {currentAge} after {missionDays:F0} days in space.";

                RosterRotationState.PostNotification(
                    EACNotificationType.Death,
                    $"Deceased on mission — {k.name}",
                    causeText + $" ({RosterRotationState.FormatGameDate(nowUT)})",
                    MessageSystemButton.MessageButtonColor.RED,
                    MessageSystemButton.ButtonIcons.ALERT,
                    12f);
            }

            InvalidateUICaches();
            _pendingForceRefresh = true;
            return true;
        }
    }

}

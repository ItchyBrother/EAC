// EAC - Enhanced Astronaut Complex - Mod.cs
// Core shared state, enums, flight tracker, and KSC addon lifecycle.
// Large sections of RosterRotationKSCUI live in companion partial-class files:
//   Mod.FlightTracker.cs  — FlightTracker reflection bridge
//   Mod.TraitGrowth.cs    — Courage/stupidity trait growth
//   Mod.Aging.cs          — Aging, retirement, and mission-death logic
//   Mod.Drawing.cs        — All OnGUI Draw* methods
//   Mod.Roster.cs         — Roster row building, status strings, crew dialog helpers

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

    internal static class MissionTimeTracker
    {
        internal static bool SyncKerbal(ProtoCrewMember k, double nowUT, bool resetWhenNotAssigned = true)
        {
            if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) return false;

            var rec = RosterRotationState.GetOrCreate(k.name);
            bool onMission = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;

            if (onMission)
            {
                if (rec.MissionStartUT <= 0 || rec.MissionStartUT > nowUT)
                {
                    rec.MissionStartUT = nowUT;
                    rec.LastMissionDeathCheckUT = 0;
                    return true;
                }
                return false;
            }

            if (resetWhenNotAssigned && (rec.MissionStartUT > 0 || rec.LastMissionDeathCheckUT > 0))
            {
                rec.MissionStartUT = 0;
                rec.LastMissionDeathCheckUT = 0;
                return true;
            }

            return false;
        }

        internal static bool SyncRoster(double nowUT, bool resetWhenNotAssigned = true)
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            bool anyDirty = false;
            foreach (var k in roster.Crew)
            {
                if (SyncKerbal(k, nowUT, resetWhenNotAssigned))
                    anyDirty = true;
            }

            return anyDirty;
        }
    }

    // ── Flight scene tracker ───────────────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RosterRotationFlightTracker : MonoBehaviour
    {
        private void Start()
        {
            GameEvents.OnVesselRecoveryRequested.Add(OnRecover);
            GameEvents.onKerbalStatusChange.Add(OnKerbalStatusChange);

            double now = Planetarium.GetUniversalTime();
            if (MissionTimeTracker.SyncRoster(now))
                SaveScheduler.RequestSave("flight mission tracking init");
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
            var recoveredCrew = v.GetVesselCrew();
            foreach (var pcm in recoveredCrew)
            {
                if (pcm == null) continue;
                var r = RosterRotationState.GetOrCreate(pcm.name);
                r.Flights++;
                r.LastFlightUT = now;
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

            foreach (var pcm in recoveredCrew)
            {
                if (pcm == null) continue;
                var r = RosterRotationState.GetOrCreate(pcm.name);
                r.MissionStartUT = 0;
                r.LastMissionDeathCheckUT = 0;
            }
        }

        private void OnKerbalStatusChange(
            ProtoCrewMember pcm,
            ProtoCrewMember.RosterStatus oldStatus,
            ProtoCrewMember.RosterStatus newStatus)
        {
            if (pcm == null) return;
            var rec = RosterRotationState.GetOrCreate(pcm.name);
            double now = Planetarium.GetUniversalTime();

            if (newStatus == ProtoCrewMember.RosterStatus.Assigned)
            {
                if (MissionTimeTracker.SyncKerbal(pcm, now, resetWhenNotAssigned: false))
                    SaveScheduler.RequestSave("kerbal assigned mission tracking");
            }
            else if (oldStatus == ProtoCrewMember.RosterStatus.Assigned && newStatus != ProtoCrewMember.RosterStatus.Assigned)
            {
                if (MissionTimeTracker.SyncKerbal(pcm, now, resetWhenNotAssigned: false))
                    SaveScheduler.RequestSave("kerbal unassigned mission tracking");
            }

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

    // ── KSC UI (partial) ────────────────────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public partial class RosterRotationKSCUI : MonoBehaviour
    {
        private const string ModVersion  = "1.2.1";
        private const string WindowTitle = "Enhanced Astronaut Complex v" + ModVersion;

        public static bool RetiredTabSelected;

        // ── Core UI state ──────────────────────────────────────────────────────
        private Texture2D _iconTex;
        private ApplicationLauncherButton _btn;
        private bool    _show;
        private Rect    _window      = new Rect(300, 100, 940, 620);
        private Vector2 _scroll;

        private enum AcOverlay { None, Applicants, Training, ForceRetire }
        private AcOverlay _acOverlay     = AcOverlay.None;
        private bool      _prevACOpen;
        private Rect      _overlayWindow = new Rect(80, 120, 820, 500);
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

        // ── UI cache constants / capacity cache ────────────────────────────────
        private const float UiCacheSeconds = 0.25f;

        private static float _lastCrewCapacityCacheRT = -10f;
        private static int   _cachedActiveNonRetiredCount;
        private static int   _cachedMaxCrew = int.MaxValue;

        private static float  _lastHireCostCacheRT        = -10f;
        private static double _cachedHireCost             = RosterRotationState.TrainingBaseFundsCost;
        private static int    _cachedHireCostActiveCount  = int.MinValue;
        private static float  _cachedHireCostFacilityLevel = float.NaN;

        private static MethodInfo _cachedRecruitCostCountMethod;
        private static MethodInfo _cachedRecruitCostFacilityMethod;

        // ── Roster-row data type ───────────────────────────────────────────────
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

        // ── Lifecycle ──────────────────────────────────────────────────────────
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
        /// Periodic performance logger — dumps EAC overhead stats every 10 s when verbose logging is on.
        /// </summary>
        private IEnumerator PerfProfilerCoroutine()
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

                    ACOpenCache.PollCount        = 0;
                    ACOpenCache.CacheHitCount    = 0;
                    ACOpenCache.ExpensiveScanCount = 0;
                    ACOpenCache.TotalScanMs      = 0;

                    RRLog.Info(sb.ToString());
                }
                yield return wait;
            }
        }

        private IEnumerator StartupDelayed()
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

            double nowUT = Planetarium.GetUniversalTime();
            bool missionTrackingDirty = MissionTimeTracker.SyncRoster(nowUT);
            if (RosterRotationState.AgingEnabled)
                CheckAgingAndRetirement();
            else if (missionTrackingDirty)
                SaveScheduler.RequestSave("mission tracking sync");
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
            bool anyCleaned = CleanupStaleTrainingRecords(roster);
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
                        try { k.experienceLevel = target; } catch (Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:CheckTraining.SetLevel", "Suppressed", ex); }
                        rec.GrantedLevel = target;
                        try { k.careerLog.AddEntry("Training" + target, "Kerbin"); } catch (Exception ex) { RRLog.VerboseExceptionOnce("Mod.cs:CheckTraining.CareerLog", "Suppressed", ex); }

                        if (RosterRotationState.AgingEnabled && rec.NaturalRetirementUT > 0)
                        {
                            rec.RetirementDelayYears += target;
                            rec.RetirementWarned = false;
                        }

                        TryApplyTrainingTraitGrowth(k, rec, target, now);

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

            if (anyDone || anyCleaned)
            {
                InvalidateUICaches();
                SaveScheduler.RequestSave(anyDone ? "training completion" : "cleanup stale training records");
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
            bool hallOpen      = HallOfHistoryWindow.IsOpen;
            GUI.enabled = hallAvailable;
            string label = hallOpen ? "Close Hall" : "Open Hall";
            if (GUILayout.Button(label, GUILayout.Width(110), GUILayout.Height(24)))
            {
                if (hallOpen) HallOfHistoryWindow.HideWindow();
                else          HallOfHistoryWindow.ShowWindow(true);
            }
            GUI.enabled = true;
        }

        private void DrawHallStatusHint()
        {
            if (HallOfHistoryWindow.IsAvailable) return;
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.65f);
            GUILayout.Label("Hall of History is still initializing.");
            GUI.color = prev;
        }

        private string GetOverlayTitle(AcOverlay ov)
        {
            if (ov == AcOverlay.Applicants)  return $"EAC v{ModVersion}: Applicants";
            if (ov == AcOverlay.Training)    return $"EAC v{ModVersion}: Send to Training";
            if (ov == AcOverlay.ForceRetire) return $"EAC v{ModVersion}: Force Retire";
            return WindowTitle;
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
                try { Funding.Instance?.AddFunds(-recallCost, TransactionReasons.CrewRecruited); } catch (Exception ex) { RRLog.VerboseExceptionOnce("Mod.DoRecall.Funds", "Suppressed", ex); }
            }

            r.Retired = false;
            if (k.type == ProtoCrewMember.KerbalType.Tourist || k.type == ProtoCrewMember.KerbalType.Unowned)
            {
                k.type = r.OriginalType;
                if (k.type == 0) k.type = ProtoCrewMember.KerbalType.Crew;
            }
            k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
            if (!string.IsNullOrEmpty(r.OriginalTrait)) k.trait = r.OriginalTrait;
            try { k.experienceLevel = effStars; } catch (Exception ex) { RRLog.VerboseExceptionOnce("Mod.DoRecall.ExpLevel", "Suppressed", ex); }
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

        internal static double GetRecallFundsCost()
            => GetNextHireCost() * RosterRotationState.RecallFundsCostMultiplier;

        // ── Training execution ─────────────────────────────────────────────────
        private static double CalcTrainingDays(ProtoCrewMember k, int targetLevel)
            => CareerRules.CalculateTrainingDays(k != null ? k.stupidity : 0f, targetLevel, UnityEngine.Random.value);

        private void ExecuteTraining(ProtoCrewMember k, int targetLevel, double fCost, double rCost)
        {
            if (k == null) return;
            try
            {
                Funding.Instance?.AddFunds(-fCost, TransactionReasons.CrewRecruited);
                ResearchAndDevelopment.Instance?.AddScience((float)-rCost, TransactionReasons.CrewRecruited);
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("Mod.ExecuteTraining.Cost", "Suppressed", ex); }

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

        // ── Cache management ───────────────────────────────────────────────────
        private void InvalidateUICaches()
        {
            _lastRosterRowsCacheRT         = -10f;
            _lastApplicantsCacheRT         = -10f;
            _lastTrainingCandidatesCacheRT = -10f;
            _lastRetireRowsCacheRT         = -10f;
            _lastCrewCapacityCacheRT       = -10f;
            InvalidateCrewCapacityCache();
            CrewRandRAdapter.InvalidateVacationCache();
        }

        internal static void InvalidateCrewCapacityCache()
        {
            _lastCrewCapacityCacheRT = -10f;
            _lastHireCostCacheRT     = -10f;
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

        private static int GetMaxCrew()             { RefreshCrewCapacityCacheIfNeeded(); return _cachedMaxCrew; }
        private static int GetActiveNonRetiredCount() { RefreshCrewCapacityCacheIfNeeded(); return _cachedActiveNonRetiredCount; }

        // ── Hire cost ──────────────────────────────────────────────────────────
        internal static double GetNextHireCost()
        {
            try
            {
                var gv = GameVariables.Instance;
                if (gv == null) return RosterRotationState.TrainingBaseFundsCost;

                float facLevel    = (float)ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex);
                int   recruitCount = GetRecruitCostInputCount();
                float nowRT       = Time.realtimeSinceStartup;

                if (nowRT - _lastHireCostCacheRT < UiCacheSeconds
                    && _cachedHireCostActiveCount == recruitCount
                    && Math.Abs(_cachedHireCostFacilityLevel - facLevel) < 0.001f)
                    return _cachedHireCost;

                if (_cachedRecruitCostCountMethod == null)
                    _cachedRecruitCostCountMethod = typeof(GameVariables).GetMethod("GetRecruitCost",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(int), typeof(float) }, null);

                if (_cachedRecruitCostFacilityMethod == null)
                    _cachedRecruitCostFacilityMethod = typeof(GameVariables).GetMethod("GetRecruitCost",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(float) }, null);

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
            catch (Exception ex) { RRLog.VerboseExceptionOnce("Mod.GetNextHireCost", "Suppressed", ex); }

            return CacheHireCost(RosterRotationState.TrainingBaseFundsCost,
                GetRecruitCostInputCount(),
                (float)ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex),
                Time.realtimeSinceStartup);
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
            _cachedHireCost             = value;
            _cachedHireCostActiveCount  = recruitCount;
            _cachedHireCostFacilityLevel = facilityLevel;
            _lastHireCostCacheRT        = nowRT;
            return value;
        }

        // ── Misc helpers ───────────────────────────────────────────────────────
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

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            double ds = RosterRotationState.DaySeconds;
            if (seconds / ds >= 1.0) return $"{seconds / ds:0.0}d";
            if (seconds / 3600 >= 1.0) return $"{seconds / 3600:0.0}h";
            return $"{seconds / 60:0}m";
        }

        private static string TrainingLabel(TrainingType t, int targetLevel)
            => CareerRules.GetTrainingLabel(t, targetLevel);

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

        // ── Age assignment ─────────────────────────────────────────────────────
        internal static void AssignAgeOnHire(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            double yearSec = RosterRotationState.YearSeconds;
            AgeAssignmentResult result = CareerRules.CalculateAgeOnHire(
                nowUT, yearSec,
                RosterRotationState.RetirementAgeMin, RosterRotationState.RetirementAgeMax,
                UnityEngine.Random.value, UnityEngine.Random.value,
                UnityEngine.Random.value, UnityEngine.Random.value);
            rec.BirthUT            = result.BirthUT;
            rec.LastAgedYears      = result.LastAgedYears;
            rec.NaturalRetirementUT = result.NaturalRetirementUT;
        }

        internal static void AssignAgeByExperience(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            double yearSec = RosterRotationState.YearSeconds;
            AgeAssignmentResult result = CareerRules.CalculateAgeByExperience(
                k != null ? (int)k.experienceLevel : 0,
                nowUT, yearSec,
                RosterRotationState.RetirementAgeMin, RosterRotationState.RetirementAgeMax,
                UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);
            rec.BirthUT            = result.BirthUT;
            rec.LastAgedYears      = result.LastAgedYears;
            rec.NaturalRetirementUT = result.NaturalRetirementUT;
        }
    }
}

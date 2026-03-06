// EAC - Enhanced Astronaut Complex - Mod.cs
// Core shared state, flight tracker, and KSC UI.

using System;
using System.Linq;
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
    public static class RosterRotationState
    {
        public static double RestDays      = 14;
        public static bool   UseKerbinDays = true;

        public static double DaySeconds => UseKerbinDays ? 21600.0 : 86400.0;

        public static int    TrainingInitialDays     = 30;
        public static int    TrainingStarDays        = 30;
        public static double TrainingFundsMultiplier = 1.0;
        public static double TrainingRDPerStar       = 10.0;
        public static double TrainingBaseFundsCost   = 62000;
        public static double RecallFundsCostMultiplier = 1.0; // multiplied by hire cost for recall

        public static readonly Dictionary<string, KerbalRecord> Records =
            new Dictionary<string, KerbalRecord>();

        public class KerbalRecord
        {
            public string OriginalTrait;
            public ProtoCrewMember.KerbalType OriginalType;
            public int    Flights;
            public double LastFlightUT;
            public double RestUntilUT;
            public double MissionStartUT;
            public bool   Retired;
            public double RetiredUT;
            public int    ExperienceAtRetire;
            public TrainingType Training           = TrainingType.None;
            public int          TrainingTargetLevel = 0;
            public int          GrantedLevel        = -1;
            public double BirthUT                = 0;
            public double NaturalRetirementUT    = 0;
            public int    RetirementDelayYears   = 0;
            public bool   RetirementWarned       = false;
            public bool   RetirementScheduled    = false;
            public double RetirementScheduledUT  = 0;
            public double DeathUT                = 0;
            public double TrainingEndUT          = 0;
            public int    LastAgedYears          = -1;
        }

        // Aging config
        public static bool AgingEnabled              = true;
        public static bool DeathNotificationsEnabled = true;
        public static bool HudNotificationsEnabled        = true;
        public static bool MessageAppNotificationsEnabled = true;
        public static bool BirthdayNotificationsEnabled   = true;
        public static bool TrainingNotificationsEnabled   = true;
        public static bool RetirementNotificationsEnabled = true;
        public static int  RetirementAgeMin          = 48;
        public static int  RetirementAgeMax          = 55;
        public static int  RetiredDeathAgeMin        = 55;
        public static bool VerboseLogging    = false;
        public static bool VerboseAgeLogging = false;

        // Field aliases for EACStateBridge reflection access
        public static bool NotifyHUD { get => HudNotificationsEnabled; set => HudNotificationsEnabled = value; }
        public static bool NotifyMessageApp { get => MessageAppNotificationsEnabled; set => MessageAppNotificationsEnabled = value; }

        public static double YearSeconds => UseKerbinDays ? 426.08 * 21600.0 : 365.25 * 86400.0;

        // ── Cached retired names (invalidated by retire/recall operations) ──────
        private static List<string> _cachedRetiredNames;
        private static int _cachedRetiredHash = -1;

        public static void InvalidateRetiredCache() => _cachedRetiredHash = -1;

        public static List<string> GetRetiredNames()
        {
            int hash = Records.Count;
            foreach (var kvp in Records)
                if (kvp.Value != null && kvp.Value.Retired) hash = hash * 31 + kvp.Key.GetHashCode();

            if (hash == _cachedRetiredHash && _cachedRetiredNames != null)
                return _cachedRetiredNames;

            var names = new List<string>();
            foreach (var kvp in Records)
                if (kvp.Value != null && kvp.Value.Retired) names.Add(kvp.Key);

            _cachedRetiredNames = names;
            _cachedRetiredHash = hash;
            return names;
        }

        // ── Cached crew name set for row matching ───────────────────────────────
        private static HashSet<string> _cachedCrewNames;
        private static int _cachedCrewCount = -1;

        public static HashSet<string> GetCrewNameSet()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return _cachedCrewNames ?? new HashSet<string>();

            int count = 0;
            foreach (var pcm in roster.Crew) if (pcm != null) count++;

            if (count == _cachedCrewCount && _cachedCrewNames != null)
                return _cachedCrewNames;

            var names = new HashSet<string>();
            foreach (var pcm in roster.Crew)
                if (pcm != null) names.Add(pcm.name);

            _cachedCrewNames = names;
            _cachedCrewCount = count;
            return names;
        }

        public static int GetKerbalAge(KerbalRecord rec, double nowUT)
        {
            if (rec == null || rec.BirthUT == 0) return -1;
            return (int)((nowUT - rec.BirthUT) / YearSeconds);
        }

        public static string FormatGameDate(double ut)
        {
            double yr  = ut / YearSeconds;
            double day = (ut % YearSeconds) / DaySeconds;
            return $"Yr {(int)yr + 1}, Day {(int)day + 1}";
        }

        public static string FormatGameDateYM(double ut)
        {
            if (ut < 0) ut = 0;
            double yearSec = YearSeconds;
            if (yearSec <= 0) return "0Y 0M";
            int year = (int)(ut / yearSec) + 1;
            double monthSec = yearSec / 12.0;
            int month = monthSec > 0 ? Math.Max(1, Math.Min(12, (int)((ut % yearSec) / monthSec) + 1)) : 1;
            return $"{year}Y {month}M";
        }

        public static string FormatGameDateYD(double ut)
        {
            if (ut < 0) ut = 0;
            double yearSec = YearSeconds;
            if (yearSec <= 0) return "Y0 D0";
            int year = (int)(ut / yearSec) + 1;
            int day  = (int)((ut % yearSec) / DaySeconds) + 1;
            return $"Y{year} D{day}";
        }

        public static int GetRetiredEffectiveStars(ProtoCrewMember k, KerbalRecord r, double nowUT)
        {
            if (r == null || !r.Retired) return (int)k.experienceLevel;
            int starsAtRetire = r.ExperienceAtRetire > 0 ? r.ExperienceAtRetire : (int)k.experienceLevel;
            if (starsAtRetire <= 0) return 0;
            int starsLost = (int)((nowUT - r.RetiredUT) / YearSeconds);
            return Math.Max(0, starsAtRetire - starsLost);
        }

        public static KerbalRecord GetOrCreate(string name)
        {
            if (!Records.TryGetValue(name, out var r))
            {
                r = new KerbalRecord();
                Records[name] = r;
            }
            return r;
        }

        private static bool IsNotificationEnabled(EACNotificationType type)
        {
            switch (type)
            {
                case EACNotificationType.Birthday:   return BirthdayNotificationsEnabled;
                case EACNotificationType.Training:   return TrainingNotificationsEnabled;
                case EACNotificationType.Retirement: return RetirementNotificationsEnabled;
                case EACNotificationType.Death:      return DeathNotificationsEnabled;
                default: return true;
            }
        }

        public static void PostNotification(
            string title, string body,
            MessageSystemButton.MessageButtonColor color,
            MessageSystemButton.ButtonIcons icon,
            float hudDuration = 8f)
        {
            PostNotification(EACNotificationType.General, title, body, color, icon, hudDuration);
        }

        public static void PostNotification(
            EACNotificationType type,
            string title, string body,
            MessageSystemButton.MessageButtonColor color,
            MessageSystemButton.ButtonIcons icon,
            float hudDuration = 8f)
        {
            if (!IsNotificationEnabled(type)) return;

            if (HudNotificationsEnabled)
                ScreenMessages.PostScreenMessage(body, hudDuration, ScreenMessageStyle.UPPER_CENTER);

            if (!MessageAppNotificationsEnabled) return;

            try
            {
                if (MessageSystem.Instance != null)
                    MessageSystem.Instance.AddMessage(
                        new MessageSystem.Message(title, body, color, icon));
            }
            catch (Exception ex) { RRLog.Warn($"PostNotification failed: {ex.Message}"); }
        }

        // ── Time formatting helpers ─────────────────────────────────────────────
        public static string FormatCountdown(double seconds)
        {
            if (seconds <= 0) return "Ready";
            double ds = DaySeconds;
            int d = (int)(seconds / ds);
            int h = (int)((seconds % ds) / 3600.0);
            int m = (int)((seconds % 3600.0) / 60.0);
            int s = (int)(seconds % 60.0);
            if (d > 0) return $"{d}d {h}h {m}m";
            if (h > 0) return $"{h}h {m}m {s}s";
            if (m > 0) return $"{m}m {s}s";
            return $"{s}s";
        }

        public static string FormatTimeAgo(double eventUT, double nowUT)
        {
            if (eventUT <= 0) return "";
            return FormatCountdown(nowUT - eventUT) + " ago";
        }
    }

    // ── AC open detection cache ────────────────────────────────────────────────
    // Replaces per-frame Resources.FindObjectsOfTypeAll scans with a cached check.
    internal static class ACOpenCache
    {
        private static Type _acType;
        private static bool _typeSearched;
        private static float _lastCheckTime = -10f;
        private static bool _lastResult;
        private const float CACHE_DURATION = 0.25f;

        public static bool IsOpen
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastCheckTime < CACHE_DURATION) return _lastResult;
                _lastCheckTime = now;
                _lastResult = CheckOpen();
                return _lastResult;
            }
        }

        public static void Invalidate() => _lastCheckTime = -10f;

        private static bool CheckOpen()
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return false;
            try
            {
                if (!_typeSearched)
                {
                    _typeSearched = true;
                    var asm = AssemblyLoader.loadedAssemblies
                        .Select(a => a.assembly)
                        .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                    if (asm != null) _acType = asm.GetType("KSP.UI.Screens.AstronautComplex");
                }
                if (_acType == null) return false;

                var all = Resources.FindObjectsOfTypeAll(_acType);
                foreach (var obj in all)
                {
                    var mb = obj as MonoBehaviour;
                    if (mb != null && mb.isActiveAndEnabled && mb.gameObject != null && mb.gameObject.activeInHierarchy)
                        return true;
                }
            }
            catch { }
            return false;
        }
    }

    // ── Flight tracker ─────────────────────────────────────────────────────────
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

                    RosterRotationState.PostNotification(
                        EACNotificationType.Retirement, $"Retired — {pcm.name}",
                        $"{pcm.name} has retired following mission recovery. ({RosterRotationState.FormatGameDate(now)})",
                        MessageSystemButton.MessageButtonColor.ORANGE,
                        MessageSystemButton.ButtonIcons.MESSAGE);
                }
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

            if (newStatus == ProtoCrewMember.RosterStatus.Available && rec.DeathUT > 0)
            {
                rec.DeathUT = 0;
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
                return;
            }

            bool isDeath = newStatus == ProtoCrewMember.RosterStatus.Dead
                        || newStatus == ProtoCrewMember.RosterStatus.Missing;
            if (!isDeath || rec.DeathUT > 0) return;

            rec.DeathUT = now;
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
            try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
        }
    }

    // ── KSC UI ─────────────────────────────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RosterRotationKSCUI : MonoBehaviour
    {
        public static bool RetiredTabSelected;

        private Texture2D _iconTex;
        private ApplicationLauncherButton _btn;
        private bool    _show;
        private Rect    _window = new Rect(300, 100, 860, 620);
        private Vector2 _scroll;

        private enum AcOverlay { None, Applicants, Training, ForceRetire }
        private AcOverlay _acOverlay      = AcOverlay.None;
        private bool      _prevACOpen;
        private Rect      _overlayWindow  = new Rect(80, 120, 640, 500);
        private Vector2   _overlayScroll;

        private enum Tab { Eligible, Active, RandR, Applicants, Retired, Training }
        private Tab _tab = Tab.Eligible;

        private float _nextCheckRT  = 0f;
        private const float CHECK_INTERVAL = 5f;

        private ProtoCrewMember _pendingTrainKerbal;
        private bool            _showTrainConfirm;
        private bool _pendingForceRefresh = false;

        private const float UiCacheSeconds = 0.25f;
        private static float _lastCrewCapacityCacheRT = -10f;
        internal static void InvalidateCrewCapacityCache()
        {
            _lastCrewCapacityCacheRT = -10f;
            _lastHireCostCacheRT = -10f;
        }
        private static int _cachedActiveNonRetiredCount;
        private static int _cachedMaxCrew = int.MaxValue;
        private static float _lastHireCostCacheRT = -10f;
        private static double _cachedHireCost = RosterRotationState.TrainingBaseFundsCost;
        private static int _cachedHireCostActiveCount = int.MinValue;
        private static float _cachedHireCostFacilityLevel = float.NaN;
        private static MethodInfo _cachedRecruitCostCountMethod;
        private static MethodInfo _cachedRecruitCostFacilityMethod;

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
            public string Status;
            public string AgeText;
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
                    rec.DeathUT = 0;
                    healed++;
                }
                if (healed > 0)
                    try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
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
                    try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
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
                        try { k.experienceLevel = target; } catch { }
                        rec.GrantedLevel = target;
                        try { k.careerLog.AddEntry("Training" + target, "Kerbin"); } catch { }

                        if (RosterRotationState.AgingEnabled && rec.NaturalRetirementUT > 0)
                        {
                            rec.RetirementDelayYears += target;
                            rec.RetirementWarned = false;
                        }

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
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
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

        // ── OnGUI ──────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (_show)
                _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, "Enhanced Astronaut Complex");

            bool acOpen = ACOpenCache.IsOpen;
            if (!acOpen && _prevACOpen) _acOverlay = AcOverlay.None;
            _prevACOpen = acOpen;
            if (!acOpen) RetiredTabSelected = false;

            if (acOpen && _acOverlay != AcOverlay.None)
            {
                string title = GetOverlayTitle(_acOverlay);
                _overlayWindow = GUILayout.Window(
                    GetInstanceID() + 55555, _overlayWindow, DrawACOverlay, title);
            }
        }

        private string GetOverlayTitle(AcOverlay ov)
        {
            if (ov == AcOverlay.Applicants)  return "EAC: Applicants";
            if (ov == AcOverlay.Training)    return "EAC: Send to Training";
            if (ov == AcOverlay.ForceRetire) return "EAC: Force Retire";
            return "Enhanced Astronaut Complex";
        }

        // ── Main toolbar window ────────────────────────────────────────────────
        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == Tab.Eligible,   "Eligible",   "Button", GUILayout.Width(90)))  _tab = Tab.Eligible;
            if (GUILayout.Toggle(_tab == Tab.Active,     "Active",     "Button", GUILayout.Width(80)))  _tab = Tab.Active;
            if (GUILayout.Toggle(_tab == Tab.RandR,      "R&R",        "Button", GUILayout.Width(60)))  _tab = Tab.RandR;
            if (GUILayout.Toggle(_tab == Tab.Applicants, "Applicants", "Button", GUILayout.Width(110))) _tab = Tab.Applicants;
            if (GUILayout.Toggle(_tab == Tab.Retired,    "Retired",    "Button", GUILayout.Width(90)))  _tab = Tab.Retired;
            if (GUILayout.Toggle(_tab == Tab.Training,   "Training",   "Button", GUILayout.Width(90)))  _tab = Tab.Training;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) { GUILayout.Label("Crew roster not available."); GUILayout.EndVertical(); GUI.DragWindow(); return; }

            double now = Planetarium.GetUniversalTime();
            if (_tab == Tab.Training) DrawTrainingTab(roster, now);
            else DrawRosterTab(roster, now);

            GUILayout.Space(8);
            if (GUILayout.Button("Close")) _show = false;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawRosterTab(KerbalRoster roster, double now)
        {
            var rows = GetRosterRowsCached(roster, now);
            int activeCount = GetActiveNonRetiredCount();
            int maxCrew = GetMaxCrew();
            bool atCap = activeCount >= maxCrew;

            GUILayout.Label($"Shown: {rows.Count}");
            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (var row in rows)
            {
                var k = row.Kerbal;
                var r = row.Record;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{k.name} — {k.trait} — L{(int)k.experienceLevel}", GUILayout.Width(280));
                GUILayout.Label($"F:{r?.Flights ?? 0}", GUILayout.Width(40));
                GUILayout.Label(row.AgeText, GUILayout.Width(55));
                GUILayout.Label(row.Status, GUILayout.Width(240));
                GUILayout.FlexibleSpace();

                if (k.type == ProtoCrewMember.KerbalType.Applicant)
                {
                    GUI.enabled = !atCap;
                    if (GUILayout.Button("Hire", GUILayout.Width(70)))
                    {
                        k.type = ProtoCrewMember.KerbalType.Crew;
                        k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                        try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
                        InvalidateUICaches();
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("Reject", GUILayout.Width(70)))
                    {
                        RejectApplicant(roster, k);
                        InvalidateUICaches();
                    }
                }
                else if (!row.Retired)
                {
                    bool inTraining = k.inactive;
                    bool onMission = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                    bool maxLevel = k.experienceLevel >= 3f;
                    GUI.enabled = !inTraining && !onMission && !maxLevel;
                    if (GUILayout.Button("Train", GUILayout.Width(70)))
                    {
                        _pendingTrainKerbal = k;
                        _showTrainConfirm = true;
                    }
                    GUI.enabled = true;
                    GUI.enabled = !inTraining && !onMission && !row.InTrainingLockout;
                    if (row.InTrainingLockout)
                        GUILayout.Label("Service commitment", GUILayout.Width(70));
                    else if (GUILayout.Button("Retire", GUILayout.Width(70)))
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
                    if (GUILayout.Button(btnLabel, GUILayout.Width(noStar ? 80 : 130)))
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
            GUILayout.BeginVertical(GUI.skin.box);
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
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                string lbl = r != null ? TrainingLabel(r.Training, r.TrainingTargetLevel) : "Inactive";
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
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕ Close", GUILayout.Width(80))) _acOverlay = AcOverlay.None;
            GUILayout.EndHorizontal();
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
                    try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
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
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r == null || r.Training == TrainingType.None) continue;
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
                GUILayout.Label($"{r?.Flights ?? 0}", GUILayout.Width(50));
                GUILayout.Label(row.AgeText, GUILayout.Width(55));
                GUILayout.Label(row.Status, GUILayout.Width(170));
                GUILayout.FlexibleSpace();

                GUI.enabled = !onMission && !inTraining && !row.InTrainingLockout;
                if (GUILayout.Button(row.InTrainingLockout ? "Committed" : "Retire", GUILayout.Width(80)))
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
                try { Funding.Instance?.AddFunds(-recallCost, TransactionReasons.CrewRecruited); } catch { }
            }

            r.Retired = false;
            if (k.type == ProtoCrewMember.KerbalType.Tourist || k.type == ProtoCrewMember.KerbalType.Unowned)
            {
                k.type = r.OriginalType;
                if (k.type == 0) k.type = ProtoCrewMember.KerbalType.Crew;
            }
            k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
            if (!string.IsNullOrEmpty(r.OriginalTrait)) k.trait = r.OriginalTrait;
            try { k.experienceLevel = effStars; } catch { }
            double sec = 30.0 * RosterRotationState.DaySeconds;
            k.inactive        = true;
            k.inactiveTimeEnd = Planetarium.GetUniversalTime() + sec;
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
            double baseDays = targetLevel * 30.0;
            float stupidity = Mathf.Clamp01(k.stupidity);
            double extraFrac = UnityEngine.Random.value * stupidity * 0.5;
            return baseDays * (1.0 + extraFrac);
        }

        private void ExecuteTraining(ProtoCrewMember k, int targetLevel, double fCost, double rCost)
        {
            if (k == null) return;
            try
            {
                Funding.Instance?.AddFunds(-fCost, TransactionReasons.CrewRecruited);
                ResearchAndDevelopment.Instance?.AddScience((float)-rCost, TransactionReasons.CrewRecruited);
            }
            catch { }

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
            try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
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
                bool isDead = k.rosterStatus == ProtoCrewMember.RosterStatus.Dead
                           || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing
                           || (r != null && r.DeathUT > 0 && !retired);

                switch (tab)
                {
                    case Tab.Eligible: if (isDead || retired || onVacation || !hasFlown) continue; break;
                    case Tab.Active:   if (isDead || retired || onVacation || hasFlown) continue; break;
                    case Tab.RandR:    if (isDead || retired || !onVacation) continue; break;
                    case Tab.Retired:  if (!retired) continue; break;
                }

                var row = new RosterRowData();
                row.Kerbal = k;
                row.Record = r;
                row.Retired = retired;
                row.HasFlown = hasFlown;
                row.Status = BuildStatusString(k, r, now, hasFlown, retired);
                row.AgeText = GetAgeDisplay(r, now);
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
            if (k.inactive && k.inactiveTimeEnd > now)
                return $"INACTIVE ({RosterRotationState.FormatCountdown(k.inactiveTimeEnd - now)})";
            if (CrewRandRAdapter.TryGetVacationUntilByName(k.name, out var vacUntil) && vacUntil > now)
                return $"R&R ({FormatTime(vacUntil - now)})";
            return hasFlown ? "ELIGIBLE" : "ACTIVE";
        }

        private static string TrainingLabel(TrainingType t, int targetLevel)
        {
            if (t == TrainingType.InitialHire)       return "introductory training";
            if (t == TrainingType.ExperienceUpgrade) return $"Level {targetLevel} training";
            if (t == TrainingType.RecallRefresher)   return "refresher training";
            return "training";
        }

        private static double TrainingFundsCost(double hireCost, int targetLevel)
            => hireCost * RosterRotationState.TrainingFundsMultiplier * targetLevel;
        private static double TrainingRDCost(int targetLevel)
            => RosterRotationState.TrainingRDPerStar * targetLevel;

        private void DrawHRule()
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.horizontalSlider,
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
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
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
            catch { }
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
            double r1 = 25.0 + UnityEngine.Random.value * 20.0;
            double r2 = 25.0 + UnityEngine.Random.value * 20.0;
            double ageYears = (r1 + r2) * 0.5;
            double birthdayOffset = (0.15 + UnityEngine.Random.value * 0.70) * yearSec;
            rec.BirthUT = nowUT - (ageYears * yearSec) - birthdayOffset;
            rec.LastAgedYears = (int)((nowUT - rec.BirthUT) / yearSec);
            int retireAge = RosterRotationState.RetirementAgeMin
                + (int)(UnityEngine.Random.value * (RosterRotationState.RetirementAgeMax - RosterRotationState.RetirementAgeMin + 1));
            rec.NaturalRetirementUT = rec.BirthUT + retireAge * yearSec;
        }

        internal static void AssignAgeByExperience(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            double yearSec = RosterRotationState.YearSeconds;
            int stars = (int)k.experienceLevel;
            double ageMin, ageMax;
            if      (stars >= 5) { ageMin = 45; ageMax = 51; }
            else if (stars == 4) { ageMin = 38; ageMax = 46; }
            else if (stars == 3) { ageMin = 35; ageMax = 42; }
            else                 { ageMin = 25; ageMax = 35; }
            double ageYears = ageMin + UnityEngine.Random.value * (ageMax - ageMin);
            double birthdayOffset = (0.15 + UnityEngine.Random.value * 0.70) * yearSec;
            rec.BirthUT = nowUT - (ageYears * yearSec) - birthdayOffset;
            rec.LastAgedYears = (int)((nowUT - rec.BirthUT) / yearSec);
            int retireAge = RosterRotationState.RetirementAgeMin
                + (int)(UnityEngine.Random.value * (RosterRotationState.RetirementAgeMax - RosterRotationState.RetirementAgeMin + 1));
            rec.NaturalRetirementUT = rec.BirthUT + retireAge * yearSec;
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

                if (rec.Retired && rec.DeathUT <= 0)
                {
                    anyDirty |= CheckRetiredDeath(k, rec, nowUT, currentAge);
                    continue;
                }
                if (rec.Retired) continue;
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

                double effectiveRetireUT = rec.NaturalRetirementUT + rec.RetirementDelayYears * yearSec;
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
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
        }

        private static double MoraleRetireProbability(ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec, double nowUT, int stars)
        {
            double baseP;
            switch (stars)
            {
                case 0: baseP = 0.08; break;
                case 1: baseP = 0.05; break;
                case 2: baseP = 0.03; break;
                default: baseP = 0.015; break;
            }
            double yearSec = RosterRotationState.YearSeconds;
            double inactive = rec.LastFlightUT > 0 ? (nowUT - rec.LastFlightUT) / yearSec : 999;
            double inactMul;
            if      (inactive >= 3.0) inactMul = 4.0;
            else if (inactive >= 2.0) inactMul = 2.5;
            else if (inactive >= 1.0) inactMul = 1.5;
            else                      inactMul = 1.0;
            double actRed = Math.Min(rec.Flights / 15.0, 0.75);
            return baseP * inactMul * (1.0 - actRed);
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
            try { k.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch { }

            if (RosterRotationState.DeathNotificationsEnabled)
                RosterRotationState.PostNotification(EACNotificationType.Death,
                    $"Deceased — {k.name}",
                    $"{k.name} has passed away at age {currentAge}. ({RosterRotationState.FormatGameDate(nowUT)})",
                    MessageSystemButton.MessageButtonColor.RED, MessageSystemButton.ButtonIcons.ALERT, 12f);

            InvalidateUICaches();
            _pendingForceRefresh = true;
            return true;
        }
    }

    // ── AC "Astronaut Management" button ────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RosterRotationACButtons : MonoBehaviour
    {
        private GUIStyle _boldBtn;

        private void OnGUI()
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return;
            if (!ACOpenCache.IsOpen) return;

            if (_boldBtn == null)
            {
                _boldBtn = new GUIStyle(GUI.skin.button);
                _boldBtn.fontStyle = FontStyle.Bold;
                _boldBtn.fontSize = 16;
                _boldBtn.wordWrap = false;
            }

            float W = Screen.width, H = Screen.height;
            float btnW = 220f, btnH = 34f;
            float x = W * 0.44f - btnW * 0.5f;
            float y = H * 0.070f - btnH * 0.5f;

            if (GUI.Button(new Rect(x, y, btnW, btnH), "Astronaut Management", _boldBtn))
                RosterRotationKSCUIBridge.RequestOverlay(RosterRotationKSCUIBridge.AcOverlayOpen);
        }
    }

    // ── Bridge ──────────────────────────────────────────────────────────────────
    public static class RosterRotationKSCUIBridge
    {
        public const int AcOverlayNone = 0;
        public const int AcOverlayOpen = 1;
        public const int AcOverlayApplicants = 1;
        public const int AcOverlayTraining = 1;
        public const int AcOverlayForceRetire = 1;

        private static volatile int _pendingOverlay = AcOverlayNone;
        public static void RequestOverlay(int which) => _pendingOverlay = which;
        public static int ConsumeOverlay()
        {
            int v = _pendingOverlay;
            _pendingOverlay = AcOverlayNone;
            return v;
        }
    }
}

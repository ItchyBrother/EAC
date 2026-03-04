using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP;
using KSP.UI.Screens;

namespace RosterRotation
{
    // ── Training type ──────────────────────────────────────────────────────────
    public enum TrainingType
    {
        None              = 0,
        InitialHire       = 1,   // 30-day onboarding; no XP gain
        ExperienceUpgrade = 2,   // 30-day session per star; grants XP on completion
        RecallRefresher   = 3    // 30-day refresher after recall from retirement
    }

    

    // ── Notification categories (for MessageSystem & HUD toggles) ─────────────
    public enum EACNotificationType
    {
        General   = 0,
        Birthday  = 1,
        Training  = 2,
        Retirement= 3,
        Death     = 4
    }
// ── Shared state ───────────────────────────────────────────────────────────
    public static class RosterRotationState
    {
        public static double RestDays      = 14;
        public static bool   UseKerbinDays = true;

        public static double DaySeconds => UseKerbinDays ? 21600.0 : 86400.0;

        // Training config — all configurable, persisted by Persistence.cs
        public static int    TrainingInitialDays     = 30;
        public static int    TrainingStarDays        = 30;
        public static double TrainingFundsMultiplier = 1.0;
        public static double TrainingRDPerStar       = 10.0;
        public static double TrainingBaseFundsCost   = 62000;

        public static readonly Dictionary<string, KerbalRecord> Records =
            new Dictionary<string, KerbalRecord>();

        public class KerbalRecord
        {
            public string OriginalTrait;
            public ProtoCrewMember.KerbalType OriginalType;
            public int    Flights;
            public double LastFlightUT;

            // Legacy (save compat)
            public double RestUntilUT;
            public double MissionStartUT;

            // Retirement
            public bool   Retired;
            public double RetiredUT;
            public int    ExperienceAtRetire;

            // Training
            public TrainingType Training           = TrainingType.None;
            public int          TrainingTargetLevel = 0;
            // Experience grant: level applied when training completed; re-applied on game load
            // because KSP recalculates experienceLevel from careerLog and would otherwise revert it.
            public int          GrantedLevel        = -1; // -1 = no grant

            // Aging
            public double BirthUT                = 0;    // UT of birth; age = (nowUT-BirthUT)/YearSeconds
            public double NaturalRetirementUT    = 0;    // age-forced retirement target
            public int    RetirementDelayYears   = 0;    // extra years granted by training completions
            public bool   RetirementWarned       = false; // "considering retirement" warning issued
            public bool   RetirementScheduled    = false; // decided but waiting for mission end
            public double RetirementScheduledUT  = 0;
            public double DeathUT                = 0;    // 0 = alive; >0 = UT of death
            public double TrainingEndUT          = 0;    // UT initial/refresher training completed
            public int    LastAgedYears          = -1;   // last age at which annual birthday check ran
        }

        // ── Aging config ────────────────────────────────────────────────────────
        public static bool AgingEnabled              = true;
        public static bool DeathNotificationsEnabled = true;
        // Notification routing
        public static bool HudNotificationsEnabled        = true; // ScreenMessages ticker
        public static bool MessageAppNotificationsEnabled = true; // KSP MessageSystem (KSC app)

        // Notification categories
        public static bool BirthdayNotificationsEnabled   = true;
        public static bool TrainingNotificationsEnabled   = true;
        public static bool RetirementNotificationsEnabled = true;

        public static int  RetirementAgeMin          = 48;
        public static int  RetirementAgeMax          = 55;
        public static int  RetiredDeathAgeMin        = 55;

        // Debug logging
        public static bool VerboseLogging = false;      // global verbose logging gate (UI & diagnostics)
        public static bool VerboseAgeLogging = false;   // legacy: age-specific verbose (still honored)

        // ── Time helpers ─────────────────────────────────────────────────────────
        // Kerbin year = 426.08 Kerbin days.  Earth year = 365.25 Earth days.
        public static double YearSeconds => UseKerbinDays ? 426.08 * 21600.0 : 365.25 * 86400.0;

        public static int GetKerbalAge(KerbalRecord rec, double nowUT)
        {
            if (rec == null) return -1;
            // BirthUT == 0 means not yet assigned (uninitialized). Negative is valid.
            // LastAgedYears < 0 means init is pending but BirthUT may already be valid.
            if (rec.BirthUT == 0) return -1;
            if (VerboseAgeLogging)
            {
            }
            return (int)((nowUT - rec.BirthUT) / YearSeconds);
        }

        // "Yr 12, Day 34"  — used for death date display
        public static string FormatGameDate(double ut)
        {
            double yr  = ut / YearSeconds;
            double day = (ut % YearSeconds) / DaySeconds;
            return $"Yr {(int)yr + 1}, Day {(int)day + 1}";
        }

        // "12Y 3M" — compact date display (year/month in current calendar system)
        // Month is 1–12 (year divided into 12 equal months).
        public static string FormatGameDateYM(double ut)
        {
            if (ut < 0) ut = 0;
            double yearSec = YearSeconds;
            if (yearSec <= 0) return "0Y 0M";

            int year = (int)(ut / yearSec) + 1;
            double monthSec = yearSec / 12.0;
            int month = 1;
            if (monthSec > 0)
            {
                month = (int)((ut % yearSec) / monthSec) + 1;
                if (month < 1) month = 1;
                if (month > 12) month = 12;
            }

            return $"{year}Y {month}M";
        }

        // "Y1 D34" — compact year/day display for death status strings
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

        // Posts to the HUD ticker (brief) AND the persistent KSC Message App.
        // Safe from any scene — MessageSystem.Instance is null outside KSC.
        public static void PostNotification(
            string title, string body,
            MessageSystemButton.MessageButtonColor color,
            MessageSystemButton.ButtonIcons icon,
            float hudDuration = 8f)
        {
            PostNotification(EACNotificationType.General, title, body, color, icon, hudDuration);
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

        // Posts to the HUD ticker (brief) AND optionally the persistent KSC Message App.
        // Safe from any scene — MessageSystem.Instance is null outside KSC.
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
            catch (Exception ex)
            {
                RRLog.Warn($"PostNotification failed: {ex.Message}");
            }
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

                    // Mark inactive so the stock editor crew dialog won't list them.
                    pcm.inactive        = true;
                    pcm.inactiveTimeEnd = now + RosterRotationState.YearSeconds * 1000.0;

                    string rorDate = RosterRotationState.FormatGameDate(now);
                    RosterRotationState.PostNotification(
                        EACNotificationType.Retirement, $"Retired — {pcm.name}",
                        $"{pcm.name} has retired following mission recovery. ({rorDate})",
                        MessageSystemButton.MessageButtonColor.ORANGE,
                        MessageSystemButton.ButtonIcons.MESSAGE);
                }
            }
        }

        // Records in-flight deaths and heals respawned kerbals (non-permadeath).
        private void OnKerbalStatusChange(
            ProtoCrewMember pcm,
            ProtoCrewMember.RosterStatus oldStatus,
            ProtoCrewMember.RosterStatus newStatus)
        {
            if (pcm == null) return;
            var rec = RosterRotationState.GetOrCreate(pcm.name);
            double now = Planetarium.GetUniversalTime();

            // Respawn: KSP restored the kerbal — clear stale death record.
            if (newStatus == ProtoCrewMember.RosterStatus.Available && rec.DeathUT > 0)
            {
                rec.DeathUT = 0;
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
                return;
            }

            // KSP non-permadeath: dying kerbals go Missing (awaiting respawn), not Dead.
            // Permadeath: goes to Dead. Catch both so we always record the death.
            bool isDeath = newStatus == ProtoCrewMember.RosterStatus.Dead
                        || newStatus == ProtoCrewMember.RosterStatus.Missing;
            if (!isDeath) return;
            if (rec.DeathUT > 0)
            {
                return;
            }

            rec.DeathUT = now;
            int kiaAge = RosterRotationState.GetKerbalAge(rec, now);

            if (RosterRotationState.DeathNotificationsEnabled)
            {
                string kiaAge2  = kiaAge >= 0 ? "Age " + kiaAge + ", " : "";
                string kiaDate  = RosterRotationState.FormatGameDate(now);
                RosterRotationState.PostNotification(
                    EACNotificationType.Death, "K.I.A. — " + pcm.name,
                    pcm.name + " was killed in action. " + kiaAge2 + kiaDate + ".",
                    MessageSystemButton.MessageButtonColor.RED,
                    MessageSystemButton.ButtonIcons.ALERT,
                    12f);
            }
            try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
        }
    }

    // ── KSC UI ─────────────────────────────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RosterRotationKSCUI : MonoBehaviour
    {
        // ── State ──────────────────────────────────────────────────────────────
        public static bool RetiredTabSelected;

        private Texture2D _iconTex;
        private ApplicationLauncherButton _btn;

        private bool    _show;
        private Rect    _window = new Rect(300, 100, 860, 620);
        private Vector2 _scroll;

        // ── Overlay windows (open when AC is open) ─────────────────────────────
        private enum AcOverlay { None, Applicants, Training, ForceRetire }
        private AcOverlay _acOverlay      = AcOverlay.None;
        private bool      _prevACOpen;
        private Rect      _overlayWindow  = new Rect(80, 120, 640, 500);
        private Vector2   _overlayScroll;

        // ── Toolbar tabs ───────────────────────────────────────────────────────
        private enum Tab { Eligible, Active, RandR, Applicants, Retired, Training }
        private Tab _tab = Tab.Eligible;

        // Training completion check throttle
        private float _nextCheckRT  = 0f;
        private const float CHECK_INTERVAL = 5f;

        // Pending confirm for training (from roster tab)
        private ProtoCrewMember _pendingTrainKerbal;
        private bool            _showTrainConfirm;

        // Deferred ForceRefresh — set from OnGUI, consumed in Update so Unity layout
        // has had one frame to measure newly cloned row RectTransforms before we reposition.
        private bool _pendingForceRefresh = false;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Start()
        {
            _iconTex = GameDatabase.Instance.GetTexture("EAC/Icons/icon", false);

            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
            GameEvents.onKerbalTypeChange.Add(OnKerbalTypeChange);

            // Wait two frames: KSP recalculates XP from careerLog on load — we stomp it back
            // after that settles.  Age init is done in Persistence.OnLoad so ages are already
            // in memory by the time this coroutine fires.
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

        // Clears stale DeathUT for kerbals KSP has already respawned (non-permadeath).
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
            catch (Exception ex)
            {
                RRLog.Warn($"[RosterRotation] HealRespawnedKerbals: {ex.Message}");
            }
        }

        // Assigns age to Crew kerbals with no age data. Runs ONCE per KSC session from StartupDelayed.
        // Never from OnLoad (which fires 4x per startup causing re-randomisation spam).
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
                {
                    try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn($"[RosterRotation] InitializeExistingKerbalAges: {ex.Message}");
            }
        }

        private void ApplyGrantedLevels()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;
            int applied = 0;
            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (rec.GrantedLevel <= 0) continue;
                if ((int)k.experienceLevel >= rec.GrantedLevel) continue; // already at or above
                try
                {
                    k.experienceLevel = rec.GrantedLevel;
                    applied++;
                }
                catch (Exception ex)
                {
                    RRLog.Warn($"[RosterRotation] ApplyGrantedLevels: {k.name} failed: {ex.Message}");
                }
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
            // Consume any pending overlay request from the AC button
            int pending = RosterRotationKSCUIBridge.ConsumeOverlay();
            if (pending != RosterRotationKSCUIBridge.AcOverlayNone)
                _acOverlay = (_acOverlay == AcOverlay.None) ? AcOverlay.Applicants : AcOverlay.None;

            // Consume any ForceRefresh deferred from OnGUI
            if (_pendingForceRefresh)
            {
                _pendingForceRefresh = false;
                ACPatches.ForceRefresh();
            }

            if (Time.realtimeSinceStartup < _nextCheckRT) return;
            _nextCheckRT = Time.realtimeSinceStartup + CHECK_INTERVAL;
            CheckTrainingCompletion();
            if (RosterRotationState.AgingEnabled)
                CheckAgingAndRetirement();
        }

        // ── Hire hook ──────────────────────────────────────────────────────────
        // FIX CS1503: correct 3-param signature matching EventData<PCM,KerbalType,KerbalType>
        private void OnKerbalTypeChange(ProtoCrewMember pcm,
                                        ProtoCrewMember.KerbalType oldType,
                                        ProtoCrewMember.KerbalType newType)
        {
            if (pcm == null) return;
            if (oldType != ProtoCrewMember.KerbalType.Applicant) return;
            if (newType != ProtoCrewMember.KerbalType.Crew) return;

            double nowUT = Planetarium.GetUniversalTime();
            var rec = RosterRotationState.GetOrCreate(pcm.name);

            // Assign initial training
            if (rec.Training == TrainingType.None)
            {
                double sec = RosterRotationState.TrainingInitialDays * RosterRotationState.DaySeconds;
                pcm.inactive        = true;
                pcm.inactiveTimeEnd = nowUT + sec;
                rec.Training            = TrainingType.InitialHire;
                rec.TrainingTargetLevel = 0;
            }

            // Assign age (applicants have no age; it is set on hire)
            if (RosterRotationState.AgingEnabled && rec.LastAgedYears < 0)
                AssignAgeOnHire(pcm, rec, nowUT);

            // Defer AC refresh so row shows "In introductory training" immediately
            _pendingForceRefresh = true;
        }

        // ── Training completion ────────────────────────────────────────────────
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
                        try { k.experienceLevel = target; }
                        catch (Exception ex)
                        {
                            RRLog.Warn($"[RosterRotation] Training: set level failed for {k.name}: {ex.Message}");
                        }
                        rec.GrantedLevel = target; // persist so we can re-apply after reload

                        // Write a career-log entry so it appears in the persistent file.
                        try
                        {
                            string entryType = "Training" + target;
                            k.careerLog.AddEntry(entryType, "Kerbin");
                        }
                        catch (Exception ex)
                        {
                            RRLog.Warn($"[RosterRotation] Training: careerLog write failed for {k.name}: {ex.Message}");
                        }

                        // Rule 4a: completing training delays retirement by targetLevel years.
                        // Also clears the RetirementWarned flag — the kerbal has renewed purpose.
                        if (RosterRotationState.AgingEnabled && rec.NaturalRetirementUT > 0)
                        {
                            rec.RetirementDelayYears += target;
                            rec.RetirementWarned      = false;
                        }

                        string trainDate = RosterRotationState.FormatGameDate(now);
                        RosterRotationState.PostNotification(
                            EACNotificationType.Training, $"Training Complete — {k.name}",
                            $"{k.name} has completed Level {target} training and is ready for duty. ({trainDate})",
                            MessageSystemButton.MessageButtonColor.GREEN,
                            MessageSystemButton.ButtonIcons.COMPLETE,
                            6f);
                    }
                }
                else
                {
                    string trainDate2 = RosterRotationState.FormatGameDate(now);
                    if (rec.Training == TrainingType.InitialHire)
                    {
                        RosterRotationState.PostNotification(
                            EACNotificationType.Training, "Training Complete — " + k.name,
                            k.name + " has completed initial training and is ready for active duty. (" + trainDate2 + ")",
                            MessageSystemButton.MessageButtonColor.GREEN,
                            MessageSystemButton.ButtonIcons.COMPLETE,
                            8f);
                        rec.TrainingEndUT = now;
                    }
                    else if (rec.Training == TrainingType.RecallRefresher)
                    {
                        RosterRotationState.PostNotification(
                            EACNotificationType.Training, "Refresher Complete — " + k.name,
                            k.name + " has completed refresher training and is cleared for missions. (" + trainDate2 + ")",
                            MessageSystemButton.MessageButtonColor.GREEN,
                            MessageSystemButton.ButtonIcons.COMPLETE,
                            8f);
                    }
                }

                rec.Training            = TrainingType.None;
                rec.TrainingTargetLevel = 0;
                if (k.inactive && now >= k.inactiveTimeEnd)
                    k.inactive = false;

                anyDone = true;
            }

            if (anyDone)
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
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
                _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, "Roster Rotation");

            bool acOpen = IsAstronautComplexOpen();
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
            if (ov == AcOverlay.Applicants)  return "RosterRotation: Applicants";
            if (ov == AcOverlay.Training)    return "RosterRotation: Send to Training";
            if (ov == AcOverlay.ForceRetire) return "RosterRotation: Force Retire";
            return "RosterRotation";
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
            if (roster == null)
            {
                GUILayout.Label("Crew roster not available.");
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            double now = Planetarium.GetUniversalTime();

            if (_tab == Tab.Training)
                DrawTrainingTab(roster, now);
            else
                DrawRosterTab(roster, now);

            GUILayout.Space(8);
            if (GUILayout.Button("Close")) _show = false;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // ── Roster tab ─────────────────────────────────────────────────────────
        private void DrawRosterTab(KerbalRoster roster, double now)
        {
            var rows = new List<ProtoCrewMember>();
            foreach (var k in roster.Kerbals())
            {
                if (k == null) continue;
                bool applicant = k.type == ProtoCrewMember.KerbalType.Applicant;
                if (_tab == Tab.Applicants) { if (applicant) rows.Add(k); continue; }
                if (applicant) continue;

                RosterRotationState.Records.TryGetValue(k.name, out var r);
                bool retired    = r?.Retired ?? false;
                bool hasFlown   = r != null && r.LastFlightUT > 0;
                bool onVacation = CrewRandRAdapter.IsOnVacationByName(k.name, now);

                bool isDead = k.rosterStatus == ProtoCrewMember.RosterStatus.Dead
                           || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing
                           || (r != null && r.DeathUT > 0 && !retired);
                switch (_tab)
                {
                    case Tab.Eligible: if (isDead || retired || onVacation || !hasFlown) continue; break;
                    case Tab.Active:   if (isDead || retired || onVacation || hasFlown)  continue; break;
                    case Tab.RandR:    if (isDead || retired || !onVacation)             continue; break;
                    case Tab.Retired:  if (!retired)                                     continue; break;
                }
                rows.Add(k);
            }
            rows.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            GUILayout.Label($"Shown: {rows.Count}");
            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (var k in rows)
            {
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                bool retired  = r?.Retired ?? false;
                bool hasFlown = r != null && r.LastFlightUT > 0;

                string status = BuildStatusString(k, r, now, hasFlown, retired);

                // Age display — assign age on-the-fly if init hasn't run yet for this kerbal.
                string ageStr = "";
                if (RosterRotationState.AgingEnabled && k.type != ProtoCrewMember.KerbalType.Applicant)
                {
                    if (r == null) r = RosterRotationState.GetOrCreate(k.name);
                    if (r.LastAgedYears < 0)
                        AssignAgeByExperience(k, r, now);
                    ageStr = $"Age {RosterRotationState.GetKerbalAge(r, now)}";
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{k.name} — {k.trait} — L{(int)k.experienceLevel}", GUILayout.Width(280));
                GUILayout.Label($"F:{r?.Flights ?? 0}", GUILayout.Width(40));
                GUILayout.Label(ageStr, GUILayout.Width(55));
                GUILayout.Label(status, GUILayout.Width(240));
                GUILayout.FlexibleSpace();

                if (k.type == ProtoCrewMember.KerbalType.Applicant)
                {
                    bool atCap = GetActiveNonRetiredCount() >= GetMaxCrew();
                    GUI.enabled = !atCap;
                    if (GUILayout.Button("Hire", GUILayout.Width(70)))
                    {
                        k.type         = ProtoCrewMember.KerbalType.Crew;
                        k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                        // onKerbalTypeChange fires → sets InitialHire training
                        try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("Reject", GUILayout.Width(70))) RejectApplicant(roster, k);
                }
                else if (!retired)
                {
                    bool inTraining = k.inactive;
                    bool onMission  = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                    bool maxLevel   = k.experienceLevel >= 3f;
                    GUI.enabled = !inTraining && !onMission && !maxLevel;
                    if (GUILayout.Button("Train", GUILayout.Width(70)))
                    {
                        _pendingTrainKerbal = k;
                        _showTrainConfirm   = true;
                    }
                    GUI.enabled = true;
                    bool inTrainingLockout = r != null && r.TrainingEndUT > 0
                        && (now - r.TrainingEndUT) < RosterRotationState.YearSeconds;
                    GUI.enabled = !inTraining && !onMission && !inTrainingLockout;
                    if (inTrainingLockout)
                        GUILayout.Label("Service commitment", GUILayout.Width(70));
                    else if (GUILayout.Button("Retire", GUILayout.Width(70)))
                    {
                        if (r == null) { r = new RosterRotationState.KerbalRecord(); RosterRotationState.Records[k.name] = r; }
                        if (string.IsNullOrEmpty(r.OriginalTrait)) r.OriginalTrait = k.trait;
                        r.OriginalType       = k.type;
                        r.Retired            = true;
                        r.RetiredUT          = Planetarium.GetUniversalTime();
                        r.ExperienceAtRetire = (int)k.experienceLevel;

                        // Mark inactive so the stock editor crew dialog won't list them.
                        k.inactive        = true;
                        k.inactiveTimeEnd = Planetarium.GetUniversalTime() + RosterRotationState.YearSeconds * 1000.0;
                    }
                    GUI.enabled = true;
                }
                else
                {
                    int eff    = RosterRotationState.GetRetiredEffectiveStars(k, r, now);
                    bool atCap = GetActiveNonRetiredCount() >= GetMaxCrew();
                    bool noStar = eff <= 0;
                    GUI.enabled = !atCap && !noStar;
                    if (GUILayout.Button(noStar ? "No Stars" : "Recall", GUILayout.Width(80)))
                    {
                        r.Retired = false;
                        if (k.type == ProtoCrewMember.KerbalType.Tourist ||
                            k.type == ProtoCrewMember.KerbalType.Unowned)
                        {
                            k.type = r.OriginalType;
                            if (k.type == 0) k.type = ProtoCrewMember.KerbalType.Crew;
                        }
                        k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                        if (!string.IsNullOrEmpty(r.OriginalTrait)) k.trait = r.OriginalTrait;
                        try { k.experienceLevel = eff; } catch { }

                        double sec = 30.0 * RosterRotationState.DaySeconds;
                        k.inactive        = true;
                        k.inactiveTimeEnd = Planetarium.GetUniversalTime() + sec;
                        r.Training            = TrainingType.RecallRefresher;
                        r.TrainingTargetLevel = 0;

                        _pendingForceRefresh = true;
                    }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            // ── Inline confirm dialog ──────────────────────────────────────────
            if (_showTrainConfirm && _pendingTrainKerbal != null)
            {
                var trainK   = _pendingTrainKerbal;
                double hire  = GetNextHireCost();
                int tgt      = (int)trainK.experienceLevel + 1;
                double fCost = TrainingFundsCost(hire, tgt);
                double rCost = TrainingRDCost(tgt);
                double funds = Funding.Instance?.Funds ?? 0;
                double rd    = ResearchAndDevelopment.Instance?.Science ?? 0;
                bool afford  = funds >= fCost && rd >= rCost;

                GUILayout.Space(6);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Train {trainK.name}  L{(int)trainK.experienceLevel} → L{tgt}");
                int cBase = tgt * 30; int cMax = (int)(cBase * 1.5);
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
        }

        // ── Training tab (toolbar) ─────────────────────────────────────────────
        private void DrawTrainingTab(KerbalRoster roster, double now)
        {
            double hire  = GetNextHireCost();
            double funds = Funding.Instance?.Funds ?? 0;
            double rd    = ResearchAndDevelopment.Instance?.Science ?? 0;

            GUILayout.Label($"Training costs based on next hire cost: √{hire:N0}");
            GUILayout.Label(
                $"  L1: √{TrainingFundsCost(hire,1):N0} + {TrainingRDCost(1):N0} R&D   " +
                $"L2: √{TrainingFundsCost(hire,2):N0} + {TrainingRDCost(2):N0} R&D   " +
                $"L3: √{TrainingFundsCost(hire,3):N0} + {TrainingRDCost(3):N0} R&D   " +
                $"({RosterRotationState.TrainingStarDays}d base per level: L1=30d L2=60d L3=90d, +0–50% for stupidity)");
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
                GUILayout.Label(FormatCountdown(rem), GUILayout.Width(110));
                GUILayout.EndHorizontal();
                anyT = true;
            }
            if (!anyT) GUILayout.Label("  None.");
            GUILayout.Space(8);

            GUILayout.Label("▶ Send to Training");
            var candidates = BuildTrainingCandidates(roster);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(200));
            if (candidates.Count == 0) GUILayout.Label("  No eligible kerbals.");
            foreach (var k in candidates)
            {
                int tgtT   = (int)k.experienceLevel + 1;
                int baseD  = tgtT * 30;
                int maxD   = (int)(baseD * 1.5);
                string dur = k.stupidity < 0.01f ? $"{baseD}d" : $"{baseD}–{maxD}d";
                double fc = TrainingFundsCost(hire, tgtT);
                double rc = TrainingRDCost(tgtT);
                bool afford = funds >= fc && rd >= rc;

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name, GUILayout.Width(140));
                GUILayout.Label($"L{(int)k.experienceLevel}→L{tgtT}", GUILayout.Width(70));
                GUILayout.Label($"√{fc:N0} + {rc:N0}R  {dur}", GUILayout.Width(200));
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
                GUILayout.Label("⚙ Aging Settings", GUI.skin.label);
                GUILayout.Label("Aging, retirement, and notification settings are configured in Difficulty Options → Enhanced Astronaut Complex.", GUI.skin.label);
                GUILayout.Label(
                    $"  Retirement Age: {RosterRotationState.RetirementAgeMin}–{RosterRotationState.RetirementAgeMax}    " +
                    $"Retired Death Age Min: {RosterRotationState.RetiredDeathAgeMin}",
                    GUI.skin.label);
            }
        }

        // ── AC Overlay dispatcher ──────────────────────────────────────────────
        // This single window changes content depending on _acOverlay.
        // Buttons to open each overlay are drawn by AstronautComplexACPatch (bottom of AC panel).
        // But since we already have AC open detection here, we also draw them ourselves
        // as floating buttons near the bottom of the screen.
        private void DrawACOverlay(int id)
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            double now = Planetarium.GetUniversalTime();

            // Header bar with tab switchers
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_acOverlay == AcOverlay.Applicants,  "📋 Applicants",    "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.Applicants;
            if (GUILayout.Toggle(_acOverlay == AcOverlay.Training,    "🎓 Send Training", "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.Training;
            if (GUILayout.Toggle(_acOverlay == AcOverlay.ForceRetire, "🚪 Force Retire",  "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.ForceRetire;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕ Close", GUILayout.Width(80))) _acOverlay = AcOverlay.None;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (roster == null) { GUILayout.Label("Roster unavailable."); GUI.DragWindow(); return; }

            if (_acOverlay == AcOverlay.Applicants)
                DrawApplicantsOverlay(roster);
            else if (_acOverlay == AcOverlay.Training)
                DrawTrainingOverlay(roster, now);
            else if (_acOverlay == AcOverlay.ForceRetire)
                DrawRetireOverlay(roster, now);

            GUI.DragWindow();
        }

        // ── Applicants overlay ─────────────────────────────────────────────────
        private void DrawApplicantsOverlay(KerbalRoster roster)
        {
            bool atCap = GetActiveNonRetiredCount() >= GetMaxCrew();

            // Column headers
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUI.skin.label, GUILayout.Width(200));
            GUILayout.Label("Skill", GUI.skin.label, GUILayout.Width(100));
            GUILayout.Label("Courage", GUI.skin.label, GUILayout.Width(80));
            GUILayout.Label("Stupidity", GUI.skin.label, GUILayout.Width(80));
            GUILayout.Label("",                        GUILayout.Width(160));
            GUILayout.EndHorizontal();
            DrawHRule();

            var applicants = roster.Applicants.ToList();
            applicants.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(300));
            int idx = 0;
            foreach (var k in applicants)
            {
                if (k == null) continue;
                var style = (idx++ % 2 == 0) ? GUI.skin.label : GUI.skin.label;
                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name,                                   style, GUILayout.Width(200));
                GUILayout.Label(k.trait,                                  style, GUILayout.Width(100));
                GUILayout.Label($"{k.courage:P0}",                        style, GUILayout.Width(80));
                GUILayout.Label($"{k.stupidity:P0}",                      style, GUILayout.Width(80));
                GUILayout.FlexibleSpace();

                GUI.enabled = !atCap;
                if (GUILayout.Button("Hire", GUI.skin.button, GUILayout.Width(70)))
                {
                    k.type         = ProtoCrewMember.KerbalType.Crew;
                    k.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                    // onKerbalTypeChange fires → sets initial training
                    try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
                    ACPatches.ForceRefresh();
                }
                GUI.enabled = true;
                if (GUILayout.Button("Reject", GUI.skin.button, GUILayout.Width(70)))
                {
                    RejectApplicant(roster, k);
                    ACPatches.ForceRefresh();
                    ACPatches.ForceRefreshApplicants();
                }
                GUILayout.EndHorizontal();
            }
            if (applicants.Count == 0)
                GUILayout.Label("No applicants available.",GUI.skin.label);
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            DrawHRule();
            GUILayout.BeginHorizontal();
            GUI.enabled = !atCap;
            GUILayout.Label($"Slots: {GetActiveNonRetiredCount()} / {GetMaxCrew()}  (cap{(atCap ? " FULL" : "")})",
                GUILayout.Width(260));
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reject All Applicants", GUI.skin.button, GUILayout.Width(180)))
            {
                var all = roster.Applicants.ToList();
                foreach (var k in all) RejectApplicant(roster, k);
                ACPatches.ForceRefresh();
                ACPatches.ForceRefreshApplicants();
            }
            GUILayout.EndHorizontal();
        }

        // ── Training overlay ───────────────────────────────────────────────────
        private void DrawTrainingOverlay(KerbalRoster roster, double now)
        {
            double hire  = GetNextHireCost();
            double funds = Funding.Instance?.Funds ?? 0;
            double rd    = ResearchAndDevelopment.Instance?.Science ?? 0;

            GUILayout.Label($"Funds: √{funds:N0}   R&D: {rd:N0}   Next Hire Base: √{hire:N0}",GUI.skin.label);
            GUILayout.Space(4);

            // Column headers
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUI.skin.label, GUILayout.Width(180));
            GUILayout.Label("Skill", GUI.skin.label, GUILayout.Width(90));
            GUILayout.Label("Level", GUI.skin.label, GUILayout.Width(50));
            GUILayout.Label("Funds Cost", GUI.skin.label, GUILayout.Width(110));
            GUILayout.Label("R&D Cost", GUI.skin.label, GUILayout.Width(80));
            GUILayout.Label("Duration", GUI.skin.label, GUILayout.Width(80));
            GUILayout.Label("",                         GUILayout.Width(140));
            GUILayout.EndHorizontal();
            DrawHRule();

            // Currently in training (read-only section)
            bool anyT = false;
            foreach (var k in roster.Crew)
            {
                if (k == null || !k.inactive || k.inactiveTimeEnd <= now) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r == null || r.Training == TrainingType.None) continue;

                double rem = Math.Max(0, k.inactiveTimeEnd - now);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"⏳ {k.name}", GUI.skin.label, GUILayout.Width(180));
                GUILayout.Label(k.trait, GUI.skin.label, GUILayout.Width(90));
                GUILayout.Label($"L{(int)k.experienceLevel}", GUI.skin.label, GUILayout.Width(50));
                GUILayout.Label(TrainingLabel(r.Training, r.TrainingTargetLevel), GUI.skin.label, GUILayout.Width(110));
                GUILayout.Label("", GUI.skin.label, GUILayout.Width(80));
                GUILayout.Label(FormatCountdown(rem), GUI.skin.label, GUILayout.Width(80));
                GUILayout.Label("In Training", GUI.skin.label, GUILayout.Width(140));
                GUILayout.EndHorizontal();
                anyT = true;
            }

            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(250));

            var candidates = BuildTrainingCandidates(roster);
            int idx = 0;
            foreach (var k in candidates)
            {
                int tgt   = (int)k.experienceLevel + 1;
                double fc = TrainingFundsCost(hire, tgt);
                double rc = TrainingRDCost(tgt);
                bool afford = funds >= fc && rd >= rc;
                var style = (idx++ % 2 == 0) ? GUI.skin.label : GUI.skin.label;

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name,  style, GUILayout.Width(180));
                GUILayout.Label(k.trait, style, GUILayout.Width(90));
                GUILayout.Label($"L{(int)k.experienceLevel}→L{tgt}", style, GUILayout.Width(50));
                GUILayout.Label($"√{fc:N0}", style, GUILayout.Width(110));
                GUILayout.Label($"{rc:N0}", style, GUILayout.Width(80));
                // Show base days and worst-case (100% stupidity bonus)
                int baseDays = tgt * 30;
                int maxDays  = (int)(baseDays * 1.5);
                string durLabel = k.stupidity < 0.01f
                    ? $"{baseDays}d"
                    : $"{baseDays}–{maxDays}d";
                GUILayout.Label(durLabel, style, GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                GUI.enabled = afford;
                if (GUILayout.Button("Send to Training", GUI.skin.button, GUILayout.Width(130)))
                    ExecuteTraining(k, tgt, fc, rc);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            if (candidates.Count == 0 && !anyT)
                GUILayout.Label("No kerbals available for training.",GUI.skin.label);

            GUILayout.EndScrollView();
        }

        // ── Force Retire overlay ───────────────────────────────────────────────
        private void DrawRetireOverlay(KerbalRoster roster, double now)
        {
            GUILayout.Label("Retire active kerbals. Retired kerbals are hidden from missions.",GUI.skin.label);
            GUILayout.Space(4);

            // Column headers
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUI.skin.label, GUILayout.Width(200));
            GUILayout.Label("Skill", GUI.skin.label, GUILayout.Width(100));
            GUILayout.Label("Level", GUI.skin.label, GUILayout.Width(60));
            GUILayout.Label("Flights", GUI.skin.label, GUILayout.Width(70));
            GUILayout.Label("Status", GUI.skin.label, GUILayout.Width(200));
            GUILayout.Label("",                         GUILayout.Width(90));
            GUILayout.EndHorizontal();
            DrawHRule();

            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(300));

            var crew = new List<ProtoCrewMember>();
            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r?.Retired == true) continue;
                if (r?.DeathUT > 0) continue;
                crew.Add(k);
            }
            crew.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            int idx = 0;
            foreach (var k in crew)
            {
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                var style = (idx++ % 2 == 0) ? GUI.skin.label : GUI.skin.label;

                string status = BuildStatusString(k, r, now, r != null && r.LastFlightUT > 0, false);
                bool onMission  = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                bool inTraining = r?.Training != TrainingType.None && k.inactive;

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name,  style, GUILayout.Width(200));
                GUILayout.Label(k.trait, style, GUILayout.Width(100));
                GUILayout.Label($"L{(int)k.experienceLevel}", style, GUILayout.Width(60));
                GUILayout.Label($"{r?.Flights ?? 0}", style, GUILayout.Width(50));
                string frAgeStr = (RosterRotationState.AgingEnabled && r?.LastAgedYears >= 0)
                    ? $"Age {RosterRotationState.GetKerbalAge(r, now)}" : "";
                GUILayout.Label(frAgeStr, style, GUILayout.Width(55));
                GUILayout.Label(status, style, GUILayout.Width(170));
                GUILayout.FlexibleSpace();

                bool frLockout = r != null && r.TrainingEndUT > 0
                    && (now - r.TrainingEndUT) < RosterRotationState.YearSeconds;
                GUI.enabled = !onMission && !inTraining && !frLockout;
                if (GUILayout.Button(frLockout ? "Committed" : "Retire", GUI.skin.button, GUILayout.Width(80)))
                {
                    if (r == null) { r = new RosterRotationState.KerbalRecord(); RosterRotationState.Records[k.name] = r; }
                    if (string.IsNullOrEmpty(r.OriginalTrait)) r.OriginalTrait = k.trait;
                    r.OriginalType       = k.type;
                    r.Retired            = true;
                    r.RetiredUT          = Planetarium.GetUniversalTime();
                    r.ExperienceAtRetire = (int)k.experienceLevel;

                    // Mark inactive so the stock editor crew dialog won't list them.
                    k.inactive        = true;
                    k.inactiveTimeEnd = Planetarium.GetUniversalTime() + RosterRotationState.YearSeconds * 1000.0;

                    // Defer ForceRefresh to next Update() frame — calling it from OnGUI causes
                    // Unity to defer layout/canvas updates, so RepositionRetiredRows sees
                    // unmeasured RectTransforms (sizeDelta.y=0) and skips rows, leaving no
                    // Recall button when the user switches to the Retired tab.
                    _pendingForceRefresh = true;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            if (crew.Count == 0) GUILayout.Label("No active kerbals.",GUI.skin.label);
            GUILayout.EndScrollView();
        }

        // ── Training execution ─────────────────────────────────────────────────
        /// <summary>
        /// Base duration: 30d × targetLevel (30/60/90).
        /// Stupidity bonus: uniformly adds 0–50% extra proportional to the kerbal's stupidity stat.
        /// e.g. stupidity=0 → always base; stupidity=1.0 → up to 50% longer (15/30/45d extra).
        /// </summary>
        private static double CalcTrainingDays(ProtoCrewMember k, int targetLevel)
        {
            double baseDays     = targetLevel * 30.0;
            float  stupidity    = Mathf.Clamp01(k.stupidity);
            double extraFrac    = UnityEngine.Random.value * stupidity * 0.5;
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
            catch (Exception ex)
            {
                RRLog.Warn($"[RosterRotation] Training: resource deduct failed for {k.name}: {ex.Message}");
            }

            double trainingDays = CalcTrainingDays(k, targetLevel);
            double sec        = trainingDays * RosterRotationState.DaySeconds;
            k.inactive        = true;
            k.inactiveTimeEnd = Planetarium.GetUniversalTime() + sec;

            var rec = RosterRotationState.GetOrCreate(k.name);
            rec.Training            = TrainingType.ExperienceUpgrade;
            rec.TrainingTargetLevel = targetLevel;

            ScreenMessages.PostScreenMessage(
                $"{k.name} sent to training → L{targetLevel}   √{fCost:N0}  {rCost:N0} R&D  {trainingDays:F0}d",
                5f, ScreenMessageStyle.UPPER_CENTER);

            try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
            ACPatches.ForceRefresh();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private List<ProtoCrewMember> BuildTrainingCandidates(KerbalRoster roster)
        {
            var list = new List<ProtoCrewMember>();
            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (k.inactive) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                if (k.experienceLevel >= 3f) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r?.Retired == true) continue;
                if (r?.DeathUT > 0) continue;
                list.Add(k);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return list;
        }

        private string BuildStatusString(ProtoCrewMember k, RosterRotationState.KerbalRecord r,
                                         double now, bool hasFlown, bool retired)
        {
            if (r != null && r.DeathUT > 0)
            {
                int age = RosterRotationState.GetKerbalAge(r, r.DeathUT);
                string ageStr  = age >= 0 ? $"Age {age}, " : "";
                string dateStr = RosterRotationState.FormatGameDateYD(r.DeathUT);

                // Retired death vs killed in action
                bool retiredDeath = (r.RetiredUT > 0) && (r.DeathUT >= r.RetiredUT - 1);
                string result;
                if (retiredDeath)
                    result = $"Died {ageStr}{dateStr}";
                else
                    result = $"K.I.A. {ageStr}{dateStr}";

                return result;
            }
            if (retired)
            {
                int eff = RosterRotationState.GetRetiredEffectiveStars(k, r, now);
                return $"RETIRED L{eff} ({FormatTimeAgo(r.RetiredUT, now)})";
            }
            if (r != null && r.Training != TrainingType.None && k.inactive && k.inactiveTimeEnd > now)
            {
                double rem = k.inactiveTimeEnd - now;
                string label = TrainingStatusString(r.Training, r.TrainingTargetLevel, rem);
                return $"{label}  {FormatCountdown(rem)}";
            }
            if (k.inactive && k.inactiveTimeEnd > now)
                return $"INACTIVE ({FormatCountdown(k.inactiveTimeEnd - now)})";
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

        // Full status string for training (used in BuildStatusString)
        private static string TrainingStatusString(TrainingType t, int targetLevel, double remaining)
        {
            string label;
            if (t == TrainingType.InitialHire)       label = "introductory training";
            else if (t == TrainingType.ExperienceUpgrade) label = $"Level {targetLevel} training";
            else if (t == TrainingType.RecallRefresher)   label = "refresher training";
            else label = "training";
            return $"In {label}";
        }

        private static double TrainingFundsCost(double hireCost, int targetLevel)
            => hireCost * RosterRotationState.TrainingFundsMultiplier * targetLevel;

        private static double TrainingRDCost(int targetLevel)
            => RosterRotationState.TrainingRDPerStar * targetLevel;

        // FIX CS0012: use non-generic FindObjectsOfTypeAll to avoid UnityEngine.AnimationModule dep
        private bool IsAstronautComplexOpen()
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return false;
            try
            {
                var asm = AssemblyLoader.loadedAssemblies
                    .Select(a => a.assembly)
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null) return false;

                var t = asm.GetType("KSP.UI.Screens.AstronautComplex");
                if (t == null) return false;

                // Non-generic overload — avoids pulling in UnityEngine.AnimationModule
                var all = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour));
                foreach (var obj in all)
                {
                    var mb = obj as MonoBehaviour;
                    if (mb == null || mb.GetType() != t) continue;
                    if (mb.isActiveAndEnabled && mb.gameObject != null && mb.gameObject.activeInHierarchy)
                        return true;
                }
            }
            catch { }
            return false;
        }

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
                foreach (var m in typeof(KerbalRoster).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
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
                    if (list != null) list.Remove(applicant);
                    else ScreenMessages.PostScreenMessage("Reject failed.", 3f, ScreenMessageStyle.UPPER_CENTER);
                }
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
            }
            catch (Exception e) { RRLog.Error("[RosterRotation] RejectApplicant failed: " + e); }
        }

        // ── GetNextHireCost ────────────────────────────────────────────────────
        private static double GetNextHireCost()
        {
            try
            {
                var gv = GameVariables.Instance;
                if (gv == null) return RosterRotationState.TrainingBaseFundsCost;
                float facLevel = (float)ScenarioUpgradeableFacilities
                    .GetFacilityLevel(SpaceCenterFacility.AstronautComplex);
                int activeCount = HighLogic.CurrentGame?.CrewRoster?.Crew
                    .Count(c => c != null && c.type != ProtoCrewMember.KerbalType.Applicant) ?? 0;
                // KSP scales hire cost by roster size — try two-arg overload first
                var m2 = typeof(GameVariables).GetMethod("GetRecruitCost",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(int), typeof(float) }, null);
                if (m2 != null)
                {
                    var res = m2.Invoke(gv, new object[] { activeCount, facLevel });
                    if (res is float f2)  return f2;
                    if (res is double d2) return d2;
                }
                var m = typeof(GameVariables).GetMethod("GetRecruitCost",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(float) }, null);
                if (m != null)
                {
                    var res = m.Invoke(gv, new object[] { facLevel });
                    if (res is float f)  return f;
                    if (res is double d) return d;
                }
            }
            catch { }
            return RosterRotationState.TrainingBaseFundsCost;
        }

        private static int GetMaxCrew()
        {
            int cached = ACPatches.GetCachedMaxCrew();
            if (cached < int.MaxValue) return cached;
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return int.MaxValue;
            int total = 0;
            foreach (var k in roster.Crew) if (k != null) total++;
            return total + 1;
        }

        private static int GetActiveNonRetiredCount()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return 0;
            int n = 0;
            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (RosterRotationState.Records.TryGetValue(k.name, out var r) && r?.Retired == true) continue;
                n++;
            }
            return n;
        }

        private string FormatCountdown(double seconds)
        {
            if (seconds <= 0) return "Ready";
            double ds = RosterRotationState.DaySeconds;
            int d = (int)(seconds / ds);
            int h = (int)((seconds % ds) / 3600.0);
            int m = (int)((seconds % 3600.0) / 60.0);
            int s = (int)(seconds % 60.0);
            if (d > 0) return $"{d}d {h}h {m}m";
            if (h > 0) return $"{h}h {m}m {s}s";
            if (m > 0) return $"{m}m {s}s";
            return $"{s}s";
        }

        private string FormatTimeAgo(double eventUT, double now)
        {
            if (eventUT <= 0) return "unknown";
            return $"{FormatTime(now - eventUT)} ago";
        }

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            double ds = RosterRotationState.DaySeconds;
            if (seconds / ds   >= 1.0) return $"{seconds / ds:0.0}d";
            if (seconds / 3600 >= 1.0) return $"{seconds / 3600:0.0}h";
            return $"{seconds / 60:0}m";
        }

        // ── Aging: assign age on hire ──────────────────────────────────────────
        private static void AssignAgeOnHire(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            // New hires: mid-20s to mid-40s, weighted toward mid-30s (average two rolls).
            double yearSec = RosterRotationState.YearSeconds;
            double r1 = 25.0 + UnityEngine.Random.value * 20.0;  // 25–45
            double r2 = 25.0 + UnityEngine.Random.value * 20.0;
            double ageYears = (r1 + r2) * 0.5;                   // bell-curve toward 35

            double birthdayOffset = (0.15 + UnityEngine.Random.value * 0.70) * yearSec;
            rec.BirthUT       = nowUT - (ageYears * yearSec) - birthdayOffset;
            rec.LastAgedYears = (int)((nowUT - rec.BirthUT) / yearSec);

            int retireAge = RosterRotationState.RetirementAgeMin
                + (int)(UnityEngine.Random.value * (RosterRotationState.RetirementAgeMax - RosterRotationState.RetirementAgeMin + 1));
            rec.NaturalRetirementUT = rec.BirthUT + retireAge * yearSec;

        }

        // ── Aging: assign age based on experience level (existing kerbals) ─────
        // Used by InitializeExistingKerbalsAges and on-demand in DrawRosterTab.
        private static void AssignAgeByExperience(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            double yearSec = RosterRotationState.YearSeconds;
            int stars = (int)k.experienceLevel;
            double ageMin, ageMax;
            if      (stars >= 5) { ageMin = 45; ageMax = 51; }
            else if (stars == 4) { ageMin = 38; ageMax = 46; }
            else if (stars == 3) { ageMin = 35; ageMax = 42; }
            else                 { ageMin = 25; ageMax = 35; }

            double ageYears       = ageMin + UnityEngine.Random.value * (ageMax - ageMin);
            // Spread birthday across a full year — offset is 0.15..0.85 of yearSec
            // to avoid clustering near year boundaries.
            double birthdayOffset = (0.15 + UnityEngine.Random.value * 0.70) * yearSec;
            rec.BirthUT           = nowUT - (ageYears * yearSec) - birthdayOffset;
            // Use the *actual* computed age (not truncated ageYears) so the next
            // birthday fires at a properly random time rather than immediately.
            rec.LastAgedYears     = (int)((nowUT - rec.BirthUT) / yearSec);

            int retireAge = RosterRotationState.RetirementAgeMin
                + (int)(UnityEngine.Random.value * (RosterRotationState.RetirementAgeMax - RosterRotationState.RetirementAgeMin + 1));
            rec.NaturalRetirementUT = rec.BirthUT + retireAge * yearSec;

        }

        // ── Aging: annual birthday check ──────────────────────────────────────
        // Runs every CHECK_INTERVAL seconds (real time), does nothing unless the
        // kerbal's in-game age has ticked over a whole year since the last check.
        private void CheckAgingAndRetirement()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;
            double nowUT    = Planetarium.GetUniversalTime();
            double yearSec  = RosterRotationState.YearSeconds;
            bool   anyDirty = false;

            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (rec.LastAgedYears < 0) continue;           // not yet initialised
                if (rec.DeathUT > 0 && !rec.Retired) continue; // killed in flight — skip all messages

                int currentAge = RosterRotationState.GetKerbalAge(rec, nowUT);
                if (currentAge < 0) continue;

                // ── Dead kerbal in retirement: check for death ─────────────────
                if (rec.Retired && rec.DeathUT <= 0)
                {
                    anyDirty |= CheckRetiredDeath(k, rec, nowUT, currentAge);
                    continue; // dead/retired kerbals skip other checks
                }

                // Skip further checks if already retired (and not dead yet)
                if (rec.Retired) continue;

                // Only run birthday logic once per age year
                if (currentAge <= rec.LastAgedYears) continue;
                rec.LastAgedYears = currentAge;
                anyDirty = true;
                if (k.rosterStatus != ProtoCrewMember.RosterStatus.Dead
                 && k.rosterStatus != ProtoCrewMember.RosterStatus.Missing)
                {
                    string bdDate = RosterRotationState.FormatGameDate(nowUT);
                    RosterRotationState.PostNotification(
                        EACNotificationType.Birthday, "Birthday — " + k.name,
                        k.name + " turns " + currentAge + " today! (" + bdDate + ")",
                        MessageSystemButton.MessageButtonColor.GREEN,
                        MessageSystemButton.ButtonIcons.MESSAGE,
                        5f);
                }

                // ── Natural retirement ─────────────────────────────────────────
                double effectiveRetireUT = rec.NaturalRetirementUT
                    + rec.RetirementDelayYears * yearSec;
                if (nowUT >= effectiveRetireUT)
                {
                    // Kerbals on a mission wait until recovery
                    if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                    {
                        rec.RetirementScheduled   = true;
                        rec.RetirementScheduledUT = nowUT;
                        RosterRotationState.PostNotification(
                            EACNotificationType.Retirement, $"Retirement Pending — {k.name}",
                            $"{k.name} has reached retirement age but will serve until mission end. ({RosterRotationState.FormatGameDate(nowUT)})",
                            MessageSystemButton.MessageButtonColor.YELLOW,
                            MessageSystemButton.ButtonIcons.ALERT);
                    }
                    else
                    {
                        FireRetirement(k, rec, nowUT, "reached retirement age");
                    }
                    continue;
                }

                // ── One-year warning before natural retirement ─────────────────
                if (!rec.RetirementWarned && (effectiveRetireUT - nowUT) < yearSec)
                {
                    rec.RetirementWarned = true;
                    RosterRotationState.PostNotification(
                        EACNotificationType.Retirement, $"Retirement Warning — {k.name}",
                        $"{k.name} is approaching retirement age and may retire within the year. ({RosterRotationState.FormatGameDate(nowUT)})",
                        MessageSystemButton.MessageButtonColor.YELLOW,
                        MessageSystemButton.ButtonIcons.ALERT);
                }

                // ── Morale / activity check (L0–L3 only) ──────────────────────
                int stars = (int)k.experienceLevel;
                if (stars >= 4) continue; // L4–L5 exempt from morale retirement

                double pRetire = MoraleRetireProbability(k, rec, nowUT, stars);
                if (pRetire <= 0) continue;

                double roll = UnityEngine.Random.value;

                if (roll < pRetire)
                {
                    // Warn one year before firing (if not already warned)
                    if (!rec.RetirementWarned)
                    {
                        rec.RetirementWarned = true;
                        RosterRotationState.PostNotification(
                            EACNotificationType.Retirement, $"Retirement Warning — {k.name}",
                            $"{k.name} is considering retirement — they may leave service next year. ({RosterRotationState.FormatGameDate(nowUT)})",
                            MessageSystemButton.MessageButtonColor.YELLOW,
                            MessageSystemButton.ButtonIcons.ALERT);
                    }
                    else
                    {
                        // Warning was already given last year — retire now
                        if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                        {
                            rec.RetirementScheduled   = true;
                            rec.RetirementScheduledUT = nowUT;
                            RosterRotationState.PostNotification(
                                EACNotificationType.Retirement, $"Retirement Pending — {k.name}",
                                $"{k.name} has decided to retire but will complete their current mission. ({RosterRotationState.FormatGameDate(nowUT)})",
                                MessageSystemButton.MessageButtonColor.YELLOW,
                                MessageSystemButton.ButtonIcons.ALERT);
                        }
                        else
                        {
                            FireRetirement(k, rec, nowUT, "decided to retire");
                        }
                    }
                }
            }

            if (anyDirty)
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }
        }

        // Annual probability of morale-based retirement for L0–L3 kerbals.
        private static double MoraleRetireProbability(ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec, double nowUT, int stars)
        {
            // Base probability by level
            double baseP;
            switch (stars)
            {
                case 0:  baseP = 0.08; break;
                case 1:  baseP = 0.05; break;
                case 2:  baseP = 0.03; break;
                default: baseP = 0.015; break; // L3
            }

            // Inactivity multiplier — years since last flight
            double yearSec  = RosterRotationState.YearSeconds;
            double inactive = rec.LastFlightUT > 0
                ? (nowUT - rec.LastFlightUT) / yearSec : 999;
            double inactMul;
            if      (inactive >= 3.0) inactMul = 4.0;
            else if (inactive >= 2.0) inactMul = 2.5;
            else if (inactive >= 1.0) inactMul = 1.5;
            else                      inactMul = 1.0;

            // Flight-count activity reduction — caps at 75% at 15 flights
            double actRed = Math.Min(rec.Flights / 15.0, 0.75);

            return baseP * inactMul * (1.0 - actRed);
        }

        private void FireRetirement(ProtoCrewMember k, RosterRotationState.KerbalRecord rec,
            double nowUT, string reason)
        {
            if (string.IsNullOrEmpty(rec.OriginalTrait)) rec.OriginalTrait = k.trait;
            rec.OriginalType       = k.type;
            rec.Retired            = true;
            rec.RetiredUT          = nowUT;
            rec.ExperienceAtRetire = (int)k.experienceLevel;
            rec.RetirementWarned   = false;
            rec.RetirementScheduled = false;

            // Mark inactive so the stock editor crew dialog won't list them.
            k.inactive        = true;
            k.inactiveTimeEnd = nowUT + RosterRotationState.YearSeconds * 1000.0;

            int    frAge  = RosterRotationState.GetKerbalAge(rec, nowUT);
            string frAgeS = frAge >= 0 ? $" at age {frAge}" : "";
            RosterRotationState.PostNotification(
                EACNotificationType.Retirement, $"Retired — {k.name}",
                $"{k.name} has {reason} and entered retirement{frAgeS}. ({RosterRotationState.FormatGameDate(nowUT)})",
                MessageSystemButton.MessageButtonColor.ORANGE,
                MessageSystemButton.ButtonIcons.MESSAGE);
            _pendingForceRefresh = true;
        }

        // Annual death check for retired kerbals.  Returns true if state changed.
        private bool CheckRetiredDeath(ProtoCrewMember k, RosterRotationState.KerbalRecord rec,
            double nowUT, int currentAge)
        {
            if (currentAge <= rec.LastAgedYears) return false;
            rec.LastAgedYears = currentAge;

            double pDeath;
            int minAge = RosterRotationState.RetiredDeathAgeMin;
            if      (currentAge >= minAge + 30) pDeath = 0.30;
            else if (currentAge >= minAge + 20) pDeath = 0.14;
            else if (currentAge >= minAge + 10) pDeath = 0.06;
            else if (currentAge >= minAge)      pDeath = 0.02;
            else                                return true;

            double roll = UnityEngine.Random.value;
            if (roll >= pDeath) return true;

            // Kerbal dies
            rec.DeathUT = nowUT;
            try { k.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch { }

            if (RosterRotationState.DeathNotificationsEnabled)
            {
                string dateStr = RosterRotationState.FormatGameDate(nowUT);
                RosterRotationState.PostNotification(
                    EACNotificationType.Death, $"Deceased — {k.name}",
                    $"{k.name} has passed away at age {currentAge}. ({dateStr})",
                    MessageSystemButton.MessageButtonColor.RED,
                    MessageSystemButton.ButtonIcons.ALERT,
                    12f);
            }
            _pendingForceRefresh = true;
            return true;
        }

    } // end class RosterRotationKSCUI

    // ── AC single "Astronaut Management" button ────────────────────────────────
    // Draws one button centered in the gap between the "Next Hire" label (left)
    // and the "Active Kerbals / Max" label (right) at the top of the AC panel.
    // Opens the unified management overlay in RosterRotationKSCUI.
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RosterRotationACButtons : MonoBehaviour
    {
        private GUIStyle _boldBtn;

        private void OnGUI()
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return;
            if (!IsACOpen()) return;

            if (_boldBtn == null)
            {
                _boldBtn           = new GUIStyle(GUI.skin.button);
                _boldBtn.fontStyle = FontStyle.Bold;
                _boldBtn.fontSize  = 16;
                _boldBtn.wordWrap  = false;
            }

            float W = Screen.width;
            float H = Screen.height;

            // The AC panel spans ~24%–77% of screen width.
            // "Next Hire" box occupies the left ~15% of that span;
            // "Active Kerbals" box occupies the right ~28% of that span.
            // The gap between them is centred around 47% of screen width.
            // We draw a 220px-wide, 32px-tall button there.
            float btnW = 220f;
            float btnH = 34f;
            float cx   = W * 0.44f;          // shifted left into the gap centre
            float cy   = H * 0.070f;         // shifted up, aligns with header row
            float x    = cx - btnW * 0.5f;
            float y    = cy - btnH * 0.5f;

            if (GUI.Button(new Rect(x, y, btnW, btnH), "Astronaut Management", _boldBtn))
                RosterRotationKSCUIBridge.RequestOverlay(RosterRotationKSCUIBridge.AcOverlayOpen);
        }

        private static bool IsACOpen()
        {
            try
            {
                var asm = AssemblyLoader.loadedAssemblies
                    .Select(a => a.assembly)
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null) return false;
                var t = asm.GetType("KSP.UI.Screens.AstronautComplex");
                if (t == null) return false;
                var all = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour));
                foreach (var obj in all)
                {
                    var mb = obj as MonoBehaviour;
                    if (mb == null || mb.GetType() != t) continue;
                    if (mb.isActiveAndEnabled && mb.gameObject != null && mb.gameObject.activeInHierarchy)
                        return true;
                }
            }
            catch { }
            return false;
        }
    }

    // ── Static bridge between ACButtons and KSCui ──────────────────────────────
    public static class RosterRotationKSCUIBridge
    {
        public const int AcOverlayNone        = 0;
        public const int AcOverlayOpen        = 1;   // one button → one overlay
        // Keep old constants so any other callers don't break
        public const int AcOverlayApplicants  = 1;
        public const int AcOverlayTraining    = 1;
        public const int AcOverlayForceRetire = 1;

        private static volatile int _pendingOverlay = AcOverlayNone;

        public static void RequestOverlay(int which) => _pendingOverlay = which;
        public static int  ConsumeOverlay()
        {
            int v = _pendingOverlay;
            _pendingOverlay = AcOverlayNone;
            return v;
        }
    }
}
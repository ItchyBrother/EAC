// EAC - Mod.State
// Extracted shared state and AC open cache from Mod.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP;
using KSP.UI.Screens;

namespace RosterRotation
{
    public static class RosterRotationState
    {
        public static double RestDays      = 14; // legacy fallback retained for older saves
        public static double RecoveryLeavePercent = 10;
        public static bool   UseKerbinDays = true;

        public static double DaySeconds => KspTimeMath.GetDaySeconds(UseKerbinDays);

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
            public double LastMissionDeathCheckUT = 0;
            public bool   DiedOnMission         = false;
            public bool   PendingMissionDeath   = false;
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
        public static bool AutoCleanupUnreferencedKerbals = false;
        public static bool VerboseLogging    = false;
        public static bool VerboseAgeLogging = false;
        public static bool VerboseSettingsDirty = false;
        public static bool SyncFlightTrackerFromEacOnce = false;
        public static bool TraitGrowthEnabled = false;
        public static bool PortraitCaptureEnabled = true;
        public static bool CrashPenaltyEnabled = true;
        public static bool MissionDeathEnabled = false;
        public static bool DebugForceMissionDeath = false; // TEMP TEST HOOK

        // Field aliases for EACStateBridge reflection access
        public static bool NotifyHUD { get => HudNotificationsEnabled; set => HudNotificationsEnabled = value; }
        public static bool NotifyMessageApp { get => MessageAppNotificationsEnabled; set => MessageAppNotificationsEnabled = value; }

        public static double YearSeconds => KspTimeMath.GetYearSeconds(UseKerbinDays);

        // ── Cached retired names (invalidated by retire/recall operations) ──────
        // Uses a simple dirty flag rather than a computed hash. The hash approach
        // was fragile: two different sets of retired kerbals could produce the same
        // hash value, returning a stale list. InvalidateRetiredCache() is already
        // called on every retire/recall/death transition so the flag is sufficient.
        private static List<string> _cachedRetiredNames;
        private static bool _retiredCacheDirty = true;

        public static void InvalidateRetiredCache() => _retiredCacheDirty = true;

        public static List<string> GetRetiredNames()
        {
            if (!_retiredCacheDirty && _cachedRetiredNames != null)
                return _cachedRetiredNames;

            var names = new List<string>();
            foreach (var kvp in Records)
                if (kvp.Value != null && kvp.Value.Retired) names.Add(kvp.Key);

            _cachedRetiredNames = names;
            _retiredCacheDirty  = false;
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
            if (rec == null) return -1;
            return CareerRules.CalculateKerbalAge(rec.BirthUT, nowUT, YearSeconds);
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
            int currentExperienceLevel = k != null ? (int)k.experienceLevel : 0;
            if (r == null) return currentExperienceLevel;
            return CareerRules.CalculateRetiredEffectiveStars(currentExperienceLevel, r.Retired, r.ExperienceAtRetire, r.RetiredUT, nowUT, YearSeconds);
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
        private const float CACHE_DURATION_OPEN = 0.5f;

        private static MonoBehaviour _cachedACInstance;

        // When true, we've confirmed the AC is closed and won't scan again
        // until Invalidate() is called (which the Harmony hooks do on dialog open/close).
        private static bool _confirmedClosed;

        internal static int PollCount;
        internal static int ExpensiveScanCount;
        internal static float TotalScanMs;
        internal static int CacheHitCount;

        public static bool IsOpen
        {
            get
            {
                // If we've confirmed closed and no hook has invalidated us, return false immediately.
                // This is the critical optimization: ZERO cost when AC is closed.
                if (_confirmedClosed) return false;

                float now = Time.realtimeSinceStartup;
                float cacheDur = _lastResult ? CACHE_DURATION_OPEN : 0f; // no throttle for "not found" — just confirm once
                if (now - _lastCheckTime < cacheDur) return _lastResult;
                _lastCheckTime = now;
                PollCount++;
                _lastResult = CheckOpen();

                // If scan found nothing, mark confirmed closed — no more scanning until hook fires
                if (!_lastResult) _confirmedClosed = true;

                return _lastResult;
            }
        }

        /// <summary>
        /// Called by Harmony hooks when AC dialog lifecycle events fire (Start/Awake/OnDestroy/OnDisable).
        /// Clears the confirmed-closed flag so the next IsOpen check will do one scan.
        /// </summary>
        public static void Invalidate()
        {
            _lastCheckTime = -10f;
            _cachedACInstance = null;
            _confirmedClosed = false;
        }

        private static bool CheckOpen()
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return false;

            // Fast path: cached reference alive and active
            if (_cachedACInstance != null)
            {
                try
                {
                    if (_cachedACInstance.isActiveAndEnabled &&
                        _cachedACInstance.gameObject != null &&
                        _cachedACInstance.gameObject.activeInHierarchy)
                    {
                        CacheHitCount++;
                        return true;
                    }
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.State.cs:324", "Suppressed exception in Mod.State.cs:324", ex); }
                _cachedACInstance = null;
                return false;
            }

            // Slow path: one-time scan after Invalidate()
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

                float t0 = Time.realtimeSinceStartup;
                var all = Resources.FindObjectsOfTypeAll(_acType);
                float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
                ExpensiveScanCount++;
                TotalScanMs += elapsed;

                foreach (var obj in all)
                {
                    var mb = obj as MonoBehaviour;
                    if (mb != null && mb.isActiveAndEnabled && mb.gameObject != null && mb.gameObject.activeInHierarchy)
                    {
                        _cachedACInstance = mb;
                        return true;
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("Mod.State.cs:358", "Suppressed exception in Mod.State.cs:358", ex); }
            return false;
        }
    }


}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    internal class EACVeteranAndCrewSetupController : MonoBehaviour
    {
        private static readonly HashSet<string> SessionCompletedStartupFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _checkedStartup;
        private bool _showStartupWindow;
        private Rect _window = new Rect(0, 0, 520, 430);
        private bool _windowPositioned;
        private Vector2 _startupScroll;
        private int _startupCrewCount;
        private bool _replaceStartingCrew;
        private bool _stripVeteranStatus;
        private bool _allowMales;
        private bool _allowFemales;
        private bool _allowPilots;
        private bool _allowEngineers;
        private bool _allowScientists;

        private void Start()
        {
            _startupCrewCount = Math.Max(1, Math.Min(10, RosterRotationState.EACStartingCrewCount));
            _replaceStartingCrew = RosterRotationState.EACReplaceStartingCrewDefault;
            _stripVeteranStatus = RosterRotationState.EACStripDefaultVeterans;
            _allowMales = RosterRotationState.EACStartingCrewAllowMales;
            _allowFemales = RosterRotationState.EACStartingCrewAllowFemales;
            _allowPilots = RosterRotationState.EACStartingCrewAllowPilots;
            _allowEngineers = RosterRotationState.EACStartingCrewAllowEngineers;
            _allowScientists = RosterRotationState.EACStartingCrewAllowScientists;
            EnsureValidStartingCrewSelection();
            StartCoroutine(DelayedStartup());
        }

        private IEnumerator DelayedStartup()
        {
            yield return null;
            yield return new WaitForSeconds(0.75f);

            if (EACVeteranService.IsDelegatedToEarnYourStripes)
            {
                RRLog.Verbose("[EAC] Earn Your Stripes detected; EAC veteran/suit/startup crew handling is delegated.");
                yield break;
            }

            _checkedStartup = true;
            if (ShouldShowStartupCrewWindow())
            {
                CenterStartupWindow();
                _showStartupWindow = true;
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, "EACStartingCrewSetup");
                yield break;
            }

            EACVeteranService.EvaluateRoster("space center startup", requestSave: true);
            EACVeteranService.ApplySuitToRoster("space center startup");
        }

        private bool ShouldShowStartupCrewWindow()
        {
            if (EACVeteranService.IsDelegatedToEarnYourStripes)
                return SuppressStartupCrewWindow("Earn Your Stripes loaded; startup crew handling is delegated");

            if (!RosterRotationState.EACNewGameCrewSetupEnabled)
                return SuppressStartupCrewWindow("new-game starting crew setup is disabled");

            string existingSaveReason;
            if (!RosterRotationState.EACNewGameCrewSetupCompleted && LooksLikeExistingEacManagedSave(out existingSaveReason))
            {
                // 1.4 introduced the starting-crew setup flag.  Older EAC saves can
                // legitimately have stock default crew plus EAC records but no flag yet.
                // Treat those as already past new-game setup instead of prompting on
                // every Space Center visit.
                RosterRotationState.EACNewGameCrewSetupCompleted = true;
                string existingSaveKey = BuildCurrentGameFingerprint();
                if (!string.IsNullOrEmpty(existingSaveKey))
                    SessionCompletedStartupFingerprints.Add(existingSaveKey);
                SaveScheduler.RequestSave("EAC starting crew setup migrated for existing save");
                return SuppressStartupCrewWindow("existing EAC-managed save detected: " + existingSaveReason);
            }

            string freshReason;
            if (!LooksLikeFreshNewGameForStartingCrew(out freshReason))
                return SuppressStartupCrewWindow("save does not look like a fresh new game: " + freshReason);

            if (RosterRotationState.EACNewGameCrewSetupCompleted)
            {
                string fp = BuildCurrentGameFingerprint();
                if (SessionCompletedStartupFingerprints.Contains(fp))
                    return SuppressStartupCrewWindow("startup crew setup was already completed for this save fingerprint");

                if (EACScenario.StartingCrewSetupCompletedLoadedForCurrentSave)
                {
                    if (!string.IsNullOrEmpty(fp))
                        SessionCompletedStartupFingerprints.Add(fp);
                    return SuppressStartupCrewWindow("startup crew setup completed flag was loaded from this save");
                }

                // If this KSP process has already completed or observed another
                // completed save, a fresh new-game roster with a completed flag is
                // probably the same-session carry-over bug we are trying to defeat.
                // On a clean KSP launch the completed flag should instead be
                // trusted as persisted save state, even if the save still has the
                // stock default four Kerbals because the player chose Keep Default Crew.
                if (SessionCompletedStartupFingerprints.Count > 0)
                {
                    RRLog.Info("[EAC] Resetting carried-over starting crew setup completion flag for fresh new game.");
                    RosterRotationState.EACNewGameCrewSetupCompleted = false;
                }
                else
                {
                    if (!string.IsNullOrEmpty(fp))
                        SessionCompletedStartupFingerprints.Add(fp);
                    return SuppressStartupCrewWindow("startup crew setup completed flag is persisted for this save");
                }
            }

            RRLog.Info("[EAC] Showing starting crew setup window: " + freshReason);
            return true;
        }

        private bool SuppressStartupCrewWindow(string reason)
        {
            RRLog.Verbose("[EAC] Starting crew setup window not shown: " + reason + "; " + DescribeStartupCrewState());
            return false;
        }

        private bool LooksLikeExistingEacManagedSave(out string reason)
        {
            reason = "no persisted EAC roster records were loaded";

            if (!EACScenario.PersistedKerbalRecordsLoadedForCurrentSave)
                return false;

            int recordCount = RosterRotationState.Records != null ? RosterRotationState.Records.Count : 0;
            if (recordCount <= 0)
                return false;

            reason = "persisted EAC roster records found: " + recordCount.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private bool LooksLikeFreshNewGameForStartingCrew(out string reason)
        {
            reason = "unknown";
            if (HighLogic.CurrentGame == null)
            {
                reason = "CurrentGame is null";
                return false;
            }

            var roster = HighLogic.CurrentGame.CrewRoster;
            if (roster == null)
            {
                reason = "CrewRoster is null";
                return false;
            }

            try
            {
                int assigned = roster.GetAssignedCrewCount();
                if (assigned > 0)
                {
                    reason = "assigned crew count is " + assigned.ToString(CultureInfo.InvariantCulture);
                    return false;
                }
            }
            catch { /* old KSP API fallback; continue */ }

            List<ProtoCrewMember> crew = GetPlayableCrew(roster);
            if (crew.Count == 0)
            {
                reason = "no playable crew found";
                return false;
            }

            // Fresh stock saves have exactly the four default Kerbals.  This is a
            // stronger new-game signal than Planetarium time, which can briefly
            // retain the previous save's UT during same-session new-game creation.
            if (LooksLikeStockDefaultStartingRoster(crew))
            {
                reason = "stock default starting roster detected";
                return true;
            }

            double nowUT = 0;
            try { nowUT = Planetarium.GetUniversalTime(); } catch { nowUT = 0; }
            double grace = Math.Max(2.0 * RosterRotationState.DaySeconds, 1.0);
            if (nowUT > grace)
            {
                reason = "universal time is outside the new-game grace window: " + nowUT.ToString("0.###", CultureInfo.InvariantCulture);
                return false;
            }

            reason = "within new-game UT grace window";
            return true;
        }

        private static List<ProtoCrewMember> GetPlayableCrew(KerbalRoster roster)
        {
            var crew = new List<ProtoCrewMember>();
            if (roster == null) return crew;

            try
            {
                foreach (ProtoCrewMember k in roster.Crew)
                {
                    if (k == null) continue;
                    if (k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                    if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                    crew.Add(k);
                }
            }
            catch { /* return best effort list */ }

            return crew;
        }

        private static bool LooksLikeStockDefaultStartingRoster(List<ProtoCrewMember> crew)
        {
            if (crew == null || crew.Count != 4) return false;

            for (int i = 0; i < crew.Count; i++)
            {
                ProtoCrewMember k = crew[i];
                if (k == null || !EACVeteranService.IsDefaultCrew(k.name)) return false;
            }

            return true;
        }

        private static string DescribeStartupCrewState()
        {
            try
            {
                string saveFolder = HighLogic.SaveFolder ?? "<none>";
                double nowUT = Planetarium.GetUniversalTime();
                var roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
                List<ProtoCrewMember> crew = GetPlayableCrew(roster);
                string names = string.Join(", ", crew.Select(k => k.name ?? "<unnamed>").ToArray());
                return "saveFolder=" + saveFolder
                    + ", completed=" + RosterRotationState.EACNewGameCrewSetupCompleted.ToString()
                    + ", sessionCompletedFingerprints=" + SessionCompletedStartupFingerprints.Count.ToString(CultureInfo.InvariantCulture)
                    + ", crewCount=" + crew.Count.ToString(CultureInfo.InvariantCulture)
                    + ", nowUT=" + nowUT.ToString("0.###", CultureInfo.InvariantCulture)
                    + ", crew=[" + names + "]";
            }
            catch (Exception ex)
            {
                return "startup state unavailable: " + ex.Message;
            }
        }

        private static string BuildCurrentGameFingerprint()
        {
            var game = HighLogic.CurrentGame;
            if (game == null) return "";

            string saveFolder = "";
            try { saveFolder = HighLogic.SaveFolder ?? ""; } catch { saveFolder = ""; }

            string title = TryReadGameString(game, "Title", "title", "Name", "name");
            string seed = TryReadGameString(game, "Seed", "seed", "GameSeed", "gameSeed", "MissionSeed", "missionSeed");

            // This key is used only to remember that this KSP process has already
            // completed/observed startup setup for the current save.  It must stay
            // stable across scene changes and normal roster edits.  Do not include
            // RuntimeHelpers.GetHashCode(CurrentGame) or roster contents here; both
            // can change after entering/exiting buildings, causing the completed
            // flag to be mistaken for stale carry-over and the popup to reappear.
            return saveFolder + "|" + title + "|" + seed;
        }

        private static string TryReadGameString(object obj, params string[] names)
        {
            if (obj == null) return "";
            Type type = obj.GetType();
            foreach (string name in names)
            {
                try
                {
                    var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null)
                    {
                        object value = f.GetValue(obj);
                        if (value != null) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    }

                    var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.CanRead)
                    {
                        object value = p.GetValue(obj, null);
                        if (value != null) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    }
                }
                catch { /* try next */ }
            }
            return "";
        }

        private void CenterStartupWindow()
        {
            if (_windowPositioned) return;

            const float width = 520f;
            const float height = 430f;

            _window.width = width;
            _window.height = height;
            _window.x = Mathf.Max(0f, (Screen.width - width) * 0.5f);
            _window.y = Mathf.Max(0f, (Screen.height - height) * 0.5f);

            _windowPositioned = true;
        }

        private void OnGUI()
        {
            if (!_showStartupWindow || !_checkedStartup) return;
            GUI.skin = HighLogic.Skin ?? GUI.skin;
            _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, "EAC Starting Crew Setup");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            _startupScroll = GUILayout.BeginScrollView(_startupScroll, GUILayout.Width(500), GUILayout.Height(330));

            GUILayout.Label("Configure EAC's starting crew handling for this new save.");
            GUILayout.Label("Earn Your Stripes is not installed, so EAC will manage veteran status, suits, and starting crew.");
            GUILayout.Space(6);

            _stripVeteranStatus = GUILayout.Toggle(_stripVeteranStatus, "Strip unearned veteran status if keeping the default crew");
            GUILayout.Label("The buttons at the bottom choose the action. Count, gender, and class options are used only when replacing the default crew.");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Replacement Kerbals:", GUILayout.Width(170));
            _startupCrewCount = (int)GUILayout.HorizontalSlider(_startupCrewCount, 1, 10, GUILayout.Width(210));
            GUILayout.Label(_startupCrewCount.ToString(CultureInfo.InvariantCulture), GUILayout.Width(24));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Allowed genders");
            GUILayout.BeginHorizontal();
            _allowMales = GUILayout.Toggle(_allowMales, "Male", GUILayout.Width(120));
            _allowFemales = GUILayout.Toggle(_allowFemales, "Female", GUILayout.Width(120));
            if (GUILayout.Button("Both", GUILayout.Width(70)))
            {
                _allowMales = true;
                _allowFemales = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Allowed classes");
            GUILayout.BeginHorizontal();
            _allowPilots = GUILayout.Toggle(_allowPilots, "Pilots", GUILayout.Width(120));
            _allowEngineers = GUILayout.Toggle(_allowEngineers, "Engineers", GUILayout.Width(120));
            _allowScientists = GUILayout.Toggle(_allowScientists, "Scientists", GUILayout.Width(120));
            if (GUILayout.Button("All", GUILayout.Width(70)))
            {
                _allowPilots = true;
                _allowEngineers = true;
                _allowScientists = true;
            }
            GUILayout.EndHorizontal();

            bool validSelection = HasValidStartingCrewSelection();
            bool guaranteesAllSelectedClasses = ReplacementWillIncludeEachSelectedClass();
            if (!validSelection)
                GUILayout.Label("Select at least one gender and one class before replacing the crew.");
            else if (guaranteesAllSelectedClasses)
                GUILayout.Label("Replacement will include at least one Kerbal from each selected class.");

            GUILayout.EndScrollView();
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Keep Default Crew"))
                CompleteStartupSetup(replaceCrew: false);

            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && validSelection;
            if (GUILayout.Button("Replace Default Crew"))
                CompleteStartupSetup(replaceCrew: true);
            GUI.enabled = oldEnabled;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private void CompleteStartupSetup(bool replaceCrew)
        {
            try
            {
                EnsureValidStartingCrewSelection();
                RosterRotationState.EACStartingCrewCount = Math.Max(1, Math.Min(10, _startupCrewCount));
                RosterRotationState.EACReplaceStartingCrewDefault = replaceCrew;
                RosterRotationState.EACStripDefaultVeterans = _stripVeteranStatus;
                RosterRotationState.EACStartingCrewAllowMales = _allowMales;
                RosterRotationState.EACStartingCrewAllowFemales = _allowFemales;
                RosterRotationState.EACStartingCrewAllowPilots = _allowPilots;
                RosterRotationState.EACStartingCrewAllowEngineers = _allowEngineers;
                RosterRotationState.EACStartingCrewAllowScientists = _allowScientists;

                if (replaceCrew)
                    EACVeteranService.GenerateReplacementStartingCrew(RosterRotationState.EACStartingCrewCount);
                else if (_stripVeteranStatus)
                    EACVeteranService.StripUnearnedVeteransFromRoster("starting crew setup");

                EACVeteranService.EvaluateRoster("starting crew setup", requestSave: false);
                EACVeteranService.ApplySuitToRoster("starting crew setup");
                RosterRotationState.EACNewGameCrewSetupCompleted = true;
                string fp = BuildCurrentGameFingerprint();
                if (!string.IsNullOrEmpty(fp))
                    SessionCompletedStartupFingerprints.Add(fp);
                SaveScheduler.RequestSave("EAC starting crew setup completed");
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Starting crew setup failed: " + ex.Message);
            }
            finally
            {
                _showStartupWindow = false;
                InputLockManager.RemoveControlLock("EACStartingCrewSetup");
            }
        }

        private bool HasValidStartingCrewSelection()
        {
            return (_allowMales || _allowFemales) && (_allowPilots || _allowEngineers || _allowScientists);
        }

        private bool ReplacementWillIncludeEachSelectedClass()
        {
            int selectedClasses = 0;
            if (_allowPilots) selectedClasses++;
            if (_allowEngineers) selectedClasses++;
            if (_allowScientists) selectedClasses++;
            return selectedClasses > 0 && _startupCrewCount >= selectedClasses;
        }

        private void EnsureValidStartingCrewSelection()
        {
            if (!_allowMales && !_allowFemales)
            {
                _allowMales = true;
                _allowFemales = true;
            }
            if (!_allowPilots && !_allowEngineers && !_allowScientists)
            {
                _allowPilots = true;
                _allowEngineers = true;
                _allowScientists = true;
            }
        }

        private void OnDestroy()
        {
            InputLockManager.RemoveControlLock("EACStartingCrewSetup");
        }
    }
}

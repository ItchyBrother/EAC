using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    internal class EACCrewRotationAdvisor : MonoBehaviour
    {
        private Rect _window = new Rect(0, 0, 640, 430);
        private bool _windowPositioned;
        private bool _visible;
        private float _nextCheck;
        private Vector2 _scroll;
        private List<CrewSuggestion> _suggestions = new List<CrewSuggestion>();
        private float _lastBuildRealtime = -1f;
        private bool _lastCrewPanelSelected;
        private bool _openedByCrewPanel;

        private Texture2D _iconTex;
        private ApplicationLauncherButton _button;

        private const float CheckInterval = 0.25f;
        private const float RebuildIntervalSeconds = 2.0f;

        private static bool _pendingShowRequest;
        private static float _lastShowRequestRealtime;
        private static float _ignoreRefreshRequestsUntil;
        private static float _suppressAutoRequestsUntil;

        private void Start()
        {
            _ignoreRefreshRequestsUntil = Time.realtimeSinceStartup + 2.5f;
            _iconTex = GameDatabase.Instance != null ? GameDatabase.Instance.GetTexture("EAC/Icons/icon", false) : null;
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
            OnAppLauncherReady();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
            RemoveAppLauncherButton();
        }

        internal static void NotifyCrewDialogRefreshed(object crewDialogInstance, string methodName)
        {
            try
            {
                if (HighLogic.LoadedScene != GameScenes.EDITOR) return;
                if (!RosterRotationState.CrewRotationAdvisorEnabled) return;
                if (EACExternalModDetector.IsCrewRandRInstalled()) return;
                if (Time.realtimeSinceStartup < _ignoreRefreshRequestsUntil) return;
                if (Time.realtimeSinceStartup < _suppressAutoRequestsUntil) return;

                if (!string.Equals(methodName, "RefreshCrewLists", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(methodName, "Refresh", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(methodName, "UpdateCrewLists", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(methodName, "RebuildCrewLists", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // RefreshCrewLists/Refresh is also called by unrelated editor actions
                // such as loading vessels and changing parts. Only treat it as a
                // crew-assignment open signal when the editor is actually on the
                // stock Crew panel. The Update() poll below is the primary path
                // for opening the advisor from the Crew button.
                if (EACCrewAssignmentDialogLocator.IsCrewEditorPanelSelected())
                    RequestShow(crewDialogInstance);
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACCrewRotationAdvisor.NotifyCrewDialogRefreshed", "Suppressed exception handling crew dialog refresh", ex);
            }
        }

        internal static void RequestShow(object crewDialogInstance)
        {
            try
            {
                if (HighLogic.LoadedScene != GameScenes.EDITOR) return;
                if (!RosterRotationState.CrewRotationAdvisorEnabled) return;
                if (EACExternalModDetector.IsCrewRandRInstalled()) return;

                // Prevent duplicate requests from multiple stock refresh calls during one click.
                float now = Time.realtimeSinceStartup;
                if (now - _lastShowRequestRealtime < 0.5f) return;

                _lastShowRequestRealtime = now;
                _pendingShowRequest = true;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACCrewRotationAdvisor.RequestShow", "Suppressed exception requesting crew rotation advisor", ex);
            }
        }

        private void OnAppLauncherReady()
        {
            try
            {
                if (_button != null || ApplicationLauncher.Instance == null) return;
                if (!RosterRotationState.CrewRotationAdvisorEnabled || EACExternalModDetector.IsCrewRandRInstalled()) return;
                _button = ApplicationLauncher.Instance.AddModApplication(
                    OpenFromToolbar,
                    HideFromToolbar,
                    null, null, null, null,
                    ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH,
                    _iconTex != null ? _iconTex : Texture2D.whiteTexture);
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACCrewRotationAdvisor.OnAppLauncherReady", "Suppressed exception adding editor toolbar button", ex);
            }
        }

        private void OnAppLauncherDestroyed()
        {
            _button = null;
            _visible = false;
        }

        private void RemoveAppLauncherButton()
        {
            try
            {
                if (_button != null && ApplicationLauncher.Instance != null)
                    ApplicationLauncher.Instance.RemoveModApplication(_button);
            }
            catch { /* ignore shutdown ordering */ }
            _button = null;
        }

        private void OpenFromToolbar()
        {
            if (!RosterRotationState.CrewRotationAdvisorEnabled || EACExternalModDetector.IsCrewRandRInstalled())
            {
                _visible = false;
                if (_button != null) _button.SetFalse(false);
                return;
            }

            CenterWindow();
            RebuildSuggestions(force: true);
            _openedByCrewPanel = false;
            _visible = true;
        }

        private void HideFromToolbar()
        {
            _visible = false;
            _suppressAutoRequestsUntil = Time.realtimeSinceStartup + 10.0f;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = Time.realtimeSinceStartup + CheckInterval;

            if (!RosterRotationState.CrewRotationAdvisorEnabled ||
                EACExternalModDetector.IsCrewRandRInstalled() ||
                HighLogic.LoadedScene != GameScenes.EDITOR)
            {
                _visible = false;
                _pendingShowRequest = false;
                _lastCrewPanelSelected = false;
                _openedByCrewPanel = false;
                RemoveAppLauncherButton();
                return;
            }

            if (_button == null)
                OnAppLauncherReady();

            bool crewPanelSelected = EACCrewAssignmentDialogLocator.IsCrewEditorPanelSelected();

            if (crewPanelSelected && !_lastCrewPanelSelected)
            {
                // User switched to the stock Crew assignment panel. Open the
                // advisor once for this panel selection.
                _pendingShowRequest = false;
                CenterWindow();
                RebuildSuggestions(force: true);
                _openedByCrewPanel = true;
                _visible = true;
                if (_button != null) _button.SetTrue(false);
            }
            else if (!crewPanelSelected && _lastCrewPanelSelected && _openedByCrewPanel)
            {
                // Keep manually opened advisor windows under user control, but
                // close the automatic crew-panel window when leaving Crew mode.
                _visible = false;
                _openedByCrewPanel = false;
                if (_button != null) _button.SetFalse(false);
            }

            _lastCrewPanelSelected = crewPanelSelected;

            if (_pendingShowRequest)
            {
                _pendingShowRequest = false;

                if (crewPanelSelected)
                {
                    CenterWindow();
                    RebuildSuggestions(force: true);
                    _openedByCrewPanel = true;
                    _visible = true;
                    if (_button != null) _button.SetTrue(false);
                }
            }

            if (_visible)
                RebuildSuggestions(force: false);
        }

        private void OnGUI()
        {
            if (!_visible) return;

            GUISkin previousSkin = GUI.skin;
            GUISkin kspSkin = KspGuiSkin.Current;
            if (kspSkin != null) GUI.skin = kspSkin;

            try
            {
                _window = GUILayout.Window(GetInstanceID() + 99201, _window, DrawWindow, "EAC Suggested Next Crew");
            }
            finally
            {
                GUI.skin = previousSkin;
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Suggested only — assign crew in the stock crew-selection window.");
            GUILayout.Label("Priority favors Kerbals who need experience, are due for flight, or have waited longest since their last mission.");
            GUILayout.Space(4);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(300));

            if (_suggestions == null || _suggestions.Count == 0)
            {
                GUILayout.Label("No eligible available crew found.");
            }
            else
            {
                DrawHeader();
                int shown = 0;
                foreach (var s in _suggestions)
                {
                    if (s == null) continue;
                    DrawSuggestionRow(s);
                    shown++;
                    if (shown >= 16) break;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Priority: Needs experience, Due for flight, Long service priority, Recently flew.");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
                RebuildSuggestions(force: true);
            if (GUILayout.Button("Close", GUILayout.Width(80)))
            {
                _visible = false;
                _openedByCrewPanel = false;
                _suppressAutoRequestsUntil = Time.realtimeSinceStartup + 10.0f;
                if (_button != null) _button.SetFalse(false);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private static void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Kerbal", GUILayout.Width(150));
            GUILayout.Label("Class", GUILayout.Width(90));
            GUILayout.Label("Lvl", GUILayout.Width(36));
            GUILayout.Label("Flights", GUILayout.Width(55));
            GUILayout.Label("Hours", GUILayout.Width(60));
            GUILayout.Label("Recommendation", GUILayout.Width(190));
            GUILayout.EndHorizontal();
        }

        private static void DrawSuggestionRow(CrewSuggestion s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(s.Name ?? "", GUILayout.Width(150));
            GUILayout.Label(s.Trait ?? "", GUILayout.Width(90));
            GUILayout.Label(s.Level.ToString(), GUILayout.Width(36));
            GUILayout.Label(s.Flights.ToString(), GUILayout.Width(55));
            GUILayout.Label(s.Hours.ToString("N1"), GUILayout.Width(60));
            GUILayout.Label(s.Label ?? "", GUILayout.Width(190));
            GUILayout.EndHorizontal();
        }

        private void CenterWindow()
        {
            if (_windowPositioned) return;

            const float width = 640f;
            const float height = 430f;
            _window.width = width;
            _window.height = height;
            _window.x = Mathf.Max(0f, (Screen.width - width) * 0.5f + 120f);
            _window.y = Mathf.Max(0f, (Screen.height - height) * 0.5f);
            _windowPositioned = true;
        }

        private void RebuildSuggestions(bool force)
        {
            if (!force && _lastBuildRealtime >= 0f && Time.realtimeSinceStartup - _lastBuildRealtime < RebuildIntervalSeconds)
                return;

            _lastBuildRealtime = Time.realtimeSinceStartup;

            try
            {
                _suggestions = EACCrewRotationRecommendationService.BuildSuggestions();
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACCrewRotationAdvisor.RebuildSuggestions", "Suppressed exception rebuilding crew rotation suggestions", ex);
                _suggestions = new List<CrewSuggestion>();
            }
        }
    }
}

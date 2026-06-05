using System;
using UnityEngine;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    internal class EACAdvancedSettingsWindow : MonoBehaviour
    {
        private static EACAdvancedSettingsWindow _instance;

        private bool _visible;
        private bool _loaded;
        private Rect _window = new Rect(220, 90, 560, 640);
        private Vector2 _scroll;

        private bool _notifyBirthdays;
        private bool _notifyTraining;
        private bool _notifyRetirement;
        private bool _notifyDeaths;
        private bool _notifyVeterans;
        private bool _notifyBadass;
        private bool _autoCleanup;
        private bool _verboseUi;
        private bool _verboseAging;

        private bool _veteransEnabled;
        private int _veteranFlights;
        private float _veteranHours;
        private bool _veteranRequireMilestone;
        private bool _stripDefaultVeterans;
        private bool _stripOtherUnearnedVeterans;
        private bool _allowPilotVeterans;
        private bool _allowEngineerVeterans;
        private bool _allowScientistVeterans;

        private bool _applySuits;
        private int _defaultSuit;
        private int _veteranSuit;

        private bool _badassEnabled;
        private bool _badassRequireVeteran;
        private bool _badassRequireMilestone;
        private int _badassChance;

        private bool _newGameCrewSetup;
        private bool _replaceStartingCrewDefault;
        private int _startingCrewCount;
        private bool _startingCrewAllowMales;
        private bool _startingCrewAllowFemales;
        private bool _startingCrewAllowPilots;
        private bool _startingCrewAllowEngineers;
        private bool _startingCrewAllowScientists;


        internal static void ToggleWindow()
        {
            if (_instance == null) return;
            _instance._visible = !_instance._visible;
            if (_instance._visible)
                _instance.LoadFromState();
        }

        internal static void ShowWindow()
        {
            if (_instance == null) return;
            _instance._visible = true;
            _instance.LoadFromState();
        }

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            GUISkin previousSkin = GUI.skin;
            GUISkin kspSkin = KspGuiSkin.Current;
            if (kspSkin != null) GUI.skin = kspSkin;

            try
            {
                _window = GUILayout.Window(GetInstanceID() + 77881, _window, DrawWindow, "EAC Advanced Settings");
            }
            finally
            {
                GUI.skin = previousSkin;
            }
        }

        private void LoadFromState()
        {
            _notifyBirthdays = RosterRotationState.BirthdayNotificationsEnabled;
            _notifyTraining = RosterRotationState.TrainingNotificationsEnabled;
            _notifyRetirement = RosterRotationState.RetirementNotificationsEnabled;
            _notifyDeaths = RosterRotationState.DeathNotificationsEnabled;
            _notifyVeterans = RosterRotationState.VeteranNotificationsEnabled;
            _notifyBadass = RosterRotationState.BadassNotificationsEnabled;
            _autoCleanup = RosterRotationState.AutoCleanupUnreferencedKerbals;
            _verboseUi = RosterRotationState.VerboseLogging;
            _verboseAging = RosterRotationState.VerboseAgeLogging;

            _veteransEnabled = RosterRotationState.EACVeteranStatusEnabled;
            _veteranFlights = RosterRotationState.EACVeteranFlightsRequired;
            _veteranHours = (float)RosterRotationState.EACVeteranHoursRequired;
            _veteranRequireMilestone = RosterRotationState.EACVeteranRequireMilestone;
            _stripDefaultVeterans = RosterRotationState.EACStripDefaultVeterans;
            _stripOtherUnearnedVeterans = RosterRotationState.EACStripOtherUnearnedVeterans;
            _allowPilotVeterans = RosterRotationState.EACAllowPilotVeterans;
            _allowEngineerVeterans = RosterRotationState.EACAllowEngineerVeterans;
            _allowScientistVeterans = RosterRotationState.EACAllowScientistVeterans;

            _applySuits = RosterRotationState.EACApplySuits;
            _defaultSuit = RosterRotationState.EACDefaultSuit;
            _veteranSuit = RosterRotationState.EACVeteranSuit;

            _badassEnabled = RosterRotationState.EACBadassProgressionEnabled;
            _badassRequireVeteran = RosterRotationState.EACBadassRequireVeteran;
            _badassRequireMilestone = RosterRotationState.EACBadassRequireMilestone;
            _badassChance = RosterRotationState.EACBadassChancePercent;

            _newGameCrewSetup = RosterRotationState.EACNewGameCrewSetupEnabled;
            _replaceStartingCrewDefault = RosterRotationState.EACReplaceStartingCrewDefault;
            _startingCrewCount = RosterRotationState.EACStartingCrewCount;
            _startingCrewAllowMales = RosterRotationState.EACStartingCrewAllowMales;
            _startingCrewAllowFemales = RosterRotationState.EACStartingCrewAllowFemales;
            _startingCrewAllowPilots = RosterRotationState.EACStartingCrewAllowPilots;
            _startingCrewAllowEngineers = RosterRotationState.EACStartingCrewAllowEngineers;
            _startingCrewAllowScientists = RosterRotationState.EACStartingCrewAllowScientists;


            _loaded = true;
        }

        private void ApplyToState(bool reevaluateVeterans)
        {
            if (!_loaded) LoadFromState();

            bool oldVerboseUi = RosterRotationState.VerboseLogging;
            bool oldVerboseAging = RosterRotationState.VerboseAgeLogging;
            bool runAutoCleanupNow = _autoCleanup;

            RosterRotationState.BirthdayNotificationsEnabled = _notifyBirthdays;
            RosterRotationState.TrainingNotificationsEnabled = _notifyTraining;
            RosterRotationState.RetirementNotificationsEnabled = _notifyRetirement;
            RosterRotationState.DeathNotificationsEnabled = _notifyDeaths;
            RosterRotationState.VeteranNotificationsEnabled = _notifyVeterans;
            RosterRotationState.BadassNotificationsEnabled = _notifyBadass;
            // Auto-clean is intentionally a one-shot command, not a persisted background setting.
            // Checking it and clicking Apply queues one cleanup pass; the checkbox resets to false
            // as the user's visible indication that the command was accepted/run.
            RosterRotationState.AutoCleanupUnreferencedKerbals = false;
            _autoCleanup = false;
            if (runAutoCleanupNow)
                RetiredKerbalCleanupService.RequestOneShotCleanup("advanced settings Apply");
            RosterRotationState.VerboseLogging = _verboseUi;
            RosterRotationState.VerboseAgeLogging = _verboseAging;
            if (oldVerboseUi != _verboseUi || oldVerboseAging != _verboseAging)
                RosterRotationState.VerboseSettingsDirty = true;

            RosterRotationState.EACVeteranStatusEnabled = _veteransEnabled;
            RosterRotationState.EACVeteranFlightsRequired = Clamp(_veteranFlights, 0, 100);
            RosterRotationState.EACVeteranHoursRequired = Math.Max(0.0, _veteranHours);
            RosterRotationState.EACVeteranRequireMilestone = _veteranRequireMilestone;
            RosterRotationState.EACStripDefaultVeterans = _stripDefaultVeterans;
            RosterRotationState.EACStripOtherUnearnedVeterans = _stripOtherUnearnedVeterans;
            RosterRotationState.EACAllowPilotVeterans = _allowPilotVeterans;
            RosterRotationState.EACAllowEngineerVeterans = _allowEngineerVeterans;
            RosterRotationState.EACAllowScientistVeterans = _allowScientistVeterans;

            RosterRotationState.EACApplySuits = _applySuits;
            RosterRotationState.EACDefaultSuit = Clamp(_defaultSuit, 0, 2);
            RosterRotationState.EACVeteranSuit = Clamp(_veteranSuit, 0, 2);

            RosterRotationState.EACBadassProgressionEnabled = _badassEnabled;
            RosterRotationState.EACBadassRequireVeteran = _badassRequireVeteran;
            RosterRotationState.EACBadassRequireMilestone = _badassRequireMilestone;
            RosterRotationState.EACBadassChancePercent = Clamp(_badassChance, 0, 100);

            RosterRotationState.EACNewGameCrewSetupEnabled = _newGameCrewSetup;
            RosterRotationState.EACReplaceStartingCrewDefault = _replaceStartingCrewDefault;
            RosterRotationState.EACStartingCrewCount = Clamp(_startingCrewCount, 1, 10);
            RosterRotationState.EACStartingCrewAllowMales = _startingCrewAllowMales;
            RosterRotationState.EACStartingCrewAllowFemales = _startingCrewAllowFemales;
            RosterRotationState.EACStartingCrewAllowPilots = _startingCrewAllowPilots;
            RosterRotationState.EACStartingCrewAllowEngineers = _startingCrewAllowEngineers;
            RosterRotationState.EACStartingCrewAllowScientists = _startingCrewAllowScientists;


            if (reevaluateVeterans && !EACVeteranService.IsDelegatedToEarnYourStripes)
            {
                EACVeteranService.EvaluateRoster("advanced settings", requestSave: false);
                EACVeteranService.ApplySuitToRoster("advanced settings");
            }

            EACGameSettings.TrySyncGameParamsFromState();
            SaveScheduler.RequestSave(runAutoCleanupNow ? "EAC advanced settings auto-clean" : "EAC advanced settings");
        }

        private void DrawWindow(int id)
        {
            if (!_loaded) LoadFromState();

            GUILayout.BeginVertical();
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(540), GUILayout.Height(540));

            DrawHeading("Messages");
            GUILayout.Label("Basic setting 'Message App' controls whether persistent messages are used. These advanced toggles control which categories are sent when it is enabled.");
            _notifyBirthdays = GUILayout.Toggle(_notifyBirthdays, "Birthdays");
            _notifyTraining = GUILayout.Toggle(_notifyTraining, "Training");
            _notifyRetirement = GUILayout.Toggle(_notifyRetirement, "Retirement");
            _notifyDeaths = GUILayout.Toggle(_notifyDeaths, "Deaths");
            _notifyVeterans = GUILayout.Toggle(_notifyVeterans, "Veteran recognition");
            _notifyBadass = GUILayout.Toggle(_notifyBadass, "Badass recognition");

            DrawHeading("Cleanup");
            _autoCleanup = GUILayout.Toggle(_autoCleanup, "Auto-clean unreferenced retired/dead Kerbals now");
            GUILayout.Label("One-shot: check this, click Apply, EAC runs one cleanup pass, then resets this box to unchecked.");
            GUILayout.Label("Caution: backup your persistent.sfs before running cleanup.");

            DrawHeading("Veterans, suits, and starting crew");
            bool eysInstalled = EACExternalModDetector.IsEarnYourStripesInstalled();
            if (eysInstalled)
            {
                GUILayout.Label("Earn Your Stripes detected: EAC veteran status, suits, Badass progression, and starting-crew setup are delegated to EYS.");
            }

            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && !eysInstalled;
            _veteransEnabled = GUILayout.Toggle(_veteransEnabled, "Enable EAC veterans");
            DrawIntSlider("Flights required", ref _veteranFlights, 0, 100);
            DrawFloatSlider("Flight hours required", ref _veteranHours, 0f, 10000f, "N0");
            _veteranRequireMilestone = GUILayout.Toggle(_veteranRequireMilestone, "Require milestone");
            _stripDefaultVeterans = GUILayout.Toggle(_stripDefaultVeterans, "Strip default veterans");
            _stripOtherUnearnedVeterans = GUILayout.Toggle(_stripOtherUnearnedVeterans, "Strip other unearned veterans");
            _allowPilotVeterans = GUILayout.Toggle(_allowPilotVeterans, "Allow Pilot veterans");
            _allowEngineerVeterans = GUILayout.Toggle(_allowEngineerVeterans, "Allow Engineer veterans");
            _allowScientistVeterans = GUILayout.Toggle(_allowScientistVeterans, "Allow Scientist veterans");

            DrawSubheading("Suits");
            _applySuits = GUILayout.Toggle(_applySuits, "Apply suits");
            DrawIntSlider("Default suit", ref _defaultSuit, 0, 2);
            DrawIntSlider("Veteran suit", ref _veteranSuit, 0, 2);
            GUILayout.Label("Suit values: 0=Basic, 1=Vintage, 2=SciFi in stock KSP 1.12.");

            DrawSubheading("Badass progression");
            _badassEnabled = GUILayout.Toggle(_badassEnabled, "Enable Badass progression");
            _badassRequireVeteran = GUILayout.Toggle(_badassRequireVeteran, "Badass requires veteran");
            _badassRequireMilestone = GUILayout.Toggle(_badassRequireMilestone, "Badass requires milestone");
            DrawIntSlider("Badass chance (%)", ref _badassChance, 0, 100);

            DrawSubheading("New-game starting crew");
            _newGameCrewSetup = GUILayout.Toggle(_newGameCrewSetup, "Starting crew setup");
            _replaceStartingCrewDefault = GUILayout.Toggle(_replaceStartingCrewDefault, "Replace crew by default");
            DrawIntSlider("Starting crew count", ref _startingCrewCount, 1, 10);
            _startingCrewAllowMales = GUILayout.Toggle(_startingCrewAllowMales, "Allow male starting crew");
            _startingCrewAllowFemales = GUILayout.Toggle(_startingCrewAllowFemales, "Allow female starting crew");
            _startingCrewAllowPilots = GUILayout.Toggle(_startingCrewAllowPilots, "Allow starting Pilots");
            _startingCrewAllowEngineers = GUILayout.Toggle(_startingCrewAllowEngineers, "Allow starting Engineers");
            _startingCrewAllowScientists = GUILayout.Toggle(_startingCrewAllowScientists, "Allow starting Scientists");
            GUI.enabled = oldEnabled;


            DrawHeading("Debug");
            _verboseUi = GUILayout.Toggle(_verboseUi, "Verbose UI logs");
            _verboseAging = GUILayout.Toggle(_verboseAging, "Verbose aging logs");

            GUILayout.EndScrollView();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-evaluate veterans now", GUILayout.Width(180)))
            {
                ApplyToState(reevaluateVeterans: true);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90)))
            {
                _visible = false;
                _loaded = false;
            }
            if (GUILayout.Button("Apply", GUILayout.Width(90)))
            {
                ApplyToState(reevaluateVeterans: false);
                _visible = false;
                _loaded = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private static void DrawHeading(string text)
        {
            GUILayout.Space(8);
            GUILayout.Label("── " + text + " ──");
        }

        private static void DrawSubheading(string text)
        {
            GUILayout.Space(4);
            GUILayout.Label(text);
        }

        private static void DrawIntSlider(string label, ref int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + value, GUILayout.Width(180));
            value = (int)Math.Round(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(260)));
            value = Clamp(value, min, max);
            GUILayout.EndHorizontal();
        }

        private static void DrawFloatSlider(string label, ref float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + value.ToString(format), GUILayout.Width(180));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(260));
            if (value < min) value = min;
            if (value > max) value = max;
            GUILayout.EndHorizontal();
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

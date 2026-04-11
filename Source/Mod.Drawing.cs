// EAC - Enhanced Astronaut Complex - Mod.Drawing.cs
// Partial class: all OnGUI drawing methods for the KSC window and AC overlay.

using System;
using UnityEngine;
using KSP;
using KSP.UI.Screens;

namespace RosterRotation
{
    public partial class RosterRotationKSCUI
    {
        // ── OnGUI entry point ──────────────────────────────────────────────────
        private void OnGUI()
        {
            GUISkin previousSkin = GUI.skin;
            GUISkin kspSkin      = KspGuiSkin.Current;
            if (kspSkin != null) GUI.skin = kspSkin;

            try
            {
                if (!_windowStyleReady || !ReferenceEquals(_windowStyleSourceSkin, kspSkin))
                {
                    _windowStyleReady      = true;
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
            if (roster == null)
            {
                GUILayout.Label("Crew roster not available.");
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            double now = Planetarium.GetUniversalTime();
            if (_tab == Tab.Applicants) DrawApplicantsTab(roster);
            else if (_tab == Tab.Training) DrawTrainingTab(roster, now);
            else DrawRosterTab(roster, now);

            GUILayout.Space(8);
            if (GUILayout.Button("Close")) _show = false;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // ── Applicants tab ─────────────────────────────────────────────────────
        private void DrawApplicantsTab(KerbalRoster roster)
        {
            int  activeCount = GetActiveNonRetiredCount();
            int  maxCrew     = GetMaxCrew();
            bool atCap       = activeCount >= maxCrew;
            var  applicants  = GetApplicantsCached(roster);

            GUILayout.Label($"Applicants: {applicants.Count}");
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name",      GUILayout.Width(260));
            GUILayout.Label("Skill",     GUILayout.Width(110));
            GUILayout.Label("Courage",   GUILayout.Width(90));
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
                GUILayout.Label(k.name,              GUILayout.Width(260));
                GUILayout.Label(k.trait,             GUILayout.Width(110));
                GUILayout.Label($"{k.courage:P0}",   GUILayout.Width(90));
                GUILayout.Label($"{k.stupidity:P0}", GUILayout.Width(90));
                GUILayout.FlexibleSpace();
                GUI.enabled = !atCap;
                if (GUILayout.Button("Hire", GUILayout.Width(70)))
                {
                    k.type         = ProtoCrewMember.KerbalType.Crew;
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
                var all = roster.Applicants;
                foreach (var k in all) RejectApplicant(roster, k);
                InvalidateUICaches();
                ACPatches.ForceRefresh();
                ACPatches.ForceRefreshApplicants();
            }
            GUILayout.EndHorizontal();
        }

        // ── Roster tab (Active / Assigned / R&R / Retired / Lost) ─────────────
        private void DrawRosterTab(KerbalRoster roster, double now)
        {
            var  rows        = GetRosterRowsCached(roster, now);
            int  activeCount = GetActiveNonRetiredCount();
            int  maxCrew     = GetMaxCrew();
            bool atCap       = activeCount >= maxCrew;

            const float nameWidth        = 300f;
            const float flightsWidth     = 85f;
            const float ageWidth         = 65f;
            const float statusWidth      = 260f;
            const float actionButtonWidth = 80f;
            const float actionAreaWidth  = actionButtonWidth * 2f;

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
                GUILayout.Label(row.Status,  GUILayout.Width(statusWidth));
                GUILayout.FlexibleSpace();

                if (row.IsLost || row.IsAssigned)
                {
                    GUILayout.Space(actionAreaWidth);
                }
                else if (!row.Retired)
                {
                    bool inTraining = k.inactive;
                    bool onMission  = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                    bool maxLevel   = k.experienceLevel >= 3f;

                    GUI.enabled = !inTraining && !onMission && !maxLevel;
                    if (GUILayout.Button("Train", GUILayout.Width(actionButtonWidth)))
                    {
                        _pendingTrainKerbal = k;
                        _showTrainConfirm   = true;
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
                    bool   noStar     = row.EffectiveStars <= 0;
                    double recallCost = GetRecallFundsCost();
                    double curFunds   = Funding.Instance?.Funds ?? 0;
                    bool   cantAfford = recallCost > 0 && curFunds < recallCost;

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
            var    trainK  = _pendingTrainKerbal;
            double hire    = GetNextHireCost();
            int    tgt     = (int)trainK.experienceLevel + 1;
            double fCost   = TrainingFundsCost(hire, tgt);
            double rCost   = TrainingRDCost(tgt);
            double funds   = Funding.Instance?.Funds ?? 0;
            double rd      = ResearchAndDevelopment.Instance?.Science ?? 0;
            bool   afford  = funds >= fCost && rd >= rCost;

            GUILayout.Space(6);
            GUILayout.BeginVertical(KspGuiSkin.Box);
            GUILayout.Label($"Train {trainK.name}  L{(int)trainK.experienceLevel} → L{tgt}");
            int    cBase = tgt * RosterRotationState.TrainingStarDays;
            int    cMax  = (int)(cBase * 1.5);
            string cDur  = trainK.stupidity < 0.01f ? $"{cBase}d" : $"{cBase}–{cMax}d";
            GUILayout.Label($"Cost: √{fCost:N0}  |  {rCost:N0} R&D  |  {cDur}");
            if (!afford) GUILayout.Label("⚠ Insufficient funds or R&D!");
            GUILayout.BeginHorizontal();
            GUI.enabled = afford;
            if (GUILayout.Button("Confirm", GUILayout.Width(100)))
            {
                ExecuteTraining(trainK, tgt, fCost, rCost);
                _showTrainConfirm = false;
                _pendingTrainKerbal = null;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Cancel", GUILayout.Width(100)))
            {
                _showTrainConfirm = false;
                _pendingTrainKerbal = null;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        // ── Training tab ───────────────────────────────────────────────────────
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
                GUILayout.Label(k.name,                                  GUILayout.Width(150));
                GUILayout.Label($"L{(int)k.experienceLevel}",            GUILayout.Width(35));
                GUILayout.Label(lbl,                                     GUILayout.Width(200));
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
                int    tgtT  = (int)k.experienceLevel + 1;
                double fc    = TrainingFundsCost(hire, tgtT);
                double rc    = TrainingRDCost(tgtT);
                bool   afford = funds >= fc && rd >= rc;
                int    baseD = tgtT * RosterRotationState.TrainingStarDays;
                int    maxD  = (int)(baseD * 1.5);

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name,           GUILayout.Width(140));
                GUILayout.Label($"L{(int)k.experienceLevel}→L{tgtT}", GUILayout.Width(70));
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
            var    roster = HighLogic.CurrentGame?.CrewRoster;
            double now    = Planetarium.GetUniversalTime();

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_acOverlay == AcOverlay.Applicants,  "📋 Applicants",    "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.Applicants;
            if (GUILayout.Toggle(_acOverlay == AcOverlay.Training,    "🎓 Send Training", "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.Training;
            if (GUILayout.Toggle(_acOverlay == AcOverlay.ForceRetire, "🚪 Force Retire",  "Button", GUILayout.Width(160))) _acOverlay = AcOverlay.ForceRetire;

            bool   hallAvailable = HallOfHistoryWindow.IsAvailable;
            bool   hallOpen      = HallOfHistoryWindow.IsOpen;
            GUI.enabled = hallAvailable;
            string hallLabel = hallOpen ? "🏛 Close Hall" : "🏛 Open Hall";
            if (GUILayout.Button(hallLabel, GUILayout.Width(160)))
            {
                if (hallOpen) HallOfHistoryWindow.HideWindow();
                else          HallOfHistoryWindow.ShowWindow(true);
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

            if      (_acOverlay == AcOverlay.Applicants)  DrawApplicantsOverlay(roster);
            else if (_acOverlay == AcOverlay.Training)    DrawTrainingOverlay(roster, now);
            else if (_acOverlay == AcOverlay.ForceRetire) DrawRetireOverlay(roster, now);

            GUI.DragWindow();
        }

        private void DrawApplicantsOverlay(KerbalRoster roster)
        {
            int  activeCount = GetActiveNonRetiredCount();
            int  maxCrew     = GetMaxCrew();
            bool atCap       = activeCount >= maxCrew;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name",      GUILayout.Width(200));
            GUILayout.Label("Skill",     GUILayout.Width(100));
            GUILayout.Label("Courage",   GUILayout.Width(80));
            GUILayout.Label("Stupidity", GUILayout.Width(80));
            GUILayout.Label("",          GUILayout.Width(160));
            GUILayout.EndHorizontal();
            DrawHRule();

            var applicants = GetApplicantsCached(roster);
            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(300));
            foreach (var k in applicants)
            {
                if (k == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name,              GUILayout.Width(200));
                GUILayout.Label(k.trait,             GUILayout.Width(100));
                GUILayout.Label($"{k.courage:P0}",   GUILayout.Width(80));
                GUILayout.Label($"{k.stupidity:P0}", GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                GUI.enabled = !atCap;
                if (GUILayout.Button("Hire", GUILayout.Width(70)))
                {
                    k.type         = ProtoCrewMember.KerbalType.Crew;
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
                var all = roster.Applicants;
                foreach (var k in all) RejectApplicant(roster, k);
                InvalidateUICaches();
                ACPatches.ForceRefresh();
                ACPatches.ForceRefreshApplicants();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawTrainingOverlay(KerbalRoster roster, double now)
        {
            double hire  = GetNextHireCost();
            double funds = Funding.Instance?.Funds ?? 0;
            double rd    = ResearchAndDevelopment.Instance?.Science ?? 0;

            GUILayout.Label($"Funds: √{funds:N0}   R&D: {rd:N0}   Next Hire Base: √{hire:N0}");
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name",       GUILayout.Width(180));
            GUILayout.Label("Skill",      GUILayout.Width(90));
            GUILayout.Label("Level",      GUILayout.Width(50));
            GUILayout.Label("Funds Cost", GUILayout.Width(110));
            GUILayout.Label("R&D Cost",   GUILayout.Width(80));
            GUILayout.Label("Duration",   GUILayout.Width(80));
            GUILayout.Label("",           GUILayout.Width(140));
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
                GUILayout.Label($"⏳ {k.name}",                           GUILayout.Width(180));
                GUILayout.Label(k.trait,                                  GUILayout.Width(90));
                GUILayout.Label($"L{(int)k.experienceLevel}",             GUILayout.Width(50));
                GUILayout.Label(TrainingLabel(r.Training, r.TrainingTargetLevel), GUILayout.Width(110));
                GUILayout.Label("",                                       GUILayout.Width(80));
                GUILayout.Label(RosterRotationState.FormatCountdown(rem), GUILayout.Width(80));
                GUILayout.Label("In Training",                            GUILayout.Width(140));
                GUILayout.EndHorizontal();
            }

            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(250));
            var candidates = GetTrainingCandidatesCached(roster);
            foreach (var k in candidates)
            {
                int    tgt      = (int)k.experienceLevel + 1;
                double fc       = TrainingFundsCost(hire, tgt);
                double rc       = TrainingRDCost(tgt);
                bool   afford   = funds >= fc && rd >= rc;
                int    baseDays = tgt * 30;
                int    maxDays  = (int)(baseDays * 1.5);

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name,    GUILayout.Width(180));
                GUILayout.Label(k.trait,   GUILayout.Width(90));
                GUILayout.Label($"L{(int)k.experienceLevel}→L{tgt}", GUILayout.Width(50));
                GUILayout.Label($"√{fc:N0}", GUILayout.Width(110));
                GUILayout.Label($"{rc:N0}",  GUILayout.Width(80));
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
            GUILayout.Label("Name",    GUILayout.Width(200));
            GUILayout.Label("Skill",   GUILayout.Width(100));
            GUILayout.Label("Level",   GUILayout.Width(60));
            GUILayout.Label("Flights", GUILayout.Width(70));
            GUILayout.Label("Status",  GUILayout.Width(200));
            GUILayout.Label("",        GUILayout.Width(90));
            GUILayout.EndHorizontal();
            DrawHRule();

            _overlayScroll = GUILayout.BeginScrollView(_overlayScroll, GUILayout.MinHeight(300));
            var crew = GetRetireRowsCached(roster, now);

            foreach (var row in crew)
            {
                var  k          = row.Kerbal;
                var  r          = row.Record;
                bool onMission  = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                bool inTraining = r?.Training != TrainingType.None && k.inactive;

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name,            GUILayout.Width(200));
                GUILayout.Label(k.trait,           GUILayout.Width(100));
                GUILayout.Label($"L{(int)k.experienceLevel}", GUILayout.Width(60));
                GUILayout.Label($"{row.DisplayFlights}", GUILayout.Width(50));
                GUILayout.Label(row.AgeText,       GUILayout.Width(55));
                GUILayout.Label(row.Status,        GUILayout.Width(170));
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
    }
}

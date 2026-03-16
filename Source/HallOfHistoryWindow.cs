using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using KSP;
using KSP.UI.Screens;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public partial class HallOfHistoryWindow : MonoBehaviour
    {
        private const string WindowTitle = "Hall of History";
        private const float CachePollSeconds = 2.5f;

        private static HallOfHistoryWindow _instance;

        public static bool IsAvailable { get { return _instance != null; } }
        public static bool IsOpen { get { return _instance != null && _instance._show; } }

        public static void ShowWindow(bool refresh = true)
        {
            if (_instance != null)
                _instance.ShowWindowInternal(refresh);
        }

        public static void HideWindow()
        {
            if (_instance != null)
                _instance.HideWindowInternal();
        }

        public static void ToggleWindow(bool refreshWhenOpening = true)
        {
            if (_instance == null)
                return;

            if (_instance._show)
                _instance.HideWindowInternal();
            else
                _instance.ShowWindowInternal(refreshWhenOpening);
        }

        private bool _show;
        private Rect _window = new Rect(720, 420, 1140, 720);

        private enum Tab
        {
            Memorial,
            Milestones
        }

        private Tab _tab = Tab.Memorial;
        private Vector2 _memorialListScroll;
        private Vector2 _memorialDetailScroll;
        private Vector2 _milestoneListScroll;
        private Vector2 _milestoneDetailScroll;
        private float _nextRefreshRt;

        private readonly HallOfHistoryCache _cache = new HallOfHistoryCache();
        private MemorialEntry _selectedMemorial;
        private MilestoneEntry _selectedMilestone;

        private GUIStyle _headerStyle;
        private GUIStyle _subheaderStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _selectedCardStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _smallMutedStyle;
        private GUIStyle _nameStyle;
        private GUIStyle _pillStyle;
        private GUIStyle _rightStyle;
        private GUIStyle _wrapStyle;
        private GUIStyle _centerMutedStyle;
        private GUIStyle _plaqueBodyStyle;
        private GUIStyle _plaqueTitleStyle;
        private GUIStyle _milestoneCardTitleStyle;
        private GUIStyle _timelineHeaderStyle;
        private GUIStyle _quoteStyle;
        private GUIStyle _metaLabelStyle;
        private GUIStyle _metaValueStyle;
        private GUIStyle _badgeStyle;
        private bool _stylesReady;
        private GUIStyle _windowStyle;
        private readonly Dictionary<string, Texture> _portraitCache = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _textureReplacerPortraitKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _textureReplacerPortraitKeysReady;
        private static Material _portraitRenderMaterial;
        private static Texture2D _fallbackMalePortrait;
        private static Texture2D _fallbackFemalePortrait;
        private readonly Dictionary<string, Texture2D> _milestoneIconCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static Material PortraitRenderMaterial
        {
            get
            {
                if (_portraitRenderMaterial == null)
                {
                    try
                    {
                        GameObject prefab = AssetBase.GetPrefab("Instructor_Gene");
                        if (prefab != null)
                        {
                            KerbalInstructor instructor = prefab.GetComponent<KerbalInstructor>();
                            if (instructor != null)
                                _portraitRenderMaterial = instructor.PortraitRenderMaterial;
                        }
                    }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.cs:116", "Suppressed exception in HallOfHistoryWindow.cs:116", ex); }
                }

                return _portraitRenderMaterial;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
        }

        private void Start()
        {
            ForceRefresh();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            _portraitCache.Clear();
            _textureReplacerPortraitKeys.Clear();
            _textureReplacerPortraitKeysReady = false;
            ClearMilestoneIconCache();
        }

        private void ShowWindowInternal(bool refresh)
        {
            _show = true;
            if (refresh)
                RefreshDataIfNeeded(false);
        }

        private void HideWindowInternal()
        {
            _show = false;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup >= _nextRefreshRt)
            {
                _nextRefreshRt = Time.realtimeSinceStartup + CachePollSeconds;
                RefreshDataIfNeeded(false);
            }
        }

        private void OnGUI()
        {
            if (!_show) return;
            EnsureStyles();
            _window = GUILayout.Window(GetInstanceID() + 919191, _window, DrawWindow, WindowTitle, _windowStyle);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            // Opaque window background — KSP's default skin texture is semi-transparent,
            // so we replace it with a solid 1x1 texture of the same dark colour.
            var bgTex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            bgTex.SetPixel(0, 0, new Color(0.12f, 0.12f, 0.12f, 0.96f)); //changes opacity
            bgTex.Apply();
            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background   = bgTex;
            _windowStyle.onNormal.background = bgTex;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };

            _subheaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };

            _sectionHeaderStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(8, 8, 5, 5),
                margin = new RectOffset(4, 4, 6, 4)
            };

            _timelineHeaderStyle = new GUIStyle(_sectionHeaderStyle)
            {
                fontSize = 13
            };

            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(4, 4, 4, 4),
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };

            _selectedCardStyle = new GUIStyle(_cardStyle)
            {
                fontStyle = FontStyle.Bold
            };

            _detailStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(14, 14, 14, 14),
                margin = new RectOffset(6, 6, 6, 6),
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };

            _mutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.80f, 0.82f, 0.86f) }
            };

            _smallMutedStyle = new GUIStyle(_mutedStyle)
            {
                fontSize = 11
            };

            _centerMutedStyle = new GUIStyle(_smallMutedStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };

            _nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.96f, 0.95f, 0.78f) }
            };

            _plaqueTitleStyle = new GUIStyle(_nameStyle)
            {
                fontSize = 19,
                wordWrap = false,
                clipping = TextClipping.Clip
            };

            _milestoneCardTitleStyle = new GUIStyle(_nameStyle)
            {
                fontSize = 16,
                wordWrap = true,
                clipping = TextClipping.Overflow
            };

            _plaqueBodyStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(14, 14, 12, 12),
                margin = new RectOffset(2, 2, 4, 4),
                wordWrap = true
            };

            _quoteStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.94f, 0.93f, 0.82f) }
            };

            _metaLabelStyle = new GUIStyle(_smallMutedStyle)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };

            _metaValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };

            _pillStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(6, 6, 3, 3),
                margin = new RectOffset(2, 2, 2, 2),
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };

            _badgeStyle = new GUIStyle(_pillStyle)
            {
                margin = new RectOffset(0, 0, 0, 0)
            };

            _rightStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 12,
                wordWrap = false
            };

            _wrapStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = 12
            };
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            DrawHeaderBar();
            GUILayout.Space(6f);
            DrawToolbar();
            GUILayout.Space(6f);

            if (!string.IsNullOrEmpty(_cache.LastLoadError))
            {
                GUILayout.Label("Load warning: " + _cache.LastLoadError, _mutedStyle);
                GUILayout.Space(4f);
            }

            if (_tab == Tab.Memorial)
                DrawMemorialTab();
            else
                DrawMilestonesTab();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(100)))
                ForceRefresh();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(100)))
                HideWindowInternal();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void DrawHeaderBar()
        {
            GUILayout.BeginVertical(_detailStyle);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            GUILayout.Label(WindowTitle, _headerStyle);
            string subtitle = string.Format(
                CultureInfo.InvariantCulture,
                "{0} memorial entr{1}  •  {2} recorded first{3}",
                _cache.Memorials.Count,
                _cache.Memorials.Count == 1 ? "y" : "ies",
                _cache.Milestones.Count,
                _cache.Milestones.Count == 1 ? string.Empty : "s");
            GUILayout.Label(subtitle, _mutedStyle);
            GUILayout.Space(4f);
            GUILayout.Label(_cache.DataSummary, _smallMutedStyle);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            DrawFlagBlock(108, 64);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal();

            string memorialLabel = _tab == Tab.Memorial ? "[ Memorial Wall ]" : "Memorial Wall";
            string milestoneLabel = _tab == Tab.Milestones ? "[ Milestone Wall ]" : "Milestone Wall";

            if (GUILayout.Button(memorialLabel, GUILayout.Width(160)))
                _tab = Tab.Memorial;

            if (GUILayout.Button(milestoneLabel, GUILayout.Width(160)))
                _tab = Tab.Milestones;

            GUILayout.Space(8f);
            GUILayout.Label(_tab == Tab.Memorial ? "Viewing memorial archive" : "Viewing milestone archive", _mutedStyle);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawMemorialTab()
        {
            const float paneGap = 8f;
            float contentWidth = Mathf.Max(860f, _window.width - 24f);
            float leftPaneWidth = Mathf.Clamp(contentWidth * 0.40f, 410f, 500f);
            float rightPaneWidth = Mathf.Max(480f, contentWidth - leftPaneWidth - paneGap);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(leftPaneWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label("Remembered Kerbals", _subheaderStyle);
            GUILayout.Box(string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(1));

            _memorialListScroll = GUILayout.BeginScrollView(_memorialListScroll, false, true, GUILayout.ExpandHeight(true));
            if (_cache.Memorials.Count == 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("No memorial entries found yet.", _mutedStyle);
                GUILayout.Label("Expected data sources: ROSTER for death status, EAC records for memorial metadata, and FlightTracker for service time when available.", _smallMutedStyle);
            }
            else
            {
                foreach (var entry in _cache.Memorials)
                    DrawMemorialCard(entry);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(paneGap);

            GUILayout.BeginVertical(GUILayout.Width(rightPaneWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label("Plaque", _subheaderStyle);
            GUILayout.Box(string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            _memorialDetailScroll = GUILayout.BeginScrollView(_memorialDetailScroll, false, true, GUILayout.ExpandHeight(true));
            if (_selectedMemorial != null)
                DrawMemorialDetail(_selectedMemorial);
            else
                GUILayout.Label("Select a memorial entry to view its plaque.", _mutedStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawMilestonesTab()
        {
            const float paneGap = 8f;
            float contentWidth = Mathf.Max(860f, _window.width - 24f);
            float leftPaneWidth = Mathf.Clamp(contentWidth * 0.43f, 400f, 500f);
            float rightPaneWidth = Mathf.Max(470f, contentWidth - leftPaneWidth - paneGap);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(leftPaneWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label("Recorded Firsts", _subheaderStyle);
            GUILayout.Box(string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(1));

            _milestoneListScroll = GUILayout.BeginScrollView(_milestoneListScroll, false, true, GUILayout.ExpandHeight(true));
            if (_cache.Milestones.Count == 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("No milestone entries found yet.", _mutedStyle);
                GUILayout.Label("Expected source: SCENARIO { name = ProgressTracking } inside persistent.sfs.", _smallMutedStyle);
            }
            else
            {
                var dayCounts = _cache.Milestones
                    .GroupBy(m => string.IsNullOrEmpty(m.DayGroupLabel) ? "Archive" : m.DayGroupLabel)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

                string activeGroup = null;
                foreach (var entry in _cache.Milestones)
                {
                    string groupLabel = string.IsNullOrEmpty(entry.DayGroupLabel) ? "Archive" : entry.DayGroupLabel;
                    if (!string.Equals(activeGroup, groupLabel, StringComparison.Ordinal))
                    {
                        activeGroup = groupLabel;
                        int count;
                        if (!dayCounts.TryGetValue(groupLabel, out count))
                            count = 0;
                        GUILayout.Label(BuildMilestoneGroupLabel(groupLabel, count), _timelineHeaderStyle, GUILayout.ExpandWidth(true));
                    }

                    DrawMilestoneCard(entry);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(paneGap);

            GUILayout.BeginVertical(GUILayout.Width(rightPaneWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label("Archive Detail", _subheaderStyle);
            GUILayout.Box(string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            _milestoneDetailScroll = GUILayout.BeginScrollView(_milestoneDetailScroll, false, true, GUILayout.ExpandHeight(true));
            if (_selectedMilestone != null)
                DrawMilestoneDetail(_selectedMilestone);
            else
                GUILayout.Label("Select a milestone entry to view its details.", _mutedStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawMemorialCard(MemorialEntry entry)
        {
            bool selected = _selectedMemorial == entry;
            GUILayout.BeginHorizontal(selected ? _selectedCardStyle : _cardStyle);

            DrawPortraitBlock(entry.Name, entry.Role, 76, 98, true, entry.IsVeteran ? "VETERAN" : null);
            GUILayout.Space(10);

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(entry.Name, _nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22f));
            GUILayout.Space(6);
            GUILayout.Label(entry.StatusText, _pillStyle, GUILayout.Width(64));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(selected ? "Selected" : "View", GUILayout.Width(72)))
                _selectedMemorial = entry;
            GUILayout.EndHorizontal();

            GUILayout.Label(BuildRoleServiceLine(entry), _mutedStyle);

            GUILayout.Label(string.Format(
                CultureInfo.InvariantCulture,
                "{0}  •  {1} flight{2}  •  {3} first{4}  •  {5}",
                string.IsNullOrEmpty(entry.RecordedDateText) ? "Date unavailable" : entry.RecordedDateText,
                entry.Flights,
                entry.Flights == 1 ? string.Empty : "s",
                entry.WorldFirsts,
                entry.WorldFirsts == 1 ? string.Empty : "s",
                entry.HoursText), _smallMutedStyle);

            GUILayout.Space(4);
            GUILayout.Label(entry.Citation, _wrapStyle, GUILayout.Height(34));
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawMemorialDetail(MemorialEntry entry)
        {
            GUILayout.BeginVertical(_detailStyle);

            GUILayout.BeginHorizontal(_plaqueBodyStyle);
            DrawPortraitBlock(entry.Name, entry.Role, 118, 150, true, entry.IsVeteran ? "VETERAN" : null);
            GUILayout.Space(12f);

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label(entry.Name, _plaqueTitleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            GUILayout.Label(entry.StatusText, _badgeStyle, GUILayout.Width(60f));
            GUILayout.Space(8f);
            GUILayout.Label(BuildRoleServiceLine(entry), _mutedStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            GUILayout.Label(entry.StatusDetailText, _smallMutedStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(4f);
            GUILayout.Label(BuildMemorialSummary(entry), _metaValueStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.BeginVertical(GUILayout.Width(118f));
            DrawFlagBlock(110f, 66f, entry.FlagUrl);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("“" + entry.Citation + "”", _quoteStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(_plaqueBodyStyle, GUILayout.Width(235));
            GUILayout.Label("Final Record", _subheaderStyle);
            GUILayout.Space(4f);
            DrawPlaqueFact("Cause", entry.CauseText);
            DrawPlaqueFact("Recorded", entry.RecordedDateText);
            DrawPlaqueFact("Disposition", entry.StatusDetailText);
            DrawPlaqueFact("Age", entry.AgeText);
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            GUILayout.BeginVertical(_plaqueBodyStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Service Profile", _subheaderStyle);
            GUILayout.Space(4f);
            DrawPlaqueFact("Role", entry.Role);
            DrawPlaqueFact("Experience", entry.ExperienceStars);
            if (entry.RetiredExperienceLevel >= 0)
                DrawPlaqueFact("Retired At", entry.RetiredExperienceStars);
            DrawPlaqueFact("Flights", entry.Flights.ToString(CultureInfo.InvariantCulture));
            DrawPlaqueFact("World Firsts", entry.WorldFirsts.ToString(CultureInfo.InvariantCulture));
            DrawPlaqueFact("Service Time", entry.HoursText);
            DrawPlaqueFact("Archive Date", entry.RecordedDateText);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginVertical(_plaqueBodyStyle);
            GUILayout.Label("Crew Profile", _subheaderStyle);
            GUILayout.Space(4f);
            DrawStatBar("Courage", entry.Courage01, entry.CourageText);
            DrawStatBar("Stupidity", entry.Stupidity01, entry.StupidityText);
            GUILayout.EndVertical();

            if (!string.IsNullOrEmpty(entry.Notes))
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(_plaqueBodyStyle);
                GUILayout.Label("Notes", _subheaderStyle);
                GUILayout.Space(4f);
                GUILayout.Label(entry.Notes, _wrapStyle);
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();
        }

        private void DrawMilestoneCard(MilestoneEntry entry)
        {
            bool selected = _selectedMilestone == entry;
            GUILayout.BeginHorizontal(selected ? _selectedCardStyle : _cardStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(110f));
            DrawMilestoneIcon(entry, 66f, 66f);
            GUILayout.Space(10f);

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.MinHeight(88f));
            GUILayout.Label(entry.Title, _milestoneCardTitleStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(3f);

            GUILayout.BeginHorizontal();
            DrawMilestoneBadge(entry.CategoryText, 74f);
            GUILayout.Space(6f);
            DrawMilestonePill(entry.SecondaryPillText, 74f);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(selected ? "Selected" : "View", GUILayout.Width(72f)))
                _selectedMilestone = entry;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label(entry.Subtitle, _mutedStyle, GUILayout.ExpandWidth(true));

            if (!string.IsNullOrEmpty(entry.Description))
            {
                GUILayout.Space(3f);
                GUILayout.Label(entry.Description, _smallMutedStyle, GUILayout.ExpandWidth(true));
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawMilestoneDetail(MilestoneEntry entry)
        {
            GUILayout.BeginVertical(_detailStyle);

            GUILayout.BeginHorizontal(_plaqueBodyStyle);
            DrawMilestoneIcon(entry, 74, 74);
            GUILayout.Space(10f);
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(entry.Title, _plaqueTitleStyle, GUILayout.ExpandWidth(true));
            DrawMilestoneBadge(entry.CategoryText, 92f);
            if (!string.IsNullOrEmpty(entry.RecordValueText))
            {
                GUILayout.Space(6f);
                DrawMilestonePill(entry.RecordValueText, 92f, _badgeStyle);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(entry.Subtitle, _mutedStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label(entry.DateText, _smallMutedStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginVertical(_plaqueBodyStyle);
            GUILayout.Label("Archive Note", _subheaderStyle);
            GUILayout.Space(4f);
            GUILayout.Label(entry.Description, _wrapStyle);
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(_plaqueBodyStyle, GUILayout.Width(250f));
            GUILayout.Label("Archive Record", _subheaderStyle);
            GUILayout.Space(4f);
            DrawPlaqueFact("Date", entry.DateText);
            if (!string.IsNullOrEmpty(entry.RecordValueText))
                DrawPlaqueFact("Record Value", entry.RecordValueText);
            DrawPlaqueFact("Category", entry.CategoryText);
            DrawPlaqueFact("Type", entry.KindText);
            DrawPlaqueFact("Body", entry.BodyText);
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            GUILayout.BeginVertical(_plaqueBodyStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Mission Context", _subheaderStyle);
            GUILayout.Space(4f);
            DrawPlaqueFact("Vessel", entry.VesselText);
            DrawPlaqueFact("Crew", entry.CrewSummaryText);
            DrawPlaqueFact("Crew Count", entry.CrewCount > 0 ? entry.CrewCount.ToString(CultureInfo.InvariantCulture) : "Uncrewed / not recorded");
            DrawPlaqueFact("Archive Path", entry.SourcePathText);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (entry.CrewCount > 0 && entry.CrewNames != null && entry.CrewNames.Length > 0)
            {
                GUILayout.Space(6f);
                GUILayout.BeginVertical(_plaqueBodyStyle);
                GUILayout.Label("Recorded Crew", _subheaderStyle);
                GUILayout.Space(4f);
                DrawMilestoneCrewRoster(entry);
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();
        }

        private void DrawMilestoneBadge(string text, float minWidth)
        {
            string value = string.IsNullOrEmpty(text) ? "Record" : text;
            float width = Mathf.Max(minWidth, 30f + (value.Length * 6f));
            GUILayout.Label(value, _pillStyle, GUILayout.Width(width));
        }

        private void DrawMilestonePill(string text, float minWidth, GUIStyle style = null)
        {
            if (string.IsNullOrEmpty(text))
                return;

            GUIStyle drawStyle = style ?? _pillStyle;
            float width = Mathf.Max(minWidth, 30f + (text.Length * 6f));
            GUILayout.Label(text, drawStyle, GUILayout.Width(width));
        }

        private void DrawMilestoneCrewRoster(MilestoneEntry entry)
        {
            if (entry == null || entry.CrewNames == null || entry.CrewNames.Length == 0)
                return;

            List<string> crewNames = entry.CrewNames
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (crewNames.Count == 0)
                return;

            const float cardWidth = 108f;
            const float portraitWidth = 82f;
            const float portraitHeight = 96f;
            int itemsPerRow = Mathf.Clamp(Mathf.FloorToInt((_window.width - 620f) / (cardWidth + 6f)), 1, 4);

            bool anyLinked = false;
            int index = 0;
            while (index < crewNames.Count)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < itemsPerRow && index < crewNames.Count; col++, index++)
                {
                    string crewName = crewNames[index];
                    MemorialEntry memorial = FindMemorialEntryByName(crewName);
                    if (memorial != null)
                        anyLinked = true;

                    DrawMilestoneCrewPortraitCard(crewName, memorial, cardWidth, portraitWidth, portraitHeight);

                    if (col < itemsPerRow - 1)
                        GUILayout.Space(6f);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (index < crewNames.Count)
                    GUILayout.Space(6f);
            }

            GUILayout.Space(3f);
            GUILayout.Label("Recorded crew portraits use the same portrait cache and fallback rules as the Memorial Wall.", _smallMutedStyle);

            if (anyLinked)
            {
                GUILayout.Space(2f);
                GUILayout.Label("Memorial links appear below a portrait when that crew member is already on the Memorial Wall.", _smallMutedStyle);
            }
        }

        private void DrawMilestoneCrewPortraitCard(string crewName, MemorialEntry memorial, float cardWidth, float portraitWidth, float portraitHeight)
        {
            bool isVeteran = memorial != null ? memorial.IsVeteran : TryGetKerbalVeteranStatus(crewName);

            GUILayout.BeginVertical(_plaqueBodyStyle, GUILayout.Width(cardWidth), GUILayout.MinHeight(166f));
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawCrewPortraitTile(crewName, portraitWidth, portraitHeight, isVeteran ? "VETERAN" : null);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label(crewName, _centerMutedStyle, GUILayout.Width(cardWidth - 12f));

            if (memorial != null)
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Memorial", GUILayout.Width(78f)))
                {
                    _selectedMemorial = memorial;
                    _tab = Tab.Memorial;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawCrewPortraitTile(string kerbalName, float w, float h, string badgeText = null)
        {
            Rect rect = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            Texture tex = TryGetPortraitTexture(kerbalName);

            GUI.Box(rect, GUIContent.none);
            if (tex != null)
            {
                DrawPortraitTexture(rect, tex);
            }
            else
            {
                DrawFallbackPortrait(rect, kerbalName, null);
            }

            if (!string.IsNullOrEmpty(badgeText))
                DrawPortraitBanner(rect, badgeText);
        }

        private MemorialEntry FindMemorialEntryByName(string crewName)
        {
            if (string.IsNullOrEmpty(crewName) || _cache == null || _cache.Memorials == null)
                return null;

            return _cache.Memorials.FirstOrDefault(x =>
                !string.IsNullOrEmpty(x.Name) &&
                string.Equals(x.Name, crewName, StringComparison.OrdinalIgnoreCase));
        }
        private bool TryGetKerbalVeteranStatus(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName) || HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null)
                return false;

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

                    return ReadProtoVeteran(pcm);
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.cs:886", "Suppressed exception in HallOfHistoryWindow.cs:886", ex); }

            return false;
        }


        private static string BuildMilestoneGroupLabel(string groupLabel, int count)
        {
            string label = string.IsNullOrEmpty(groupLabel) ? "Archive" : groupLabel;
            if (count <= 1)
                return label;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}  •  {1} record{2}",
                label,
                count,
                count == 1 ? string.Empty : "s");
        }

        private void DrawDetailRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _subheaderStyle, GUILayout.Width(110));
            GUILayout.Label(string.IsNullOrEmpty(value) ? "Unknown" : value, _wrapStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawPlaqueFact(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _metaLabelStyle, GUILayout.Width(88));
            GUILayout.Label(string.IsNullOrEmpty(value) ? "Unknown" : value, _metaValueStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        private static string BuildMemorialSummary(MemorialEntry entry)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Recorded {0}  •  {1} flight{2}  •  {3} first{4}  •  {5}",
                string.IsNullOrEmpty(entry.RecordedDateText) ? "date unavailable" : entry.RecordedDateText,
                entry.Flights,
                entry.Flights == 1 ? string.Empty : "s",
                entry.WorldFirsts,
                entry.WorldFirsts == 1 ? string.Empty : "s",
                entry.HoursText);
        }

        private static string BuildRoleServiceLine(MemorialEntry entry)
        {
            if (entry == null)
                return string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}  •  {1}  •  {2}",
                string.IsNullOrEmpty(entry.Role) ? "Kerbonaut" : entry.Role,
                string.IsNullOrEmpty(entry.ExperienceStars) ? "☆☆☆☆☆" : entry.ExperienceStars,
                string.IsNullOrEmpty(entry.ServiceTag) ? "Service record" : entry.ServiceTag);
        }

        private static string BuildExperienceStars(int level)
        {
            if (level < 0)
                return "☆☆☆☆☆";

            int clamped = Mathf.Clamp(level, 0, 5);
            return new string('★', clamped) + new string('☆', 5 - clamped);
        }


        private void DrawFlagBlock(float w, float h, string flagUrl = null)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Program Flag", _rightStyle, GUILayout.Width(w));
            Texture tex = null;
            string resolved = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flagURL : null;
            if (!string.IsNullOrEmpty(resolved))
                tex = GameDatabase.Instance.GetTexture(resolved, false);

            Rect rect = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            GUI.Box(rect, GUIContent.none);
            if (tex != null)
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
            else
                GUI.Label(rect, "NO FLAG", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndVertical();
        }

        private void DrawMilestoneIcon(MilestoneEntry entry, float w, float h)
        {
            Texture tex = GetMilestoneIconTexture(entry);
            Rect rect = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));

            if (tex != null)
            {
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
                return;
            }

            GUI.Box(rect, GUIContent.none);
            string glyph = GetMilestoneGlyph(FirstNonEmpty(entry.CategoryText, entry.KindText, entry.Title));
            GUI.Label(rect, glyph, new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(h * 0.40f),
                fontStyle = FontStyle.Bold
            });
        }

        private void ClearMilestoneIconCache()
        {
            if (_milestoneIconCache == null || _milestoneIconCache.Count == 0)
                return;

            foreach (var kvp in _milestoneIconCache)
            {
                try
                {
                    if (kvp.Value != null)
                        UnityEngine.Object.Destroy(kvp.Value);
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.cs:1010", "Suppressed exception in HallOfHistoryWindow.cs:1010", ex); }
            }

            _milestoneIconCache.Clear();
        }

        private Texture GetMilestoneIconTexture(MilestoneEntry entry)
        {
            string key = ResolveMilestoneIconKey(entry);
            if (string.IsNullOrEmpty(key))
                key = "Record";

            Texture2D cached;
            if (_milestoneIconCache.TryGetValue(key, out cached))
                return cached;

            Texture2D tex = LoadFirstExistingTexture(GetMilestoneIconPaths(key));
            if (tex != null)
                _milestoneIconCache[key] = tex;

            return tex;
        }

        private static IEnumerable<string> GetMilestoneIconPaths(string key)
        {
            string root = KSPUtil.ApplicationRootPath;
            if (string.IsNullOrEmpty(root))
                yield break;

            string normalized = NormalizeMilestoneIconKey(key);
            if (string.IsNullOrEmpty(normalized))
                normalized = "Record";

            yield return Path.Combine(root, "GameData", "EAC", "PluginData", "HallMilestone_" + normalized + ".png");
            yield return Path.Combine(root, "GameData", "EAC", "PluginData", "Milestone_" + normalized + ".png");
            yield return Path.Combine(root, "GameData", "EAC", "PluginData", normalized + ".png");
            yield return Path.Combine(root, "GameData", "EAC", "PluginData", normalized.ToLowerInvariant() + ".png");
        }

        private static string NormalizeMilestoneIconKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            }

            return sb.ToString();
        }

        private static string ResolveMilestoneIconKey(MilestoneEntry entry)
        {
            string key = string.Join(" ", new[]
            {
                FirstNonEmpty(entry != null ? entry.CategoryText : null, string.Empty),
                FirstNonEmpty(entry != null ? entry.KindText : null, string.Empty),
                FirstNonEmpty(entry != null ? entry.Title : null, string.Empty),
                FirstNonEmpty(entry != null ? entry.BodyText : null, string.Empty)
            }).ToLowerInvariant();

            if (key.Contains("launch") || key.Contains("space") || key.Contains("suborbital"))
                return "Launch";
            if (key.Contains("orbit"))
                return "Orbit";
            if (key.Contains("landing") || key.Contains("surface") || key.Contains("splash") || key.Contains("touchdown"))
                return "Landing";
            if (key.Contains("flyby"))
                return "Flyby";
            if (key.Contains("return") || key.Contains("recover") || key.Contains("survive") || key.Contains("home"))
                return "Return";
            if (key.Contains("rendezvous") || key.Contains("dock") || key.Contains("docking"))
                return "Rendezvous";
            if (key.Contains("eva") || key.Contains("spacewalk"))
                return "EVA";
            if (key.Contains("science") || key.Contains("sample") || key.Contains("research"))
                return "Science";
            if (key.Contains("crew"))
                return "Crewed";
            if (key.Contains("station") || key.Contains("base") || key.Contains("outpost") || key.Contains("flag"))
                return "Mission";
            if (key.Contains("mun") || key.Contains("minmus") || key.Contains("duna") || key.Contains("eve") ||
                key.Contains("moho") || key.Contains("jool") || key.Contains("laythe") || key.Contains("vall") ||
                key.Contains("bop") || key.Contains("pol") || key.Contains("gilly") || key.Contains("ike") ||
                key.Contains("dres") || key.Contains("eeloo") || key.Contains("kerbin"))
                return "Exploration";

            return "Record";
        }

        private static string GetMilestoneGlyph(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "*";
            kind = kind.ToLowerInvariant();
            if (kind.Contains("launch") || kind.Contains("space") || kind.Contains("suborbital")) return "^";
            if (kind.Contains("orbit")) return "O";
            if (kind.Contains("landing") || kind.Contains("surface") || kind.Contains("splash")) return "[]";
            if (kind.Contains("flyby")) return ">";
            if (kind.Contains("return") || kind.Contains("recover") || kind.Contains("survive")) return "<";
            if (kind.Contains("rendezvous") || kind.Contains("dock")) return "<>";
            if (kind.Contains("eva")) return "E";
            if (kind.Contains("science") || kind.Contains("sample")) return "S";
            if (kind.Contains("crew")) return "+";
            if (kind.Contains("station") || kind.Contains("base") || kind.Contains("flag")) return "#";
            return "*";
        }

        private void DrawStatBar(string label, float value01, string text)
        {
            DrawPlaqueFact(label, text);
        }

        private void ForceRefresh()
        {
            RefreshDataIfNeeded(true);
        }

        private void RefreshDataIfNeeded(bool force)
        {
            string path = GetPersistentPath();
            DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (!force && _cache.IsFreshFor(path, stamp)) return;

            _cache.Clear();
            _portraitCache.Clear();
            _cache.LastPath = path;
            _cache.LastWriteUtc = stamp;

            try
            {
                BuildData(path);
            }
            catch (Exception ex)
            {
                _cache.LastLoadError = ex.GetType().Name + ": " + ex.Message;
                Debug.LogError("[EAC.HallOfHistory] Failed to load data: " + ex);
            }

            if (_selectedMemorial != null)
                _selectedMemorial = _cache.Memorials.FirstOrDefault(x => x.Name == _selectedMemorial.Name) ?? _cache.Memorials.FirstOrDefault();
            else
                _selectedMemorial = _cache.Memorials.FirstOrDefault();

            if (_selectedMilestone != null)
                _selectedMilestone = _cache.Milestones.FirstOrDefault(x => x.Key == _selectedMilestone.Key) ?? _cache.Milestones.FirstOrDefault();
            else
                _selectedMilestone = _cache.Milestones.FirstOrDefault();
        }

        private string GetPersistentPath()
        {
            return Path.Combine(KSPUtil.ApplicationRootPath, "saves", HighLogic.SaveFolder, "persistent.sfs");
        }

        private void BuildData(string persistentPath)
        {
            ConfigNode saveRoot = null;
            if (File.Exists(persistentPath))
            {
                saveRoot = ConfigNode.Load(persistentPath);
                if (saveRoot == null)
                    _cache.LastLoadError = "persistent.sfs was found but could not be parsed.";
            }
            else
            {
                _cache.LastLoadError = "persistent.sfs not found at " + persistentPath;
            }

            var recordMap = BuildEacRecordMap(saveRoot);
            var rosterMap = BuildRosterMap(saveRoot);
            var liveMap = BuildLiveCrewMap();

            BuildMilestones(saveRoot);
            BuildMemorials(recordMap, rosterMap, liveMap);

            string ftMsg = FlightTrackerShim.Available
                ? "FlightTracker detected: flights and mission hours merged where possible."
                : "FlightTracker not detected: memorials use roster first, then EAC metadata.";
            _cache.DataSummary = ftMsg;
        }

        private Dictionary<string, EacRecordSnapshot> BuildEacRecordMap(ConfigNode saveRoot)
        {
            var map = new Dictionary<string, EacRecordSnapshot>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var liveRecords = RosterRotationState.Records;
                if (liveRecords != null)
                {
                    foreach (var kv in liveRecords)
                    {
                        if (kv.Key == null) continue;
                        var snap = EacRecordSnapshot.FromLiveRecord(kv.Key, kv.Value);
                        if (snap != null)
                            map[kv.Key] = snap;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EAC.HallOfHistory] Live EAC record merge failed: " + ex.Message);
            }

            if (saveRoot == null) return map;

            foreach (var node in FindNodesRecursive(saveRoot, "Record"))
            {
                var snap = EacRecordSnapshot.FromConfig(node);
                if (snap == null || string.IsNullOrEmpty(snap.Name))
                    continue;

                if (!map.ContainsKey(snap.Name) || snap.ScoreForCompleteness() > map[snap.Name].ScoreForCompleteness())
                    map[snap.Name] = snap;
            }

            return map;
        }

        private Dictionary<string, ProtoCrewSnapshot> BuildRosterMap(ConfigNode saveRoot)
        {
            var map = new Dictionary<string, ProtoCrewSnapshot>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.CrewRoster != null)
                {
                    foreach (var pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                    {
                        var snap = ProtoCrewSnapshot.FromProto(pcm);
                        if (snap != null && !string.IsNullOrEmpty(snap.Name))
                            map[snap.Name] = snap;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EAC.HallOfHistory] Live crew roster merge failed: " + ex.Message);
            }

            if (saveRoot == null) return map;

            foreach (var roster in FindNodesRecursive(saveRoot, "ROSTER"))
            {
                foreach (ConfigNode kerbal in roster.GetNodes("KERBAL"))
                {
                    var snap = ProtoCrewSnapshot.FromConfig(kerbal);
                    if (snap == null || string.IsNullOrEmpty(snap.Name))
                        continue;

                    ProtoCrewSnapshot existing;
                    if (map.TryGetValue(snap.Name, out existing))
                        map[snap.Name] = MergeRosterSnapshots(existing, snap);
                    else
                        map[snap.Name] = snap;
                }
            }

            return map;
        }

        private static ProtoCrewSnapshot MergeRosterSnapshots(ProtoCrewSnapshot live, ProtoCrewSnapshot saved)
        {
            if (live == null) return saved;
            if (saved == null) return live;

            return new ProtoCrewSnapshot
            {
                Name = !string.IsNullOrEmpty(saved.Name) ? saved.Name : live.Name,
                TraitTitle = !string.IsNullOrEmpty(saved.TraitTitle) ? saved.TraitTitle : live.TraitTitle,
                IsDead = saved.IsDead || live.IsDead,
                Flights = saved.Flights >= 0 ? saved.Flights : live.Flights,
                ExperienceLevel = saved.ExperienceLevel >= 0 ? saved.ExperienceLevel : live.ExperienceLevel,
                Courage = !float.IsNaN(saved.Courage) ? saved.Courage : live.Courage,
                Stupidity = !float.IsNaN(saved.Stupidity) ? saved.Stupidity : live.Stupidity,
                UT = saved.UT >= 0d ? saved.UT : live.UT,
                FlagUrl = !string.IsNullOrEmpty(saved.FlagUrl) ? saved.FlagUrl : live.FlagUrl,
                IsVeteran = saved.IsVeteran || live.IsVeteran
            };
        }

        private Dictionary<string, ProtoCrewSnapshot> BuildLiveCrewMap()
        {
            var map = new Dictionary<string, ProtoCrewSnapshot>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.CrewRoster != null)
                {
                    foreach (var pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                    {
                        var snap = ProtoCrewSnapshot.FromProto(pcm);
                        if (snap != null && !string.IsNullOrEmpty(snap.Name))
                        {
                            if (string.IsNullOrEmpty(snap.FlagUrl))
                                snap.FlagUrl = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flagURL : null;
                            map[snap.Name] = snap;
                        }
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.cs:1315", "Suppressed exception in HallOfHistoryWindow.cs:1315", ex); }

            return map;
        }

        private void BuildMemorials(
            Dictionary<string, EacRecordSnapshot> recordMap,
            Dictionary<string, ProtoCrewSnapshot> rosterMap,
            Dictionary<string, ProtoCrewSnapshot> liveMap)
        {
            var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in recordMap.Keys) allNames.Add(k);
            foreach (var k in rosterMap.Keys) allNames.Add(k);
            foreach (var k in liveMap.Keys) allNames.Add(k);

            foreach (string name in allNames)
            {
                EacRecordSnapshot eac;
                ProtoCrewSnapshot roster;
                ProtoCrewSnapshot live;
                recordMap.TryGetValue(name, out eac);
                rosterMap.TryGetValue(name, out roster);
                liveMap.TryGetValue(name, out live);

                if (!IsMemorialCandidate(eac, roster, live))
                    continue;

                var entry = new MemorialEntry();
                entry.Name = name;

                entry.Role = FirstNonEmpty(
                    roster != null ? roster.TraitTitle : null,
                    live != null ? live.TraitTitle : null,
                    eac != null ? eac.TraitTitle : null,
                    "Kerbonaut");

                entry.StatusText = ResolveStatusPill(eac, roster, live);
                entry.StatusDetailText = ResolveStatusDetail(eac, roster, live);
                entry.ServiceTag = FirstNonEmpty(
                    eac != null ? eac.ServiceTag : null,
                    "Service record");

                entry.CauseText = FirstNonEmpty(
                    eac != null ? eac.CauseOfDeath : null,
                    IsRetiredDeath(eac) ? "Died after retirement" : null,
                    IsKilledInAction(eac) ? "Killed in action" : null,
                    "Not recorded");

                entry.RecordedDateText = BuildRecordedDateText(eac, roster, live);
                entry.FlagUrl = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.flagURL : null;
                entry.ExperienceLevel = ResolveExperienceLevel(eac, roster, live);
                entry.ExperienceStars = BuildExperienceStars(entry.ExperienceLevel);
                entry.RetiredExperienceLevel = ShouldShowRetiredExperience(entry, eac)
                    ? ResolveRetiredBaselineExperienceLevel(eac)
                    : -1;
                entry.RetiredExperienceStars = entry.RetiredExperienceLevel >= 0
                    ? BuildExperienceStars(entry.RetiredExperienceLevel)
                    : null;
                entry.WorldFirsts = CountWorldFirstsForKerbal(name);

                entry.Courage01 = Mathf.Clamp01(FirstFloat(
                    roster != null ? roster.Courage : float.NaN,
                    live != null ? live.Courage : float.NaN,
                    eac != null ? eac.Courage : float.NaN,
                    0.5f));
                entry.Stupidity01 = Mathf.Clamp01(FirstFloat(
                    roster != null ? roster.Stupidity : float.NaN,
                    live != null ? live.Stupidity : float.NaN,
                    eac != null ? eac.Stupidity : float.NaN,
                    0.5f));
                entry.CourageText = Mathf.RoundToInt(entry.Courage01 * 100f).ToString(CultureInfo.InvariantCulture) + " %";
                entry.StupidityText = Mathf.RoundToInt(entry.Stupidity01 * 100f).ToString(CultureInfo.InvariantCulture) + " %";

                int flights = 0;
                if (roster != null && roster.Flights >= 0)
                    flights = roster.Flights;
                else if (live != null && live.Flights >= 0)
                    flights = live.Flights;
                else
                {
                    int ftFlights = FlightTrackerShim.GetFlightCount(name);
                    if (ftFlights > 0)
                        flights = ftFlights;
                    else if (eac != null && eac.Flights > 0)
                        flights = eac.Flights;
                }
                entry.Flights = flights;

                double hours = FlightTrackerShim.GetHours(name);
                if (hours < 0d && eac != null && eac.MissionHours > 0d)
                    hours = eac.MissionHours;
                entry.HoursText = hours > 0d ? FormatHours(hours) : "service time unavailable";

                if (eac != null && eac.BirthUT >= 0d && eac.DeathUT >= eac.BirthUT)
                {
                    entry.Age = CalculateAgeYears(eac.BirthUT, eac.DeathUT);
                    entry.AgeText = entry.Age.ToString(CultureInfo.InvariantCulture) + " years";
                }
                else if (eac != null && eac.LastAgedYears >= 0)
                {
                    entry.Age = eac.LastAgedYears;
                    entry.AgeText = entry.Age.ToString(CultureInfo.InvariantCulture) + " years";
                }
                else
                {
                    entry.Age = -1;
                    entry.AgeText = "Unknown";
                }

                entry.IsVeteran = (roster != null && roster.IsVeteran) || (live != null && live.IsVeteran);
                entry.Citation = BuildMemorialCitation(entry, eac);
                entry.Notes = BuildMemorialNotes(eac);

                _cache.Memorials.Add(entry);
            }

            _cache.Memorials.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private void BuildMilestones(ConfigNode saveRoot)
        {
            if (saveRoot == null) return;

            var scenarioNodes = FindNodesRecursive(saveRoot, "SCENARIO");
            foreach (var scn in scenarioNodes)
            {
                string scnName = ReadValue(scn, "name", "Name", "moduleName");
                if (!string.Equals(scnName, "ProgressTracking", StringComparison.OrdinalIgnoreCase))
                    continue;

                ConfigNode progress = scn.GetNode("Progress") ?? scn;
                ParseMilestoneChildren(progress, null, null, null);
            }

            _cache.Milestones = _cache.Milestones
                .Where(m => !string.IsNullOrEmpty(m.Title))
                .GroupBy(m => m.Key)
                .Select(g => g.OrderByDescending(x => x.SortValue).First())
                .OrderBy(x => x.SortValue)
                .ToList();
        }

        private void ParseMilestoneChildren(ConfigNode node, string inheritedBody, string inheritedKind, string inheritedPath)
        {
            if (node == null) return;

            foreach (ConfigNode child in node.GetNodes())
            {
                string body = inheritedBody;
                string kind = inheritedKind;
                string path = string.IsNullOrEmpty(inheritedPath) ? child.name : inheritedPath + "/" + child.name;

                if (LooksLikeCelestialBodyNode(child))
                    body = child.name;
                else
                    kind = child.name;

                MilestoneEntry entry;
                if (TryBuildMilestone(child, body, kind, path, out entry))
                    _cache.Milestones.Add(entry);

                ParseMilestoneChildren(child, body, kind, path);
            }
        }

        private bool TryBuildMilestone(ConfigNode node, string body, string kind, string path, out MilestoneEntry entry)
        {
            entry = null;

            double when;
            if (!TryReadProgressState(node, out when))
                return false;

            string nodeName = node.name ?? string.Empty;
            string resolvedBody = FirstNonEmpty(body, ReadValue(node, "body", "Body", "bodyName", "celestialBody", "targetBody", "TargetBody", "planet"));
            string vessel = ReadValue(node, "vessel", "vesselName", "VesselName", "shipName", "ship");
            List<string> crewNames = ReadCrewNames(node);
            string crew = crewNames.Count == 0 ? null : string.Join(", ", crewNames.ToArray());
            int crewCount = crewNames.Count;
            string title = HumanizeMilestoneName(nodeName, kind, resolvedBody);
            string category = BuildMilestoneCategory(nodeName, kind, resolvedBody, vessel, crew);
            string recordValueText = BuildMilestoneRecordValueText(node, nodeName, kind, path);
            string desc = BuildMilestoneDescription(title, resolvedBody, vessel, crew, category, recordValueText);

            int year;
            int day;
            GetKspYearDay(when, out year, out day);

            entry = new MilestoneEntry
            {
                Key = path,
                Title = title,
                Subtitle = BuildMilestoneSubtitle(resolvedBody, vessel, crew, crewCount),
                Description = desc,
                DateText = BuildDayGroupLabel(year, day),
                TimeText = null,
                RecordValueText = recordValueText,
                SecondaryPillText = !string.IsNullOrEmpty(recordValueText) ? recordValueText : BuildCompactDayLabel(year, day),
                SortValue = when,
                BodyText = string.IsNullOrEmpty(resolvedBody) ? "Kerbol system" : resolvedBody,
                VesselText = string.IsNullOrEmpty(vessel) ? "Unknown" : vessel,
                CrewText = string.IsNullOrEmpty(crew) ? "Uncrewed / not recorded" : crew,
                CrewNames = crewNames.ToArray(),
                CrewCount = crewCount,
                CrewSummaryText = BuildMilestoneCrewSummary(crew, crewCount),
                KindText = HumanizeMilestoneName(FirstNonEmpty(kind, nodeName, "Milestone"), null, null),
                CategoryText = category,
                SourcePathText = BuildMilestoneSourcePath(path),
                DayGroupLabel = BuildDayGroupLabel(year, day)
            };
            return true;
        }

        private static bool TryReadProgressState(ConfigNode node, out double when)
        {
            when = -1d;

            string[] timeKeys =
            {
                "completed", "reached", "ut", "UT", "time", "Time", "completionTime", "achievedUT"
            };

            foreach (string key in timeKeys)
            {
                double parsed;
                if (TryReadDouble(node, key, out parsed) && parsed >= 0d)
                {
                    when = parsed;
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeCelestialBodyNode(ConfigNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.name)) return false;
            string n = node.name;
            return FlightGlobals.Bodies.Any(b => string.Equals(b.bodyName, n, StringComparison.OrdinalIgnoreCase));
        }

        private static string HumanizeMilestoneName(string nodeName, string kind, string body)
        {
            string raw = !string.IsNullOrEmpty(nodeName) ? nodeName : kind;
            if (string.IsNullOrEmpty(raw)) raw = "Milestone";

            var sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '_' || c == '-')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                        sb.Append(' ');
                    continue;
                }

                bool addSpace = i > 0 && char.IsUpper(c) &&
                    (char.IsLower(raw[i - 1]) || (i + 1 < raw.Length && char.IsLower(raw[i + 1])));
                if (addSpace && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    sb.Append(' ');

                sb.Append(c);
            }

            return CollapseWhitespace(sb.ToString());
        }

        private static string BuildMilestoneSubtitle(string body, string vessel, string crew, int crewCount)
        {
            var bits = new List<string>();
            if (!string.IsNullOrEmpty(body)) bits.Add(body);
            if (!string.IsNullOrEmpty(vessel)) bits.Add(vessel);

            string crewSummary = BuildMilestoneCrewSummary(crew, crewCount);
            if (crewCount > 0 && !string.IsNullOrEmpty(crewSummary))
                bits.Add(crewSummary);

            return bits.Count == 0 ? "Historical record" : string.Join("  •  ", bits.ToArray());
        }

        private static string BuildMilestoneDescription(string title, string body, string vessel, string crew, string category, string recordValueText)
        {
            var sb = new StringBuilder();
            sb.Append(title).Append(" was recorded in the program archive");
            if (!string.IsNullOrEmpty(category) && !string.Equals(category, "Record", StringComparison.OrdinalIgnoreCase))
                sb.Append(" as a ").Append(category.ToLowerInvariant()).Append(" milestone");
            if (!string.IsNullOrEmpty(body)) sb.Append(" for ").Append(body);
            sb.Append('.');
            if (!string.IsNullOrEmpty(recordValueText)) sb.Append(" Record value: ").Append(recordValueText).Append('.');
            if (!string.IsNullOrEmpty(vessel)) sb.Append(" Vessel: ").Append(vessel).Append('.');
            if (!string.IsNullOrEmpty(crew)) sb.Append(" Crew: ").Append(crew).Append('.');
            return sb.ToString();
        }

        private static string BuildMilestoneCategory(string nodeName, string kind, string body, string vessel, string crew)
        {
            string key = FirstNonEmpty(kind, nodeName, string.Empty).ToLowerInvariant();
            if (key.Contains("launch")) return "Launch";
            if (key.Contains("orbit")) return "Orbit";
            if (key.Contains("landing") || key.Contains("surface") || key.Contains("splash")) return "Landing";
            if (key.Contains("flyby")) return "Flyby";
            if (key.Contains("return") || key.Contains("recover") || key.Contains("survive")) return "Return";
            if (key.Contains("rendezvous") || key.Contains("dock")) return "Rendezvous";
            if (key.Contains("eva")) return "EVA";
            if (key.Contains("science")) return "Science";
            if (key.Contains("crew") || !string.IsNullOrEmpty(crew)) return "Crewed";
            if (!string.IsNullOrEmpty(vessel)) return "Mission";
            if (!string.IsNullOrEmpty(body)) return "Exploration";
            return "Record";
        }

        private static string BuildMilestoneCrewSummary(string crew, int crewCount)
        {
            if (crewCount <= 0 || string.IsNullOrEmpty(crew))
                return "Uncrewed / not recorded";

            if (crewCount <= 2)
                return crew;

            string first = crew.Split(',').Select(x => x.Trim()).FirstOrDefault(x => !string.IsNullOrEmpty(x));
            if (string.IsNullOrEmpty(first))
                return crewCount.ToString(CultureInfo.InvariantCulture) + " crew";

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} +{1} crew",
                first,
                crewCount - 1);
        }

        private static string BuildMilestoneSourcePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "ProgressTracking";

            string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = HumanizeMilestoneName(parts[i], null, null);

            return string.Join("  ›  ", parts);
        }

        private static string BuildMilestoneRecordValueText(ConfigNode node, string nodeName, string kind, string path)
        {
            if (node == null)
                return null;

            double recordValue;
            if (!TryReadDouble(node, "record", out recordValue) || double.IsNaN(recordValue) || double.IsInfinity(recordValue) || recordValue < 0d)
                return null;

            string unit = GetMilestoneRecordUnit(nodeName, kind, path);
            return FormatMilestoneRecordValue(recordValue, unit);
        }

        private static string GetMilestoneRecordUnit(string nodeName, string kind, string path)
        {
            string key = (FirstNonEmpty(nodeName, string.Empty) + " " + FirstNonEmpty(kind, string.Empty) + " " + FirstNonEmpty(path, string.Empty)).ToLowerInvariant();
            if (key.Contains("speed")) return "m/s";
            if (key.Contains("altitude") || key.Contains("distance") || key.Contains("depth")) return "m";
            if (key.Contains("science")) return "science";
            return string.Empty;
        }

        private static string FormatMilestoneRecordValue(double value, string unit)
        {
            string numberText;
            double abs = Math.Abs(value);
            if (abs >= 100d)
                numberText = value.ToString("0.0", CultureInfo.InvariantCulture);
            else
                numberText = value.ToString("0.##", CultureInfo.InvariantCulture);

            return string.IsNullOrEmpty(unit) ? numberText : numberText + " " + unit;
        }

        private static string CollapseWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var sb = new StringBuilder(value.Length);
            bool previousWasSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace)
                {
                    if (!previousWasSpace)
                        sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }

                previousWasSpace = isSpace;
            }

            return sb.ToString().Trim();
        }

        private static List<string> ReadCrewNames(ConfigNode node)
        {
            var names = new List<string>();
            if (node == null)
                return names;

            foreach (ConfigNode crewNode in node.GetNodes("crew"))
            {
                foreach (ConfigNode.Value v in crewNode.values)
                {
                    if (v == null || string.IsNullOrEmpty(v.value))
                        continue;

                    foreach (string piece in v.value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = CollapseWhitespace(piece.Trim());
                        if (!string.IsNullOrEmpty(trimmed))
                            names.Add(trimmed);
                    }
                }
            }

            string inlineCrew = ReadValue(node, "crew", "crews", "kerbal", "kerbals");
            if (!string.IsNullOrEmpty(inlineCrew))
            {
                foreach (string piece in inlineCrew.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = CollapseWhitespace(piece.Trim());
                    if (!string.IsNullOrEmpty(trimmed))
                        names.Add(trimmed);
                }
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ReadCrewList(ConfigNode node)
        {
            List<string> names = ReadCrewNames(node);
            return names.Count == 0 ? null : string.Join(", ", names.ToArray());
        }

        private static bool IsMemorialCandidate(EacRecordSnapshot eac, ProtoCrewSnapshot roster, ProtoCrewSnapshot live)
        {
            if (roster != null)
                return roster.IsDead;

            if (live != null)
                return live.IsDead;

            return false;
        }

        private static string ResolveStatusPill(EacRecordSnapshot eac, ProtoCrewSnapshot roster, ProtoCrewSnapshot live)
        {
            bool isDead =
                (roster != null && roster.IsDead) ||
                (live != null && live.IsDead);

            if (!isDead)
                return "Archive";

            return IsKilledInAction(eac) ? "K.I.A." : "Dead";
        }

        private static string ResolveStatusDetail(EacRecordSnapshot eac, ProtoCrewSnapshot roster, ProtoCrewSnapshot live)
        {
            bool isDead =
                (roster != null && roster.IsDead) ||
                (live != null && live.IsDead);

            if (!isDead)
                return "Honored in archive";

            if (IsKilledInAction(eac))
                return "Killed in action";

            if (IsRetiredDeath(eac))
                return "Died after retirement";

            return "Deceased";
        }

        private static bool IsKilledInAction(EacRecordSnapshot eac)
        {
            return eac != null && HallOfHistoryRules.IsKilledInAction(eac.Retired, eac.RetiredUT, eac.DeathUT);
        }

        private static bool IsRetiredDeath(EacRecordSnapshot eac)
        {
            return eac != null && HallOfHistoryRules.IsRetiredDeath(eac.Retired, eac.RetiredUT, eac.DeathUT);
        }

        private static string BuildRecordedDateText(EacRecordSnapshot eac, ProtoCrewSnapshot roster, ProtoCrewSnapshot live)
        {
            double when = HallOfHistoryRules.ResolveRecordedUt(
                eac != null ? eac.DeathUT : -1d,
                eac != null ? eac.LastSeenUT : -1d,
                roster != null ? roster.UT : -1d,
                live != null ? live.UT : -1d);
            return when > 0d ? FormatDate(when) : "Unknown";
        }

        private static string BuildMemorialCitation(MemorialEntry entry, EacRecordSnapshot eac)
        {
            if (!string.IsNullOrEmpty(eac != null ? eac.Citation : null))
                return eac.Citation;

            return string.Format(
                CultureInfo.InvariantCulture,
                "Remembered as a {0} and a member of the program. Their name is kept here so every launch carries some memory of the crews who came before.",
                string.IsNullOrEmpty(entry.Role) ? "kerbonaut" : entry.Role.ToLowerInvariant());
        }

        private static string BuildMemorialNotes(EacRecordSnapshot eac)
        {
            if (eac == null) return null;
            return FirstNonEmpty(eac.Notes, eac.LastMission);
        }

        private int CountWorldFirstsForKerbal(string kerbalName)
        {
            if (string.IsNullOrEmpty(kerbalName) || _cache == null || _cache.Milestones == null)
                return 0;

            int count = 0;
            foreach (var milestone in _cache.Milestones)
            {
                if (milestone == null || string.IsNullOrEmpty(milestone.CrewText))
                    continue;

                if (ContainsCrewName(milestone.CrewText, kerbalName))
                    count++;
            }

            return count;
        }

        private static bool ContainsCrewName(string crewText, string kerbalName)
        {
            if (string.IsNullOrEmpty(crewText) || string.IsNullOrEmpty(kerbalName))
                return false;

            return crewText.IndexOf(kerbalName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ResolveExperienceLevel(EacRecordSnapshot eac, ProtoCrewSnapshot roster, ProtoCrewSnapshot live)
        {
            if (IsRetiredDeath(eac))
                return ResolveRetiredEffectiveExperienceLevel(eac, eac.DeathUT, roster, live);

            if (eac != null && eac.Retired && eac.RetiredUT > 0d && eac.DeathUT <= 0d)
                return ResolveRetiredEffectiveExperienceLevel(eac, Planetarium.GetUniversalTime(), roster, live);

            return ResolveActiveExperienceLevel(eac, roster, live);
        }

        private static int ResolveActiveExperienceLevel(EacRecordSnapshot eac, ProtoCrewSnapshot roster, ProtoCrewSnapshot live)
        {
            if (roster != null && roster.ExperienceLevel >= 0)
                return roster.ExperienceLevel;
            if (live != null && live.ExperienceLevel >= 0)
                return live.ExperienceLevel;
            return -1;
        }

        private static int ResolveRetiredEffectiveExperienceLevel(EacRecordSnapshot eac, double whenUT, ProtoCrewSnapshot roster, ProtoCrewSnapshot live)
        {
            if (eac == null)
                return ResolveActiveExperienceLevel(null, roster, live);

            return HallOfHistoryRules.ResolveRetiredEffectiveExperienceLevel(
                ResolveRetiredBaselineExperienceLevel(eac),
                eac.RetiredUT,
                whenUT,
                ResolveActiveExperienceLevel(null, roster, live),
                GetKspYearSeconds());
        }

        private static int ResolveRetiredBaselineExperienceLevel(EacRecordSnapshot eac)
        {
            return eac != null ? eac.ExperienceAtRetire : -1;
        }

        private static bool ShouldShowRetiredExperience(MemorialEntry entry, EacRecordSnapshot eac)
        {
            return entry != null && HallOfHistoryRules.ShouldShowRetiredExperience(IsRetiredDeath(eac), ResolveRetiredBaselineExperienceLevel(eac));
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrEmpty(values[i]))
                    return values[i];
            return null;
        }

        private static float FirstFloat(float a, float b, float c, float fallback)
        {
            if (!float.IsNaN(a)) return a;
            if (!float.IsNaN(b)) return b;
            if (!float.IsNaN(c)) return c;
            return fallback;
        }

        private static string FormatHours(double hours)
        {
            if (hours < 0d) return "service time unavailable";
            return hours.ToString("0.0", CultureInfo.InvariantCulture) + " h";
        }

        private static string FormatDate(double ut)
        {
            try
            {
                return KSPUtil.PrintDate(ut, false, true);
            }
            catch
            {
                return "UT " + ut.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        private static double GetKspYearSeconds()
        {
            return KspTimeMath.GetDisplayYearSeconds(GetKerbinTimeSettingForHistory());
        }

        private static bool GetKerbinTimeSettingForHistory()
        {
            try
            {
                return GameSettings.KERBIN_TIME;
            }
            catch (global::System.Exception ex)
            {
                RRLog.VerboseExceptionOnce("HallOfHistoryWindow.GetKerbinTimeSettingForHistory", "Suppressed exception resolving GameSettings.KERBIN_TIME", ex);
                return true;
            }
        }

        private static string BuildTimeOnlyText(double ut)
        {
            int year, day, hour, minute;
            GetKspYearDayHourMinute(ut, out year, out day, out hour, out minute);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", hour, minute);
        }

        private static string BuildDayGroupLabel(int year, int day)
        {
            return string.Format(CultureInfo.InvariantCulture, "Year {0}, Day {1}", year, day);
        }

        private static string BuildCompactDayLabel(int year, int day)
        {
            return string.Format(CultureInfo.InvariantCulture, "Y{0} D{1}", year, day);
        }

        private static void GetKspYearDay(double ut, out int year, out int day)
        {
            int hour, minute;
            GetKspYearDayHourMinute(ut, out year, out day, out hour, out minute);
        }

        private static void GetKspYearDayHourMinute(double ut, out int year, out int day, out int hour, out int minute)
        {
            KspTimeMath.GetYearDayHourMinute(ut, GetKerbinTimeSettingForHistory(), out year, out day, out hour, out minute);
        }

        private static int CalculateAgeYears(double birthUT, double deathUT)
        {
            return KspTimeMath.CalculateAgeYears(birthUT, deathUT, GetKspYearSeconds());
        }

        private static int ResolveRosterExperienceLevel(ConfigNode kerbal)
        {
            if (kerbal == null)
                return -1;

            int directLevel = ReadIntLike(kerbal, -1, "experienceLevel", "ExperienceLevel", "level", "Level");
            int trainingFloor = ReadTrainingLevelFromRosterLogs(kerbal);
            int reflectedLevel = TryCalculateRosterExperienceLevelViaProtoCrew(kerbal);
            int heuristicLevel = ApproximateExperienceLevelFromRosterLogs(kerbal);

            int resolved = directLevel;
            if (resolved < 0)
                resolved = reflectedLevel;
            if (resolved < 0)
                resolved = heuristicLevel;

            if (trainingFloor >= 0)
                resolved = Math.Max(resolved, trainingFloor);
            if (heuristicLevel >= 0 && resolved >= 0 && resolved == 0)
                resolved = Math.Max(resolved, heuristicLevel);

            return resolved;
        }

        private static int ReadTrainingLevelFromRosterLogs(ConfigNode kerbal)
        {
            int highest = -1;
            foreach (string entry in EnumerateRosterLogEntries(kerbal))
            {
                if (string.IsNullOrEmpty(entry))
                    continue;

                string kind;
                string body;
                SplitRosterLogEntry(entry, out kind, out body);
                if (string.IsNullOrEmpty(kind) || !kind.StartsWith("Training", StringComparison.OrdinalIgnoreCase))
                    continue;

                string digits = new string(kind.SkipWhile(c => !char.IsDigit(c)).ToArray());
                if (string.IsNullOrEmpty(digits))
                    continue;

                if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    highest = Math.Max(highest, parsed);
            }

            return highest;
        }

        private static int TryCalculateRosterExperienceLevelViaProtoCrew(ConfigNode kerbal)
        {
            if (kerbal == null)
                return -1;

            object proto = null;
            try
            {
                try
                {
                    proto = Activator.CreateInstance(typeof(ProtoCrewMember), true);
                }
                catch
                {
                    proto = FormatterServices.GetUninitializedObject(typeof(ProtoCrewMember));
                }

                if (proto == null)
                    return -1;

                Type type = proto.GetType();
                MethodInfo loadMethod = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m != null && (m.Name == "Load" || m.Name == "load" || m.Name == "OnLoad") &&
                                         m.GetParameters().Length == 1 &&
                                         m.GetParameters()[0].ParameterType == typeof(ConfigNode));
                if (loadMethod == null)
                    return -1;

                loadMethod.Invoke(proto, new object[] { kerbal });

                MethodInfo archiveMethod = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m != null && (m.Name == "ArchiveFlightLog" || m.Name == "archiveFlightLog") && m.GetParameters().Length == 0);
                if (archiveMethod != null)
                    archiveMethod.Invoke(proto, null);

                MethodInfo updateMethod = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m != null && (m.Name == "UpdateExperience" || m.Name == "updateExperience") && m.GetParameters().Length == 0);
                if (updateMethod != null)
                    updateMethod.Invoke(proto, null);

                return ReadProtoExperienceLevelFromObject(proto, type);
            }
            catch
            {
                return -1;
            }
        }

        private static int ApproximateExperienceLevelFromRosterLogs(ConfigNode kerbal)
        {
            if (kerbal == null)
                return -1;

            var bestPerBodyXp = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            bool hasRecover = false;

            foreach (string entry in EnumerateRosterLogEntries(kerbal))
            {
                if (string.IsNullOrEmpty(entry))
                    continue;

                string kind;
                string body;
                SplitRosterLogEntry(entry, out kind, out body);
                if (string.IsNullOrEmpty(kind))
                    continue;

                if (kind.StartsWith("Training", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (kind.Equals("Recover", StringComparison.OrdinalIgnoreCase))
                {
                    hasRecover = true;
                    continue;
                }

                double eventXp = GetApproximateRosterEventXp(kind, body);
                if (eventXp <= 0d)
                    continue;

                string bodyKey = string.IsNullOrEmpty(body) ? "<global>" : body;
                if (!bestPerBodyXp.TryGetValue(bodyKey, out double current) || eventXp > current)
                    bestPerBodyXp[bodyKey] = eventXp;
            }

            double totalXp = bestPerBodyXp.Values.Sum();
            if (hasRecover)
                totalXp += 0.5d;

            double extraXp = ReadDouble(kerbal, 0d, "extraXP", "ExtraXP");
            if (extraXp > 0d)
                totalXp += extraXp;

            return CalculateExperienceLevelFromApproxXp(totalXp);
        }

        private static int CalculateExperienceLevelFromApproxXp(double xp)
        {
            if (xp < 0d)
                return -1;
            if (xp >= 64d)
                return 5;
            if (xp >= 32d)
                return 4;
            if (xp >= 16d)
                return 3;
            if (xp >= 8d)
                return 2;
            if (xp >= 2d)
                return 1;
            return 0;
        }

        private static IEnumerable<string> EnumerateRosterLogEntries(ConfigNode kerbal)
        {
            if (kerbal == null)
                yield break;

            foreach (string nodeName in new[] { "CAREER_LOG", "careerLog", "FLIGHT_LOG", "flightLog" })
            {
                ConfigNode log = kerbal.GetNode(nodeName);
                if (log == null)
                    continue;

                foreach (ConfigNode.Value value in log.values)
                {
                    if (value == null || string.IsNullOrEmpty(value.value))
                        continue;
                    if (string.Equals(value.name, "flight", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value.name, "flights", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string trimmed = CollapseWhitespace(value.value);
                    if (!string.IsNullOrEmpty(trimmed))
                        yield return trimmed;
                }
            }
        }

        private static void SplitRosterLogEntry(string entry, out string kind, out string body)
        {
            kind = null;
            body = null;
            if (string.IsNullOrEmpty(entry))
                return;

            string[] parts = entry.Split(new[] { ',' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                kind = CollapseWhitespace(parts[0].Trim());
            if (parts.Length > 1)
                body = CollapseWhitespace(parts[1].Trim());
        }

        private static double GetApproximateRosterEventXp(string kind, string body)
        {
            if (string.IsNullOrEmpty(kind))
                return 0d;

            double bodyMultiplier = GetApproximateRosterBodyMultiplier(body);
            string normalized = kind.Trim();

            if (normalized.Equals("Flight", StringComparison.OrdinalIgnoreCase))
                return 0.5d * bodyMultiplier;
            if (normalized.Equals("Suborbit", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("SubOrbital", StringComparison.OrdinalIgnoreCase))
                return 1.0d * bodyMultiplier;
            if (normalized.Equals("Flyby", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Escape", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("ReturnFromFlyBy", StringComparison.OrdinalIgnoreCase))
                return 1.0d * bodyMultiplier;
            if (normalized.Equals("Orbit", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("ReturnFromOrbit", StringComparison.OrdinalIgnoreCase))
                return 1.5d * bodyMultiplier;
            if (normalized.Equals("Land", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Landing", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("ReturnFromSurface", StringComparison.OrdinalIgnoreCase))
                return 2.0d * bodyMultiplier;
            if (normalized.IndexOf("Flag", StringComparison.OrdinalIgnoreCase) >= 0)
                return 2.5d * bodyMultiplier;
            if (normalized.IndexOf("Science", StringComparison.OrdinalIgnoreCase) >= 0)
                return 2.0d * bodyMultiplier;
            if (normalized.IndexOf("EVA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Spacewalk", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1.5d * bodyMultiplier;
            if (normalized.IndexOf("Rendezvous", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("Dock", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1.5d * bodyMultiplier;
            return 0d;
        }

        private static double GetApproximateRosterBodyMultiplier(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Equals("Kerbin", StringComparison.OrdinalIgnoreCase))
                return 1d;

            try
            {
                if (FlightGlobals.Bodies != null)
                {
                    foreach (CelestialBody cb in FlightGlobals.Bodies)
                    {
                        if (cb == null || !string.Equals(cb.bodyName, body, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (cb.scienceValues != null)
                            return cb.scienceValues.RecoveryValue;
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.cs:2314", "Suppressed exception in HallOfHistoryWindow.cs:2314", ex); }

            switch (body.ToLowerInvariant())
            {
                case "mun": return 2d;
                case "minmus": return 2.5d;
                case "moho": return 10d;
                case "eve": return 8d;
                case "gilly": return 9d;
                case "duna": return 8d;
                case "ike": return 8d;
                case "dres": return 7d;
                case "jool": return 7d;
                case "laythe": return 12d;
                case "vall": return 12d;
                case "tylo": return 12d;
                case "bop": return 8d;
                case "pol": return 8d;
                case "eeloo": return 15d;
                case "kerbol":
                case "sun": return 6d;
                default: return 1d;
            }
        }

        private static int CountRosterFlights(ConfigNode kerbal)
        {
            if (kerbal == null)
                return -1;

            int careerCount = ReadRosterFlightCounter(kerbal.GetNode("CAREER_LOG"));
            if (careerCount < 0)
                careerCount = ReadRosterFlightCounter(kerbal.GetNode("careerLog"));

            int flightCount = ReadRosterFlightCounter(kerbal.GetNode("FLIGHT_LOG"));
            if (flightCount < 0)
                flightCount = ReadRosterFlightCounter(kerbal.GetNode("flightLog"));

            if (careerCount >= 0 && flightCount >= 0)
                return Mathf.Max(careerCount, flightCount);

            if (careerCount >= 0)
                return careerCount;

            if (flightCount >= 0)
                return flightCount;

            return ReadInt(kerbal, -1, "Flights", "flights", "FlightCount", "flightCount", "missionsFlown", "MissionsFlown");
        }

        private static int ReadRosterFlightCounter(ConfigNode log)
        {
            if (log == null)
                return -1;

            int parsed;
            if (log.HasValue("flight") && int.TryParse(log.GetValue("flight"), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            if (log.HasValue("Flight") && int.TryParse(log.GetValue("Flight"), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            if (log.HasValue("flights") && int.TryParse(log.GetValue("flights"), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            if (log.HasValue("Flights") && int.TryParse(log.GetValue("Flights"), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return -1;
        }

        private static int ReadProtoFlights(ProtoCrewMember pcm)
        {
            if (pcm == null)
                return -1;

            try
            {
                Type t = pcm.GetType();

                foreach (string name in new[] { "missionsFlown", "Flights", "flights", "FlightCount", "flightCount" })
                {
                    PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        object v = p.GetValue(pcm, null);
                        if (v is int) return (int)v;
                    }

                    FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        object v = f.GetValue(pcm);
                        if (v is int) return (int)v;
                    }
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.cs:2408", "Suppressed exception in HallOfHistoryWindow.cs:2408", ex); }

            return -1;
        }

        private static bool TryConvertObjectToInt(object value, out int parsed)
        {
            parsed = 0;
            if (value == null)
                return false;

            if (value is int)
            {
                parsed = (int)value;
                return true;
            }

            if (value is float)
            {
                parsed = Mathf.RoundToInt((float)value);
                return true;
            }

            if (value is double)
            {
                parsed = (int)Math.Round((double)value, MidpointRounding.AwayFromZero);
                return true;
            }

            if (value is long)
            {
                parsed = (int)(long)value;
                return true;
            }

            if (value is string)
                return TryParseIntLike((string)value, out parsed);

            return false;
        }

        private static bool TryParseIntLike(string value, out int parsed)
        {
            parsed = 0;
            if (string.IsNullOrEmpty(value))
                return false;

            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return true;

            double asDouble;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out asDouble))
            {
                parsed = (int)Math.Round(asDouble, MidpointRounding.AwayFromZero);
                return true;
            }

            return false;
        }

        private static bool ReadProtoVeteran(ProtoCrewMember pcm)
        {
            if (pcm == null)
                return false;

            try
            {
                return ReadProtoVeteranFromObject(pcm, pcm.GetType());
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadProtoVeteranFromObject(object obj, Type type)
        {
            foreach (string name in new[] { "veteran", "Veteran", "isVeteran", "IsVeteran" })
            {
                PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    object value = p.GetValue(obj, null);
                    if (value is bool)
                        return (bool)value;
                }

                FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    object value = f.GetValue(obj);
                    if (value is bool)
                        return (bool)value;
                }
            }

            return false;
        }

        private static int ReadProtoExperienceLevelFromObject(object obj, Type type)
        {
            if (obj == null || type == null)
                return -1;

            foreach (string name in new[] { "experienceLevel", "ExperienceLevel", "level", "Level" })
            {
                PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    object v = p.GetValue(obj, null);
                    if (TryConvertObjectToInt(v, out int parsed))
                        return parsed;
                }

                FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    object v = f.GetValue(obj);
                    if (TryConvertObjectToInt(v, out int parsed))
                        return parsed;
                }
            }

            return -1;
        }

        private static int ReadProtoExperienceLevel(ProtoCrewMember pcm)
        {
            if (pcm == null)
                return -1;

            try
            {
                return ReadProtoExperienceLevelFromObject(pcm, pcm.GetType());
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HallOfHistoryWindow.cs:2543", "Suppressed exception in HallOfHistoryWindow.cs:2543", ex); }

            return -1;
        }

        private static string ReadValue(ConfigNode node, params string[] keys)
        {
            if (node == null || keys == null) return null;
            foreach (var key in keys)
            {
                if (node.HasValue(key))
                    return node.GetValue(key);
            }
            return null;
        }

        private static bool TryReadDouble(ConfigNode node, string key, out double value)
        {
            value = -1d;
            if (node == null || string.IsNullOrEmpty(key) || !node.HasValue(key)) return false;
            return double.TryParse(node.GetValue(key), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static IEnumerable<ConfigNode> FindNodesRecursive(ConfigNode root, string nodeName)
        {
            if (root == null || string.IsNullOrEmpty(nodeName)) yield break;
            var stack = new Stack<ConfigNode>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null) continue;

                if (string.Equals(current.name, nodeName, StringComparison.OrdinalIgnoreCase))
                    yield return current;

                foreach (ConfigNode child in current.GetNodes())
                    if (child != null)
                        stack.Push(child);
            }
        }

    }
}

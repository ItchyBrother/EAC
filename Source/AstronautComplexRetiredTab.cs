// EAC - AstronautComplexRetiredTab
// Adds a "Retired" tab to the Astronaut Complex between Assigned and Lost.
// PERF FIX: FastHideWorker throttled to 1s (was every frame with heavy reflection).
// PERF FIX: Root discovery results cached (was scanning all Transforms every 0.4s).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    public class RetiredTabClickProxy : MonoBehaviour
    {
        public Transform RetiredScrollList;
        public Transform VesselScrollRect;
        public Transform TabsRoot;
        public Transform OurTab;
        public System.Action OnClicked;
        public System.Action OnNativeTabActivated;

        private RectTransform _rt;
        private Camera _uiCamera;

        private void Start()
        {
            _rt = GetComponent<RectTransform>();
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                _uiCamera = canvas.worldCamera;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.MouseDown) return;
            if (_rt == null) return;
            Vector2 screenPos = new Vector2(
                Event.current.mousePosition.x,
                Screen.height - Event.current.mousePosition.y);
            if (RectTransformUtility.RectangleContainsScreenPoint(_rt, screenPos, _uiCamera))
            {
                ACPatches.RetiredTabShowing = true;
                ACPatches.RetiredTabShowTime = Time.realtimeSinceStartup;
                HighlightOurTab(true);
                OnClicked?.Invoke();
                ShowRetiredList();
            }
        }

        public void HandleNativeTabActivated(int index)
        {
            HighlightOurTab(false);
            if (RetiredScrollList != null) RetiredScrollList.gameObject.SetActive(false);
            if (VesselScrollRect != null)
                for (int i = 0; i < VesselScrollRect.childCount; i++)
                {
                    Transform ch = VesselScrollRect.GetChild(i);
                    if (ch == null || ch == RetiredScrollList) continue;
                    if (ch.name.StartsWith("scrollList_", StringComparison.OrdinalIgnoreCase))
                        ch.gameObject.SetActive(true);
                }
            OnNativeTabActivated?.Invoke();
        }

        private void HighlightOurTab(bool on)
        {
            if (OurTab == null) return;
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Component c in OurTab.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var colorProp = c.GetType().GetProperty("color", f);
                if (colorProp == null || colorProp.PropertyType != typeof(Color)) continue;
                try { colorProp.SetValue(c, on ? new Color(1f,1f,1f,1f) : new Color(1f,1f,1f,0.5f), null); } catch { }
            }
        }

        internal void ShowRetiredList()
        {
            if (RetiredScrollList == null) return;
            ACPatches.RetiredTabShowing = true;
            ACPatches.RetiredTabShowTime = Time.realtimeSinceStartup;

            if (VesselScrollRect != null)
                for (int i = 0; i < VesselScrollRect.childCount; i++)
                {
                    Transform ch = VesselScrollRect.GetChild(i);
                    if (ch == null || ch == RetiredScrollList) continue;
                    if (ch.name.StartsWith("scrollList_", StringComparison.OrdinalIgnoreCase))
                        ch.gameObject.SetActive(false);
                }
            RetiredScrollList.gameObject.SetActive(true);
            // Immediately destroy UIHoverPanel components that OnEnable just re-initialized.
            // Must happen before rewire since UIHoverPanel.Update() can hide Button GOs.
            NeuterUIHoverPanelsOnRows(RetiredScrollList);
            ACPatches.RepositionRetiredRows(RetiredScrollList);
            ACPatches.RewireTooltipsInRetiredList(RetiredScrollList);
            ACPatches.ReenableRetiredButtons(RetiredScrollList);
            // Deferred rewire: SetActive(true) fires OnEnable on tooltip components,
            // which resets their registration with KSP's tooltip manager. The immediate
            // rewire above may fail if the tooltip system hasn't settled yet.
            // Re-run after one frame to catch any that were reset.
            StartCoroutine(DeferredRewire());
        }

        private System.Collections.IEnumerator DeferredRewire()
        {
            yield return null;
            if (RetiredScrollList != null && RetiredScrollList.gameObject.activeInHierarchy)
            {
                // Kill UIHoverPanel components — their OnEnable repopulates hoverObjects
                // which causes Update() to hide the Button GO every frame, blocking tooltips.
                // Field-clearing doesn't survive re-activation, so destroy the component entirely.
                NeuterUIHoverPanelsOnRows(RetiredScrollList);
                ACPatches.RewireTooltipsInRetiredList(RetiredScrollList);
                ACPatches.ReenableRetiredButtons(RetiredScrollList);
            }
            yield return new WaitForSeconds(0.3f);
            if (RetiredScrollList != null && RetiredScrollList.gameObject.activeInHierarchy)
            {
                NeuterUIHoverPanelsOnRows(RetiredScrollList);
                ACPatches.RewireTooltipsInRetiredList(RetiredScrollList);
                ACPatches.ReenableRetiredButtons(RetiredScrollList);
            }
        }

        /// <summary>
        /// Neuters UIHoverPanel on each retired row WITHOUT destroying it.
        /// UIHoverPanel must stay alive because KSP's tooltip system routes hover events
        /// through it. We just clear hoverObjects so its Update() stops hiding the Button GO.
        /// Does NOT destroy EventTriggerForwarder — it's needed for tooltip event delivery.
        /// This must be re-applied every time the tab is shown because OnEnable repopulates
        /// the internal state from serialized data.
        /// </summary>
        private static void NeuterUIHoverPanelsOnRows(Transform list)
        {
            if (list == null) return;
            int neutered = 0;
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (int i = 0; i < list.childCount; i++)
            {
                Transform row = list.GetChild(i);
                if (row == null || !row.gameObject.activeSelf) continue;

                // Find UIHoverPanel on the row
                Component uhp = null;
                foreach (Component c in row.GetComponents<Component>())
                {
                    if (c != null && c.GetType().Name == "UIHoverPanel") { uhp = c; break; }
                }
                if (uhp == null) continue;

                Type t = uhp.GetType();

                // Clear hoverObjects so Update() stops calling SetActive(false) on Button GO
                foreach (string fieldName in new[] { "hoverObjects", "_hoverObjects", "HoverObjects" })
                {
                    var hoF = t.GetField(fieldName, bf);
                    if (hoF == null) continue;
                    var hoVal = hoF.GetValue(uhp);
                    if (hoVal == null) break;
                    var clearM = hoVal.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public);
                    if (clearM != null) try { clearM.Invoke(hoVal, null); } catch { }
                    break;
                }

                // CRITICAL: Disable the UIHoverPanel component entirely.
                // Clearing hoverObjects isn't enough — OnEnable repopulates them from
                // serialized data, and Update() runs between our clear passes.
                // With enabled=false, Unity skips Update/LateUpdate so it can never
                // hide the Button GO again. Tooltips work independently via
                // UIStateButtonTooltip which registers directly with TooltipController.
                var enabledP = t.GetProperty("enabled", bf);
                if (enabledP != null) try { enabledP.SetValue(uhp, false, null); } catch { }

                neutered++;

                // Force Button GO active in case UIHoverPanel.OnEnable already hid it
                foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
                {
                    if (ch.name == "Button" && !ch.gameObject.activeSelf)
                    {
                        ch.gameObject.SetActive(true);
                        break;
                    }
                }
            }
            if (neutered > 0)
                RRLog.Verbose("[EAC] NeuterUIHoverPanelsOnRows: neutered " + neutered + " rows");
        }

        public void WireToggle(object onValueChanged) { }
    }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class AstronautComplexRetiredTab : MonoBehaviour
    {
        private const string LOGP = "[EAC] AC RetiredTab: ";

        // Cached roots (no longer re-scanned every tick)
        private Transform _tabsRoot;
        private Transform _vesselScrollRect;
        private bool _rootsSearched;

        private Component _toggleController;
        private FieldInfo _listsField;
        private FieldInfo _currentListField;
        private int _retiredTabIndex = -1;
        private bool _registeredWithController;
        private bool _controllerRegistrationFailed;

        private GameObject _tabRetiredGO;
        private object _retiredToggleComp;
        private bool _toggleListenersCleared;
        private Transform _scrollListRetired;

        public static bool RetiredTabShowing => ACPatches.RetiredTabShowing;
        private bool _retiredTabActive;
        private RetiredTabClickProxy _retiredProxy;
        private int _lastBadgeCount = -1;
        private bool _setupComplete;

        private void Start()
        {
            try
            {
                var h = new Harmony("RosterRotation.Patches");
                AstronautComplexACPatch.Apply(h);
                KerbalRosterHook.Apply(h);
            }
            catch (Exception ex) { RRLog.Error(LOGP + "Harmony fallback apply failed: " + ex); }

            StartCoroutine(SetupWorker());
            StartCoroutine(ThrottledHideWorker());
        }

        private void Update()
        {
            if (_toggleListenersCleared)
                try { DetectTabSelection(); } catch { }
        }

        // ── Setup coroutine ──────────────────────────────────────────────────────
        // Runs setup until complete, then monitors for AC UI destruction (close/reopen).
        // When the AC dialog is destroyed by KSP, our cloned tab and scroll list die with it.
        // We detect this via the cached _tabRetiredGO becoming null and re-run setup.
        private IEnumerator SetupWorker()
        {
            var wait = new WaitForSeconds(0.4f);
            var slowWait = new WaitForSeconds(2f);

            while (true)
            {
                // Phase 1: Setup loop — runs until tabs are created
                while (!_setupComplete)
                {
                    yield return wait;
                    try { DoSetupTick(); }
                    catch (Exception ex) { RRLog.Error(LOGP + "SetupWorker exception: " + ex); }
                }

                // Phase 2: Monitor loop — badge updates + detect destroyed UI
                while (_setupComplete)
                {
                    yield return slowWait;

                    // Check if our tab was destroyed (AC dialog was closed/reopened by KSP).
                    // Unity overloads == so destroyed objects compare equal to null.
                    if (_tabRetiredGO == null)
                    {
                        RRLog.Verbose(LOGP + "Tab destroyed — resetting setup for next AC open.");
                        ResetSetupState();
                        break; // Back to Phase 1
                    }

                    UpdateRetiredBadge();
                }
            }
        }

        /// <summary>Clears all cached state so setup runs fresh on next AC open.</summary>
        private void ResetSetupState()
        {
            _tabsRoot = null;
            _vesselScrollRect = null;
            _rootsSearched = false;
            _toggleController = null;
            _listsField = null;
            _currentListField = null;
            _retiredTabIndex = -1;
            _registeredWithController = false;
            _controllerRegistrationFailed = false;
            _tabRetiredGO = null;
            _retiredToggleComp = null;
            _toggleListenersCleared = false;
            _scrollListRetired = null;
            _retiredTabActive = false;
            _retiredProxy = null;
            _lastBadgeCount = -1;
            _setupComplete = false;
        }

        private void DoSetupTick()
        {
            if (!_rootsSearched)
            {
                float t0 = Time.realtimeSinceStartup;
                _tabsRoot = FindTabsRoot();
                _vesselScrollRect = FindVesselScrollRect();
                float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
                if (_tabsRoot != null && _vesselScrollRect != null)
                {
                    _rootsSearched = true;
                    RRLog.Verbose(LOGP + $"Root discovery took {elapsed:F1}ms");
                }
                else
                {
                    return; // Don't proceed if roots not found
                }
            }

            if (_toggleController == null && _tabsRoot != null)
                _toggleController = FindToggleController(_tabsRoot);

            if (_toggleController != null && _listsField == null)
                CacheControllerFields();

            if (_tabsRoot != null && _tabRetiredGO == null)
                TryCreateRetiredTab();

            if (_vesselScrollRect != null && _scrollListRetired == null)
                TryCreateRetiredScrollList();

            if (!_registeredWithController && !_controllerRegistrationFailed &&
                _listsField != null && _scrollListRetired != null && _tabRetiredGO != null)
                TryRegisterWithToggleController();

            if (_tabRetiredGO != null && !_toggleListenersCleared)
                TryClearStaleToggleListeners();

            if (_tabRetiredGO != null && _toggleListenersCleared && _registeredWithController)
                _setupComplete = true;
        }

        // ── PERF: Only hides retired rows when AC is open ───────────────────────
        private IEnumerator ThrottledHideWorker()
        {
            var wait = new WaitForSeconds(1.0f);
            while (true)
            {
                yield return wait;
                try
                {
                    if (!_retiredTabActive && _vesselScrollRect != null)
                        HideRetiredRowsInActiveList();
                }
                catch { }
            }
        }

        // ── Root discovery (cached after first find) ───────────────────────────
        private static Transform FindTabsRoot()
        {
            Transform best = null; int bestScore = -1;
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !string.Equals(t.name, "Tabs", StringComparison.Ordinal)) continue;
                if (t.Find("Tab Available") == null && t.Find("Tab Assigned") == null) continue;
                int score = t.childCount;
                if (GetPath(t).IndexOf("AstronautComplex", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
                if (score > bestScore) { best = t; bestScore = score; }
            }
            return best;
        }

        private static Transform FindVesselScrollRect()
        {
            Transform best = null; int bestScore = -1;
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !string.Equals(t.name, "VesselScrollRect", StringComparison.Ordinal)) continue;
                int score = 0;
                if (GetPath(t).IndexOf("AstronautComplex", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
                for (int c = 0; c < t.childCount; c++)
                {
                    var ch = t.GetChild(c);
                    if (ch?.name != null && ch.name.IndexOf("scrollList_", StringComparison.OrdinalIgnoreCase) >= 0) score += 3;
                }
                if (score > bestScore) { best = t; bestScore = score; }
            }
            return best;
        }

        private static Component FindToggleController(Transform tabsRoot)
        {
            foreach (Component c in tabsRoot.GetComponents<Component>())
            {
                if (c == null) continue;
                string fn = c.GetType().FullName ?? c.GetType().Name;
                if (fn.IndexOf("UIListToggleController", StringComparison.OrdinalIgnoreCase) >= 0) return c;
            }
            return null;
        }

        private void CacheControllerFields()
        {
            Type ct = _toggleController.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _listsField = ct.GetField("lists", flags);
            _currentListField = ct.GetField("currentList", flags);
        }

        // ── Register with UIListToggleController ───────────────────────────────
        // Appends our UIList at the END of the lists[] array.
        // Tab visual order (Available/Assigned/Retired/Lost) is handled by
        // SetSiblingIndex on the tab button GO — the lists array order must NOT
        // change because Lost's toggle is already wired to ActivateList(2).
        // Our RetiredTabClickProxy handles retired tab clicks independently.
        private void TryRegisterWithToggleController()
        {
            try
            {
                var oldArr = _listsField.GetValue(_toggleController) as Array;
                if (oldArr == null) { _controllerRegistrationFailed = true; return; }

                // Already extended
                if (oldArr.Length > 3)
                {
                    _retiredTabIndex = oldArr.Length - 1;
                    _registeredWithController = true;
                    return;
                }

                Component ourUIList = FindUIListComp(_scrollListRetired);
                if (ourUIList == null) { _controllerRegistrationFailed = true; return; }

                // Append at end: [Available(0), Assigned(1), Lost(2), Retired(3)]
                Type elemType = oldArr.GetType().GetElementType();
                var newArr = Array.CreateInstance(elemType, oldArr.Length + 1);
                Array.Copy(oldArr, newArr, oldArr.Length);
                newArr.SetValue(ourUIList, oldArr.Length);

                _listsField.SetValue(_toggleController, newArr);
                _retiredTabIndex = oldArr.Length;
                _registeredWithController = true;

                RRLog.Verbose(LOGP + "Extended lists[] to " + newArr.Length + ", retired index=" + _retiredTabIndex);
            }
            catch (Exception ex)
            {
                _controllerRegistrationFailed = true;
                RRLog.Error(LOGP + "TryRegisterWithToggleController failed: " + ex);
            }
        }

        private void TryClearStaleToggleListeners()
        {
            _retiredToggleComp = FindToggleOnGO(_tabRetiredGO);
            if (_retiredToggleComp != null)
                SetBoolReflect(_retiredToggleComp, "interactable", true);
            _toggleListenersCleared = true;
        }

        private void DetectTabSelection()
        {
            if (!_retiredTabActive || _vesselScrollRect == null) return;
            for (int i = 0; i < _vesselScrollRect.childCount; i++)
            {
                Transform ch = _vesselScrollRect.GetChild(i);
                if (ch == null || ch == _scrollListRetired) continue;
                if (ch.name.IndexOf("scrollList_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (ch.gameObject.activeSelf)
                {
                    _retiredProxy?.HandleNativeTabActivated(-1);
                    OnRetiredTabDeselected();
                    return;
                }
            }
        }

        private void OnRetiredTabSelected()
        {
            _retiredTabActive = true;
            ACPatches.RetiredTabShowing = true;
            RosterRotationKSCUI.RetiredTabSelected = true;
        }

        private void OnRetiredTabDeselected()
        {
            _retiredTabActive = false;
            ACPatches.RetiredTabShowing = false;
            RosterRotationKSCUI.RetiredTabSelected = false;
        }

        private void UpdateRetiredBadge()
        {
            int count = 0;
            foreach (var kvp in RosterRotationState.Records)
                if (kvp.Value != null && kvp.Value.Retired && kvp.Value.DeathUT <= 0) count++;

            if (count == _lastBadgeCount) return;
            _lastBadgeCount = count;

            foreach (Component c in _tabRetiredGO.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                string cur = null;
                try { cur = p.GetValue(c, null) as string; } catch { continue; }
                if (cur == null) continue;
                if (cur.IndexOf("Retired", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cur.IndexOf("Lost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cur.IndexOf("Available", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try { p.SetValue(c, "Retired[" + count + "]", null); } catch { }
                    return;
                }
            }
        }

        // ── Per-second: hide retired rows from non-Retired list ────────────────
        private void HideRetiredRowsInActiveList()
        {
            Transform activeList = null;
            for (int i = 0; i < _vesselScrollRect.childCount; i++)
            {
                var ch = _vesselScrollRect.GetChild(i);
                if (ch == null || ch.name == null) continue;
                if (ch.name.IndexOf("scrollList_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!ch.gameObject.activeInHierarchy) continue;
                if (_scrollListRetired != null && ch == _scrollListRetired) continue;
                activeList = ch;
                break;
            }
            if (activeList == null) return;

            Transform content = FindContent(activeList) ?? activeList;
            var retiredNames = RosterRotationState.GetRetiredNames();
            if (retiredNames.Count == 0) return;

            var crewNames = RosterRotationState.GetCrewNameSet();
            for (int i = 0; i < content.childCount; i++)
            {
                Transform row = content.GetChild(i);
                if (row == null || !row.gameObject.activeSelf) continue;
                if (RowContainsRetiredName(row.gameObject, retiredNames, crewNames))
                    row.gameObject.SetActive(false);
            }
        }

        // ── Tab creation (inserts between Assigned and Lost) ───────────────────
        private void TryCreateRetiredTab()
        {
            Transform existing = _tabsRoot.Find("Tab Retired");
            if (existing != null) { _tabRetiredGO = existing.gameObject; return; }

            // Clone from Lost tab (last native tab)
            Transform template = _tabsRoot.Find("Tab Lost") ?? _tabsRoot.Find("Tab Available");
            if (template == null) return;

            // Find "Tab Assigned" to insert after it
            Transform assignedTab = _tabsRoot.Find("Tab Assigned");
            int insertIndex = assignedTab != null ? assignedTab.GetSiblingIndex() + 1 : template.GetSiblingIndex();

            GameObject clone = (GameObject)UnityEngine.Object.Instantiate(template.gameObject);
            clone.name = "Tab Retired";
            clone.transform.SetParent(_tabsRoot, false);
            clone.transform.SetSiblingIndex(insertIndex);
            clone.SetActive(true);

            _tabRetiredGO = clone;

            var proxy = clone.AddComponent<RetiredTabClickProxy>();
            proxy.RetiredScrollList = null;
            proxy.TabsRoot = _tabsRoot;
            proxy.OurTab = clone.transform;
            proxy.OnClicked = () => { _retiredTabActive = true; ACPatches.RetiredTabShowing = true; OnRetiredTabSelected(); };
            proxy.OnNativeTabActivated = () => { _retiredTabActive = false; ACPatches.RetiredTabShowing = false; OnRetiredTabDeselected(); };
            _retiredProxy = proxy;
            ACPatches.RegisterProxy(proxy);
        }

        private void TryCreateRetiredScrollList()
        {
            if (_vesselScrollRect == null) return;
            Transform existing = _vesselScrollRect.Find("scrollList_retired");
            if (existing != null) { _scrollListRetired = existing; return; }

            Transform template = null;
            for (int i = 0; i < _vesselScrollRect.childCount; i++)
            {
                var ch = _vesselScrollRect.GetChild(i);
                if (ch?.name == null || ch.name == "scrollList_retired") continue;
                if (string.Equals(ch.name, "scrollList_Available", StringComparison.Ordinal)) { template = ch; break; }
                if (template == null && ch.name.IndexOf("scrollList_", StringComparison.OrdinalIgnoreCase) >= 0) template = ch;
            }
            if (template == null) return;

            GameObject clone = (GameObject)UnityEngine.Object.Instantiate(template.gameObject);
            clone.name = "scrollList_retired";
            clone.transform.SetParent(_vesselScrollRect, false);

            Transform content = FindContent(clone.transform);
            if (content != null)
                for (int i = content.childCount - 1; i >= 0; i--)
                    Destroy(content.GetChild(i).gameObject);

            clone.SetActive(false);
            _scrollListRetired = clone.transform;

            if (_retiredProxy != null)
            {
                _retiredProxy.RetiredScrollList = _scrollListRetired;
                _retiredProxy.VesselScrollRect = _vesselScrollRect;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static Component FindUIListComp(Transform root)
        {
            if (root == null) return null;
            foreach (Component c in root.GetComponents<Component>())
            {
                if (c == null) continue;
                string tn = c.GetType().FullName ?? c.GetType().Name;
                if (tn.IndexOf("UIList", StringComparison.OrdinalIgnoreCase) >= 0) return c;
            }
            for (int i = 0; i < root.childCount; i++)
                foreach (Component c in root.GetChild(i).GetComponents<Component>())
                {
                    if (c == null) continue;
                    string tn = c.GetType().FullName ?? c.GetType().Name;
                    if (tn.IndexOf("UIList", StringComparison.OrdinalIgnoreCase) >= 0) return c;
                }
            return null;
        }

        private static object FindToggleOnGO(GameObject go)
        {
            if (go == null) return null;
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("isOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(bool)) continue;
                return c;
            }
            return null;
        }

        private static void SetBoolReflect(object obj, string name, bool value)
        {
            if (obj == null) return;
            try
            {
                var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p?.CanWrite == true) p.SetValue(obj, value, null);
            }
            catch { }
        }

        private static Transform FindContent(Transform root)
        {
            if (root == null) return null;
            Transform t = root.Find("Viewport/Content");
            if (t != null) return t;
            foreach (Transform x in root.GetComponentsInChildren<Transform>(true))
                if (x != null && string.Equals(x.name, "Content", StringComparison.Ordinal)) return x;
            return null;
        }

        // Optimized: accepts pre-built crew name set to avoid rebuilding per row
        private static bool RowContainsRetiredName(GameObject row, List<string> retiredNames, HashSet<string> crewNames)
        {
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (string.IsNullOrEmpty(s) || !crewNames.Contains(s)) continue;
                for (int i = 0; i < retiredNames.Count; i++)
                    if (s == retiredNames[i]) return true;
            }
            return false;
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "<null>";
            string path = t.name; Transform p = t.parent; int guard = 0;
            while (p != null && guard++ < 64) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }
    }
}

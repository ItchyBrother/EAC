// RosterRotation - AstronautComplexRetiredTab (2026-02-23d)
//
// Root cause of ActivateList NullReferenceException (confirmed by diagnostic dump):
//   UIListToggleController has a UIList[] lists field with 4 entries (one per original tab).
//   After we add "Tab Retired" as the 5th toggle, UIListToggleController.Start() hooks
//   ActivateList(i) to each toggle in order. When Tab Retired is clicked, ActivateList(4)
//   fires. lists[4] does not exist → NullReferenceException.
//
// Fix: In TryRegisterWithToggleController, directly access the "lists" field (known name
//   from diagnostic), extend it to length+1, and place our cloned UIList at index 4.
//   UIListToggleController then manages all scroll list visibility natively — no more
//   fighting with it from the outside.
//
// Tab detection: poll UIListToggleController.currentList (known field name from diagnostic).
//   When currentList == _retiredTabIndex → show IMGUI overlay (RetiredTabSelected = true).
//
// Toggle listener strategy: call RemoveAllListeners() to clear the stale template listener
//   that the clone inherited (which called ActivateList with the WRONG index). Do NOT add
//   our own listener — UIListToggleController.Start() will add a fresh ActivateList(4)
//   listener on the next Start() call. We detect activation via currentList polling.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    // -----------------------------------------------------------------------
    // Simple click proxy — checks mouse position against tab bounds every frame,
    // no EventSystems dependency required.
    // -----------------------------------------------------------------------
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
                //RRLog.Verbose("[RosterRotation] RetiredTabClickProxy: OnGUI click detected.");
                ACPatches.RetiredTabShowing = true;
                ACPatches.RetiredTabShowTime = UnityEngine.Time.realtimeSinceStartup;
                HighlightOurTab(true);
                OnClicked?.Invoke();
                ShowRetiredList();
            }
        }

        private void Update() { /* ACPatch.Prefix_ActivateList handles switch-away */ }

        // Called by ACPatches when a native tab's ActivateList fires while we're showing
        public void HandleNativeTabActivated(int index)
        {
            RRLog.Verbose("[RosterRotation] RetiredTabClickProxy: native ActivateList(" + index + ") → hiding retired.");
            HighlightOurTab(false);
            if (RetiredScrollList != null)
                RetiredScrollList.gameObject.SetActive(false);
            // Restore ALL native scroll lists — ShowRetiredList hid them as whole GameObjects
            // but KSP's ActivateList only restores their contents, not the GameObject itself
            if (VesselScrollRect != null)
                for (int i = 0; i < VesselScrollRect.childCount; i++)
                {
                    Transform ch = VesselScrollRect.GetChild(i);
                    if (ch == null || ch == RetiredScrollList) continue;
                    if (ch.name.StartsWith("scrollList_", System.StringComparison.OrdinalIgnoreCase))
                        ch.gameObject.SetActive(true);
                }
            OnNativeTabActivated?.Invoke();
        }

        private void HighlightOurTab(bool on)
        {
            if (OurTab == null) return;
            const System.Reflection.BindingFlags f =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            foreach (Component c in OurTab.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var colorProp = c.GetType().GetProperty("color", f);
                if (colorProp == null || colorProp.PropertyType != typeof(Color)) continue;
                try
                {
                    colorProp.SetValue(c, on ? new Color(1f,1f,1f,1f) : new Color(1f,1f,1f,0.5f), null);
                }
                catch { }
            }
            RRLog.Verbose("[RosterRotation] RetiredTabClickProxy: HighlightOurTab(" + on + ")");
        }

        internal void ShowRetiredListNextFrame()
        {
            StartCoroutine(ShowRetiredListDeferred());
        }

        private IEnumerator ShowRetiredListDeferred()
        {
            // Wait one frame so Unity's layout system processes the newly cloned rows
            // before we make them visible. Without this, row backgrounds aren't sized yet
            // and appear as missing borders.
            yield return null;
            ShowRetiredList();
        }

        internal void ShowRetiredList()
        {
            if (RetiredScrollList == null) { RRLog.Verbose("[RosterRotation] RetiredTabClickProxy: ShowRetiredList — RetiredScrollList is NULL"); return; }
            ACPatches.RetiredTabShowing = true;
            ACPatches.RetiredTabShowTime = UnityEngine.Time.realtimeSinceStartup;

            // Dump VesselScrollRect children so we know their actual names
            if (VesselScrollRect != null)
            {
                var sb = new System.Text.StringBuilder("[RosterRotation] RetiredTabClickProxy: VesselScrollRect children: ");
                for (int i = 0; i < VesselScrollRect.childCount; i++)
                {
                    Transform ch = VesselScrollRect.GetChild(i);
                    if (ch != null) sb.Append(ch.name).Append("(active=").Append(ch.gameObject.activeSelf).Append(") ");
                }
                RRLog.Verbose(sb.ToString());
            }

            // Hide all scrollList_ siblings
            if (VesselScrollRect != null)
                for (int i = 0; i < VesselScrollRect.childCount; i++)
                {
                    Transform ch = VesselScrollRect.GetChild(i);
                    if (ch == null || ch == RetiredScrollList) continue;
                    if (ch.name.StartsWith("scrollList_", System.StringComparison.OrdinalIgnoreCase))
                    {
                        ch.gameObject.SetActive(false);
                        RRLog.Verbose("[RosterRotation] RetiredTabClickProxy: hid " + ch.name);
                    }
                }
            RetiredScrollList.gameObject.SetActive(true);
            // OnEnable fires on SetActive(true) and collapses row positions — reposition first, then wire.
            ACPatches.RepositionRetiredRows(RetiredScrollList);
            ACPatches.RewireTooltipsInRetiredList(RetiredScrollList);
            RRLog.Verbose("[RosterRotation] RetiredTabClickProxy: ShowRetiredList() — retired list active=" + RetiredScrollList.gameObject.activeSelf + " path=" + GetPath(RetiredScrollList));
        }

        private string GetPath(Transform t)
        {
            if (t == null) return "<null>";
            string path = t.name;
            Transform p = t.parent;
            int g = 0;
            while (p != null && g++ < 10) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }

        public void WireToggle(object onValueChanged) { }
    }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class AstronautComplexRetiredTab : MonoBehaviour
    {
        private const string LOGP = "[RosterRotation] AC RetiredTab: ";

        // --- Roots ---
        private Transform _tabsRoot;
        private Transform _vesselScrollRect;

        // --- UIListToggleController (KSP native, on Tabs root) ---
        private Component _toggleController;
        private FieldInfo _listsField;       // UIList[] lists
        private FieldInfo _currentListField; // Int32 currentList
        private int _retiredTabIndex = -1;   // index in lists[] that is ours
        private bool _registeredWithController;
        private bool _controllerRegistrationFailed;

        // --- Our tab ---
        private GameObject _tabRetiredGO;
        private object _retiredToggleComp;    // UnityEngine.UI.Toggle (reflection)
        private bool _toggleListenersCleared; // true once RemoveAllListeners() was called

        // --- Our scroll list ---
        private Transform _scrollListRetired;

        // --- State ---
        // Static flag read by AstronautComplexACPatch to protect retired list from being hidden
        public static bool RetiredTabShowing => ACPatches.RetiredTabShowing;

        private bool _retiredTabActive;
        private RetiredTabClickProxy _retiredProxy;
        private int _lastBadgeCount = -1;
        private bool _diagnosticsDumped;

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Start()
        {
            // Ensure the handful of Harmony hooks that this mod relies on are applied.
            // NOTE: HarmonyPatches.cs is not included in the .csproj, so we do it here.
            try
            {
                var h = new Harmony("RosterRotation.Patches");
                AstronautComplexACPatch.Apply(h);
                KerbalRosterHook.Apply(h);

                RRLog.Verbose(LOGP + "Harmony hooks ensured (ACPatch + KerbalRosterHook).");
            }
            catch (Exception ex)
            {
                RRLog.Error(LOGP + "Harmony fallback apply failed: " + ex);
            }

            StartCoroutine(SetupWorker());
            StartCoroutine(FastHideWorker());
        }

        private void Update()
        {
            if (_toggleListenersCleared)
                try { DetectTabSelection(); } catch { }
        }

        // -----------------------------------------------------------------------
        // Setup coroutine (0.4s tick)
        // -----------------------------------------------------------------------

        private IEnumerator SetupWorker()
        {
            var wait = new WaitForSeconds(0.4f);
            while (true)
            {
                yield return wait;
                try { DoSetupTick(); }
                catch (Exception ex) { RRLog.Error(LOGP + "SetupWorker exception: " + ex); }
            }
        }

        private void DoSetupTick()
        {
            // Phase 1: find roots
            if (_tabsRoot == null) _tabsRoot = FindTabsRoot();
            if (_vesselScrollRect == null) _vesselScrollRect = FindVesselScrollRect();

            // Phase 2: find UIListToggleController
            if (_toggleController == null && _tabsRoot != null)
                _toggleController = FindToggleController(_tabsRoot);

            // Phase 3: cache controller fields (by known name from diagnostic)
            if (_toggleController != null && _listsField == null)
                CacheControllerFields();

            // Phase 4: create tab + scroll list
            if (_tabsRoot != null && _tabRetiredGO == null)
                TryCreateRetiredTab();

            if (_vesselScrollRect != null && _scrollListRetired == null)
                TryCreateRetiredScrollList();

            // Phase 5: register with UIListToggleController (extend lists[])
            if (!_registeredWithController && !_controllerRegistrationFailed &&
                _listsField != null && _scrollListRetired != null && _tabRetiredGO != null)
                TryRegisterWithToggleController();

            // Phase 6: clear stale toggle listeners from cloned template tab
            if (_tabRetiredGO != null && !_toggleListenersCleared)
                TryClearStaleToggleListeners();

            // Phase 7: badge
            if (_tabRetiredGO != null)
                UpdateRetiredBadge();

            // Phase 8: one-shot diagnostics
            if (_tabRetiredGO != null && _toggleListenersCleared && !_diagnosticsDumped)
            {
                _diagnosticsDumped = true;
                DumpACDiagnostics();
            }

            // Phase 9: tab selection now handled in Update()
            // DetectTabSelection();
        }

        // -----------------------------------------------------------------------
        // Fast coroutine: per-frame hiding of retired rows in Available list
        // -----------------------------------------------------------------------

        private IEnumerator FastHideWorker()
        {
            var wait = new WaitForEndOfFrame();
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

        // -----------------------------------------------------------------------
        // Root discovery
        // -----------------------------------------------------------------------

        private Transform FindTabsRoot()
        {
            Transform best = null; int bestScore = -1;
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !string.Equals(t.name, "Tabs", StringComparison.Ordinal)) continue;
                if (t.Find("Tab Available") == null && t.Find("Tab Assigned") == null) continue;
                int score = 0;
                if (GetPath(t).IndexOf("AstronautComplex", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
                score += t.childCount;
                if (score > bestScore) { best = t; bestScore = score; }
            }
            return best;
        }

        private Transform FindVesselScrollRect()
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

        // -----------------------------------------------------------------------
        // Cache known controller fields (names confirmed by diagnostic dump)
        // -----------------------------------------------------------------------

        private void CacheControllerFields()
        {
            Type ct = _toggleController.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _listsField       = ct.GetField("lists",       flags);
            _currentListField = ct.GetField("currentList", flags);

            if (_listsField == null)
                RRLog.Warn(LOGP + "UIListToggleController: 'lists' field not found.");
            if (_currentListField == null)
                RRLog.Warn(LOGP + "UIListToggleController: 'currentList' field not found.");
        }

        // -----------------------------------------------------------------------
        // Extend UIList[] lists  ← THE core fix for the NullReferenceException crash
        //
        // UIListToggleController.ActivateList(index) does lists[index].Show() (approx).
        // There are 4 built-in entries (Available=0, Assigned=1, Applicants=2, KIA=3).
        // Our tab becomes sibling index 4. When clicked, ActivateList(4) fires.
        // Without lists[4], that crashes. We extend the array with our UIList component.
        // -----------------------------------------------------------------------

        private void TryRegisterWithToggleController()
        {
            try
            {
                var oldArr = _listsField.GetValue(_toggleController) as Array;
                if (oldArr == null)
                {
                    RRLog.Warn(LOGP + "lists field is null — cannot register.");
                    _controllerRegistrationFailed = true;
                    return;
                }

                // Already extended (e.g. called twice)
                if (oldArr.Length > 4)
                {
                    _retiredTabIndex = oldArr.Length - 1;
                    _registeredWithController = true;
                    RRLog.Verbose(LOGP + "lists[] already extended (length=" + oldArr.Length + "), retired index=" + _retiredTabIndex);
                    return;
                }

                // Find the UIList component on our cloned scroll list
                Component ourUIList = FindUIListComp(_scrollListRetired);
                if (ourUIList == null)
                {
                    RRLog.Warn(LOGP + "No UIList component found on scrollList_retired.");
                    _controllerRegistrationFailed = true;
                    return;
                }

                // Extend the array
                Type elemType = oldArr.GetType().GetElementType();
                var newArr = Array.CreateInstance(elemType, oldArr.Length + 1);
                Array.Copy(oldArr, newArr, oldArr.Length);
                newArr.SetValue(ourUIList, oldArr.Length);
                _listsField.SetValue(_toggleController, newArr);

                _retiredTabIndex = oldArr.Length;
                _registeredWithController = true;

                RRLog.Verbose(LOGP + "Extended UIListToggleController.lists[] to " + newArr.Length +
                          " entries. Retired tab index=" + _retiredTabIndex +
                          " UIList=" + ourUIList.GetType().Name);

                // Expose index to Harmony patch in AstronautComplexACPatch
                //AstronautComplexACPatch.RetiredTabListIndex = _retiredTabIndex;
            }
            catch (Exception ex)
            {
                _controllerRegistrationFailed = true;
                RRLog.Error(LOGP + "TryRegisterWithToggleController failed: " + ex);
            }
        }

        // -----------------------------------------------------------------------
        // Clear stale toggle listeners from the cloned template tab.
        //
        // The cloned tab inherits the template's persistent onValueChanged listener.
        // That listener calls ActivateList(template_index) — the wrong index for our tab.
        // Removing it lets UIListToggleController.Start() add a fresh ActivateList(4) later.
        // We do NOT add our own toggle listener; we detect selection via currentList polling.
        // -----------------------------------------------------------------------

        private void TryClearStaleToggleListeners()
        {
            _retiredToggleComp = FindToggleOnGO(_tabRetiredGO);
            if (_retiredToggleComp == null)
            {
                RRLog.Warn(LOGP + "No Toggle component found on Tab Retired.");
                _toggleListenersCleared = true;
                return;
            }

            // NOTE: We do NOT call RemoveAllListeners here anymore.
            // The cloned tab's stale listener (ActivateList(wrong_index)) is acceptable —
            // our RetiredTabClickProxy.OnToggleValueChanged fires AFTER it and handles correctly.
            // RemoveAllListeners would also strip ToggleGroup's internal listener.

            SetBoolReflect(_retiredToggleComp, "interactable", true);

            // DO NOT rejoin ToggleGroup here — it causes ActivateList cascades

            _toggleListenersCleared = true;
            RRLog.Verbose(LOGP + "Cleared stale toggle listeners from Tab Retired.");
        }

        private void TryRejoinToggleGroup()
        {
            if (_tabRetiredGO == null || _tabsRoot == null) return;
            try
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                object nativeGroup = null;
                for (int i = 0; i < _tabsRoot.childCount; i++)
                {
                    Transform t = _tabsRoot.GetChild(i);
                    if (t == null || t.gameObject == _tabRetiredGO) continue;
                    foreach (Component c in t.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null) continue;
                        var gp = c.GetType().GetProperty("group", f);
                        if (gp == null) continue;
                        nativeGroup = gp.GetValue(c, null);
                        if (nativeGroup != null) break;
                    }
                    if (nativeGroup != null) break;
                }
                if (nativeGroup == null) { RRLog.Warn(LOGP + "TryRejoinToggleGroup: no group found."); return; }

                foreach (Component c in _tabRetiredGO.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var gp = c.GetType().GetProperty("group", f);
                    if (gp == null || !gp.CanWrite) continue;
                    gp.SetValue(c, nativeGroup, null);
                    RRLog.Verbose(LOGP + "Re-joined ToggleGroup after clearing listeners.");
                    return;
                }
            }
            catch (Exception ex) { RRLog.Error(LOGP + "TryRejoinToggleGroup failed: " + ex); }
        }

        private void TryAddToggleListener(object onValueChanged)
        {
            if (onValueChanged == null || _toggleController == null || _retiredTabIndex < 0) return;
            try
            {
                // Find ActivateList(int) on the controller
                var activateMethod = _toggleController.GetType().GetMethod("ActivateList",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(int) }, null);
                if (activateMethod == null)
                {
                    RRLog.Warn(LOGP + "ActivateList method not found for toggle listener.");
                    return;
                }

                // We need UnityAction<bool> — get it from the AddListener method signature
                var addListenerMethod = onValueChanged.GetType().GetMethod("AddListener",
                    BindingFlags.Instance | BindingFlags.Public);
                if (addListenerMethod == null)
                {
                    RRLog.Warn(LOGP + "AddListener not found on onValueChanged.");
                    return;
                }

                Type delegateType = addListenerMethod.GetParameters()[0].ParameterType;

                // Our callback: when isOn==true, call ActivateList(_retiredTabIndex)
                int capturedIndex = _retiredTabIndex;
                Component capturedController = _toggleController;
                MethodInfo capturedActivate = activateMethod;

                Action<bool> action = (isOn) =>
                {
                    if (isOn)
                        try { capturedActivate.Invoke(capturedController, new object[] { capturedIndex }); }
                        catch (Exception ex) { RRLog.Error(LOGP + "ActivateList invoke failed: " + ex); }
                };

                Delegate d = Delegate.CreateDelegate(delegateType, action.Target, action.Method);
                addListenerMethod.Invoke(onValueChanged, new object[] { d });
                RRLog.Verbose(LOGP + "Added toggle listener → ActivateList(" + capturedIndex + ").");
            }
            catch (Exception ex)
            {
                RRLog.Error(LOGP + "TryAddToggleListener failed: " + ex);
            }
        }

        // -----------------------------------------------------------------------
        // Tab selection detection via currentList poll
        // -----------------------------------------------------------------------

        private void DetectTabSelection()
        {
            // Proxy handles showing the retired list directly.
            // Here we just detect when a native tab reactivates and deselect retired.
            if (!_retiredTabActive || _vesselScrollRect == null) return;

            for (int i = 0; i < _vesselScrollRect.childCount; i++)
            {
                Transform ch = _vesselScrollRect.GetChild(i);
                if (ch == null || ch == _scrollListRetired) continue;
                if (ch.name.IndexOf("scrollList_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (ch.gameObject.activeSelf)
                {
                    RRLog.Verbose(LOGP + "Native list '" + ch.name + "' became active → deselect Retired.");
                    _retiredProxy?.HandleNativeTabActivated(-1);
                    OnRetiredTabDeselected();
                    return;
                }
            }
        }

        // -----------------------------------------------------------------------
        // Tab selected / deselected
        // -----------------------------------------------------------------------

        private void OnRetiredTabSelected()
        {
            _retiredTabActive = true;
            ACPatches.RetiredTabShowing = true;
            RosterRotationKSCUI.RetiredTabSelected = true;
            RRLog.Verbose(LOGP + "Retired tab SELECTED — overlay shown.");
        }

        private void OnRetiredTabDeselected()
        {
            _retiredTabActive = false;
            ACPatches.RetiredTabShowing = false;
            RosterRotationKSCUI.RetiredTabSelected = false;
            RRLog.Verbose(LOGP + "Retired tab DESELECTED.");
        }

        // -----------------------------------------------------------------------
        // Badge
        // -----------------------------------------------------------------------

        private void UpdateRetiredBadge()
        {
            int count = GetRetiredCount();
            if (count == _lastBadgeCount) return;
            _lastBadgeCount = count;

            foreach (Component c in _tabRetiredGO.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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

        // -----------------------------------------------------------------------
        // Per-frame: hide retired rows from the active (non-Retired) scroll list
        // -----------------------------------------------------------------------

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

            // KSP UIList is flat — rows are direct children, no Viewport/Content wrapper
            Transform content = FindContent(activeList) ?? activeList;

            List<string> retiredNames = GetRetiredNameSet();
            if (retiredNames.Count == 0) return;

            for (int i = 0; i < content.childCount; i++)
            {
                Transform row = content.GetChild(i);
                if (row == null || !row.gameObject.activeSelf) continue;
                if (RowContainsRetiredName(row.gameObject, retiredNames))
                    row.gameObject.SetActive(false);
            }
        }

        // -----------------------------------------------------------------------
        // Tab creation
        // -----------------------------------------------------------------------

        private void TryCreateRetiredTab()
        {
            Transform existing = _tabsRoot.Find("Tab Retired");
            if (existing != null) { _tabRetiredGO = existing.gameObject; return; }

            Transform template = _tabsRoot.Find("Tab Lost") ?? _tabsRoot.Find("Tab Available");
            if (template == null) { RRLog.Verbose(LOGP + "Tab template not found."); return; }

            GameObject clone = (GameObject)UnityEngine.Object.Instantiate(template.gameObject);
            clone.name = "Tab Retired";
            clone.transform.SetParent(_tabsRoot, false);
            clone.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);
            clone.SetActive(true);

            _tabRetiredGO = clone;
            RRLog.Verbose(LOGP + "Created 'Tab Retired' (cloned from '" + template.name + "').");

            var proxy = clone.AddComponent<RetiredTabClickProxy>();
            proxy.RetiredScrollList = null;  // set after scroll list created
            proxy.TabsRoot = _tabsRoot;
            proxy.OurTab = clone.transform;
            proxy.OnClicked = () => {
                _retiredTabActive = true;
                ACPatches.RetiredTabShowing = true;
                RRLog.Verbose(LOGP + "RetiredTabClickProxy: clicked!");
                OnRetiredTabSelected();
            };
            proxy.OnNativeTabActivated = () => {
                _retiredTabActive = false;
                ACPatches.RetiredTabShowing = false;
                OnRetiredTabDeselected();
            };
            _retiredProxy = proxy;
            ACPatches.RegisterProxy(proxy);

            // DO NOT join ToggleGroup — it causes ActivateList cascades that fight our proxy
        }

        private void TryJoinToggleGroup(GameObject ourTab, GameObject nativeTab)
        {
            try
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Find the ToggleGroup from any native tab toggle
                object nativeGroup = null;
                Component nativeToggle = null;
                foreach (Component c in nativeTab.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var groupProp = c.GetType().GetProperty("group", f);
                    if (groupProp == null) continue;
                    nativeGroup = groupProp.GetValue(c, null);
                    if (nativeGroup != null) { nativeToggle = c; break; }
                }

                if (nativeGroup == null)
                {
                    RRLog.Warn(LOGP + "TryJoinToggleGroup: no ToggleGroup found on native tab.");
                    return;
                }

                // Assign same group to our tab's toggle
                foreach (Component c in ourTab.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var groupProp = c.GetType().GetProperty("group", f);
                    if (groupProp == null || !groupProp.CanWrite) continue;
                    groupProp.SetValue(c, nativeGroup, null);
                    RRLog.Verbose(LOGP + "Joined ToggleGroup on Tab Retired.");
                    return;
                }
                RRLog.Warn(LOGP + "TryJoinToggleGroup: no Toggle found on our tab.");
            }
            catch (Exception ex)
            {
                RRLog.Error(LOGP + "TryJoinToggleGroup failed: " + ex);
            }
        }

        // -----------------------------------------------------------------------
        // Scroll list creation
        // -----------------------------------------------------------------------

        private void TryCreateRetiredScrollList()
        {
            if (_vesselScrollRect == null) return;

            Transform existing = _vesselScrollRect.Find("scrollList_retired");
            if (existing != null) { _scrollListRetired = existing; return; }

            // Prefer scrollList_Available as template — it has the correct
            // Viewport/Content hierarchy. Fall back to any other scrollList_ if needed.
            Transform template = null;
            Transform fallback = null;
            for (int i = 0; i < _vesselScrollRect.childCount; i++)
            {
                var ch = _vesselScrollRect.GetChild(i);
                if (ch?.name == null) continue;
                if (string.Equals(ch.name, "scrollList_retired", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(ch.name, "scrollList_Available", StringComparison.Ordinal))
                { template = ch; break; }
                if (ch.name.IndexOf("scrollList_", StringComparison.OrdinalIgnoreCase) >= 0 && fallback == null)
                    fallback = ch;
            }
            if (template == null) template = fallback;
            if (template == null) return;

            GameObject clone = (GameObject)UnityEngine.Object.Instantiate(template.gameObject);
            clone.name = "scrollList_retired";
            clone.transform.SetParent(_vesselScrollRect, false);

            // Clear any rows copied from the template
            Transform content = FindContent(clone.transform);
            if (content != null)
                for (int i = content.childCount - 1; i >= 0; i--)
                    Destroy(content.GetChild(i).gameObject);

            // Start inactive — UIListToggleController will show/hide it based on tab selection
            clone.SetActive(false);
            _scrollListRetired = clone.transform;
            RRLog.Verbose(LOGP + "Created scrollList_retired (cloned from '" + template.name + "').");

            // Wire proxy references now that scroll list exists
            if (_retiredProxy != null)
            {
                _retiredProxy.RetiredScrollList = _scrollListRetired;
                _retiredProxy.VesselScrollRect = _vesselScrollRect;
            }
            RRLog.Verbose(LOGP + "scrollList_retired path: " + GetPath(_scrollListRetired));
            RRLog.Verbose(LOGP + "_vesselScrollRect path: " + GetPath(_vesselScrollRect));
        }

        // -----------------------------------------------------------------------
        // Diagnostics (Assembly-CSharp only, v3)
        // -----------------------------------------------------------------------

        private void DumpACDiagnostics()
        {
            try
            {
                RRLog.Verbose(LOGP + "=== DIAGNOSTIC DUMP (v3) ===");
                RRLog.Verbose(LOGP + "Registration: " + (_registeredWithController ? "OK, index=" + _retiredTabIndex : "FAILED"));
                RRLog.Verbose(LOGP + "currentList field: " + (_currentListField != null ? "found" : "NOT FOUND"));
                RRLog.Verbose(LOGP + "scrollList_retired UIList: " +
                    (_scrollListRetired != null ? (FindUIListComp(_scrollListRetired) != null ? "found" : "NOT FOUND") : "no scroll list"));
                RRLog.Verbose(LOGP + "=== END DIAGNOSTIC DUMP ===");
            }
            catch (Exception ex) { RRLog.Error(LOGP + "DumpACDiagnostics failed: " + ex); }
        }

        // -----------------------------------------------------------------------
        // Data helpers
        // -----------------------------------------------------------------------

        private int GetRetiredCount()
        {
            int n = 0;
            foreach (var kvp in RosterRotationState.Records)
                if (kvp.Value != null && kvp.Value.Retired) n++;
            return n;
        }

        private List<string> GetRetiredNameSet()
        {
            var names = new List<string>();
            foreach (var kvp in RosterRotationState.Records)
                if (kvp.Value != null && kvp.Value.Retired) names.Add(kvp.Key);
            return names;
        }

        // -----------------------------------------------------------------------
        // Reflection helpers
        // -----------------------------------------------------------------------

        private static Component FindUIListComp(Transform root)
        {
            if (root == null) return null;
            foreach (Component c in root.GetComponents<Component>())
            {
                if (c == null) continue;
                string tn = c.GetType().FullName ?? c.GetType().Name;
                if (tn.IndexOf("UIList", StringComparison.OrdinalIgnoreCase) >= 0) return c;
            }
            // Check direct children only (some builds put UIList on a child)
            for (int i = 0; i < root.childCount; i++)
            {
                foreach (Component c in root.GetChild(i).GetComponents<Component>())
                {
                    if (c == null) continue;
                    string tn = c.GetType().FullName ?? c.GetType().Name;
                    if (tn.IndexOf("UIList", StringComparison.OrdinalIgnoreCase) >= 0) return c;
                }
            }
            return null;
        }

        private static object FindToggleOnGO(GameObject go)
        {
            if (go == null) return null;
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("isOn",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(bool)) continue;
                if (GetMemberValueReflect(c, "onValueChanged") != null) return c;
            }
            return null;
        }

        private static object GetMemberValueReflect(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            try { var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); if (p != null) return p.GetValue(obj, null); } catch { }
            try { var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); if (f != null) return f.GetValue(obj); } catch { }
            return null;
        }

        private static void InvokeVoidNoArgs(object obj, string method)
        {
            if (obj == null) return;
            try
            {
                var m = obj.GetType().GetMethod(method,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null) m.Invoke(obj, null);
            }
            catch { }
        }

        private static void SetBoolReflect(object obj, string name, bool value)
        {
            if (obj == null) return;
            try
            {
                var p = obj.GetType().GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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

        private static bool RowContainsRetiredName(GameObject row, List<string> names)
        {
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (string.IsNullOrEmpty(s)) continue;
                foreach (string n in names) if (s == n) return true;
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
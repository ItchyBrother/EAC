// RosterRotation - AstronautComplexACPatch (2026-02-23k)
//
// CHANGES from 2026-02-23j:
//   - Re-added Prefix_AddItem_Available (dropped in j): blocks retired kerbals
//     from being added to the Available list when KSP rebuilds it.
//   - Removed _bypassPrefix complexity (no longer needed since we use row-clone
//     strategy, not swap strategy).
//   - Cleaned up: back to 4 patches (ActivateList, AddItem_Available,
//     CreateAvailableList, UpdateCrewCounts).

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    public static class AstronautComplexACPatch
    {
        private static bool _patchesApplied = false;

        public static void Apply(Harmony h)
        {
            if (_patchesApplied) return;  // prevent double-patching
            _patchesApplied = true;
            try
            {
                System.Reflection.Assembly kspAsm = null;
                foreach (var la in AssemblyLoader.loadedAssemblies)
                {
                    if (la?.assembly?.GetName()?.Name == "Assembly-CSharp")
                    { kspAsm = la.assembly; break; }
                }
                if (kspAsm == null)
                {
                    RRLog.Warn("[RosterRotation] ACPatch: Assembly-CSharp not found.");
                    return;
                }

                int patched = 0;

                // Patch 0: UIListToggleController.ActivateList — null-safe Prefix
                Type tcType = kspAsm.GetType("KSP.UI.UIListToggleController");
                if (tcType != null)
                {
                    var activateMethod = tcType.GetMethod("ActivateList",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new Type[] { typeof(int) }, null);
                    if (activateMethod != null)
                    {
                        ACPatches.CacheToggleControllerFields(tcType);
                        h.Patch(activateMethod,
                            prefix: new HarmonyMethod(typeof(ACPatches),
                                nameof(ACPatches.Prefix_ActivateList)));
                        patched++;
                    }
                    else RRLog.Warn("[RosterRotation] ACPatch: ActivateList not found.");
                }
                else RRLog.Warn("[RosterRotation] ACPatch: UIListToggleController not found.");

                // Find AstronautComplex
                Type acType = kspAsm.GetType("KSP.UI.Screens.AstronautComplex");
                if (acType == null)
                {
                    foreach (Type x in SafeGetTypes(kspAsm))
                    {
                        if (x == null) continue;
                        string xn = x.FullName ?? x.Name;
                        if (xn.IndexOf("AstronautComplex", StringComparison.OrdinalIgnoreCase) >= 0
                            && typeof(MonoBehaviour).IsAssignableFrom(x))
                        { acType = x; break; }
                    }
                }
                if (acType == null)
                {
                    RRLog.Warn("[RosterRotation] ACPatch: AstronautComplex not found.");
                    return;
                }

                ACPatches.CacheACFields(acType);

                // NOTE: We do NOT patch AddItem_Available — we let retired kerbals into the
                // Available list so we can clone their rows, then hide them in the postfix
                // AND re-hide them every time ActivateList(0) fires.

                // Patch 2: CreateAvailableList — Postfix
                var createListMethod = acType.GetMethod("CreateAvailableList",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (createListMethod != null)
                {
                    ACPatches._createAvailableListMethod = createListMethod;
                    h.Patch(createListMethod,
                        postfix: new HarmonyMethod(typeof(ACPatches),
                            nameof(ACPatches.Postfix_CreateAvailableList)));
                    patched++;
                }
                else RRLog.Warn("[RosterRotation] ACPatch: CreateAvailableList not found.");

                // Patch 3: UpdateCrewCounts — Postfix
                var updateCountsMethod = acType.GetMethod("UpdateCrewCounts",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (updateCountsMethod != null)
                {
                    ACPatches._updateCrewCountsMethod = updateCountsMethod;
                    h.Patch(updateCountsMethod,
                        postfix: new HarmonyMethod(typeof(ACPatches),
                            nameof(ACPatches.Postfix_UpdateCrewCounts)));
                    patched++;
                }
                else RRLog.Warn("[RosterRotation] ACPatch: UpdateCrewCounts not found.");

                // Patch 4: CreateApplicantList — Postfix
                // KSP sets the hire button's interactable state in here, using a count that
                // includes retired kerbals. We fix the hire button state in the postfix.
                var createApplicantMethod = acType.GetMethod("CreateApplicantList",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (createApplicantMethod != null)
                {
                    ACPatches._createApplicantListMethod = createApplicantMethod;
                    h.Patch(createApplicantMethod,
                        postfix: new HarmonyMethod(typeof(ACPatches),
                            nameof(ACPatches.Postfix_CreateApplicantList)));
                    patched++;
                }
                else RRLog.Warn("[RosterRotation] ACPatch: CreateApplicantList not found.");

            }
            catch (Exception ex)
            {
                _patchesApplied = false;  // allow retry on next call
                RRLog.Error("[RosterRotation] ACPatch.Apply failed: " + ex);
            }
        }

        private static Type[] SafeGetTypes(System.Reflection.Assembly a)
        {
            try { return a.GetTypes(); } catch { return new Type[0]; }
        }
    }

    internal static class ACPatches
    {
        // UIListToggleController
        private static FieldInfo _tcListsField;
        private static FieldInfo _tcCurrentListField;

        // AstronautComplex
        private static FieldInfo _activeCrewsField;
        private static FieldInfo _activeCrewsCountField;
        private static FieldInfo _maxActiveCrewsField;

        // UIList.SetActive cache
        private static MethodInfo _uiListSetActiveMethod;
        private static bool _uiListSetActiveSearched;

        // Set by RetiredTabClickProxy to block ActivateList while retired tab shows
        public static bool RetiredTabShowing = false;
        public static float RetiredTabShowTime = -1f;
        private const float RETIRED_GRACE = 1.5f;
        private static bool _refreshing = false;  // true while ForceRefresh is running — suppress ActivateList side effects
        private static object _blockedInstance;
        public static RetiredTabClickProxy _retiredProxy;
        public static Transform RetiredListTransform = null;  // set once at creation, never by name
        public static float LastRetiredRowH = 54f;           // most recently measured single-row height, for post-show repositioning

        public static Transform AvailListTransform = null;
        public static Transform ApplicantsListTransform = null;
        public static Transform LostListTransform  = null;
        private static object _cachedACInstance = null;  // for forced refresh
        public static MethodInfo _createAvailableListMethod = null;
        public static MethodInfo _updateCrewCountsMethod = null;
        public static MethodInfo _createApplicantListMethod = null;
        private static int _cachedMaxCrew = int.MaxValue; // updated every UpdateCrewCounts call
        private static float _cachedFacilityLevel = float.NaN; // track AC facility level to refresh cap after upgrades
        private static bool _warnedNoHireButtons;
        private static bool _warnedMaxCrewUnknown;
        private static bool _populatingRetiredList = false; // re-entrancy guard for clone block

        public static void RegisterProxy(RetiredTabClickProxy proxy)
        {
            _retiredProxy = proxy;
        }

        // Called by RetiredTabClickProxy.ShowRetiredList() AFTER SetActive(true).
        // OnEnable() resets wired fields on UIStateButtonTooltip, so we must re-wire after activation.
        public static void RewireTooltipsInRetiredList(Transform retiredList)
        {
            if (retiredList == null) return;
            try
            {
                double nowUT = Planetarium.GetUniversalTime();
                foreach (Transform row in retiredList)
                {
                    if (row == null) continue;
                    // Find Button GO (for UIStateButton and Button)
                    Transform btnT = null;
                    foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
                        if (ch.name == "Button") { btnT = ch; break; }
                    if (btnT == null) continue;

                    // Get components from Button GO
                    Component uisb = null, btn = null, btnImg = null, tooltip = null;
                    foreach (Component c in btnT.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        if (c.GetType().Name == "UIStateButton")        uisb    = c;
                        if (c.GetType().Name == "Button")               btn     = c;
                        if (c.GetType().Name == "Image")                btnImg  = c;
                        if (c.GetType().Name == "UIStateButtonTooltip") tooltip = c;
                    }

                    // UIStateButtonTooltip stays on Button — EventTriggerForwarder forwards
                    // PointerEnter from DragObject (raycast target) down to Button.

                    // --- Re-apply UIStateButton state (OnEnable resets currentStateIndex to 0) ---
                    int correctStateIdx = 0;
                    string correctStateName = "X";
                    if (uisb != null && btnImg != null)
                    {
                        var spriteProp = btnImg.GetType().GetProperty("sprite", BindingFlags.Instance | BindingFlags.Public);
                        if (spriteProp != null)
                        {
                            var sprite = spriteProp.GetValue(btnImg, null) as UnityEngine.Object;
                            if (sprite != null && sprite.name != null && sprite.name.EndsWith("_v", System.StringComparison.OrdinalIgnoreCase))
                            {
                                correctStateIdx = 1;
                                correctStateName = "V";
                            }
                        }

                        var csiF = uisb.GetType().GetField("currentStateIndex",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (csiF != null) try { csiF.SetValue(uisb, correctStateIdx); } catch { }

                        var csF = uisb.GetType().GetField("currentState",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (csF != null) try { csF.SetValue(uisb, correctStateName); } catch { }

                        var ssF = uisb.GetType().GetField("stateSet",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (ssF != null) try { ssF.SetValue(uisb, true); } catch { }

                    }

                    if (tooltip == null) continue;

                    // RequireInteractable = false so tooltip fires without a valid selectableBase
                    foreach (string fn in new[] { "RequireInteractable", "requireInteractable" })
                    {
                        var f = tooltip.GetType().GetField(fn,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (f != null) try { f.SetValue(tooltip, false); break; } catch { }
                    }

                    // Wire stateButton so tooltip reads currentStateIndex
                    if (uisb != null)
                    {
                        var sbF = tooltip.GetType().GetField("stateButton",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (sbF != null) try { sbF.SetValue(tooltip, uisb); } catch { }
                    }

                    // Wire tooltipPrefab if null
                    var prefabF = tooltip.GetType().GetField("tooltipPrefab",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prefabF != null)
                    {
                        var prefabVal = prefabF.GetValue(tooltip);
                        if (prefabVal == null)
                        {
                            foreach (Component tc in UnityEngine.Object.FindObjectsOfType(tooltip.GetType()))
                            {
                                if (tc == tooltip || tc == null) continue;
                                var pf = prefabF.GetValue(tc);
                                if (pf != null) { try { prefabF.SetValue(tooltip, pf); prefabVal = pf; } catch { } break; }
                            }
                        }
                        else
                        {
                        }
                    }

                    // Wire selectableBase to Button so interactable check passes
                    if (btn != null)
                    {
                        var selF = tooltip.GetType().GetField("selectableBase",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (selF != null) try { selF.SetValue(tooltip, btn); } catch { }
                    }

                    // Force OnEnable by cycling enabled false→true. UIStateButtonTooltip
                    // registers itself with KSP's tooltip manager in OnEnable. When the
                    // scroll list was SetActive(false) the GO went inactive and OnDisable
                    // fired, deregistering it. Just setting enabled=true won't re-register
                    // because the property setter only fires OnEnable if value changes from
                    // false to true — so we must explicitly cycle it.
                    var enabledP = tooltip.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
                    if (enabledP != null)
                    {
                        try { enabledP.SetValue(tooltip, false, null); } catch { }
                        try { enabledP.SetValue(tooltip, true, null); } catch { }
                    }

                    // Re-apply retired kerbal stars AFTER SetActive(true).
                    // OnEnable resets UIStateImage.currentStateIndex to 0, so we must set it again
                    // after activation using our effective (decayed) star count.
                    try
                    {
                        string kName = GetKerbalNameFromRow(row.gameObject);
                        if (!string.IsNullOrEmpty(kName)
                            && RosterRotationState.Records.TryGetValue(kName, out var rec)
                            && rec != null)
                        {
                            ProtoCrewMember k = FindKerbalByName(kName);
                            int effStars = GetRetiredEffectiveStarsSafe(k, rec, nowUT);
                            SetStarsState(row.gameObject, effStars);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                RRLog.WarnOnce("ac.rewire.fail", "[RosterRotation] RewireTooltipsInRetiredList failed: " + ex.Message);
            }
        }

        public static void RepositionRetiredRows(Transform retiredList)
        {
            if (retiredList == null) return;
            try
            {
                // Do NOT call ForceUpdateCanvases — it triggers UIStateImage/UIHoverPanel
                // OnEnable/Rebuild callbacks that undo our sprite and enable state settings.
                // VLG and CSF are already disabled so no layout system competes with us.

                // Read rowH from sizeDelta (set during cloning), not rect.height (layout-driven).
                float rowH = LastRetiredRowH > 1f ? LastRetiredRowH : 72f;
                for (int i = 0; i < retiredList.childCount; i++)
                {
                    Transform row = retiredList.GetChild(i);
                    if (row == null || !row.gameObject.activeSelf) continue;
                    RectTransform rt = row.GetComponent<RectTransform>();
                    if (rt != null && rt.sizeDelta.y > 1f && rt.sizeDelta.y <= 120f)
                    {
                        rowH = rt.sizeDelta.y;
                        break;
                    }
                }
                LastRetiredRowH = rowH;

                float yOffset = 0f;
                int count = 0;
                for (int i = 0; i < retiredList.childCount; i++)
                {
                    Transform row = retiredList.GetChild(i);
                    if (row == null || !row.gameObject.activeSelf) continue;
                    RectTransform rt = row.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    rt.anchorMin        = new Vector2(0f, 1f);
                    rt.anchorMax        = new Vector2(1f, 1f);
                    rt.pivot            = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, yOffset);
                    rt.sizeDelta        = new Vector2(0f, rowH);
                    yOffset -= rowH;
                    count++;
                    // Ensure UIHoverPanel is disabled on every display pass
                    DisableUIHoverPanel(row.gameObject);
                }

                RectTransform listRT = retiredList.GetComponent<RectTransform>();
                if (listRT != null && count > 0)
                    listRT.sizeDelta = new Vector2(listRT.sizeDelta.x, rowH * count);
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterRotation] RepositionRetiredRows failed: " + ex.Message);
            }
        }

        // UIHoverPanel field dump revealed:
        //   backgroundImage  — the Image whose sprite it toggles (DragObject.Image)
        //   backgroundNormal — transparent sprite shown when NOT hovered
        //   backgroundHover  — visible border sprite shown when hovered
        //   hoverEnabled     — gates the sprite swap AND the onPointerEnter delegate
        //   onPointerEnter/Exit — delegates the tooltip system subscribes to
        //
        // The border disappears because PointerExit sets backgroundImage.sprite = backgroundNormal
        // (transparent). The tooltip fires via onPointerEnter delegate when hoverEnabled=true.
        //
        // Fix: swap backgroundNormal = backgroundHover sprite so BOTH enter and exit write
        // the visible sprite. Keep hoverEnabled=true so onPointerEnter fires for tooltips.
        // Also destroy EventTriggerForwarder (holds UIHoverPanel ref, was forwarding hover-
        // highlight events — no longer needed since UIHoverPanel is handling itself).
        public static void DestroyUIHoverPanel(GameObject row)
        {
            if (row == null) return;
            try
            {
                foreach (Component c in row.GetComponents<Component>())
                {
                    if (c == null || c.GetType().Name != "UIHoverPanel") continue;

                    Type t = c.GetType();

                    // Swap backgroundNormal to the hover sprite so the border is always visible.
                    // UIHoverPanel.PointerExit() writes backgroundNormal back to backgroundImage.sprite;
                    // with backgroundNormal = backgroundHover both enter and exit show the border.
                    // hoverEnabled stays TRUE so onPointerEnter still fires for tooltip delivery.
                    var bgNF = t.GetField("backgroundNormal",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var bgHF = t.GetField("backgroundHover",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (bgNF != null && bgHF != null)
                    {
                        try
                        {
                            var hoverSprite = bgHF.GetValue(c);
                            if (hoverSprite != null) bgNF.SetValue(c, hoverSprite);
                        }
                        catch { }
                    }

                    // Set backgroundImage.sprite to the hover sprite immediately.
                    var bgIF = t.GetField("backgroundImage",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (bgIF != null && bgHF != null)
                    {
                        try
                        {
                            var img = bgIF.GetValue(c);
                            var hoverSprite = bgHF.GetValue(c);
                            if (img != null && hoverSprite != null)
                            {
                                var spriteP = img.GetType().GetProperty("sprite",
                                    BindingFlags.Instance | BindingFlags.Public);
                                if (spriteP != null) spriteP.SetValue(img, hoverSprite, null);
                                var enabledP = img.GetType().GetProperty("enabled",
                                    BindingFlags.Instance | BindingFlags.Public);
                                if (enabledP != null) enabledP.SetValue(img, true, null);
                            }
                        }
                        catch { }
                    }

                    // CRITICAL: Clear hoverObjects so UIHoverPanel.PointerExit() stops calling
                    // SetActive(false) on the Button GO.  In stock AC rows the Button is in this
                    // list so it only appears on hover; for our Recall button we always want it
                    // visible.  Clearing the list has no effect on tooltip delivery (that uses
                    // onPointerEnter, gated by hoverEnabled which stays true).
                    foreach (string fieldName in new[] { "hoverObjects", "_hoverObjects", "HoverObjects" })
                    {
                        var hoF = t.GetField(fieldName,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (hoF == null) continue;
                        var hoVal = hoF.GetValue(c);
                        if (hoVal == null) break;
                        // Clear via reflection — works for List<T> or array
                        var clearM = hoVal.GetType().GetMethod("Clear",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (clearM != null)
                        {
                            try { clearM.Invoke(hoVal, null); }
                            catch { }
                        }
                        break;
                    }

                    break;
                }

                // Destroy EventTriggerForwarder from DragObject.
                for (int i = 0; i < row.transform.childCount; i++)
                {
                    Transform dragObj = row.transform.GetChild(i);
                    if (dragObj == null || dragObj.name != "DragObject") continue;
                    foreach (Component c in dragObj.GetComponents<Component>())
                    {
                        if (c == null || c.GetType().Name != "EventTriggerForwarder") continue;
                        UnityEngine.Object.DestroyImmediate(c);
                        break;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterRotation] DestroyUIHoverPanel failed: " + ex.Message);
            }
        }

        public static void DisableUIHoverPanel(GameObject row) { DestroyUIHoverPanel(row); }
        public static void AttachDragObjectFixer(GameObject row) { DestroyUIHoverPanel(row); }

        // Kept for diagnostics only
        private static void ForceRowImagesVisible(GameObject row, bool dump)
        {
            if (dump)
            {
                try
                {
                    var sb = new System.Text.StringBuilder("[RosterRotation] RowHierarchyDump: " + row.name + "\n");
                    DumpHierarchy(row.transform, sb, "  ");
                } catch { }
            }
            DisableUIHoverPanel(row);
        }

        private static void DumpHierarchy(Transform t, System.Text.StringBuilder sb, string indent)
        {
            if (t == null || indent.Length > 20) return;
            sb.Append(indent).Append(t.name).Append(" active=").Append(t.gameObject.activeSelf);
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                sb.Append(" [").Append(tn);
                if (tn == "Image" || tn == "RawImage")
                {
                    var ep = c.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
                    var cp = c.GetType().GetProperty("color",   BindingFlags.Instance | BindingFlags.Public);
                    if (ep != null) try { sb.Append(" en=").Append((bool)ep.GetValue(c, null)); } catch { }
                    if (cp != null) try { var col = (Color)cp.GetValue(c, null); sb.Append(" a=").Append(col.a.ToString("F2")); } catch { }
                }
                sb.Append("]");
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++)
                DumpHierarchy(t.GetChild(i), sb, indent + "  ");
        }

        public static void CacheToggleControllerFields(Type tcType)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _tcListsField       = tcType.GetField("lists",       f);
            _tcCurrentListField = tcType.GetField("currentList", f);
        }

        public static void CacheACFields(Type acType)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _activeCrewsField      = acType.GetField("activeCrews",         f);
            _activeCrewsCountField = acType.GetField("activeCrewsCount",    f);
            _maxActiveCrewsField   = acType.GetField("maxActiveCrewCount",  f)
                                  ?? acType.GetField("maxCrewCount",        f)
                                  ?? acType.GetField("maxActiveCrews",      f);
        }

        // Called by Mod.cs to check cap before Hire/Recall
        public static int GetCachedMaxCrew()
        {
            // _cachedMaxCrew is updated every UpdateCrewCounts postfix call
            if (_cachedMaxCrew < int.MaxValue) return _cachedMaxCrew;
            // Fallback: try reading from the cached AC instance directly
            if (_cachedACInstance != null && _maxActiveCrewsField != null)
                try { return (int)_maxActiveCrewsField.GetValue(_cachedACInstance); }
                catch { }
            // Final fallback: ask KSP's GameVariables for the AC-level crew limit
            int cap2 = TryGetMaxCrewFromGameVariables();
            if (cap2 > 0) { _cachedMaxCrew = cap2; return cap2; }
            return int.MaxValue;
        }

        // Ensures _cachedMaxCrew is populated before FixHireButtons is called.
        // GameVariables crew cap fallback
private static int TryGetMaxCrewFromGameVariables()
{
    try
    {
        if (GameVariables.Instance == null) return 0;

        float level = ScenarioUpgradeableFacilities.GetFacilityLevel(
            SpaceCenterFacility.AstronautComplex);

        // Facility upgrades can happen mid-session; refresh cached cap when level changes.
        if (!float.IsNaN(level) && level != _cachedFacilityLevel)
        {
            _cachedFacilityLevel = level;
            _cachedMaxCrew = int.MaxValue;
        }

        // KSP versions differ on whether this level is 0..2 or 1..3. Try a few mappings.
        float lvl0 = level;
        float lvl1 = level + 1f;
        float lvlM = Mathf.Max(0f, level - 1f);

        foreach (float lv in new float[] { lvl0, lvl1, lvlM })
        {
            int cap = GameVariables.Instance.GetActiveCrewLimit(lv);
            if (cap > 0) return cap;
        }
    }
    catch { }
    return 0;
}

// Ensures _cachedMaxCrew is populated before FixHireButtons is called.
private static void EnsureMaxCrewCached()
{
    // If we have a cached cap, still invalidate it if the facility level changed.
    if (_cachedMaxCrew < int.MaxValue)
    {
        try
        {
            float level = ScenarioUpgradeableFacilities.GetFacilityLevel(
                SpaceCenterFacility.AstronautComplex);
            if (!float.IsNaN(level) && level != _cachedFacilityLevel)
            {
                _cachedFacilityLevel = level;
                _cachedMaxCrew = int.MaxValue;
            }
        }
        catch { }
        if (_cachedMaxCrew < int.MaxValue) return;
    }

    int cap2 = TryGetMaxCrewFromGameVariables();
    if (cap2 > 0) _cachedMaxCrew = cap2;
}

    // Parses "[Max: N]" from a count label string — used as last-resort cap source.
    private static bool TryParseMaxFromLabel(string labelText, out int max)
{
    max = 0;
    if (string.IsNullOrEmpty(labelText)) return false;

    // Strip rich-text tags (e.g., <color>, <b>) to make parsing robust.
    string s = System.Text.RegularExpressions.Regex.Replace(labelText, "<.*?>", "");

    // Common formats:
    //   "Active Kerbals: 4 [Max: 5]"
    //   "Active Kerbals: 4 [MAX: 5}"
    //   "Active Kerbals: 4 / 5"
    var opt = System.Text.RegularExpressions.RegexOptions.IgnoreCase;

    var m1 = System.Text.RegularExpressions.Regex.Match(s, @"max[^0-9]{0,10}(\d+)", opt);
    if (m1.Success && int.TryParse(m1.Groups[1].Value, out int n1) && n1 > 0)
    { max = n1; return true; }

    var m2 = System.Text.RegularExpressions.Regex.Match(s, @"\b(\d+)\s*/\s*(\d+)\b", opt);
    if (m2.Success && int.TryParse(m2.Groups[2].Value, out int n2) && n2 > 0)
    { max = n2; return true; }

    return false;
}

// ---------------------------------------------------------------

        // Prefix: ActivateList — null-safe replacement
        // For our cloned retired list (last index), UIList.SetActive is
        // broken so we skip it and use gameObject.SetActive directly.
        // ---------------------------------------------------------------
        public static bool Prefix_ActivateList(object __instance, int index)
        {
            if (RetiredTabShowing)
            {
                // If within grace period, this is the ToggleGroup auto-reselecting — block it silently
                if ((Time.realtimeSinceStartup - RetiredTabShowTime) < RETIRED_GRACE)
                {
                    return false;
                }
                // If we're in a programmatic refresh, don't treat this as a real user tab click
                if (_refreshing)
                {
                    return false;
                }
                // After grace period, this is a real user click on a native tab
                RetiredTabShowing = false;
                _blockedInstance = __instance;
                if (_retiredProxy != null)
                    _retiredProxy.HandleNativeTabActivated(index);
                // Fall through — let ActivateList proceed normally
            }

            try
            {
                if (_tcListsField == null) return true;
                var lists = _tcListsField.GetValue(__instance) as System.Array;
                if (lists == null) return true;

                if (_tcCurrentListField != null)
                    _tcCurrentListField.SetValue(__instance, index);

                for (int i = 0; i < lists.Length; i++)
                {
                    object uiList = lists.GetValue(i);
                    if (uiList == null) continue;
                    bool active = (i == index);
                    bool isOurList = (i == lists.Length - 1);

                    if (!isOurList)
                    {
                        if (!_uiListSetActiveSearched)
                        {
                            _uiListSetActiveSearched = true;
                            _uiListSetActiveMethod = uiList.GetType().GetMethod("SetActive",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                null, new Type[] { typeof(bool) }, null);
                        }
                        if (_uiListSetActiveMethod != null)
                            try { _uiListSetActiveMethod.Invoke(uiList, new object[] { active }); } catch { }

                        // Capture the Lost list content from lists[2]'s UIList
                        // lists[i] is a UIList on the TAB BUTTON — we reflect to find content.
                        if (i == 2 && LostListTransform == null)
                        {
                            LostListTransform = FindUIListContent(uiList);
                        }

                        // After activating the Available list, re-hide any retired rows
                        // UIList.SetActive(true) may re-show all child GameObjects
                        if (active && i == 0 && AvailListTransform != null)
                        {
                            var retiredNames = GetRetiredNames();
                            for (int r = 0; r < AvailListTransform.childCount; r++)
                            {
                                Transform row = AvailListTransform.GetChild(r);
                                if (row == null || !row.gameObject.activeSelf) continue;
                                if (RowContainsName(row.gameObject, retiredNames))
                                {
                                    row.gameObject.SetActive(false);
                                }
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            var comp = uiList as Component;
                            if (comp != null)
                            {
                                if (active || !AstronautComplexRetiredTab.RetiredTabShowing)
                                {
                                    comp.gameObject.SetActive(active);
                                }
                            }
                        }
                        catch { }
                    }
                }

                // AFTER activation: patch Lost tab labels if Lost tab was just shown
                if (index == 2)
                {
                    PatchLostListStatusText();
                }

                return false;
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] Prefix_ActivateList failed: " + ex);
                return true;
            }
        }

        // ---------------------------------------------------------------
        // Prefix: AddItem_Available — block retired kerbals
        // ---------------------------------------------------------------
        public static bool Prefix_AddItem_Available(ProtoCrewMember crew)
        {
            try
            {
                if (crew == null) return true;
                if (crew.type == ProtoCrewMember.KerbalType.Applicant) return true;
                if (RosterRotationState.Records.TryGetValue(crew.name, out var rec)
                    && rec != null && rec.Retired)
                    return false;
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] Prefix_AddItem_Available failed: " + ex);
            }
            return true;
        }

        // ---------------------------------------------------------------
        // Postfix: CreateAvailableList
        // ---------------------------------------------------------------
        public static void Postfix_CreateAvailableList(object __instance)
        {
            try
            {
                if (__instance == null) return;
                var go = (__instance as MonoBehaviour)?.gameObject;
                if (go == null) return;
                _cachedACInstance = __instance;  // cache for forced refresh

                var retiredNames = GetRetiredNames();

                // Find available list (rows are direct children)
                Transform availList = FindDescendant(go.transform, "scrollList_Available");

                // Measure actual row height from Available row.
                // Prefer rect.height (layout-computed, reflects actual rendered height).
                // Fall back to sizeDelta.y if rect.height is 0 (layout not yet run).
                float ROW_H = LastRetiredRowH > 1f ? LastRetiredRowH : 72f;
                if (availList != null)
                {
                    for (int i = 0; i < availList.childCount; i++)
                    {
                        Transform sample = availList.GetChild(i);
                        if (sample == null || !sample.gameObject.activeSelf) continue;
                        RectTransform sampleRT = sample.GetComponent<RectTransform>();
                        if (sampleRT == null) continue;
                        float h = sampleRT.rect.height > 1f ? sampleRT.rect.height : sampleRT.sizeDelta.y;
                        if (h > 1f && h <= 120f)
                        {
                            ROW_H = h;
                            LastRetiredRowH = ROW_H;
                            break;
                        }
                    }
                }

                // Hide retired rows from Available AFTER measuring
                if (availList != null && retiredNames.Count > 0)
                {
                    int hidden = 0;
                    for (int i = 0; i < availList.childCount; i++)
                    {
                        Transform row = availList.GetChild(i);
                        if (row == null || !row.gameObject.activeSelf) continue;
                        if (RowContainsName(row.gameObject, retiredNames))
                        { row.gameObject.SetActive(false); hidden++; }
                    }
                }

                if (availList == null)
                {
                    RRLog.Warn("[RosterRotation] ACPatch: scrollList_Available not found.");
                    FixAvailableBadge(go, retiredNames);
                    return;
                }

                // Find or CREATE scrollList_retired as a sibling of scrollList_Available (crew panel)
                Transform vesselScrollRect = availList?.parent;
                AvailListTransform = availList;  // cache for re-hiding in Prefix_ActivateList

                // Cache applicants list transform for hire-button fixes (needed after AC upgrades)
                if (ApplicantsListTransform == null && vesselScrollRect != null)
                {
                    for (int i = 0; i < vesselScrollRect.childCount; i++)
                    {
                        Transform ch = vesselScrollRect.GetChild(i);
                        if (ch == null) continue;
                        string nm = ch.name ?? "";
                        if (nm.IndexOf("Applicant", StringComparison.OrdinalIgnoreCase) >= 0
                            || nm.IndexOf("applicant", StringComparison.OrdinalIgnoreCase) >= 0)
                        { ApplicantsListTransform = ch; break; }
                    }
                }

                // --- Diagnostic: dump all siblings under VesselScrollRect ---
                if (vesselScrollRect != null)
                {
                    for (int i = 0; i < vesselScrollRect.childCount; i++)
                    {
                        Transform ch = vesselScrollRect.GetChild(i);
                    }
                }

                // --- Discover the Lost list from UIListToggleController array (index 2) ---
                // The stock list name is NOT "scrollList_lost" — it's an auto-generated name.
                // We must get it from the UIListToggleController's lists array.
                if (LostListTransform == null)
                {
                    LostListTransform = DiscoverLostListFromController(go);
                    if (LostListTransform == null)
                        RRLog.VerboseOnce("ac.lost.discoverfail", "[RosterRotation] ACPatch: LostListTransform could NOT be discovered.");
                }

                // --- Fallback: try name-based search ---
                if (LostListTransform == null)
                {
                    Transform lostList = FindDescendant(go.transform, "scrollList_lost");
                    if (lostList != null)
                    {
                        LostListTransform = lostList;
                    }
                }

                // --- Fallback 2: try any sibling that isn't Available, Applicant, or retired ---
                if (LostListTransform == null && vesselScrollRect != null)
                {
                    for (int i = 0; i < vesselScrollRect.childCount; i++)
                    {
                        Transform ch = vesselScrollRect.GetChild(i);
                        if (ch == null) continue;
                        string chn = ch.name ?? "";
                        if (chn == "scrollList_Available") continue;
                        if (chn == "scrollList_retired") continue;
                        if (chn.IndexOf("Applicant", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        // Check if this list has any rows with dead kerbals
                        if (HasDeadKerbalRow(ch))
                        {
                            LostListTransform = ch;
                            break;
                        }
                    }
                }

                Transform retiredList = null;
                if (vesselScrollRect != null)
                {
                    for (int i = 0; i < vesselScrollRect.childCount; i++)
                    {
                        Transform ch = vesselScrollRect.GetChild(i);
                        if (ch != null && ch.name == "scrollList_retired") { retiredList = ch; break; }
                    }
                }
                if (retiredList == null && availList != null && vesselScrollRect != null)
                {
                    GameObject clone = UnityEngine.Object.Instantiate(availList.gameObject, vesselScrollRect);
                    clone.name = "scrollList_retired";
                    clone.SetActive(false);
                    for (int i = clone.transform.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.Destroy(clone.transform.GetChild(i).gameObject);
                    // Disable layout components so manual row positioning works
                    foreach (Component c in clone.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        string tn = c.GetType().Name;
                        if (tn.Contains("LayoutGroup") || tn.Contains("ContentSizeFitter"))
                        {
                            var ep = c.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
                            if (ep != null) try { ep.SetValue(c, false, null); } catch { }
                        }
                    }
                    retiredList = clone.transform;
                    RetiredListTransform = retiredList;  // store IMMEDIATELY before any corruption
                }
                else if (retiredList != null)
                {
                    RetiredListTransform = retiredList;
                }
                if (retiredList != null && availList != null)
                {
                    // Copy RectTransform from scrollList_Available so retired list
                    // occupies the same screen area (cloned from applicants which differs)
                    RectTransform availRT = availList as RectTransform
                        ?? availList.GetComponent<RectTransform>();
                    RectTransform retiredRT = retiredList as RectTransform
                        ?? retiredList.GetComponent<RectTransform>();
                    if (availRT != null && retiredRT != null)
                    {
                        retiredRT.anchorMin        = availRT.anchorMin;
                        retiredRT.anchorMax        = availRT.anchorMax;
                        retiredRT.anchoredPosition = availRT.anchoredPosition;
                        retiredRT.sizeDelta        = availRT.sizeDelta;
                        retiredRT.pivot            = availRT.pivot;
                    }
                    // DO NOT call CopyLayoutComponents — it copies the 'name' property and overwrites 'scrollList_retired'
                    // Layout groups were already disabled during creation above

                    // Safety: re-assert name in case anything overwrote it
                    retiredList.gameObject.name = "scrollList_retired";

                    // Re-entrancy guard: KSP calls CreateAvailableList more than once per
                    // refresh cycle (roster change, SaveGame, tab switch).  Only the FIRST
                    // call should rebuild the retired list; suppress all re-entrant calls.
                    if (_populatingRetiredList)
                    {
                        FixAvailableBadge(go, retiredNames);
                        return;
                    }
                    _populatingRetiredList = true;
                    try
                    {
                    // Clear previous clones synchronously (deferred Destroy causes duplicates)
                    for (int i = retiredList.childCount - 1; i >= 0; i--)
                    {
                        Transform ch = retiredList.GetChild(i);
                        if (ch != null) UnityEngine.Object.DestroyImmediate(ch.gameObject);
                    }

                    // Use ROW_H measured from active Available rows above

                    int moved = 0;
                    float yOffset = 0f;
                    // KSP sometimes appends to the Available list rather than rebuilding it,
                    // producing duplicate rows for the same kerbal. Track cloned names to skip dupes.
                    var clonedNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < availList.childCount; i++)
                    {
                        Transform row = availList.GetChild(i);
                        if (row == null) continue;
                        if (RowContainsName(row.gameObject, retiredNames))
                        {
                            // Get name before instantiating to check for duplicates
                            string preCheckName = GetKerbalNameFromRow(row.gameObject);
                            if (preCheckName != null && !clonedNames.Add(preCheckName))
                            {
                                continue; // already cloned this kerbal
                            }

                            GameObject clone = UnityEngine.Object.Instantiate(row.gameObject, retiredList);
                            clone.SetActive(true);

                            // DestroyImmediate — regular Destroy is deferred to end-of-frame,
                            // meaning CrewListItem runs one more Update() and resets our star/label changes.
                            var toDestroy = new System.Collections.Generic.List<Component>();
                            foreach (Component c in clone.GetComponentsInChildren<Component>(true))
                                if (c != null && (c.GetType().Name == "CrewListItem"
                                               || c.GetType().Name == "TooltipController_CrewAC"))
                                    toDestroy.Add(c);
                            foreach (var c in toDestroy)
                                try { UnityEngine.Object.DestroyImmediate(c); } catch { }

                            // Find the kerbal for this row
                            string rowKerbalName = GetKerbalNameFromRow(clone);
                            double nowUT = Planetarium.GetUniversalTime();
                            ProtoCrewMember rowKerbal = null;
                            if (rowKerbalName != null && HighLogic.CurrentGame?.CrewRoster != null)
                                foreach (var pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                                    if (pcm != null && pcm.name == rowKerbalName) { rowKerbal = pcm; break; }

                            if (rowKerbal != null)
                            {
                                RosterRotationState.Records.TryGetValue(rowKerbal.name, out var rec);

                                // --- Status label (TextMeshProUGUI on GO='label') ---
                                string newStatus = "Retired";
                                if (rec != null && rec.RetiredUT > 0)
                                    newStatus = "Retired " + FormatTimeAgoStatic(rec.RetiredUT, nowUT);

                                // Age display
                                if (rec != null && rec.LastAgedYears >= 0 && RosterRotationState.AgingEnabled)
                                {
                                    int age = RosterRotationState.GetKerbalAge(rec, nowUT);
                                    newStatus = $"Age {age}  " + newStatus;
                                    if (rec.DeathUT > 0)
                                        newStatus = $"Died Age: {age}, {RosterRotationState.FormatGameDateYM(rec.DeathUT)}";
                                }

                                // Eligibility hint on second line — visible without needing tooltip hover
                                int effStars = RosterRotationState.GetRetiredEffectiveStars(rowKerbal, rec, nowUT);
                                newStatus += effStars > 0 ? "\nEligible for Recall" : "\nNot Eligible for Recall";
                                SetTextOnGO(clone, "label", newStatus);

                                // --- Stars ---
                                SetStarsState(clone, effStars);

                                // --- Recall button ---
                                WireRecallButton(clone, rowKerbal, effStars);
                            }

                            // Disable UIHoverPanel — its Update/LateUpdate continuously disables
                            // DragObject.Image when no mouse hover is active, killing both the
                            // border visual and GraphicRaycaster hit detection. We don't need
                            // hover highlighting on retired rows.
                            DisableUIHoverPanel(clone);

                            RectTransform cloneRT = clone.GetComponent<RectTransform>();
                            if (cloneRT != null)
                            {
                                cloneRT.anchorMin        = new Vector2(0, 1);
                                cloneRT.anchorMax        = new Vector2(1, 1);
                                cloneRT.pivot            = new Vector2(0.5f, 1f);
                                cloneRT.sizeDelta        = new Vector2(0, ROW_H);
                                cloneRT.anchoredPosition = new Vector2(0, yOffset);
                                yOffset -= ROW_H;
                            }
                            moved++;
                        }
                    }
                    // Disable any layout group on the retired list — it fights manual row positions
                    foreach (Component c in retiredList.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        var enabledProp = c.GetType().GetProperty("enabled",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (enabledProp == null || !enabledProp.CanWrite) continue;
                        string typeName = c.GetType().Name;
                        if (typeName.Contains("LayoutGroup") || typeName.Contains("ContentSizeFitter"))
                        {
                            try { enabledProp.SetValue(c, false, null); } catch { }
                        }
                    }

                    // Set explicit height — Available's height is 0 when inactive (content-driven)
                    if (retiredRT != null && moved > 0)
                    {
                        float totalH = Mathf.Abs(yOffset);
                        retiredRT.sizeDelta = new Vector2(retiredRT.sizeDelta.x, totalH);
                    }

                    FixRetiredBadge(go, moved);

                    // Update proxy using static ref set at creation — name-based lookup gets corrupted
                    var proxy = UnityEngine.Object.FindObjectOfType<RetiredTabClickProxy>();
                    Transform safeRef = RetiredListTransform;
                    if (proxy != null && safeRef != null)
                    {
                        proxy.RetiredScrollList = safeRef;
                        proxy.VesselScrollRect = vesselScrollRect;
                    }
                    else RRLog.Warn("[RosterRotation] ACPatch: proxy=" + (proxy == null ? "NULL" : "OK") + " safeRef=" + (safeRef == null ? "NULL" : safeRef.name));
                    if (_retiredProxy != null && safeRef != null)
                    {
                        _retiredProxy.RetiredScrollList = safeRef;
                        _retiredProxy.VesselScrollRect = vesselScrollRect;
                    }
                    } // end _populatingRetiredList guard
                    finally { _populatingRetiredList = false; }
                } // end if (retiredList != null && availList != null)

                // Available badge is fixed in Postfix_UpdateCrewCounts which runs after KSP sets it
                // Patch "In training" status text on recalled kerbals in Available list
                PatchAvailableListStatusText();
                PatchLostListStatusText();
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] Postfix_CreateAvailableList failed: " + ex);
            }
        }

        // ---------------------------------------------------------------
        // Postfix: UpdateCrewCounts
        // ---------------------------------------------------------------
        public static void Postfix_UpdateCrewCounts(object __instance)
        {
            try
            {
                if (__instance == null || _activeCrewsField == null) return;

                // Ensure the crew cap is cached (field lookup may have failed; GameVariables is the fallback)
                EnsureMaxCrewCached();

                // Cache max crew from this instance every time (guards against field-name mismatches)
                if (_maxActiveCrewsField != null)
                {
                    try
                    {
                        int instCap = (int)_maxActiveCrewsField.GetValue(__instance);
                        if (instCap > 0 && instCap < 10000) _cachedMaxCrew = instCap;
                    }
                    catch { }
                }

                int corrected = CountActiveNonRetiredCrew();

                int activeCrews = (int)_activeCrewsField.GetValue(__instance);
                if (corrected >= 0 && corrected != activeCrews)
                {
                    _activeCrewsField.SetValue(__instance, corrected);
                    activeCrews = corrected;
                }

                if (_activeCrewsCountField != null)
                {
                    object label = _activeCrewsCountField.GetValue(__instance);
                    if (label != null)
                    {
                        var textProp = label.GetType().GetProperty("text",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (textProp != null)
                        {
                            string cur = textProp.GetValue(label, null) as string ?? "";
                            // Target "Active Kerbals: N" pattern specifically — ReplaceFirstInteger
                            // incorrectly matches digits inside <color=#rrggbb> hex codes.
                            string fix = ReplaceActiveKerbalsCount(cur, activeCrews);
                            if (fix != cur)
                            {
                                textProp.SetValue(label, fix, null);
                            }
                            else
                            {
                                // If it's already correct, don't spam warnings.
                                if (!TryParseActiveKerbalsCount(cur, out int existing) || existing != activeCrews)
                                    RRLog.VerboseOnce("ac.count.replacefail", "[RosterRotation] ACPatch: could not replace count in: '" + cur + "'");
                            }

                        }
                    }
                }

                // Fix Available badge — do this here, AFTER KSP has set it, so we overwrite correctly
                var go = (__instance as MonoBehaviour)?.gameObject;
                if (go != null)
                {
                    FixAvailableBadge(go, GetRetiredNames());

                    // Fix hire button: KSP disabled it because activeCrews (including retired) >= max.
                    // Now that we've corrected activeCrews, re-enable if the real count is under cap.
                    // Last-resort fallback: parse [Max: N] from the label string we already have.
                    if (_cachedMaxCrew == int.MaxValue && _activeCrewsCountField != null)
                    {
                        try
                        {
                            object label = _activeCrewsCountField.GetValue(__instance);
                            if (label != null)
                            {
                                var tp = label.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                string raw = tp?.GetValue(label, null) as string ?? "";
                                if (TryParseMaxFromLabel(raw, out int parsed))
                                {
                                    _cachedMaxCrew = parsed;
                                }
                            }
                        }
                        catch { }
                    }

                    if (_cachedMaxCrew < int.MaxValue)
                    {
                        bool atCap = activeCrews >= _cachedMaxCrew;
                        FixHireButtons(go, !atCap);
                    }
                    else
                    {
                        if (!_warnedMaxCrewUnknown)
                        {
                            _warnedMaxCrewUnknown = true;
                            RRLog.WarnOnce("ac.cap.unknown", "[RosterRotation] ACPatch: could not determine max crew cap — enabling hire buttons (best effort).");
                        }
                        FixHireButtons(go, true);
                    }
                }
                PatchLostListStatusText();  // Refresh K.I.A./Died labels whenever crew counts update
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] Postfix_UpdateCrewCounts failed: " + ex);
            }
        }

        // ---------------------------------------------------------------
        // Postfix: CreateApplicantList
        // KSP computes active crew count (including retired kerbals) and uses it
        // to set the hire button's interactable state. We correct it here.
        // Also patches Available list status text — hiring fires CreateApplicantList
        // (not CreateAvailableList), so this is the only postfix hook that runs after hire.
        // ---------------------------------------------------------------
        public static void Postfix_CreateApplicantList(object __instance)
        {
            try
            {
                if (__instance == null) return;

                // ISSUE 1 FIX: KSP calls CreateApplicantList (not CreateAvailableList) when a
                // kerbal is hired. Our onKerbalTypeChange sets Training=InitialHire before this
                // postfix fires, so calling PatchAvailableListStatusText here ensures the newly
                // hired kerbal's row shows "In introductory training Xd Xh Xm" immediately.
                PatchAvailableListStatusText();
                PatchLostListStatusText();


                var go = (__instance as MonoBehaviour)?.gameObject;
                if (go == null) return;

                EnsureMaxCrewCached();
                // Keep crew cap logic consistent with our retired exclusion.
                int activeCrews = _activeCrewsField != null ? (int)_activeCrewsField.GetValue(__instance) : 0;
                int corrected = CountActiveNonRetiredCrew();
                if (_activeCrewsField != null && corrected >= 0 && corrected != activeCrews)
                {
                    _activeCrewsField.SetValue(__instance, corrected);
                    activeCrews = corrected;
                }

                // Find all Button components in the AC and re-enable any that were disabled
                // purely because retired kerbals pushed the count over the cap.
                int maxCrew = _cachedMaxCrew < int.MaxValue ? _cachedMaxCrew : GetCachedMaxCrew();
                if (maxCrew == int.MaxValue)
                {
                    if (!_warnedMaxCrewUnknown)
                    {
                        _warnedMaxCrewUnknown = true;
                        RRLog.WarnOnce("ac.cap.unknown", "[RosterRotation] ACPatch: could not determine max crew cap — enabling hire buttons (best effort).");
                    }
                    FixHireButtons(go, true);
                    return;
                }

                bool atCap = activeCrews >= maxCrew;

                // Fix hire buttons KSP may have disabled based on the (now corrected) active crew count.
                FixHireButtons(go, !atCap);

            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] Postfix_CreateApplicantList failed: " + ex);
            }
        }

        // Called by the mod when a kerbal is retired or recalled — forces full list rebuild
        public static void ForceRefresh()
        {
            try
            {
                if (_cachedACInstance == null || _createAvailableListMethod == null) return;
                // Guard: make sure the AC MonoBehaviour is still alive (not destroyed after close)
                var mb = _cachedACInstance as MonoBehaviour;
                if (mb == null || mb.gameObject == null)
                {
                    RRLog.VerboseOnce("ac.forcerefresh.destroyed", "[RosterRotation] ACPatch: ForceRefresh — cached AC instance is destroyed, skipping.");
                    _cachedACInstance = null;
                    return;
                }

                // Destroy old retired list children NOW so re-clone doesn't double them up.
                if (RetiredListTransform != null)
                {
                    for (int i = RetiredListTransform.childCount - 1; i >= 0; i--)
                    {
                        var ch = RetiredListTransform.GetChild(i);
                        if (ch != null) UnityEngine.Object.DestroyImmediate(ch.gameObject);
                    }
                }

                bool wasShowingRetired = RetiredTabShowing;

                // Deactivate the retired list before CreateAvailableList clones rows into it.
                // Rows parented into an ACTIVE GO get OnEnable fired immediately, which corrupts
                // their background Image components (causing the missing border visual).
                // Deactivating first mirrors the normal first-open path exactly.
                if (RetiredListTransform != null)
                    RetiredListTransform.gameObject.SetActive(false);

                // Suppress ActivateList tab-switching side effects during programmatic rebuild
                _refreshing = true;
                try { _createAvailableListMethod.Invoke(_cachedACInstance, null); }
                finally { _refreshing = false; }

                // Immediately refresh the "Active Kerbals" count label too
                if (_updateCrewCountsMethod != null)
                    try { _updateCrewCountsMethod.Invoke(_cachedACInstance, null); } catch { }

                // Always reposition + rewire retired rows even when the tab is not currently visible.
                // This ensures Recall buttons exist the moment the user switches to the Retired tab
                // (e.g. after Force-Retiring from the overlay while Available tab was showing).
                if (RetiredListTransform != null)
                {
                    RepositionRetiredRows(RetiredListTransform);
                    RewireTooltipsInRetiredList(RetiredListTransform);
                    // UIHoverPanel.Awake() may have fired and deactivated Button GOs — force them on.
                    for (int ri = 0; ri < RetiredListTransform.childCount; ri++)
                    {
                        Transform row = RetiredListTransform.GetChild(ri);
                        if (row == null) continue;
                        foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
                        {
                            if (ch.name != "Button") continue;
                            if (!ch.gameObject.activeSelf)
                            {
                                ch.gameObject.SetActive(true);
                            }
                            break;
                        }
                    }
                }

                // Also apply updated Available and Lost list status labels right after rebuild.
                PatchAvailableListStatusText();
                PatchLostListStatusText();

                // If the retired tab was visible, restore its display state.
                if (wasShowingRetired && RetiredListTransform != null)
                {
                    Transform vesselScrollRect = RetiredListTransform.parent;
                    if (vesselScrollRect != null)
                    {
                        for (int i = 0; i < vesselScrollRect.childCount; i++)
                        {
                            Transform ch = vesselScrollRect.GetChild(i);
                            if (ch == null || ch == RetiredListTransform) continue;
                            if (ch.name.StartsWith("scrollList_", System.StringComparison.OrdinalIgnoreCase))
                                ch.gameObject.SetActive(false);
                        }
                    }
                    RetiredListTransform.gameObject.SetActive(true);
                    // OnEnable fires on SetActive(true) — UIHoverPanel may deactivate Button GOs.
                    // Reposition, rewire, then explicitly re-enable any deactivated buttons.
                    RepositionRetiredRows(RetiredListTransform);
                    RewireTooltipsInRetiredList(RetiredListTransform);
                    ReenableRetiredButtons(RetiredListTransform);
                }
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] ACPatch.ForceRefresh failed: " + ex);
                _cachedACInstance = null; // clear stale reference so next open re-caches cleanly
            }
        }

        // ---------------------------------------------------------------
        // Re-enable any Button GOs deactivated by UIHoverPanel.OnEnable.
        // Called from ShowRetiredList after SetActive(true) on the retired list.
        // ---------------------------------------------------------------
        public static void ReenableRetiredButtons(Transform retiredList)
        {
            if (retiredList == null) return;
            try
            {
                for (int ri = 0; ri < retiredList.childCount; ri++)
                {
                    Transform row = retiredList.GetChild(ri);
                    if (row == null || !row.gameObject.activeSelf) continue;
                    foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
                    {
                        if (ch.name != "Button") continue;
                        if (!ch.gameObject.activeSelf)
                        {
                            ch.gameObject.SetActive(true);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterRotation] ReenableRetiredButtons failed: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------
        // Refresh just the applicant list (e.g. after Reject All)
        // ---------------------------------------------------------------
        public static void ForceRefreshApplicants()
        {
            try
            {
                if (_cachedACInstance == null) return;
                var mb = _cachedACInstance as MonoBehaviour;
                if (mb == null || mb.gameObject == null) { _cachedACInstance = null; return; }

                if (_createApplicantListMethod != null)
                    _createApplicantListMethod.Invoke(_cachedACInstance, null);

                if (_updateCrewCountsMethod != null)
                    _updateCrewCountsMethod.Invoke(_cachedACInstance, null);
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] ACPatch.ForceRefreshApplicants failed: " + ex);
            }
        }

        // ---------------------------------------------------------------
        // Badge helpers
        // ---------------------------------------------------------------
        private static void FixHireButtons(GameObject acGo, bool interactable)
{
    if (acGo == null) return;

    Transform list = ApplicantsListTransform;

    // Resolve applicants list once per AC instance/session.
    if (list == null)
    {
        // Prefer scroll lists that explicitly mention applicants/recruits in their name.
        foreach (Transform t in acGo.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            string nm = t.name ?? "";
            if (nm.IndexOf("scrollList", StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (nm.IndexOf("applicant", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("recruit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                list = t;
                break;
            }
        }

        // Fallback: any transform containing applicant with ListItem children.
        if (list == null)
        {
            foreach (Transform t in acGo.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string nm = t.name ?? "";
                if (nm.IndexOf("applicant", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (t.childCount <= 0) continue;
                var ch0 = t.GetChild(0);
                if (ch0 != null && (ch0.name ?? "").StartsWith("ListItem", StringComparison.OrdinalIgnoreCase))
                {
                    list = t;
                    break;
                }
            }
        }

        ApplicantsListTransform = list;
    }

    if (list == null)
    {
        if (!_warnedNoHireButtons)
        {
            _warnedNoHireButtons = true;
            RRLog.WarnOnce("ac.fixhire.noapplicants", "[RosterRotation] FixHireButtons: applicants list not found — cannot adjust hire buttons.");
        }
        return;
    }

    int found = 0;
    int changed = 0;

    // Only touch buttons inside applicant list rows. This avoids breaking other tabs/controls.
    for (int i = 0; i < list.childCount; i++)
    {
        Transform row = list.GetChild(i);
        if (row == null) continue;
        if (!(row.name ?? "").StartsWith("ListItem", StringComparison.OrdinalIgnoreCase)) continue;

        foreach (Component c in row.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            if (c.GetType().Name != "Button") continue;

            var ip = c.GetType().GetProperty("interactable", BindingFlags.Instance | BindingFlags.Public);
            if (ip == null) continue;

            try
            {
                bool cur = (bool)ip.GetValue(c, null);
                found++;
                if (cur != interactable)
                {
                    ip.SetValue(c, interactable, null);
                    changed++;
                }
            }
            catch { }
        }
    }

    if (found == 0 && !_warnedNoHireButtons)
    {
        _warnedNoHireButtons = true;
        RRLog.WarnOnce("ac.fixhire.nobuttons", "[RosterRotation] FixHireButtons: no Button components found under applicants list rows — cannot adjust hire buttons.");
    }
}
private static void FixAvailableBadge(GameObject acGo,
            System.Collections.Generic.List<string> retiredNames)
        {
            try
            {
                Transform tab = FindDescendant(acGo.transform, "Tab Available");
                if (tab == null)
                {
                    // Dump all tab names once for diagnosis
                    RRLog.Warn("[RosterRotation] ACPatch: 'Tab Available' not found. Tabs in scene:");
                    foreach (Transform t in acGo.transform.GetComponentsInChildren<Transform>(true))
                        if (t != null && t.name.StartsWith("Tab", StringComparison.OrdinalIgnoreCase))
                    return;
                }
                int count = 0;
                foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                {
                    if (pcm.rosterStatus != ProtoCrewMember.RosterStatus.Available) continue;
                    if (retiredNames.Contains(pcm.name)) continue;
                    count++;
                }
                SetBadgeText(tab, count, "Available");
            }
            catch (Exception ex) { RRLog.Error("[RosterRotation] FixAvailableBadge: " + ex); }
        }

        private static void FixRetiredBadge(GameObject acGo, int count)
        {
            try
            {
                Transform tab = FindDescendant(acGo.transform, "Tab Retired");
                if (tab == null) return;
                SetBadgeText(tab, count, "Retired");
            }
            catch (Exception ex) { RRLog.Error("[RosterRotation] FixRetiredBadge: " + ex); }
        }

        private static void SetBadgeText(Transform tab, int count, string label)
        {
            foreach (Component c in tab.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (string.IsNullOrEmpty(s)) continue;

                string newText = null;
                if (s.Contains("[") && s.Contains("]"))
                {
                    // e.g. "Retired[2]" → "Retired[3]"
                    int bracket = s.IndexOf('[');
                    newText = s.Substring(0, bracket + 1) + count + "]";
                }
                else if (s.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // e.g. "Available" → "Available[2]"
                    newText = s + "[" + count + "]";
                }

                if (newText != null)
                {
                    try
                    {
                        p.SetValue(c, newText, null);
                    }
                    catch { }
                    return;
                }
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private static System.Collections.Generic.List<string> GetRetiredNames()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var kvp in RosterRotationState.Records)
                if (kvp.Value != null && kvp.Value.Retired) names.Add(kvp.Key);
            return names;
        }

        private static int GetRetiredCrewCount()
        {
            int n = 0;
            foreach (var kvp in RosterRotationState.Records)
                if (kvp.Value != null && kvp.Value.Retired) n++;
            return n;
        }

        // Counts crew that KSP generally treats as 'active' for AC capacity,
        // excluding our 'retired' kerbals.
        private static int CountActiveNonRetiredCrew()
        {
            try
            {
                var roster = HighLogic.CurrentGame?.CrewRoster;
                if (roster == null || roster.Crew == null) return -1;

                int n = 0;
                foreach (var pcm in roster.Crew)
                {
                    if (pcm == null) continue;
                    if (pcm.type == ProtoCrewMember.KerbalType.Applicant) continue;
                    if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead) continue;
                    if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;

                    if (RosterRotationState.Records.TryGetValue(pcm.name, out var r) && r != null && r.Retired) continue;
                    n++;
                }
                return n;
            }
            catch
            {
                return -1;
            }
        }

        // Attempts to locate any kerbal by name in the current game's CrewRoster.
        // Uses a reflection-based fallback so it works across KSP builds.
        private static ProtoCrewMember FindKerbalByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return null;

            // Fast path: scan the main Crew list.
            try
            {
                if (roster.Crew != null)
                    foreach (var k in roster.Crew)
                        if (k != null && k.name == name) return k;
            }
            catch { }

            // Try any method that looks like Get*(string) -> ProtoCrewMember
            try
            {
                var ms = roster.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var m in ms)
                {
                    if (m == null) continue;
                    if (m.ReturnType != typeof(ProtoCrewMember)) continue;
                    var ps = m.GetParameters();
                    if (ps == null || ps.Length != 1 || ps[0].ParameterType != typeof(string)) continue;
                    try
                    {
                        var res = m.Invoke(roster, new object[] { name }) as ProtoCrewMember;
                        if (res != null && res.name == name) return res;
                    }
                    catch { }
                }
            }
            catch { }

            // Reflection fallback: scan any IEnumerable<ProtoCrewMember> properties/fields.
            try
            {
                foreach (var p in roster.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (p == null || !p.CanRead) continue;
                    var pt = p.PropertyType;
                    if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(pt)) continue;
                    if (!pt.IsGenericType) continue;
                    var ga = pt.GetGenericArguments();
                    if (ga == null || ga.Length != 1 || ga[0] != typeof(ProtoCrewMember)) continue;
                    object val = null;
                    try { val = p.GetValue(roster, null); } catch { continue; }
                    if (val is System.Collections.IEnumerable e)
                        foreach (var o in e)
                            if (o is ProtoCrewMember k && k != null && k.name == name) return k;
                }
                foreach (var f in roster.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f == null) continue;
                    var ft = f.FieldType;
                    if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(ft)) continue;
                    if (!ft.IsGenericType) continue;
                    var ga = ft.GetGenericArguments();
                    if (ga == null || ga.Length != 1 || ga[0] != typeof(ProtoCrewMember)) continue;
                    object val = null;
                    try { val = f.GetValue(roster); } catch { continue; }
                    if (val is System.Collections.IEnumerable e)
                        foreach (var o in e)
                            if (o is ProtoCrewMember k && k != null && k.name == name) return k;
                }
            }
            catch { }

            return null;
        }

        private static int GetRetiredEffectiveStarsSafe(ProtoCrewMember k, RosterRotationState.KerbalRecord r, double nowUT)
        {
            if (r == null)
                return k != null ? (int)k.experienceLevel : 0;
            if (!r.Retired)
                return k != null ? (int)k.experienceLevel : 0;

            int starsAtRetire = r.ExperienceAtRetire > 0 ? r.ExperienceAtRetire : (k != null ? (int)k.experienceLevel : 0);
            if (starsAtRetire <= 0) return 0;
            double yearSec = RosterRotationState.YearSeconds;
            if (yearSec <= 0) yearSec = 9201600.0; // fallback
            int starsLost = (int)((nowUT - r.RetiredUT) / yearSec);
            return Math.Max(0, starsAtRetire - starsLost);
        }

        private static void CopyLayoutComponents(Transform source, Transform dest)
        {
            // Component type names to copy from source to dest
            string[] layoutTypes = { "VerticalLayoutGroup", "HorizontalLayoutGroup",
                                     "ContentSizeFitter", "LayoutElement", "GridLayoutGroup" };
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (Component sc in source.GetComponents<Component>())
            {
                if (sc == null) continue;
                string tn = sc.GetType().Name;
                bool isLayout = false;
                foreach (string lt in layoutTypes) if (tn == lt) { isLayout = true; break; }
                if (!isLayout) continue;

                // Check if dest already has this type
                Component dc = dest.GetComponent(sc.GetType());
                if (dc == null)
                    dc = dest.gameObject.AddComponent(sc.GetType());
                if (dc == null) continue;

                // Copy all serializable fields
                foreach (FieldInfo fi in sc.GetType().GetFields(f))
                {
                    if (fi.IsStatic || fi.IsLiteral) continue;
                    try { fi.SetValue(dc, fi.GetValue(sc)); } catch { }
                }
                // Copy public properties that have setters
                foreach (PropertyInfo pi in sc.GetType().GetProperties(f))
                {
                    if (!pi.CanRead || !pi.CanWrite) continue;
                    try { pi.SetValue(dc, pi.GetValue(sc, null), null); } catch { }
                }
            }
        }

        private static string GetTransformPath(Transform t)
        {
            if (t == null) return "<null>";
            string path = t.name;
            Transform p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 20) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && string.Equals(t.name, name, StringComparison.Ordinal)) return t;
            return null;
        }

        /// <summary>
        /// Discovers the actual Lost list content Transform from the UIListToggleController.
        /// The lists[] array contains UIList components on the TAB BUTTONS, not the scroll lists.
        /// We reflect into UIList at index 2 to find its internal content container.
        /// </summary>
        private static Transform DiscoverLostListFromController(GameObject acGo)
        {
            try
            {
                if (_tcListsField == null || acGo == null) return null;

                var tcComponents = acGo.GetComponentsInChildren<Component>(true);
                object tcInstance = null;
                foreach (var c in tcComponents)
                {
                    if (c == null) continue;
                    if (c.GetType().Name == "UIListToggleController")
                    { tcInstance = c; break; }
                }
                if (tcInstance == null) return null;

                var lists = _tcListsField.GetValue(tcInstance) as System.Array;
                if (lists == null) return null;

                for (int i = 0; i < lists.Length; i++)
                {
                    object uiList = lists.GetValue(i);
                    if (uiList == null) continue;
                    var comp = uiList as Component;
                }

                // Lost tab is index 2 (Available=0, Assigned=1, Lost=2)
                const int LOST_INDEX = 2;
                if (lists.Length <= LOST_INDEX) return null;

                object lostUIList = lists.GetValue(LOST_INDEX);
                if (lostUIList == null) return null;

                // Reflect into UIList to find its content container
                // UIList.customListAnchor is the shared scroll content area
                Type uiListType = lostUIList.GetType();
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Look for customListAnchor first (known field name from diagnostics)
                FieldInfo anchorField = uiListType.GetField("customListAnchor", bf);
                if (anchorField != null)
                {
                    object val = null;
                    try { val = anchorField.GetValue(lostUIList); } catch { }
                    Transform anchor = null;
                    if (val is Transform t2) anchor = t2;
                    else if (val is Component c3) anchor = c3.transform;
                    if (anchor != null)
                    {
                        return anchor;
                    }
                }

                // Fallback: search all fields for a Transform with dead kerbal rows
                var lostComp = lostUIList as Component;
                Transform bestCandidate = null;

                foreach (var field in uiListType.GetFields(bf))
                {
                    if (field == null) continue;
                    Transform candidate = null;
                    try
                    {
                        object val = field.GetValue(lostUIList);
                        if (val is Transform t2) candidate = t2;
                        else if (val is Component c3) candidate = c3.transform;
                        else if (val is GameObject g2) candidate = g2.transform;
                    }
                    catch { continue; }
                    if (candidate == null) continue;
                    if (lostComp != null && candidate == lostComp.transform) continue; // skip self

                    if (candidate.childCount > 0 && HasDeadKerbalRow(candidate))
                    {
                        return candidate;
                    }
                    if (bestCandidate == null || candidate.childCount > bestCandidate.childCount)
                        bestCandidate = candidate;
                }

                if (bestCandidate != null)
                {
                    return bestCandidate;
                }

                return null;
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] ACPatch: DiscoverLostListFromController failed: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Given a UIList component (which sits on a TAB BUTTON), reflects into it to find
        /// the actual content Transform where kerbal rows live.
        /// </summary>
        private static Transform FindUIListContent(object uiList)
        {
            if (uiList == null) return null;
            try
            {
                Type t = uiList.GetType();
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var selfComp = uiList as Component;

                // Fast path: known field name from KSP UIList
                FieldInfo anchorField = t.GetField("customListAnchor", bf);
                if (anchorField != null)
                {
                    object av = null;
                    try { av = anchorField.GetValue(uiList); } catch { }
                    Transform anchor = null;
                    if (av is Transform at) anchor = at;
                    else if (av is Component ac) anchor = ac.transform;
                    if (anchor != null && (selfComp == null || anchor != selfComp.transform))
                        return anchor;
                }

                // Fallback: search all fields
                Transform bestCandidate = null;

                foreach (var field in t.GetFields(bf))
                {
                    if (field == null) continue;
                    Transform candidate = null;
                    try
                    {
                        object val = field.GetValue(uiList);
                        if (val is Transform tr) candidate = tr;
                        else if (val is Component co) candidate = co.transform;
                        else if (val is GameObject go) candidate = go.transform;
                    }
                    catch { continue; }
                    if (candidate == null) continue;
                    if (selfComp != null && candidate == selfComp.transform) continue;

                    // Best match: has dead kerbal rows
                    if (candidate.childCount > 0 && HasDeadKerbalRow(candidate))
                        return candidate;

                    // Track largest child as fallback
                    if (bestCandidate == null || candidate.childCount > bestCandidate.childCount)
                        bestCandidate = candidate;
                }

                return bestCandidate;
            }
            catch { return null; }
        }

        /// <summary>
        /// Returns true if the given list Transform contains a row whose name matches a dead kerbal.
        /// </summary>
        private static bool HasDeadKerbalRow(Transform list)
        {
            if (list == null || HighLogic.CurrentGame?.CrewRoster == null) return false;
            var deadNames = new System.Collections.Generic.HashSet<string>();
            var roster = HighLogic.CurrentGame.CrewRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                ProtoCrewMember pcm;
                try { pcm = roster[i]; } catch { continue; }
                if (pcm == null) continue;
                if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                    pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing)
                    deadNames.Add(pcm.name);
            }
            if (deadNames.Count == 0) return false;

            for (int i = 0; i < list.childCount; i++)
            {
                Transform row = list.GetChild(i);
                if (row == null) continue;
                string name = GetKerbalNameFromRowAllRoster(row.gameObject);
                if (name != null && deadNames.Contains(name)) return true;
            }
            return false;
        }

        /// <summary>
        /// Like GetKerbalNameFromRow but checks ALL roster kerbals (Dead, Missing, Unowned, etc.)
        /// not just Crew.
        /// </summary>
        private static string GetKerbalNameFromRowAllRoster(GameObject row)
        {
            if (row == null || HighLogic.CurrentGame?.CrewRoster == null) return null;
            var names = new System.Collections.Generic.HashSet<string>();
            var roster = HighLogic.CurrentGame.CrewRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                ProtoCrewMember pcm;
                try { pcm = roster[i]; } catch { continue; }
                if (pcm != null) names.Add(pcm.name);
            }
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (!string.IsNullOrEmpty(s) && names.Contains(s)) return s;
            }
            return null;
        }

        private static bool RowContainsName(GameObject row,
            System.Collections.Generic.List<string> names)
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

        // Targeted replacement: finds "Active Kerbals: N" and replaces N with corrected value.
        // Much safer than ReplaceFirstInteger which matches digits inside color hex codes.
        private static string ReplaceActiveKerbalsCount(string text, int corrected)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Matches "Active Kerbals: " followed by one or more digits
            int labelIdx = text.IndexOf("Active Kerbals:", StringComparison.OrdinalIgnoreCase);
            if (labelIdx < 0) return text;
            int numStart = labelIdx + "Active Kerbals:".Length;
            // Skip spaces/tabs
            while (numStart < text.Length && (text[numStart] == ' ' || text[numStart] == '\t')) numStart++;
            if (numStart >= text.Length || !char.IsDigit(text[numStart])) return text;
            int numEnd = numStart;
            while (numEnd < text.Length && char.IsDigit(text[numEnd])) numEnd++;
            return text.Substring(0, numStart) + corrected + text.Substring(numEnd);
        }

        private static bool TryParseActiveKerbalsCount(string text, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int labelIdx = text.IndexOf("Active Kerbals:", StringComparison.OrdinalIgnoreCase);
            if (labelIdx < 0) return false;
            int numStart = labelIdx + "Active Kerbals:".Length;
            while (numStart < text.Length && char.IsWhiteSpace(text[numStart])) numStart++;
            int numEnd = numStart;
            while (numEnd < text.Length && char.IsDigit(text[numEnd])) numEnd++;
            if (numEnd <= numStart) return false;
            return int.TryParse(text.Substring(numStart, numEnd - numStart), out count);
        }

        // Dumps all component type names and all text property values on a row for diagnostics.
        private static void DumpRowDiagnostics(GameObject row, string label)
        {
            if (row == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RosterRotation] ACPatch: === ROW DUMP for '" + label + "' ===");
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                string goName   = c.gameObject.name;
                // Try to get text value
                string textVal = "";
                var tp = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (tp != null && tp.PropertyType == typeof(string))
                    try { textVal = " text='" + tp.GetValue(c, null) + "'"; } catch { }
                // Check for onClick
                bool hasClick = c.GetType().GetProperty("onClick",
                    BindingFlags.Instance | BindingFlags.Public) != null;
                sb.AppendLine("  GO='" + goName + "' comp=" + typeName
                    + textVal + (hasClick ? " [HAS onClick]" : ""));
            }
            sb.AppendLine("[RosterRotation] ACPatch: === END ROW DUMP ===");
        }

        private static string ReplaceFirstInteger(string text, int oldVal, int newVal)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string oldStr = oldVal.ToString();
            int idx = text.IndexOf(oldStr, StringComparison.Ordinal);
            if (idx < 0) return text;
            bool prevOk = idx == 0 || !char.IsDigit(text[idx - 1]);
            bool nextOk = idx + oldStr.Length >= text.Length
                          || !char.IsDigit(text[idx + oldStr.Length]);
            if (!prevOk || !nextOk) return text;
            return text.Substring(0, idx) + newVal + text.Substring(idx + oldStr.Length);
        }

        // Finds the kerbal name displayed in a row (the first Text component whose value is
        // a known crew member name — same pattern used by RowContainsName).
        private static string GetKerbalNameFromRow(GameObject row)
        {
            if (row == null || HighLogic.CurrentGame?.CrewRoster == null) return null;
            // Build a quick lookup of known crew names
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (var pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                if (pcm != null) names.Add(pcm.name);
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (!string.IsNullOrEmpty(s) && names.Contains(s)) return s;
            }
            return null;
        }

        // Replaces the status text on a kerbal row.
        // Targets the 'label' child GO specifically (where KSP puts status text).
        private static void ReplaceStatusText(GameObject row, string newText)
        {
            if (row == null) return;

            // Primary: find the 'label' GO and set its text directly
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.gameObject.name != "label") continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                try { p.SetValue(c, newText, null); } catch { }
                return;
            }

            // Fallback: pattern-match but skip 'name', 'stats', 'label_courage', 'label_stupidity'
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string goName = c.gameObject.name ?? "";
                if (goName == "name" || goName == "stats" ||
                    goName.StartsWith("label_", StringComparison.Ordinal)) continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (string.IsNullOrEmpty(s)) continue;
                if (IsStatusText(s))
                {
                    try { p.SetValue(c, newText, null); } catch { }
                    return;
                }
            }
        }

        // Returns true if the string looks like a status label (not a kerbal name).
        private static bool IsStatusText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("Available for next mission", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("In refresher training", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("In training", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Retired", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Age ", StringComparison.OrdinalIgnoreCase) >= 0
                // Lost tab / Dead / Missing status strings:
                || s.IndexOf("K.I.A", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("KIA", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("M.I.A", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0
                || s.Equals("Died", StringComparison.OrdinalIgnoreCase)
                || s.IndexOf("Died ", StringComparison.OrdinalIgnoreCase) >= 0
                // KSP placeholder patterns (%.%.%.%.%):
                || s.IndexOf("%.%", StringComparison.Ordinal) >= 0;
        }

        // Like ReplaceStatusText but only fires when the text still reads the KSP default
        // "Available for next mission" — avoids overwriting other mods' status text.
        private static void ReplaceStatusTextIfDefault(GameObject row, string newText)
        {
            if (row == null) return;
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;
                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (string.IsNullOrEmpty(s)) continue;
                // Only replace if it's the vanilla default or already our age-prefixed default
                if (s.IndexOf("Available for next mission", StringComparison.OrdinalIgnoreCase) >= 0
                    || (s.IndexOf("Age ", StringComparison.OrdinalIgnoreCase) >= 0
                        && s.IndexOf("Available for next mission", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    try { p.SetValue(c, newText, null); } catch { }
                    return;
                }
            }
        }

        // Sets the text on the child GO with name `goName` (first TextMeshProUGUI or Text found).
        private static void SetTextOnGO(GameObject row, string goName, string text)
        {
            Transform t = row.transform.Find(goName);
            if (t == null)
            {
                // Try deep search
                foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
                    if (ch.name == goName) { t = ch; break; }
            }
            if (t == null) { RRLog.Warn("[RosterRotation] ACPatch: SetTextOnGO — GO '" + goName + "' not found."); return; }
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(string))
                {
                    try { p.SetValue(c, text, null); return; }
                    catch { }
                }
            }
            RRLog.Warn("[RosterRotation] ACPatch: SetTextOnGO — no text component on '" + goName + "'");
        }

        private static void SetStarsState(GameObject row, int stars)
        {
            // --- 1. XP Slider fill ---
            bool sliderSet = false;
            foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
            {
                if (ch == null || ch.name != "Slider") continue;
                if (ch.parent != null && (ch.parent.name.ToLowerInvariant().Contains("courage")
                                       || ch.parent.name.ToLowerInvariant().Contains("stupidity"))) continue;

                foreach (Component sc in ch.GetComponents<Component>())
                {
                    if (sc == null || sc.GetType().Name != "Slider") continue;
                    try
                    {
                        var maxProp = sc.GetType().GetProperty("maxValue", BindingFlags.Instance | BindingFlags.Public);
                        var valProp = sc.GetType().GetProperty("value",    BindingFlags.Instance | BindingFlags.Public);
                        if (valProp == null) break;
                        float maxVal = 5f;
                        if (maxProp != null) try { maxVal = (float)maxProp.GetValue(sc, null); } catch { }
                        float sliderVal = (maxVal > 1f) ? (float)stars : (stars / 5f);
                        valProp.SetValue(sc, sliderVal, null);
                        sliderSet = true;
                    }
                    catch { }
                    break;
                }

                Transform fillArea = ch.Find("Fill Area");
                if (fillArea == null) foreach (Transform fc in ch) { if (fc.name == "Fill Area") { fillArea = fc; break; } }
                if (fillArea != null)
                {
                    Transform fill = fillArea.Find("Fill");
                    if (fill == null) foreach (Transform fc in fillArea) { fill = fc; break; }
                    if (fill != null)
                    {
                        foreach (Component ic in fill.GetComponents<Component>())
                        {
                            if (ic == null || ic.GetType().Name != "Image") continue;
                            var fillAmtProp = ic.GetType().GetProperty("fillAmount", BindingFlags.Instance | BindingFlags.Public);
                            if (fillAmtProp != null)
                                try { fillAmtProp.SetValue(ic, stars / 5f, null); } catch { }
                            ForceImageVisible(ic);
                            break;
                        }
                    }
                }
                if (sliderSet) break;
            }

            // --- 2. UIStateImage star widget ---
            // Do NOT use GetComponentsInChildren here — Unity 2019/KSP can silently skip
            // children of inactive parents even with includeInactive=true.
            if (TryFindStarsStateImage(row.transform, out Transform starsT, out Component usiComp))
            {
                ActivateUpTo(starsT, row.transform);

                // Enable the component itself
                var enabledP = usiComp.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
                if (enabledP != null) try { enabledP.SetValue(usiComp, true, null); } catch { }

                // Dump revealed: image = Image field (the target Image), states = ImageState[6]
                // ImageState[N] corresponds to N stars. Get the Image from the 'image' field,
                // then set its sprite from states[stars].sprite (or similar field).
                var imageF = usiComp.GetType().GetField("image",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Component imgComp = imageF?.GetValue(usiComp) as Component;
                // Fallback: find Image on same GO
                if (imgComp == null)
                    foreach (Component c in starsT.GetComponents<Component>())
                        if (c != null && c.GetType().Name == "Image") { imgComp = c; break; }

                if (imgComp != null) ForceImageVisible(imgComp);

                var statesF = usiComp.GetType().GetField("states",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                System.Array arr = null;
                if (statesF != null) try { arr = statesF.GetValue(usiComp) as System.Array; } catch { }
                if (arr != null && arr.Length > 0 && imgComp != null)
                {
                    int idx = Mathf.Clamp(stars, 0, arr.Length - 1);
                    var imageState = arr.GetValue(idx);
                    if (imageState != null)
                    {
                        foreach (string sfn in new[] { "sprite", "image", "texture", "tex" })
                        {
                            var sf = imageState.GetType().GetField(sfn,
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (sf == null) continue;
                            var spriteVal = sf.GetValue(imageState);
                            if (spriteVal == null) continue;
                            var spriteProp = imgComp.GetType().GetProperty("sprite",
                                BindingFlags.Instance | BindingFlags.Public);
                            if (spriteProp != null)
                                try { spriteProp.SetValue(imgComp, spriteVal, null); } catch { }
                            break;
                        }
                    }
                }

                // Also call SetState(int) in case it does additional work
                var setStateM = usiComp.GetType().GetMethod("SetState",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(int) }, null);
                if (setStateM != null) try { setStateM.Invoke(usiComp, new object[] { stars }); } catch { }

                // Critically: set currentStateIndex so future OnEnable refreshes to the right sprite.
                var csiF2 = usiComp.GetType().GetField("currentStateIndex",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (csiF2 != null) try { csiF2.SetValue(usiComp, stars); } catch { }
            }

            if (!sliderSet)
                RRLog.WarnOnce("ac.stars.noslider", "[RosterRotation] ACPatch: SetStarsState — XP slider not found.");
        }

        private static void ForceImageVisible(Component imgComp)
        {
            if (imgComp == null) return;
            var enabledProp = imgComp.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
            if (enabledProp != null) try { enabledProp.SetValue(imgComp, true, null); } catch { }
            var colorProp = imgComp.GetType().GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
            if (colorProp != null)
                try
                {
                    var c = colorProp.GetValue(imgComp, null);
                    if (c is Color col) { col.a = 1f; colorProp.SetValue(imgComp, col, null); }
                } catch { }
        }

        // Walks up from a leaf to the given row root and ensures every GO is active.
        private static void ActivateUpTo(Transform leaf, Transform rowRoot)
        {
            Transform t = leaf;
            int guard = 0;
            while (t != null && guard++ < 64)
            {
                try { t.gameObject.SetActive(true); } catch { }
                if (t == rowRoot) break;
                t = t.parent;
            }
        }

        // Robustly locates the star UIStateImage in a crew list row.
        // Does NOT rely on GO name "stars" because KSP UI prefabs vary.
        // Picks the UIStateImage whose states[] array has >= 6 entries (0..5 stars).
        private static bool TryFindStarsStateImage(Transform rowRoot, out Transform starsT, out Component uiStateImage)
        {
            starsT = null;
            uiStateImage = null;
            if (rowRoot == null) return false;

            int bestScore = -1;
            var stack = new System.Collections.Generic.Stack<Transform>();
            stack.Push(rowRoot);

            while (stack.Count > 0)
            {
                Transform t = null;
                try { t = stack.Pop(); } catch { break; }
                if (t == null) continue;

                // Evaluate UIStateImage components on this transform
                try
                {
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null || c.GetType().Name != "UIStateImage") continue;
                        var statesF = c.GetType().GetField("states",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        System.Array arr = null;
                        if (statesF != null)
                            try { arr = statesF.GetValue(c) as System.Array; } catch { }
                        int len = arr != null ? arr.Length : 0;
                        if (len < 6) continue;

                        int score = len;
                        string n = (t.name ?? "").ToLowerInvariant();
                        if (n == "stars") score += 1000;
                        else if (n.Contains("star")) score += 500;
                        if (t.parent != null)
                        {
                            string pn = (t.parent.name ?? "").ToLowerInvariant();
                            if (pn.Contains("star")) score += 50;
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            starsT = t;
                            uiStateImage = c;
                        }
                    }
                }
                catch { }

                // Recurse into children regardless of active state
                try
                {
                    for (int i = 0; i < t.childCount; i++)
                        stack.Push(t.GetChild(i));
                }
                catch { }
            }

            return uiStateImage != null && starsT != null;
        }

        // Finds the dismiss "Button" GO (named exactly "Button"), switches UIStateButton state,
        // sets UIStateButtonTooltip text, and replaces onClick with Recall.
        private static void WireRecallButton(GameObject row, ProtoCrewMember kerbal, int effStars)
        {
            if (row == null || kerbal == null) return;
            try
            {
                // Button GO confirmed named "Button" from row dump.
                Transform btnT = null;
                foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
                    if (ch.name == "Button") { btnT = ch; break; }
                if (btnT == null) { RRLog.Warn("[RosterRotation] WireRecallButton — 'Button' GO not found for " + kerbal.name); return; }

                bool canRecall = effStars > 0;

                // The Button GO may be inactive (isActiveAndEnabled=False seen in dump).
                // Force it active — it holds UIStateButton, UIStateButtonTooltip, and Button.
                btnT.gameObject.SetActive(true);

                // --- Button visual: read sprite directly from UIStateButton.states[] ---
                // SetState() fails on clones because its Button/Image backing fields don't survive
                // Instantiate(). Instead, read the target sprite from ButtonState[stateIdx] and
                // set Image.sprite directly — same pattern used successfully for stars.
                Component uisb = null;
                foreach (Component c in btnT.GetComponents<Component>())
                    if (c != null && c.GetType().Name == "UIStateButton") { uisb = c; break; }

                // Get the Image component on the Button GO (the one that shows X or checkmark)
                Component btnImgComp = null;
                foreach (Component c in btnT.GetComponents<Component>())
                    if (c != null && c.GetType().Name == "Image") { btnImgComp = c; break; }

                if (uisb != null && btnImgComp != null)
                {
                    int targetStateIdx = canRecall ? 1 : 0;

                    // Update currentStateIndex so UIStateButtonTooltip reads the correct tooltipStates entry.
                    // We stopped calling SetState() because its backing fields are null on clones,
                    // but the tooltip needs currentStateIndex to be correct.
                    var csiF = uisb.GetType().GetField("currentStateIndex",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (csiF != null) try { csiF.SetValue(uisb, targetStateIdx); } catch { }
                    var ssF = uisb.GetType().GetField("stateSet",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (ssF != null) try { ssF.SetValue(uisb, true); } catch { }

                    var statesF = uisb.GetType().GetField("states",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (statesF != null)
                    {
                        var statesArr = statesF.GetValue(uisb) as System.Array;
                        if (statesArr != null && statesArr.Length > targetStateIdx)
                        {
                            var bstate = statesArr.GetValue(targetStateIdx);

                            // ButtonState fields to try for sprite: sprite, normalSprite, selectedSprite, image
                            bool spriteSet = false;
                            foreach (string sfn in new[] { "normal", "sprite", "normalSprite", "selectedSprite", "image", "icon" })
                            {
                                if (bstate == null) break;
                                var sf = bstate.GetType().GetField(sfn,
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (sf == null) continue;
                                var val = sf.GetValue(bstate);
                                if (val == null) continue;
                                // Only set if it's actually a sprite type
                                if (val.GetType().Name != "Sprite") continue;
                                var spriteProp = btnImgComp.GetType().GetProperty("sprite",
                                    BindingFlags.Instance | BindingFlags.Public);
                                if (spriteProp != null)
                                {
                                    try
                                    {
                                        spriteProp.SetValue(btnImgComp, val, null);
                                        spriteSet = true;
                                    }
                                    catch { }
                                }
                                break;
                            }

                            // Also try a ColorBlock — ButtonState may tint the image instead of/as well as swap sprite
                            foreach (string cfn in new[] { "color", "normalColor", "tintColor" })
                            {
                                if (bstate == null) break;
                                var cf = bstate.GetType().GetField(cfn,
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (cf == null) continue;
                                var val = cf.GetValue(bstate);
                                if (val == null || !(val is Color)) continue;
                                var colorProp = btnImgComp.GetType().GetProperty("color",
                                    BindingFlags.Instance | BindingFlags.Public);
                                if (colorProp != null)
                                    try { colorProp.SetValue(btnImgComp, val, null); }
                                    catch { }
                                break;
                            }

                            if (!spriteSet)
                                RRLog.Warn("[RosterRotation] ACPatch: could not set Button sprite for " + kerbal.name + " — check ButtonState DUMP above for field names.");
                        }
                    }
                }
                // canRecall=false: state 0 = orange X already rendered by default

                // --- Neutralize Button color tint so hover doesn't turn green button red ---
                // Button.transition=ColorTint multiplies Image color by highlightedColor on hover.
                // The original dismiss button has an orange/red highlightedColor. Set all colors
                // to white so the sprite we chose shows through unmodified.
                {
                    Component btnForColors = null;
                    foreach (Component c in btnT.GetComponents<Component>())
                        if (c != null && c.GetType().Name == "Button") { btnForColors = c; break; }
                    if (btnForColors != null)
                    {
                        var colorsProp = btnForColors.GetType().GetProperty("colors",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (colorsProp != null)
                        {
                            try
                            {
                                var cb = colorsProp.GetValue(btnForColors, null);
                                if (cb != null)
                                {
                                    var t = cb.GetType();
                                    // Set normalColor, highlightedColor, pressedColor to white,
                                    // disabledColor to light grey — all fully opaque
                                    foreach (string cn in new[] { "normalColor", "highlightedColor", "pressedColor" })
                                        SetColorField(t, cb, cn, Color.white);
                                    SetColorField(t, cb, "disabledColor", new Color(0.78f, 0.78f, 0.78f, 0.5f));
                                    // colorMultiplier = 1 so tint is not amplified
                                    var cmF = t.GetField("m_ColorMultiplier",
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (cmF == null) cmF = t.GetField("colorMultiplier",
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (cmF != null) try { cmF.SetValue(cb, 1f); } catch { }
                                    colorsProp.SetValue(btnForColors, cb, null);
                                }
                            }
                            catch { }
                        }
                    }
                }

                // --- UIStateButtonTooltip ---
                Component tooltip = null;
                foreach (Component c in btnT.GetComponents<Component>())
                    if (c != null && c.GetType().Name == "UIStateButtonTooltip") { tooltip = c; break; }

                if (tooltip != null)
                {
                    // Force enabled — cycle false→true to trigger OnEnable/re-registration
                    var tooltipEnabledP = tooltip.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
                    if (tooltipEnabledP != null) try { tooltipEnabledP.SetValue(tooltip, false, null); } catch { }
                    if (tooltipEnabledP != null) try { tooltipEnabledP.SetValue(tooltip, true, null); } catch { }

                    // RequireInteractable=True (from dump) blocks tooltip when selectableBase is null.
                    // Set it false so the tooltip fires on hover unconditionally.
                    var riF = tooltip.GetType().GetField("RequireInteractable",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (riF == null) riF = tooltip.GetType().GetField("requireInteractable",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (riF != null) try { riF.SetValue(tooltip, false); } catch { }

                    // Wire stateButton so tooltip reads currentStateIndex for text selection
                    if (uisb != null)
                    {
                        var sbF = tooltip.GetType().GetField("stateButton",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (sbF != null) try { sbF.SetValue(tooltip, uisb); } catch { }
                    }

                    // Wire tooltipPrefab if null — copy from any working instance in scene
                    var prefabF = tooltip.GetType().GetField("tooltipPrefab",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prefabF != null && prefabF.GetValue(tooltip) == null)
                    {
                        foreach (Component tc in UnityEngine.Object.FindObjectsOfType(tooltip.GetType()))
                        {
                            if (tc == tooltip) continue;
                            var pf = prefabF.GetValue(tc);
                            if (pf != null) { try { prefabF.SetValue(tooltip, pf); } catch { } break; }
                        }
                    }

                    // Set tooltipStates text: index 0 = "Cannot Recall", index 1 = "Recall Kerbal"
                    var tsF = tooltip.GetType().GetField("tooltipStates",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (tsF != null)
                    {
                        var arr = tsF.GetValue(tooltip) as System.Array;
                        if (arr != null)
                        {
                            for (int si = 0; si < arr.Length; si++)
                            {
                                var entry = arr.GetValue(si);
                                if (entry == null) continue;
                                string tipText = si == 0 ? "Cannot Recall" : "Recall Kerbal";
                                bool tipSet = false;
                                foreach (string fn in new[] { "tooltipText", "text", "tip", "message", "content", "label" })
                                {
                                    var tf = entry.GetType().GetField(fn,
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (tf != null && tf.FieldType == typeof(string))
                                    {
                                        try { tf.SetValue(entry, tipText); tipSet = true; break; }
                                        catch { }
                                    }
                                }
                                if (!tipSet)
                                    RRLog.Warn("[RosterRotation] ACPatch: tooltip[" + si + "] — could not find text field (known: tooltipText, text, tip, message, content, label)");
                            }
                            try { tsF.SetValue(tooltip, arr); } catch { }
                        }
                    }
                }

                // --- Button onClick ---
                Component btn = null;
                foreach (Component c in btnT.GetComponents<Component>())
                    if (c != null && c.GetType().Name == "Button") { btn = c; break; }
                if (btn != null)
                {
                    var onClickP = btn.GetType().GetProperty("onClick", BindingFlags.Instance | BindingFlags.Public);
                    if (onClickP != null)
                    {
                        try
                        {
                            var onClick = onClickP.GetValue(btn, null);
                            onClick?.GetType().GetMethod("RemoveAllListeners")?.Invoke(onClick, null);
                            var addListener = onClick?.GetType().GetMethod("AddListener",
                                new Type[] { typeof(UnityEngine.Events.UnityAction) });
                            if (canRecall)
                            {
                                UnityEngine.Events.UnityAction action = () => RecallKerbalFromRetiredTab(kerbal);
                                addListener?.Invoke(onClick, new object[] { action });
                            }
                            else
                            {
                                string kName = kerbal.name;
                                UnityEngine.Events.UnityAction action = () =>
                                    ScreenMessages.PostScreenMessage(
                                        kName + " has lost all experience in retirement and cannot be recalled.",
                                        4f, ScreenMessageStyle.UPPER_CENTER);
                                addListener?.Invoke(onClick, new object[] { action });
                            }
                        }
                        catch { }
                    }
                    // Always interactable — clicking gives feedback even for zero-star kerbals
                    var interactP = btn.GetType().GetProperty("interactable", BindingFlags.Instance | BindingFlags.Public);
                    if (interactP != null) try { interactP.SetValue(btn, true, null); } catch { }
                }

            }
            catch (Exception ex)
            {
                RRLog.WarnOnce("ac.recall.wire."+kerbal.name, "[RosterRotation] ACPatch: WireRecallButton failed for " + kerbal.name + ": " + ex.Message);
            }
        }

        // Manual recursive descent to find the "stars" GO and re-apply UIStateImage state.
        // Used instead of GetComponentsInChildren because Unity 2019 has a known issue where
        // GetComponentsInChildren<Transform>(true) silently skips children of inactive GOs.
        private static void FindAndRestoreStars(Transform node, ref bool found)
        {
            if (node == null || found) return;
            if (node.name == "stars")
            {
                // Found the GO — activate it and re-apply UIStateImage
                node.gameObject.SetActive(true);
                foreach (Component sc in node.GetComponents<Component>())
                {
                    if (sc == null || sc.GetType().Name != "UIStateImage") continue;
                    var csiF = sc.GetType().GetField("currentStateIndex",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (csiF == null) break;
                    int idx = 0;
                    try { idx = (int)csiF.GetValue(sc); } catch { break; }
                    var setStateM = sc.GetType().GetMethod("SetState",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new Type[] { typeof(int) }, null);
                    if (setStateM != null) try { setStateM.Invoke(sc, new object[] { idx }); } catch { }
                    found = true;
                    break;
                }
                return;
            }
            // Recurse into ALL children regardless of active state
            for (int i = 0; i < node.childCount; i++)
                FindAndRestoreStars(node.GetChild(i), ref found);
        }

        private static void SetColorField(Type t, object obj, string fieldName, Color color)
        {
            var f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) try { f.SetValue(obj, color); } catch { }
        }

        private static void RecallKerbalFromRetiredTab(ProtoCrewMember kerbal)
        {
            try
            {
                if (kerbal == null) return;
                if (!RosterRotationState.Records.TryGetValue(kerbal.name, out var rec) || rec == null) return;

                double nowUT = Planetarium.GetUniversalTime();

                // Check: zero stars → cannot recall
                int effStars = RosterRotationState.GetRetiredEffectiveStars(kerbal, rec, nowUT);
                if (effStars <= 0)
                {
                    RRLog.Warn("[RosterRotation] ACPatch: Cannot recall " + kerbal.name + " — zero stars.");
                    ScreenMessages.PostScreenMessage(kerbal.name + " has no experience remaining and cannot be recalled.", 4f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }

                // Check cap
                if (ACPatches.GetCachedMaxCrew() < int.MaxValue)
                {
                    int active = 0;
                    if (HighLogic.CurrentGame?.CrewRoster != null)
                        foreach (var pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                        {
                            if (pcm == null) continue;
                            if (RosterRotationState.Records.TryGetValue(pcm.name, out var pr) && pr != null && pr.Retired) continue;
                            active++;
                        }
                    if (active >= ACPatches.GetCachedMaxCrew())
                    {
                        ScreenMessages.PostScreenMessage("Cannot recall " + kerbal.name + " — crew roster is full.", 4f, ScreenMessageStyle.UPPER_CENTER);
                        return;
                    }
                }

                rec.Retired = false;

                if (kerbal.type == ProtoCrewMember.KerbalType.Tourist ||
                    kerbal.type == ProtoCrewMember.KerbalType.Unowned)
                {
                    kerbal.type = rec.OriginalType;
                    if (kerbal.type == 0) kerbal.type = ProtoCrewMember.KerbalType.Crew;
                }
                kerbal.rosterStatus = ProtoCrewMember.RosterStatus.Available;

                if (!string.IsNullOrEmpty(rec.OriginalTrait))
                    kerbal.trait = rec.OriginalTrait;

                // Apply experience decay — kerbal retains effStars level, not original level
                // KSP's experienceLevel is a float; we set it to the decayed integer value.
                try { kerbal.experienceLevel = effStars; }
                catch (Exception ex2) { RRLog.Warn("[RosterRotation] Could not set experienceLevel: " + ex2.Message); }

                // 30-day refresher training
                double recallSeconds = 30.0 * RosterRotationState.DaySeconds;
                kerbal.inactive = true;
                kerbal.inactiveTimeEnd = nowUT + recallSeconds;
                rec.Training            = TrainingType.RecallRefresher;
                rec.TrainingTargetLevel = 0;

                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch { }

                ScreenMessages.PostScreenMessage(kerbal.name + " recalled — 30-day refresher training begins.", 4f, ScreenMessageStyle.UPPER_CENTER);

                // Rebuild lists and immediately update status labels so the row shows
                // "In refresher training Xd Xh Xm" instead of "Available for next mission".
                ForceRefresh();
                // ForceRefresh() now calls PatchAvailableListStatusText() internally,
                // but call it again here to handle the case where AvailListTransform
                // was populated AFTER the internal call.
                PatchAvailableListStatusText();
            }
            catch (Exception ex)
            {
                RRLog.Error("[RosterRotation] RecallKerbalFromRetiredTab failed: " + ex);
            }
        }
        public static void PatchAvailableListStatusText()
        {
            if (AvailListTransform == null) return;
            double nowUT = Planetarium.GetUniversalTime();
            for (int i = 0; i < AvailListTransform.childCount; i++)
            {
                Transform row = AvailListTransform.GetChild(i);
                if (row == null) continue;
                string kerbalName = GetKerbalNameFromRow(row.gameObject);
                if (kerbalName == null) continue;
                ProtoCrewMember pcm = null;
                if (HighLogic.CurrentGame?.CrewRoster != null)
                    foreach (var k in HighLogic.CurrentGame.CrewRoster.Crew)
                        if (k != null && k.name == kerbalName) { pcm = k; break; }
                if (pcm == null) continue;

                // Build age prefix — shown for all active kerbals when aging is enabled
                string agePrefix = "";
                if (RosterRotationState.AgingEnabled
                    && RosterRotationState.Records.TryGetValue(kerbalName, out var recAge)
                    && recAge.LastAgedYears >= 0)
                {
                    agePrefix = "Age " + RosterRotationState.GetKerbalAge(recAge, nowUT) + "  ";
                }

                if (pcm.inactive && pcm.inactiveTimeEnd > nowUT)
                {
                    string timeLeft = FormatCountdownStatic(pcm.inactiveTimeEnd - nowUT);

                    string label = "In refresher training"; // fallback / RecallRefresher
                    if (RosterRotationState.Records.TryGetValue(kerbalName, out var rec))
                    {
                        if (rec.Training == TrainingType.InitialHire)
                            label = "In introductory training";
                        else if (rec.Training == TrainingType.ExperienceUpgrade)
                            label = "In Level " + rec.TrainingTargetLevel + " training";
                        else if (rec.Training == TrainingType.RecallRefresher)
                            label = "In refresher training";
                        else
                            continue; // None+inactive = RandR/other, skip
                    }

                    ReplaceStatusText(row.gameObject, agePrefix + label + " " + timeLeft);
                }
                else if (agePrefix.Length > 0)
                {
                    // Not in training — show age alongside the default "Available for next mission"
                    // text that KSP already set.  We only replace if it still reads the default,
                    // to avoid clobbering other mods' status text.
                    ReplaceStatusTextIfDefault(row.gameObject, agePrefix + "Available for next mission");
                }
            }
        }

        // Patches K.I.A. rows in the Lost tab to show age+date from our DeathUT record.

        // Patches K.I.A. rows in the Lost tab to show age+date from our DeathUT record.
        // Searches ALL descendants of LostListTransform for dead kerbal name text components,
        // then replaces the status text on the same row.
        public static void PatchLostListStatusText()
        {

            if (LostListTransform == null)
            {
                RRLog.VerboseOnce("ac.lost.null", "[RosterRotation] ACPatch LostTab: LostListTransform is null — cannot patch.");
                return;
            }

            // Build lookup of all dead/missing kerbals that have DeathUT records
            var deadKerbals = new System.Collections.Generic.Dictionary<string, RosterRotationState.KerbalRecord>();
            foreach (var kvp in RosterRotationState.Records)
            {
                if (kvp.Value == null || kvp.Value.DeathUT <= 0) continue;
                deadKerbals[kvp.Key] = kvp.Value;
            }

            if (deadKerbals.Count == 0)
            {
                return;
            }

            // Search ALL descendant text components for dead kerbal names
            int patched = 0;
            var allComponents = LostListTransform.GetComponentsInChildren<Component>(true);
            var processedRows = new System.Collections.Generic.HashSet<int>(); // track by instance ID

            foreach (Component c in allComponents)
            {
                if (c == null) continue;
                var textProp = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (textProp == null || textProp.PropertyType != typeof(string)) continue;

                string textVal = null;
                try { textVal = textProp.GetValue(c, null) as string; } catch { continue; }
                if (string.IsNullOrEmpty(textVal)) continue;

                // Is this text a dead kerbal's name?
                if (!deadKerbals.TryGetValue(textVal, out var rec)) continue;

                // Find the row-level parent (walk up until we find a meaningful container)
                GameObject row = FindRowParent(c.transform);
                if (row == null) row = c.transform.parent != null ? c.transform.parent.gameObject : c.gameObject;

                // Don't process the same row twice
                int rowId = row.GetInstanceID();
                if (processedRows.Contains(rowId)) continue;
                processedRows.Add(rowId);

                string kerbalName = textVal;

                // Compute the status text
                int age = -1;
                if (RosterRotationState.AgingEnabled && rec.LastAgedYears >= 0)
                    age = RosterRotationState.GetKerbalAge(rec, rec.DeathUT);

                string dateStr = RosterRotationState.FormatGameDateYD(rec.DeathUT);
                bool retiredDeath = (rec.RetiredUT > 0) && (rec.DeathUT >= rec.RetiredUT - 1);

                string newStatus;
                if (retiredDeath)
                {
                    newStatus = age >= 0 ? "Died Age " + age + ", " + dateStr : "Died " + dateStr;
                }
                else
                {
                    newStatus = age >= 0 ? "K.I.A. Age " + age + ", " + dateStr : "K.I.A. " + dateStr;
                }

                // Replace the status text on this row
                bool replaced = ReplaceNonNameText(row, kerbalName, newStatus);
                if (replaced)
                    patched++;
                else
                    RRLog.Warn("[RosterRotation] ACPatch LostTab: could not replace status for '" + kerbalName + "'");
            }

        }

        /// <summary>
        /// Walk up the transform hierarchy to find the row-level parent.
        /// Stops when we find a GameObject whose name contains "ListItem" or "Clone",
        /// or when we've gone up 5 levels, whichever comes first.
        /// </summary>
        private static GameObject FindRowParent(Transform child)
        {
            if (child == null) return null;
            Transform current = child.parent;
            int depth = 0;
            while (current != null && depth < 5)
            {
                string n = current.name ?? "";
                if (n.Contains("ListItem") || n.Contains("Clone") || n.Contains("enlisted"))
                    return current.gameObject;
                current = current.parent;
                depth++;
            }
            // Fallback: just use the direct parent of the text component
            return child.parent != null ? child.parent.gameObject : null;
        }

        /// <summary>
        /// Find the 'label' text component on the row and replace its text.
        /// Specifically targets the GO named 'label' to avoid clobbering
        /// 'stats' (class), 'label_courage', or 'label_stupidity'.
        /// </summary>
        private static bool ReplaceNonNameText(GameObject row, string kerbalName, string newText)
        {
            if (row == null) return false;

            // First pass: look for a text component on a GO named "label"
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.gameObject.name != "label") continue;

                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;

                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }

                // Skip if already replaced
                if (s == newText) return true;

                try
                {
                    p.SetValue(c, newText, null);
                    return true;
                }
                catch { }
            }

            // Fallback: try any text component that matches a known status pattern
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                // Skip GOs that are NOT status labels
                string goName = c.gameObject.name ?? "";
                if (goName == "name" || goName == "stats" ||
                    goName == "label_courage" || goName == "label_stupidity")
                    continue;

                var p = c.GetType().GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || p.PropertyType != typeof(string)) continue;

                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (string.IsNullOrEmpty(s)) continue;
                if (s == kerbalName || s == newText) continue;
                if (!IsStatusText(s)) continue;

                try
                {
                    p.SetValue(c, newText, null);
                    return true;
                }
                catch { }
            }

            return false;
        }

        private static string FormatTimeAgoStatic(double eventUT, double nowUT)
        {
            if (eventUT <= 0) return "";
            double elapsed = nowUT - eventUT;
            return FormatCountdownStatic(elapsed) + " ago";
        }

        private static string FormatCountdownStatic(double seconds)
        {
            if (seconds <= 0) return "Ready";
            double daySec = RosterRotationState.DaySeconds;
            int days    = (int)(seconds / daySec);
            int hours   = (int)((seconds % daySec) / 3600.0);
            int minutes = (int)((seconds % 3600.0) / 60.0);
            int secs    = (int)(seconds % 60.0);
            if (days > 0)    return days + "d " + hours + "h " + minutes + "m";
            if (hours > 0)   return hours + "h " + minutes + "m " + secs + "s";
            if (minutes > 0) return minutes + "m " + secs + "s";
            return secs + "s";
        }
    }

}
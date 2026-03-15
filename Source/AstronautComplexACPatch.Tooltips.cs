// EAC - AstronautComplexACPatch.Tooltips
// Extracted tooltip and row-layout helpers for the Astronaut Complex UI.

using System;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    internal static partial class ACPatches
    {
        // Called by RetiredTabClickProxy.ShowRetiredList() AFTER SetActive(true).
        // OnEnable() resets wired fields on UIStateButtonTooltip, so we must re-wire after activation.
        public static void RewireTooltipsInRetiredList(Transform retiredList)
        {
            if (retiredList == null) return;
            try
            {
                double nowUT = Planetarium.GetUniversalTime();
                int rowCount = 0, tooltipWired = 0, prefabFound = 0, prefabRewired = 0;
                foreach (Transform row in retiredList)
                {
                    if (row == null) continue;
                    // Find Button GO (for UIStateButton and Button)
                    Transform btnT = null;
                    foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
                        if (ch.name == "Button") { btnT = ch; break; }
                    if (btnT == null) continue;
                    rowCount++;

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
                        if (csiF != null) try { csiF.SetValue(uisb, correctStateIdx); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:63", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:63", ex); }

                        var csF = uisb.GetType().GetField("currentState",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (csF != null) try { csF.SetValue(uisb, correctStateName); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:67", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:67", ex); }

                        var ssF = uisb.GetType().GetField("stateSet",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (ssF != null) try { ssF.SetValue(uisb, true); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:71", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:71", ex); }

                    }

                    if (tooltip == null) continue;
                    tooltipWired++;

                    // RequireInteractable = false so tooltip fires without a valid selectableBase
                    foreach (string fn in new[] { "RequireInteractable", "requireInteractable" })
                    {
                        var f = tooltip.GetType().GetField(fn,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (f != null) try { f.SetValue(tooltip, false); break; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:83", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:83", ex); }
                    }

                    // Wire stateButton so tooltip reads currentStateIndex
                    if (uisb != null)
                    {
                        var sbF = tooltip.GetType().GetField("stateButton",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (sbF != null) try { sbF.SetValue(tooltip, uisb); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:91", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:91", ex); }
                    }

                    // Wire tooltipPrefab — ALWAYS re-wire on every rewire pass.
                    // After AC close/reopen, the old prefab reference is a destroyed Unity object:
                    // non-null in C# but dead in Unity. Must use Unity's == null to detect this.
                    var prefabF = tooltip.GetType().GetField("tooltipPrefab",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prefabF != null)
                    {
                        var prefabVal = prefabF.GetValue(tooltip);
                        // Unity null check: catches both C# null AND destroyed Unity objects
                        bool prefabDead = (prefabVal is UnityEngine.Object uObj) ? (uObj == null) : (prefabVal == null);
                        if (prefabDead)
                        {
                            bool found = false;
                            foreach (Component tc in UnityEngine.Object.FindObjectsOfType(tooltip.GetType()))
                            {
                                if (tc == tooltip || tc == null) continue;
                                var pf = prefabF.GetValue(tc);
                                if (pf is UnityEngine.Object pfObj && pfObj != null)
                                {
                                    try { prefabF.SetValue(tooltip, pf); prefabRewired++; found = true; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:113", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:113", ex); }
                                    break;
                                }
                            }
                            if (!found)
                                RRLog.Verbose("[EAC] RewireTooltips: could not find live tooltip prefab for row");
                        }
                        else
                        {
                            prefabFound++;
                        }
                    }

                    // Wire selectableBase to Button so interactable check passes
                    if (btn != null)
                    {
                        var selF = tooltip.GetType().GetField("selectableBase",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (selF != null) try { selF.SetValue(tooltip, btn); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:131", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:131", ex); }
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
                        try { enabledP.SetValue(tooltip, false, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:143", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:143", ex); }
                        try { enabledP.SetValue(tooltip, true, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:144", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:144", ex); }
                    }

                    // ── Tooltip chain diagnostics (verbose logging) ──
                    if (RRLog.VerboseEnabled)
                    {
                        var sb = new System.Text.StringBuilder("[EAC] TooltipDiag row='");
                        sb.Append(row.name).Append("': ");

                        // Button GO active?
                        sb.Append("btnGO=").Append(btnT.gameObject.activeSelf ? "ON" : "OFF").Append(" ");

                        // Tooltip enabled?
                        bool ttEnabled = false;
                        if (enabledP != null) try { ttEnabled = (bool)enabledP.GetValue(tooltip, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:158", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:158", ex); }
                        sb.Append("ttEnabled=").Append(ttEnabled).Append(" ");

                        // Prefab alive?
                        var pfF = tooltip.GetType().GetField("tooltipPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pfF != null)
                        {
                            var pfVal = pfF.GetValue(tooltip);
                            bool pfAlive = (pfVal is UnityEngine.Object pfObj) ? (pfObj != null) : (pfVal != null);
                            sb.Append("prefab=").Append(pfAlive ? "LIVE" : "DEAD").Append(" ");
                        }

                        // selectableBase alive?
                        var selF = tooltip.GetType().GetField("selectableBase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (selF != null)
                        {
                            var selVal = selF.GetValue(tooltip);
                            bool selAlive = (selVal is UnityEngine.Object selObj) ? (selObj != null) : (selVal != null);
                            sb.Append("selBase=").Append(selAlive ? "LIVE" : "DEAD").Append(" ");
                        }

                        // stateButton alive?
                        var sbF = tooltip.GetType().GetField("stateButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (sbF != null)
                        {
                            var sbVal = sbF.GetValue(tooltip);
                            bool sbAlive = (sbVal is UnityEngine.Object sbObj) ? (sbObj != null) : (sbVal != null);
                            sb.Append("stateBtn=").Append(sbAlive ? "LIVE" : "DEAD").Append(" ");
                        }

                        // RequireInteractable?
                        var riF = tooltip.GetType().GetField("RequireInteractable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                               ?? tooltip.GetType().GetField("requireInteractable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (riF != null) try { sb.Append("reqInteract=").Append(riF.GetValue(tooltip)).Append(" "); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:191", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:191", ex); }

                        // UIHoverPanel on row?
                        bool uhpFound = false, uhpEnabled = false;
                        foreach (Component c in row.GetComponents<Component>())
                        {
                            if (c != null && c.GetType().Name == "UIHoverPanel")
                            {
                                uhpFound = true;
                                var ep = c.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
                                if (ep != null) try { uhpEnabled = (bool)ep.GetValue(c, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:201", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:201", ex); }
                                break;
                            }
                        }
                        sb.Append("uhp=").Append(uhpFound ? (uhpEnabled ? "ENABLED" : "disabled") : "NONE").Append(" ");

                        // DragObject has EventTriggerForwarder?
                        bool etfFound = false;
                        for (int di = 0; di < row.childCount; di++)
                        {
                            var dragObj = row.GetChild(di);
                            if (dragObj == null || dragObj.name != "DragObject") continue;
                            foreach (Component c in dragObj.GetComponents<Component>())
                                if (c != null && c.GetType().Name == "EventTriggerForwarder") { etfFound = true; break; }
                            break;
                        }
                        sb.Append("etf=").Append(etfFound ? "YES" : "NO");

                        RRLog.Verbose(sb.ToString());
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
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:237", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:237", ex); }
                }
                RRLog.Verbose("[EAC] RewireTooltips: rows=" + rowCount + " tooltips=" + tooltipWired + " prefabs=" + prefabFound + " rewired=" + prefabRewired);
            }
            catch (Exception ex)
            {
                RRLog.WarnOnce("ac.rewire.fail", "RewireTooltipsInRetiredList failed: " + ex.Message);
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
                    NeuterUIHoverPanel(row.gameObject);
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
        // Does NOT destroy EventTriggerForwarder — it is still needed to relay PointerEnter
        // events from DragObject to Button for tooltip delivery.
        public static void NeuterUIHoverPanel(GameObject row)
        {
            if (row == null) return;
            try
            {
                foreach (Component c in row.GetComponents<Component>())
                {
                    if (c == null || c.GetType().Name != "UIHoverPanel") continue;

                    Type t = c.GetType();

                    // Swap backgroundNormal to the hover sprite so the border is always visible.
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
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:337", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:337", ex); }
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
                                var enabledP2 = img.GetType().GetProperty("enabled",
                                    BindingFlags.Instance | BindingFlags.Public);
                                if (enabledP2 != null) enabledP2.SetValue(img, true, null);
                            }
                        }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:359", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:359", ex); }
                    }

                    // Clear hoverObjects so PointerExit() stops calling SetActive(false) on Button GO.
                    foreach (string fieldName in new[] { "hoverObjects", "_hoverObjects", "HoverObjects" })
                    {
                        var hoF = t.GetField(fieldName,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (hoF == null) continue;
                        var hoVal = hoF.GetValue(c);
                        if (hoVal == null) break;
                        var clearM = hoVal.GetType().GetMethod("Clear",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (clearM != null)
                            try { clearM.Invoke(hoVal, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:373", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:373", ex); }
                        break;
                    }

                    // CRITICAL: Disable UIHoverPanel entirely so its Update() never runs.
                    // This is the definitive fix — clearing hoverObjects alone is a race condition
                    // because OnEnable repopulates them from serialized data on each SetActive(true).
                    var enabledP = t.GetProperty("enabled",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (enabledP != null) try { enabledP.SetValue(c, false, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:382", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:382", ex); }

                    break;
                }

                // DO NOT destroy EventTriggerForwarder from DragObject.
                // EventTriggerForwarder relays PointerEnter events from DragObject (the raycast
                // target) to Button (where UIStateButtonTooltip lives). Without it, hover events
                // never reach the tooltip system and tooltips stop working after AC close/reopen.
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] NeuterUIHoverPanel failed: " + ex.Message);
            }
        }

        // Kept for diagnostics only
        private static void ForceRowImagesVisible(GameObject row, bool dump)
        {
            if (dump)
            {
                try
                {
                    var sb = new System.Text.StringBuilder("[RosterRotation] RowHierarchyDump: " + row.name + "\n");
                    DumpHierarchy(row.transform, sb, "  ");
                } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:410", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:410", ex); }
            }
            NeuterUIHoverPanel(row);
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
                    if (ep != null) try { sb.Append(" en=").Append((bool)ep.GetValue(c, null)); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:428", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:428", ex); }
                    if (cp != null) try { var col = (Color)cp.GetValue(c, null); sb.Append(" a=").Append(col.a.ToString("F2")); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:429", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:429", ex); }
                }
                sb.Append("]");
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++)
                DumpHierarchy(t.GetChild(i), sb, indent + "  ");
        }
    }
}

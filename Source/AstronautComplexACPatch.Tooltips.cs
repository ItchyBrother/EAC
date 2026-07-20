// EAC - AstronautComplexACPatch.Tooltips
// Extracted tooltip and row-layout helpers for the Astronaut Complex UI.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RosterRotation
{
    internal static partial class ACPatches
    {
        // Called by RetiredTabClickProxy.ShowRetiredList() AFTER SetActive(true).
        // This restores cloned references and the stock pointer route. TooltipController has
        // Awake/OnDisable/OnDestroy lifecycle methods, but no OnEnable registration hook.
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

                    // Preserve the stock row hover component and pointer events while preventing
                    // it from hiding the recall button. This also restores the EventTriggerForwarder
                    // flags used by the stock prefab after a fresh AC rebuild.
                    NeuterUIHoverPanel(row.gameObject);

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

                    // Arbitrate the nested row and Recall tooltips. Unity sends pointer-enter
                    // to the button first and then to each parent in the same frame; the guard
                    // suspends TooltipController_CrewAC while the pointer is over Recall.
                    InstallRetiredRecallHoverGuard(row, btnT);

                    // UIStateButtonTooltip stays on Button. The runtime trace confirms the
                    // Button graphic is the direct top raycast target across its whole rectangle.

                    // --- Re-apply UIStateButton state after activation/rebuild ---
                    int correctStateIdx = 0;
                    string correctStateName = "X";
                    if (uisb != null && btnImg != null)
                    {
                        var spriteProp = ReflectionUtils.FindProperty(btnImg.GetType(), "sprite");
                        if (spriteProp != null)
                        {
                            var sprite = spriteProp.GetValue(btnImg, null) as UnityEngine.Object;
                            if (sprite != null && sprite.name != null && sprite.name.EndsWith("_v", System.StringComparison.OrdinalIgnoreCase))
                            {
                                correctStateIdx = 1;
                                correctStateName = "V";
                            }
                        }

                        var csiF = ReflectionUtils.FindField(uisb.GetType(), "currentStateIndex");
                        if (csiF != null) try { csiF.SetValue(uisb, correctStateIdx); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:63", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:63", ex); }

                        var csF = ReflectionUtils.FindField(uisb.GetType(), "currentState");
                        if (csF != null) try { csF.SetValue(uisb, correctStateName); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:67", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:67", ex); }

                        var ssF = ReflectionUtils.FindField(uisb.GetType(), "stateSet");
                        if (ssF != null) try { ssF.SetValue(uisb, true); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:71", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:71", ex); }

                    }

                    if (tooltip == null) continue;
                    tooltipWired++;

                    // RequireInteractable = false so tooltip fires without a valid selectableBase
                    foreach (string fn in new[] { "RequireInteractable", "requireInteractable" })
                    {
                        var f = ReflectionUtils.FindField(tooltip.GetType(), fn);
                        if (f != null) try { f.SetValue(tooltip, false); break; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:83", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:83", ex); }
                    }

                    // Wire stateButton so tooltip reads currentStateIndex
                    if (uisb != null)
                    {
                        var sbF = ReflectionUtils.FindField(tooltip.GetType(), "stateButton");
                        if (sbF != null) try { sbF.SetValue(tooltip, uisb); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:91", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:91", ex); }
                    }

                    // Wire tooltipPrefab — ALWAYS re-wire on every rewire pass.
                    // After AC close/reopen, the old prefab reference is a destroyed Unity object:
                    // non-null in C# but dead in Unity. Must use Unity's == null to detect this.
                    var prefabF = ReflectionUtils.FindField(tooltip.GetType(), "tooltipPrefab");
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
                        var selF = ReflectionUtils.FindField(tooltip.GetType(), "selectableBase");
                        if (selF != null) try { selF.SetValue(tooltip, btn); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:131", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:131", ex); }
                    }

                    // TooltipController does not register in OnEnable. Keep the component enabled,
                    // but do not cycle it because that calls OnDisable and may despawn/cancel the
                    // tooltip currently associated with this pointer.
                    var enabledP = ReflectionUtils.FindProperty(tooltip.GetType(), "enabled");
                    if (enabledP != null)
                    {
                        try
                        {
                            if (!(bool)enabledP.GetValue(tooltip, null))
                                enabledP.SetValue(tooltip, true, null);
                        }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:tooltip.enable", "Unable to enable retired recall tooltip", ex); }
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
                        var pfF = ReflectionUtils.FindField(tooltip.GetType(), "tooltipPrefab");
                        if (pfF != null)
                        {
                            var pfVal = pfF.GetValue(tooltip);
                            bool pfAlive = (pfVal is UnityEngine.Object pfObj) ? (pfObj != null) : (pfVal != null);
                            sb.Append("prefab=").Append(pfAlive ? "LIVE" : "DEAD").Append(" ");
                        }

                        // selectableBase alive?
                        var selF = ReflectionUtils.FindField(tooltip.GetType(), "selectableBase");
                        if (selF != null)
                        {
                            var selVal = selF.GetValue(tooltip);
                            bool selAlive = (selVal is UnityEngine.Object selObj) ? (selObj != null) : (selVal != null);
                            sb.Append("selBase=").Append(selAlive ? "LIVE" : "DEAD").Append(" ");
                        }

                        // stateButton alive?
                        var sbF = ReflectionUtils.FindField(tooltip.GetType(), "stateButton");
                        if (sbF != null)
                        {
                            var sbVal = sbF.GetValue(tooltip);
                            bool sbAlive = (sbVal is UnityEngine.Object sbObj) ? (sbObj != null) : (sbVal != null);
                            sb.Append("stateBtn=").Append(sbAlive ? "LIVE" : "DEAD").Append(" ");
                        }

                        // RequireInteractable?
                        var riF = ReflectionUtils.FindField(tooltip.GetType(), "RequireInteractable")
                               ?? ReflectionUtils.FindField(tooltip.GetType(), "requireInteractable");
                        if (riF != null) try { sb.Append("reqInteract=").Append(riF.GetValue(tooltip)).Append(" "); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:191", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:191", ex); }

                        // UIHoverPanel on row?
                        bool uhpFound = false, uhpEnabled = false;
                        foreach (Component c in row.GetComponents<Component>())
                        {
                            if (c != null && c.GetType().Name == "UIHoverPanel")
                            {
                                uhpFound = true;
                                var ep = ReflectionUtils.FindProperty(c.GetType(), "enabled");
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
                    // Activation/rebuild can reset UIStateImage.currentStateIndex to 0, so set it again
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
                            if (k != null)
                                WireRecallButton(row.gameObject, k, effStars);
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
                // activation/rebuild callbacks that undo our sprite and enable state settings.
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
                    // Restore the stock hover route on every display pass while keeping the
                    // recall button permanently visible.
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
        // the visible sprite. Keep UIHoverPanel enabled and hoverEnabled=true because it is a
        // stock PointerEnterExitHandler and owns the row's hover events. Remove only the recall
        // Button from hoverObjects so the panel no longer hides it. Restore EventTriggerForwarder
        // pointer flags instead of replacing the stock delivery path.
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
                    var bgNF = ReflectionUtils.FindField(t, "backgroundNormal");
                    var bgHF = ReflectionUtils.FindField(t, "backgroundHover");
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
                    var bgIF = ReflectionUtils.FindField(t, "backgroundImage");
                    if (bgIF != null && bgHF != null)
                    {
                        try
                        {
                            var img = bgIF.GetValue(c);
                            var hoverSprite = bgHF.GetValue(c);
                            if (img != null && hoverSprite != null)
                            {
                                var spriteP = ReflectionUtils.FindProperty(img.GetType(), "sprite");
                                if (spriteP != null) spriteP.SetValue(img, hoverSprite, null);
                                var enabledP2 = ReflectionUtils.FindProperty(img.GetType(), "enabled");
                                if (enabledP2 != null) enabledP2.SetValue(img, true, null);
                            }
                        }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:359", "Suppressed exception in AstronautComplexACPatch.Tooltips.cs:359", ex); }
                    }

                    // Remove only the recall button from the panel's object lists. Clearing either
                    // list discards stock row behavior; disabling this component prevents its pointer
                    // events from firing at all. Removing the button from both lists means neither
                    // pointer enter nor pointer exit can hide the always-visible recall control.
                    GameObject recallButton = null;
                    foreach (Transform child in row.GetComponentsInChildren<Transform>(true))
                    {
                        if (child != null && child.name == "Button")
                        {
                            recallButton = child.gameObject;
                            recallButton.SetActive(true);
                            break;
                        }
                    }

                    foreach (string fieldName in new[] { "hoverObjects", "normalObjects" })
                    {
                        var objectListF = ReflectionUtils.FindField(t, fieldName);
                        if (objectListF == null) continue;
                        var list = objectListF.GetValue(c) as IList;
                        if (list == null || recallButton == null) continue;
                        try
                        {
                            while (list.Contains(recallButton)) list.Remove(recallButton);
                        }
                        catch (global::System.Exception ex)
                        {
                            RRLog.VerboseExceptionOnce(
                                "AstronautComplexACPatch.Tooltips.cs:" + fieldName + ".remove",
                                "Unable to remove recall button from UIHoverPanel." + fieldName,
                                ex);
                        }
                    }

                    var hoverEnabledF = ReflectionUtils.FindField(t, "hoverEnabled");
                    if (hoverEnabledF != null)
                        try { hoverEnabledF.SetValue(c, true); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:hoverenabled", "Unable to enable stock retired-row hover", ex); }

                    // Keep the stock pointer handler alive. Older EAC builds disabled this every
                    // time the tab was shown, which removed the row from the stock hover lifecycle.
                    var enabledP = ReflectionUtils.FindProperty(t, "enabled");
                    if (enabledP != null)
                    {
                        try
                        {
                            if (!(bool)enabledP.GetValue(c, null))
                                enabledP.SetValue(c, true, null);
                        }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:hoverpanel.enable", "Unable to enable stock retired-row hover panel", ex); }
                    }

                    break;
                }

                // The normal EventSystem hierarchy already sends enter/exit from DragObject and
                // Button to the row. Forwarding the same events duplicates the row callbacks and
                // lets the crew tooltip run after the nested Recall tooltip. Disable only these
                // redundant enter/exit relays; leave the component and all other forwarding intact.
                foreach (Component c in row.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c.GetType().Name != "EventTriggerForwarder") continue;

                    foreach (string flagName in new[] { "forwardPointerEnter", "forwardPointerExit" })
                    {
                        var flag = ReflectionUtils.FindField(c.GetType(), flagName);
                        if (flag != null)
                            try { flag.SetValue(c, false); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:forwarder." + flagName, "Unable to disable duplicate retired-row pointer forwarding", ex); }
                    }

                    var enabledP = ReflectionUtils.FindProperty(c.GetType(), "enabled");
                    if (enabledP != null)
                        try
                        {
                            if (!(bool)enabledP.GetValue(c, null))
                                enabledP.SetValue(c, true, null);
                        }
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Tooltips.cs:forwarder.enable", "Unable to enable stock pointer forwarder", ex); }
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] NeuterUIHoverPanel failed: " + ex.Message);
            }
        }


        private static void InstallRetiredRecallHoverGuard(Transform row, Transform button)
        {
            if (row == null || button == null) return;

            try
            {
                RetiredRecallHoverGuard guard =
                    button.gameObject.GetComponent<RetiredRecallHoverGuard>();
                if (guard == null)
                    guard = button.gameObject.AddComponent<RetiredRecallHoverGuard>();

                guard.Configure(row);
                RRLog.VerboseOnce(
                    "ac.recall.hoverguard.v6",
                    "[EAC] RecallHoverGuard v6 installed; parent CrewAC tooltip is suspended while Recall is hovered.");
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce(
                    "ac.recall.hoverguard.install",
                    "Unable to install retired Recall hover arbitration",
                    ex);
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
                    var ep = ReflectionUtils.FindProperty(c.GetType(), "enabled");
                    var cp = ReflectionUtils.FindProperty(c.GetType(), "color");
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


    /// <summary>
    /// The Recall button is nested inside a row that also owns TooltipController_CrewAC.
    /// Unity enters the button first and then executes pointer-enter handlers on its parents.
    /// Suspend the actual parent tooltip component while Recall is hovered so the parent cannot
    /// replace the nested button tooltip. Re-enable and re-enter CrewAC when the pointer moves
    /// from Recall back onto the rest of the row.
    /// </summary>
    internal sealed class RetiredRecallHoverGuard : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        private Transform _row;
        private Behaviour _crewTooltipBehaviour;
        private IPointerEnterHandler _crewTooltipEnterHandler;
        private bool _suppressed;
        private bool _crewTooltipWasEnabled;

        internal void Configure(Transform row)
        {
            if (_row == row) return;

            RestoreCrewTooltip(false, null);
            _row = row;
            _crewTooltipBehaviour = null;
            _crewTooltipEnterHandler = null;
            ResolveCrewTooltip();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_suppressed || !ResolveCrewTooltip()) return;

            _crewTooltipWasEnabled = _crewTooltipBehaviour.enabled;
            if (_crewTooltipWasEnabled)
                _crewTooltipBehaviour.enabled = false;

            _suppressed = true;
            RRLog.VerboseOnce(
                "ac.recall.hoverguard.v6.suppressed",
                "[EAC] RecallHoverGuard v6 suppressed TooltipController_CrewAC on Recall enter.");
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            RestoreCrewTooltip(true, eventData);
        }

        private void OnDisable()
        {
            RestoreCrewTooltip(false, null);
        }

        private bool ResolveCrewTooltip()
        {
            if (_crewTooltipBehaviour != null) return true;
            if (_row == null) return false;

            foreach (Component component in _row.GetComponents<Component>())
            {
                if (component == null
                    || component.GetType().Name != "TooltipController_CrewAC")
                    continue;

                _crewTooltipBehaviour = component as Behaviour;
                _crewTooltipEnterHandler = component as IPointerEnterHandler;
                break;
            }

            if (_crewTooltipBehaviour == null)
            {
                RRLog.VerboseOnce(
                    "ac.recall.hoverguard.v6.crewtooltip.missing",
                    "[EAC] RecallHoverGuard v6 could not resolve TooltipController_CrewAC.");
                return false;
            }

            return true;
        }

        private void RestoreCrewTooltip(
            bool enterCrewTooltipIfStillInsideRow,
            PointerEventData eventData)
        {
            if (!_suppressed) return;

            bool shouldEnter = _crewTooltipWasEnabled
                && enterCrewTooltipIfStillInsideRow
                && IsPointerInsideRowButOutsideRecall(eventData);

            if (_crewTooltipBehaviour != null
                && _crewTooltipWasEnabled
                && !_crewTooltipBehaviour.enabled)
            {
                _crewTooltipBehaviour.enabled = true;
            }

            _suppressed = false;

            if (!shouldEnter
                || _crewTooltipEnterHandler == null
                || _crewTooltipBehaviour == null
                || !_crewTooltipBehaviour.isActiveAndEnabled
                || _row == null
                || !_row.gameObject.activeInHierarchy)
            {
                return;
            }

            _crewTooltipEnterHandler.OnPointerEnter(eventData);
            RRLog.VerboseOnce(
                "ac.recall.hoverguard.v6.restored",
                "[EAC] RecallHoverGuard v6 restored and re-entered TooltipController_CrewAC.");
        }

        private bool IsPointerInsideRowButOutsideRecall(PointerEventData eventData)
        {
            if (_row == null || eventData == null) return false;

            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            if (hit == null) hit = eventData.pointerEnter;
            Transform current = hit != null ? hit.transform : null;
            bool insideRow = false;

            while (current != null)
            {
                if (current == transform)
                    return false;

                if (current == _row)
                {
                    insideRow = true;
                    break;
                }

                current = current.parent;
            }

            return insideRow;
        }
    }

}

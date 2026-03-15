// EAC - AstronautComplexACPatch
// Harmony patches for the KSP Astronaut Complex UI.
// PERF: Uses cached retired names and crew name sets to avoid per-frame allocations.

using System;
using System.Collections.Generic;
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
            try { return a.GetTypes(); }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("acpatch.safegettypes", "[EAC] ACPatch: failed to enumerate assembly types; using empty type list.", ex);
                return new Type[0];
            }
        }
    }

    internal static partial class ACPatches
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
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:205", "Suppressed exception in AstronautComplexACPatch.cs:205", ex); }
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
    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:241", "Suppressed exception in AstronautComplexACPatch.cs:241", ex); }
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
        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:261", "Suppressed exception in AstronautComplexACPatch.cs:261", ex); }
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
                            ReflectionUtils.TryInvoke(_uiListSetActiveMethod, uiList, new object[] { active }, "ACPatch.Prefix_ActivateList.SetActive(" + i + ")");

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
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:387", "Suppressed exception in AstronautComplexACPatch.cs:387", ex); }
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
                            if (ep != null) try { ep.SetValue(c, false, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:576", "Suppressed exception in AstronautComplexACPatch.cs:576", ex); }
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

                            // Skip dead kerbals — they belong in the Lost tab, not Retired
                            if (preCheckName != null
                                && RosterRotationState.Records.TryGetValue(preCheckName, out var preRec)
                                && preRec != null && preRec.DeathUT > 0)
                            {
                                continue;
                            }

                            GameObject clone = UnityEngine.Object.Instantiate(row.gameObject, retiredList);
                            clone.SetActive(true);

                            // DestroyImmediate — regular Destroy is deferred to end-of-frame,
                            // meaning CrewListItem runs one more Update() and resets our star/label changes.
                            // Keep TooltipController_CrewAC alive — it provides the kerbal info popup on hover.
                            var toDestroy = new System.Collections.Generic.List<Component>();
                            foreach (Component c in clone.GetComponentsInChildren<Component>(true))
                                if (c != null && c.GetType().Name == "CrewListItem")
                                    toDestroy.Add(c);
                            foreach (var c in toDestroy)
                                try { UnityEngine.Object.DestroyImmediate(c); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:665", "Suppressed exception in AstronautComplexACPatch.cs:665", ex); }

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
                            NeuterUIHoverPanel(clone);

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
                            try { enabledProp.SetValue(c, false, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:734", "Suppressed exception in AstronautComplexACPatch.cs:734", ex); }
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

                // Add display-only rows for inactive / recovery kerbals that KSP omits from the
                // Available list when another mod changes rosterStatus away from Available.
                InjectUnavailableVisibleRows(availList, ROW_H);

                // Available badge is fixed in Postfix_UpdateCrewCounts which runs after KSP sets it
                // Patch "In training" / recovery status text on kerbals in Available list
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
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:800", "Suppressed exception in AstronautComplexACPatch.cs:800", ex); }
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
                    FixLostBadge(go);

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
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:865", "Suppressed exception in AstronautComplexACPatch.cs:865", ex); }
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
                    try { _updateCrewCountsMethod.Invoke(_cachedACInstance, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:992", "Suppressed exception in AstronautComplexACPatch.cs:992", ex); }

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
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:1190", "Suppressed exception in AstronautComplexACPatch.cs:1190", ex); }
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
                    RRLog.WarnOnce("acpatch.tabavailable.missing", "[RosterRotation] ACPatch: 'Tab Available' not found while fixing Available badge.");
                    if (RRLog.VerboseEnabled)
                    {
                        foreach (Transform t in acGo.transform.GetComponentsInChildren<Transform>(true))
                        {
                            if (t != null && t.name.StartsWith("Tab", StringComparison.OrdinalIgnoreCase))
                                RRLog.VerboseOnce("acpatch.tabavailable.seen." + t.name, "[EAC] ACPatch: saw tab '" + t.name + "' while looking for 'Tab Available'.");
                        }
                    }
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
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:1268", "Suppressed exception in AstronautComplexACPatch.cs:1268", ex); }
                    return;
                }
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private static System.Collections.Generic.List<string> GetRetiredNames()
        {
            return RosterRotationState.GetRetiredNames();
        }

        private static int GetRetiredCrewCount()
        {
            int n = 0;
            foreach (var kvp in RosterRotationState.Records)
                if (kvp.Value != null && kvp.Value.Retired) n++;
            return n;
        }

        private static int CountLostCrew()
        {
            try
            {
                var roster = HighLogic.CurrentGame?.CrewRoster;
                if (roster == null) return 0;

                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < roster.Count; i++)
                {
                    ProtoCrewMember pcm;
                    try { pcm = roster[i]; } catch { continue; }
                    if (pcm == null) continue;
                    if (pcm.type == ProtoCrewMember.KerbalType.Applicant) continue;

                    if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                        pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing)
                    {
                        names.Add(pcm.name);
                        continue;
                    }

                    if (RosterRotationState.Records.TryGetValue(pcm.name, out var rec) && rec != null && rec.DeathUT > 0)
                        names.Add(pcm.name);
                }

                foreach (var kvp in RosterRotationState.Records)
                {
                    if (kvp.Value != null && kvp.Value.DeathUT > 0)
                        names.Add(kvp.Key);
                }

                return names.Count;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("acpatch.countlost.fail", "[EAC] ACPatch: CountLostCrew failed; returning 0.", ex);
                return 0;
            }
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == null) continue;
                if (string.Equals(cur.name, name, StringComparison.Ordinal)) return cur;
                for (int i = cur.childCount - 1; i >= 0; i--)
                {
                    var child = cur.GetChild(i);
                    if (child != null) stack.Push(child);
                }
            }
            return null;
        }

        private static void FixLostBadge(GameObject acGo)
        {
            try
            {
                if (acGo == null) return;
                Transform tab = FindDeepChild(acGo.transform, "Tab Lost");
                if (tab == null) return;

                int count = CountLostCrew();
                string replacement = "Lost [" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";

                foreach (Component c in tab.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var p = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p == null || p.PropertyType != typeof(string)) continue;
                    string cur = null;
                    try { cur = p.GetValue(c, null) as string; } catch { continue; }
                    if (string.IsNullOrEmpty(cur)) continue;
                    if (cur.IndexOf("Lost", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try { p.SetValue(c, replacement, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.cs:1369", "Suppressed exception in AstronautComplexACPatch.cs:1369", ex); }
                    return;
                }
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterRotation] ACPatch: FixLostBadge failed: " + ex.Message);
            }
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
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("acpatch.countactivenonretired.fail", "[EAC] ACPatch: CountActiveNonRetiredCrew failed; returning -1.", ex);
                return -1;
            }
        }

    }

}

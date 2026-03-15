// EAC - AstronautComplexACPatch.Rows
// Extracted row-cloning, recall-button, and synthetic-row helpers for the Astronaut Complex UI.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    internal static partial class ACPatches
    {
        // Attempts to locate any kerbal by name in the current game's CrewRoster.
        // Uses a reflection-based fallback so it works across KSP builds.
        private static ProtoCrewMember FindKerbalByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return null;
            try
            {
                if (roster.Crew != null)
                    foreach (var k in roster.Crew)
                        if (k != null && k.name == name) return k;
                // Also check full roster (Dead/Missing kerbals)
                for (int i = 0; i < roster.Count; i++)
                {
                    ProtoCrewMember pcm;
                    try { pcm = roster[i]; } catch { continue; }
                    if (pcm != null && pcm.name == name) return pcm;
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:34", "Suppressed exception in AstronautComplexACPatch.Rows.cs:34", ex); }
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
                    try { fi.SetValue(dc, fi.GetValue(sc)); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:78", "Suppressed exception in AstronautComplexACPatch.Rows.cs:78", ex); }
                }
                // Copy public properties that have setters
                foreach (PropertyInfo pi in sc.GetType().GetProperties(f))
                {
                    if (!pi.CanRead || !pi.CanWrite) continue;
                    try { pi.SetValue(dc, pi.GetValue(sc, null), null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:84", "Suppressed exception in AstronautComplexACPatch.Rows.cs:84", ex); }
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
            if (!string.IsNullOrEmpty(name))
                RRLog.VerboseOnce("acpatch.finddesc." + name, "[EAC] ACPatch: transform '" + name + "' not found under '" + GetTransformPath(root) + "'.");
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
                if (_tcListsField == null || acGo == null)
                {
                    RRLog.VerboseOnce("acpatch.discoverlost.prereq", "[EAC] ACPatch: cannot discover Lost list because toggle-controller fields or AC root are unavailable.");
                    return null;
                }

                var tcComponents = acGo.GetComponentsInChildren<Component>(true);
                object tcInstance = null;
                foreach (var c in tcComponents)
                {
                    if (c == null) continue;
                    if (c.GetType().Name == "UIListToggleController")
                    { tcInstance = c; break; }
                }
                if (tcInstance == null)
                {
                    RRLog.WarnOnce("acpatch.discoverlost.togglecontroller.missing", "[EAC] ACPatch: UIListToggleController instance not found while discovering Lost list.");
                    return null;
                }

                var lists = _tcListsField.GetValue(tcInstance) as System.Array;
                if (lists == null)
                {
                    RRLog.WarnOnce("acpatch.discoverlost.lists.missing", "[EAC] ACPatch: UIListToggleController.lists was null while discovering Lost list.");
                    return null;
                }

                for (int i = 0; i < lists.Length; i++)
                {
                    object uiList = lists.GetValue(i);
                    if (uiList == null) continue;
                    var comp = uiList as Component;
                }

                // Lost tab is index 2 (Available=0, Assigned=1, Lost=2)
                const int LOST_INDEX = 2;
                if (lists.Length <= LOST_INDEX)
                {
                    RRLog.WarnOnce("acpatch.discoverlost.lists.short", "[EAC] ACPatch: UIListToggleController.lists does not contain Lost tab index 2.");
                    return null;
                }

                object lostUIList = lists.GetValue(LOST_INDEX);
                if (lostUIList == null)
                {
                    RRLog.WarnOnce("acpatch.discoverlost.lostuilist.null", "[EAC] ACPatch: Lost UIList entry was null while discovering Lost list.");
                    return null;
                }

                // Reflect into UIList to find its content container
                // UIList.customListAnchor is the shared scroll content area
                Type uiListType = lostUIList.GetType();
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Look for customListAnchor first (known field name from diagnostics)
                FieldInfo anchorField = uiListType.GetField("customListAnchor", bf);
                if (anchorField != null)
                {
                    object val = null;
                    try { val = anchorField.GetValue(lostUIList); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:155", "Suppressed exception in AstronautComplexACPatch.Rows.cs:155", ex); }
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

                RRLog.WarnOnce("acpatch.discoverlost.failed", "[EAC] ACPatch: could not resolve Lost list content from UIListToggleController.");
                return null;
            }
            catch (Exception ex)
            {
                RRLog.ErrorException("[RosterRotation] ACPatch: DiscoverLostListFromController failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Given a UIList component (which sits on a TAB BUTTON), reflects into it to find
        /// the actual content Transform where kerbal rows live.
        /// </summary>
        private static Transform FindUIListContent(object uiList)
        {
            if (uiList == null)
            {
                RRLog.VerboseOnce("acpatch.finduilistcontent.null", "[EAC] ACPatch: FindUIListContent called with null UIList.");
                return null;
            }
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
                    try { av = anchorField.GetValue(uiList); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:224", "Suppressed exception in AstronautComplexACPatch.Rows.cs:224", ex); }
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
            // Build lookup once (cheaper than per-component)
            var roster = HighLogic.CurrentGame.CrewRoster;
            var names = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < roster.Count; i++)
            {
                ProtoCrewMember pcm;
                try { pcm = roster[i]; } catch { continue; }
                if (pcm != null) names.Add(pcm.name);
            }
            if (names.Count == 0) return null;
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
                    try { textVal = " text='" + tp.GetValue(c, null) + "'"; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:387", "Suppressed exception in AstronautComplexACPatch.Rows.cs:387", ex); }
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
            if (row == null) return null;
            var names = RosterRotationState.GetCrewNameSet();
            if (names.Count == 0) return null;
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
                try { p.SetValue(c, newText, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:444", "Suppressed exception in AstronautComplexACPatch.Rows.cs:444", ex); }
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
                    try { p.SetValue(c, newText, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:463", "Suppressed exception in AstronautComplexACPatch.Rows.cs:463", ex); }
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
                    try { p.SetValue(c, newText, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:508", "Suppressed exception in AstronautComplexACPatch.Rows.cs:508", ex); }
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
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:533", "Suppressed exception in AstronautComplexACPatch.Rows.cs:533", ex); }
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
                        if (maxProp != null) try { maxVal = (float)maxProp.GetValue(sc, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:558", "Suppressed exception in AstronautComplexACPatch.Rows.cs:558", ex); }
                        float sliderVal = (maxVal > 1f) ? (float)stars : (stars / 5f);
                        valProp.SetValue(sc, sliderVal, null);
                        sliderSet = true;
                    }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:563", "Suppressed exception in AstronautComplexACPatch.Rows.cs:563", ex); }
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
                                try { fillAmtProp.SetValue(ic, stars / 5f, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:580", "Suppressed exception in AstronautComplexACPatch.Rows.cs:580", ex); }
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
                if (enabledP != null) try { enabledP.SetValue(usiComp, true, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:598", "Suppressed exception in AstronautComplexACPatch.Rows.cs:598", ex); }

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
                if (statesF != null) try { arr = statesF.GetValue(usiComp) as System.Array; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:616", "Suppressed exception in AstronautComplexACPatch.Rows.cs:616", ex); }
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
                                try { spriteProp.SetValue(imgComp, spriteVal, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:633", "Suppressed exception in AstronautComplexACPatch.Rows.cs:633", ex); }
                            break;
                        }
                    }
                }

                // Also call SetState(int) in case it does additional work
                var setStateM = usiComp.GetType().GetMethod("SetState",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(int) }, null);
                if (setStateM != null) try { setStateM.Invoke(usiComp, new object[] { stars }); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:643", "Suppressed exception in AstronautComplexACPatch.Rows.cs:643", ex); }

                // Critically: set currentStateIndex so future OnEnable refreshes to the right sprite.
                var csiF2 = usiComp.GetType().GetField("currentStateIndex",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (csiF2 != null) try { csiF2.SetValue(usiComp, stars); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:648", "Suppressed exception in AstronautComplexACPatch.Rows.cs:648", ex); }
            }

            if (!sliderSet)
                RRLog.WarnOnce("ac.stars.noslider", "[RosterRotation] ACPatch: SetStarsState — XP slider not found.");
        }

        private static void ForceImageVisible(Component imgComp)
        {
            if (imgComp == null) return;
            var enabledProp = imgComp.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
            if (enabledProp != null) try { enabledProp.SetValue(imgComp, true, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:659", "Suppressed exception in AstronautComplexACPatch.Rows.cs:659", ex); }
            var colorProp = imgComp.GetType().GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
            if (colorProp != null)
                try
                {
                    var c = colorProp.GetValue(imgComp, null);
                    if (c is Color col) { col.a = 1f; colorProp.SetValue(imgComp, col, null); }
                } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:666", "Suppressed exception in AstronautComplexACPatch.Rows.cs:666", ex); }
        }

        // Walks up from a leaf to the given row root and ensures every GO is active.
        private static void ActivateUpTo(Transform leaf, Transform rowRoot)
        {
            Transform t = leaf;
            int guard = 0;
            while (t != null && guard++ < 64)
            {
                try { t.gameObject.SetActive(true); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:676", "Suppressed exception in AstronautComplexACPatch.Rows.cs:676", ex); }
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
                            try { arr = statesF.GetValue(c) as System.Array; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:711", "Suppressed exception in AstronautComplexACPatch.Rows.cs:711", ex); }
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
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:733", "Suppressed exception in AstronautComplexACPatch.Rows.cs:733", ex); }

                // Recurse into children regardless of active state
                try
                {
                    for (int i = 0; i < t.childCount; i++)
                        stack.Push(t.GetChild(i));
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:741", "Suppressed exception in AstronautComplexACPatch.Rows.cs:741", ex); }
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
                    if (csiF != null) try { csiF.SetValue(uisb, targetStateIdx); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:788", "Suppressed exception in AstronautComplexACPatch.Rows.cs:788", ex); }
                    var ssF = uisb.GetType().GetField("stateSet",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (ssF != null) try { ssF.SetValue(uisb, true); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:791", "Suppressed exception in AstronautComplexACPatch.Rows.cs:791", ex); }

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
                                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:823", "Suppressed exception in AstronautComplexACPatch.Rows.cs:823", ex); }
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
                                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:841", "Suppressed exception in AstronautComplexACPatch.Rows.cs:841", ex); }
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
                                    if (cmF != null) try { cmF.SetValue(cb, 1f); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:882", "Suppressed exception in AstronautComplexACPatch.Rows.cs:882", ex); }
                                    colorsProp.SetValue(btnForColors, cb, null);
                                }
                            }
                            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:886", "Suppressed exception in AstronautComplexACPatch.Rows.cs:886", ex); }
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
                    if (tooltipEnabledP != null) try { tooltipEnabledP.SetValue(tooltip, false, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:900", "Suppressed exception in AstronautComplexACPatch.Rows.cs:900", ex); }
                    if (tooltipEnabledP != null) try { tooltipEnabledP.SetValue(tooltip, true, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:901", "Suppressed exception in AstronautComplexACPatch.Rows.cs:901", ex); }

                    // RequireInteractable=True (from dump) blocks tooltip when selectableBase is null.
                    // Set it false so the tooltip fires on hover unconditionally.
                    var riF = tooltip.GetType().GetField("RequireInteractable",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (riF == null) riF = tooltip.GetType().GetField("requireInteractable",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (riF != null) try { riF.SetValue(tooltip, false); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:909", "Suppressed exception in AstronautComplexACPatch.Rows.cs:909", ex); }

                    // Wire stateButton so tooltip reads currentStateIndex for text selection
                    if (uisb != null)
                    {
                        var sbF = tooltip.GetType().GetField("stateButton",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (sbF != null) try { sbF.SetValue(tooltip, uisb); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:916", "Suppressed exception in AstronautComplexACPatch.Rows.cs:916", ex); }
                    }

                    // Wire tooltipPrefab — check for both null AND destroyed Unity objects
                    var prefabF = tooltip.GetType().GetField("tooltipPrefab",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prefabF != null)
                    {
                        var pfVal = prefabF.GetValue(tooltip);
                        bool pfDead = (pfVal is UnityEngine.Object pfObj) ? (pfObj == null) : (pfVal == null);
                        if (pfDead)
                        {
                            foreach (Component tc in UnityEngine.Object.FindObjectsOfType(tooltip.GetType()))
                            {
                                if (tc == tooltip) continue;
                                var pf = prefabF.GetValue(tc);
                                if (pf is UnityEngine.Object livePf && livePf != null)
                                { try { prefabF.SetValue(tooltip, pf); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:933", "Suppressed exception in AstronautComplexACPatch.Rows.cs:933", ex); } break; }
                            }
                        }
                    }

                    // Set tooltipStates text: index 0 = "Cannot Recall", index 1 = "Recall Kerbal (cost)"
                    var tsF = tooltip.GetType().GetField("tooltipStates",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (tsF != null)
                    {
                        var arr = tsF.GetValue(tooltip) as System.Array;
                        if (arr != null)
                        {
                            double rCost = 0;
                            try { rCost = RosterRotationKSCUI.GetRecallFundsCost(); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:947", "Suppressed exception in AstronautComplexACPatch.Rows.cs:947", ex); }
                            string costStr = rCost > 0 ? $" ({rCost:N0})" : "";
                            for (int si = 0; si < arr.Length; si++)
                            {
                                var entry = arr.GetValue(si);
                                if (entry == null) continue;
                                string tipText = si == 0 ? "Cannot Recall" : ("Recall Kerbal" + costStr);
                                bool tipSet = false;
                                foreach (string fn in new[] { "tooltipText", "text", "tip", "message", "content", "label" })
                                {
                                    var tf = entry.GetType().GetField(fn,
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (tf != null && tf.FieldType == typeof(string))
                                    {
                                        try { tf.SetValue(entry, tipText); tipSet = true; break; }
                                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:962", "Suppressed exception in AstronautComplexACPatch.Rows.cs:962", ex); }
                                    }
                                }
                                if (!tipSet)
                                    RRLog.Warn("[RosterRotation] ACPatch: tooltip[" + si + "] — could not find text field (known: tooltipText, text, tip, message, content, label)");
                            }
                            try { tsF.SetValue(tooltip, arr); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:968", "Suppressed exception in AstronautComplexACPatch.Rows.cs:968", ex); }
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
                        catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1003", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1003", ex); }
                    }
                    // Always interactable — clicking gives feedback even for zero-star kerbals
                    var interactP = btn.GetType().GetProperty("interactable", BindingFlags.Instance | BindingFlags.Public);
                    if (interactP != null) try { interactP.SetValue(btn, true, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1007", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1007", ex); }
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
                    if (setStateM != null) try { setStateM.Invoke(sc, new object[] { idx }); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1038", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1038", ex); }
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
            if (f != null) try { f.SetValue(obj, color); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1052", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1052", ex); }
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
                    int retiredSkipped = 0, deadSkipped = 0, applicantSkipped = 0;
                    if (HighLogic.CurrentGame?.CrewRoster != null)
                        foreach (var pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                        {
                            if (pcm == null) continue;
                            if (pcm.type == ProtoCrewMember.KerbalType.Applicant) { applicantSkipped++; continue; }
                            if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                                pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing) { deadSkipped++; continue; }
                            if (RosterRotationState.Records.TryGetValue(pcm.name, out var pr) && pr != null && pr.Retired) { retiredSkipped++; continue; }
                            active++;
                        }
                    int maxCrew = ACPatches.GetCachedMaxCrew();
                    RRLog.Verbose("[EAC] RecallCheck: active=" + active + " max=" + maxCrew
                        + " retiredSkipped=" + retiredSkipped + " deadSkipped=" + deadSkipped
                        + " applicantSkipped=" + applicantSkipped);
                    if (active >= maxCrew)
                    {
                        ScreenMessages.PostScreenMessage("Cannot recall " + kerbal.name + " — crew roster is full (" + active + "/" + maxCrew + ").", 4f, ScreenMessageStyle.UPPER_CENTER);
                        return;
                    }
                }

                // Check recall funds cost
                double recallCost = RosterRotationKSCUI.GetRecallFundsCost();
                if (recallCost > 0)
                {
                    double funds = 0;
                    try { funds = Funding.Instance?.Funds ?? 0; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1104", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1104", ex); }
                    if (funds < recallCost)
                    {
                        ScreenMessages.PostScreenMessage(
                            "Cannot recall " + kerbal.name + " — insufficient funds (need √" + recallCost.ToString("N0") + ").",
                            4f, ScreenMessageStyle.UPPER_CENTER);
                        return;
                    }
                    try { Funding.Instance?.AddFunds(-recallCost, TransactionReasons.CrewRecruited); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1112", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1112", ex); }
                }

                rec.Retired = false;
                rec.RetiredUT = 0d;
                rec.ExperienceAtRetire = -1;
                RosterRotationState.InvalidateRetiredCache();
                RetiredKerbalCleanupService.ResetAutoCleanupRequest(kerbal.name);
                RosterRotationKSCUI.InvalidateCrewCapacityCache();

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

                SaveScheduler.RequestSave("recall retired kerbal");

                string costMsg = recallCost > 0 ? " (√" + recallCost.ToString("N0") + ")" : "";
                ScreenMessages.PostScreenMessage(kerbal.name + " recalled — 30-day refresher training begins." + costMsg, 4f, ScreenMessageStyle.UPPER_CENTER);

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
        private static Transform FindAnyCrewRowTemplate(Transform availList)
        {
            Transform[] roots = new Transform[] { availList, RetiredListTransform, ApplicantsListTransform, LostListTransform };
            foreach (var root in roots)
            {
                if (root == null) continue;
                for (int i = 0; i < root.childCount; i++)
                {
                    var ch = root.GetChild(i);
                    if (ch != null) return ch;
                }
            }
            return null;
        }

        private static string BuildUnavailableStatusText(ProtoCrewMember pcm, RosterRotationState.KerbalRecord rec, double nowUT)
        {
            string agePrefix = "";
            if (RosterRotationState.AgingEnabled && rec != null && rec.LastAgedYears >= 0)
                agePrefix = "Age " + RosterRotationState.GetKerbalAge(rec, nowUT) + "  ";

            double untilUT = 0;
            if (rec != null && rec.RestUntilUT > nowUT) untilUT = Math.Max(untilUT, rec.RestUntilUT);
            if (pcm != null && pcm.inactive && pcm.inactiveTimeEnd > nowUT) untilUT = Math.Max(untilUT, pcm.inactiveTimeEnd);
            string timeLeft = untilUT > nowUT ? FormatCountdownStatic(untilUT - nowUT) : "";

            string label = "Unavailable";
            if (rec != null)
            {
                if (rec.Training == TrainingType.InitialHire)
                    label = "In introductory training";
                else if (rec.Training == TrainingType.ExperienceUpgrade)
                    label = "In Level " + rec.TrainingTargetLevel + " training";
                else if (rec.Training == TrainingType.RecallRefresher)
                    label = "In refresher training";
                else if (rec.RestUntilUT > nowUT)
                    label = "In recovery";
                else if (pcm != null && pcm.inactive && pcm.inactiveTimeEnd > nowUT)
                    label = "Unavailable";
            }
            else if (pcm != null && pcm.inactive && pcm.inactiveTimeEnd > nowUT)
            {
                label = "Unavailable";
            }

            return agePrefix + label + (string.IsNullOrEmpty(timeLeft) ? "" : " " + timeLeft);
        }

        private static bool RebindSyntheticCrewRow(GameObject row, ProtoCrewMember pcm)
        {
            if (row == null || pcm == null) return false;
            bool reboundAny = false;

            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.GetType().Name != "CrewListItem") continue;
                var t = c.GetType();
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // 1) Push the target kerbal into any obvious backing field/property.
                foreach (var f in t.GetFields(bf))
                {
                    if (f == null) continue;
                    if (typeof(ProtoCrewMember).IsAssignableFrom(f.FieldType))
                    {
                        try { f.SetValue(c, pcm); reboundAny = true; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1228", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1228", ex); }
                    }
                }
                foreach (var p in t.GetProperties(bf))
                {
                    if (p == null || !p.CanWrite) continue;
                    if (typeof(ProtoCrewMember).IsAssignableFrom(p.PropertyType))
                    {
                        try { p.SetValue(c, pcm, null); reboundAny = true; } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1236", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1236", ex); }
                    }
                }

                // 2) Try the most likely setup/init methods.
                foreach (var m in t.GetMethods(bf))
                {
                    if (m == null) continue;
                    var ps = m.GetParameters();
                    try
                    {
                        if (ps.Length == 1 && typeof(ProtoCrewMember).IsAssignableFrom(ps[0].ParameterType))
                        {
                            m.Invoke(c, new object[] { pcm });
                            reboundAny = true;
                            continue;
                        }
                        if (ps.Length == 2 && typeof(ProtoCrewMember).IsAssignableFrom(ps[0].ParameterType) && ps[1].ParameterType == typeof(bool))
                        {
                            m.Invoke(c, new object[] { pcm, true });
                            reboundAny = true;
                            continue;
                        }
                    }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1260", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1260", ex); }
                }

                // 3) Stop future updates from snapping the row back to the donor kerbal.
                try { c.GetType().GetProperty("enabled", bf)?.SetValue(c, false, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1264", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1264", ex); }
            }

            return reboundAny;
        }

        private static void DisableSyntheticRowTooltips(GameObject row)
        {
            if (row == null) return;
            foreach (Component c in row.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (!tn.Contains("Tooltip") && tn != "UIStateButtonTooltip") continue;
                try { c.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(c, false, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1278", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1278", ex); }
            }
        }

        private static string GetCrewRoleText(ProtoCrewMember pcm)
        {
            if (pcm == null) return "";
            try
            {
                if (pcm.experienceTrait != null)
                {
                    var titleProp = pcm.experienceTrait.GetType().GetProperty("Title", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var title = titleProp != null ? titleProp.GetValue(pcm.experienceTrait, null) as string : null;
                    if (!string.IsNullOrEmpty(title)) return title;
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1294", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1294", ex); }
            return pcm.trait ?? "";
        }

        private static void ApplyCrewVitalsToRow(GameObject row, ProtoCrewMember pcm)
        {
            if (row == null || pcm == null) return;
            try { SetNamedSliderValue(row, "courage", Mathf.Clamp01(pcm.courage)); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1301", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1301", ex); }
            try { SetNamedSliderValue(row, "stupidity", Mathf.Clamp01(pcm.stupidity)); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1302", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1302", ex); }
        }

        private static void SetNamedSliderValue(GameObject row, string key, float value01)
        {
            foreach (Transform ch in row.GetComponentsInChildren<Transform>(true))
            {
                if (ch == null) continue;
                string name = (ch.name ?? string.Empty).ToLowerInvariant();
                string parent = (ch.parent != null ? (ch.parent.name ?? string.Empty).ToLowerInvariant() : string.Empty);
                if (!(name.Contains(key) || parent.Contains(key))) continue;

                foreach (Component sc in ch.GetComponents<Component>())
                {
                    if (sc == null || sc.GetType().Name != "Slider") continue;
                    var valProp = sc.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
                    var maxProp = sc.GetType().GetProperty("maxValue", BindingFlags.Instance | BindingFlags.Public);
                    if (valProp == null) continue;
                    float maxVal = 1f;
                    if (maxProp != null)
                        try { maxVal = Convert.ToSingle(maxProp.GetValue(sc, null)); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1322", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1322", ex); }
                    try { valProp.SetValue(sc, maxVal > 1.01f ? value01 * maxVal : value01, null); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1323", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1323", ex); }
                    return;
                }
            }
        }

        private static void InjectUnavailableVisibleRows(Transform availList, float rowH)
        {
            if (availList == null) return;

            // Remove old synthetic rows before rebuilding.
            for (int i = availList.childCount - 1; i >= 0; i--)
            {
                var ch = availList.GetChild(i);
                if (ch != null && ch.name != null && ch.name.StartsWith("EAC_Unavailable_", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(ch.gameObject);
            }

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;

            var visibleNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < availList.childCount; i++)
            {
                var row = availList.GetChild(i);
                if (row == null) continue;
                var name = GetKerbalNameFromRow(row.gameObject);
                if (!string.IsNullOrEmpty(name)) visibleNames.Add(name);
            }

            var template = FindAnyCrewRowTemplate(availList);
            if (template == null) return;

            double nowUT = Planetarium.GetUniversalTime();
            foreach (var pcm in roster.Crew)
            {
                if (pcm == null) continue;
                if (pcm.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (visibleNames.Contains(pcm.name)) continue;

                RosterRotationState.Records.TryGetValue(pcm.name, out var rec);
                if (rec != null && rec.Retired) continue;
                if (rec != null && rec.DeathUT > 0) continue;

                bool shouldShow = false;
                if (rec != null && rec.RestUntilUT > nowUT) shouldShow = true;
                if (pcm.inactive && pcm.inactiveTimeEnd > nowUT) shouldShow = true;
                if (rec != null && (rec.Training == TrainingType.InitialHire || rec.Training == TrainingType.ExperienceUpgrade || rec.Training == TrainingType.RecallRefresher) && pcm.inactive && pcm.inactiveTimeEnd > nowUT)
                    shouldShow = true;
                if (!shouldShow) continue;

                GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, availList);
                clone.name = "EAC_Unavailable_" + pcm.name;
                clone.SetActive(true);

                bool rebound = RebindSyntheticCrewRow(clone, pcm);
                if (!rebound)
                {
                    // Fall back to a safe display-only row if stock binding could not be retargeted.
                    DisableSyntheticRowTooltips(clone);
                }

                SetTextOnGO(clone, "name", pcm.name);
                SetTextOnGO(clone, "stats", GetCrewRoleText(pcm));
                ApplyCrewVitalsToRow(clone, pcm);
                ReplaceStatusText(clone, BuildUnavailableStatusText(pcm, rec, nowUT));
                SetStarsState(clone, (int)pcm.experienceLevel);

                var cloneRT = clone.GetComponent<RectTransform>();
                if (cloneRT != null && rowH > 1f)
                    cloneRT.sizeDelta = new Vector2(cloneRT.sizeDelta.x, rowH);
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
                        else if (rec.RestUntilUT > nowUT)
                            label = "In recovery";
                        else
                            label = "Inactive";
                    }
                    else
                    {
                        label = "Inactive";
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

            // Skip the expensive GetComponentsInChildren scan when the Lost tab is not visible.
            // This method is called from several postfixes (UpdateCrewCounts, CreateAvailableList,
            // CreateApplicantList, ForceRefresh) that fire frequently during normal play.
            // The Lost tab is rarely open, so in practice this guard makes nearly every call a no-op.
            if (!LostListTransform.gameObject.activeInHierarchy)
                return;

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
                if (rec.DiedOnMission)
                {
                    newStatus = age >= 0 ? "Died on mission Age " + age + ", " + dateStr : "Died on mission " + dateStr;
                }
                else if (retiredDeath)
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
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1598", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1598", ex); }
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
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.Rows.cs:1626", "Suppressed exception in AstronautComplexACPatch.Rows.cs:1626", ex); }
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

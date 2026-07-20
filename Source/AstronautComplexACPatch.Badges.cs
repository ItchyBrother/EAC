// EAC - AstronautComplexACPatch.Badges
// Extracted crew-count and badge-maintenance helpers.
// Tab creation and tab-selection entry points remain in AstronautComplexACPatch.cs.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    internal static partial class ACPatches
    {
private static bool IsStockAstronautCrew(ProtoCrewMember pcm)
        {
            return pcm != null && pcm.type == ProtoCrewMember.KerbalType.Crew;
        }

        private static ProtoCrewMember FindRosterKerbalByNameForAcCount(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var pcm in EnumerateAllRosterKerbals())
            {
                if (pcm == null || string.IsNullOrEmpty(pcm.name)) continue;
                if (string.Equals(pcm.name, name, StringComparison.Ordinal))
                    return pcm;
            }
            return null;
        }

        private static bool IsStockAstronautCrewNameForAcCount(string name)
        {
            var pcm = FindRosterKerbalByNameForAcCount(name);
            // DeepFreeze can have names cached while the stock roster entry is temporarily
            // absent.  If we cannot resolve the name, keep the DeepFreeze row/count rather
            // than dropping a legitimate frozen astronaut.
            return pcm == null || IsStockAstronautCrew(pcm);
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
                    if (!IsStockAstronautCrew(pcm)) continue;
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
            if (tab == null) return;

            Component fallbackComponent = null;
            System.Reflection.PropertyInfo fallbackProperty = null;
            string fallbackText = null;

            foreach (Component c in tab.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var p = ReflectionUtils.FindProperty(c.GetType(), "text");
                if (p == null || p.PropertyType != typeof(string)) continue;
                string s = null;
                try { s = p.GetValue(c, null) as string; } catch { continue; }
                if (string.IsNullOrEmpty(s)) continue;

                // Prefer the visible tab label itself.  Some KSP tab objects have
                // child text fields that contain bracketed values for unrelated UI
                // elements; updating the first bracketed text can leave the tab's
                // displayed count unchanged.
                if (s.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string newText;
                    if (s.Contains("[") && s.Contains("]"))
                    {
                        int bracket = s.IndexOf('[');
                        newText = s.Substring(0, bracket + 1) + count + "]";
                    }
                    else
                    {
                        newText = s + "[" + count + "]";
                    }

                    try { p.SetValue(c, newText, null); }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.SetBadgeText.label", "Suppressed exception while setting tab badge text", ex); }
                    return;
                }

                if (fallbackComponent == null && s.Contains("[") && s.Contains("]"))
                {
                    fallbackComponent = c;
                    fallbackProperty = p;
                    fallbackText = s;
                }
            }

            if (fallbackComponent != null && fallbackProperty != null)
            {
                try
                {
                    // Do not preserve the fallback label prefix.  In KSP 1.12 the
                    // reflected text component under a tab can be stale or cloned
                    // from another tab (most visibly Available).  Preserving that
                    // prefix makes Assigned inherit Available's badge text.  Force
                    // the requested tab label while keeping the corrected count.
                    string newText = label + "[" + count + "]";
                    fallbackProperty.SetValue(fallbackComponent, newText, null);
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexACPatch.SetBadgeText.fallback", "Suppressed exception while setting fallback tab badge text", ex); }
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

        private static int CountAssignedCrew()
        {
            try
            {
                var names = new HashSet<string>(StringComparer.Ordinal);

                // Refresh DeepFreeze before testing roster members.  The assigned
                // badge treats frozen Kerbals as assigned/unavailable, and using a
                // stale cache can make the badge drift from the visible Assigned list.
                try { RosterRotation.DeepFreeze.EACDeepFreezeBridge.Update(force: true); } catch { }

                foreach (var pcm in EnumerateAllRosterKerbals())
                {
                    if (pcm == null || string.IsNullOrEmpty(pcm.name)) continue;
                    if (!IsStockAstronautCrew(pcm)) continue;

                    RosterRotationState.Records.TryGetValue(pcm.name, out var rec);
                    if (rec != null && (rec.Retired || (rec.DeathUT > 0 && !rec.DeepFreezeActive)))
                        continue;

                    bool frozen = RosterRotationKSCUI.IsDeepFreezeFrozen(pcm) || (rec != null && rec.DeepFreezeActive);
                    if (frozen || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                        names.Add(pcm.name);
                }

                // Frozen Kerbals can be absent from roster.Crew and can be stored
                // as Unowned/Dead in the full roster.  The DeepFreeze cache is the
                // authoritative list for the synthetic Assigned rows, so count it
                // directly for the Assigned tab badge too.
                foreach (var frozenInfo in RosterRotation.DeepFreeze.EACDeepFreezeBridge.FrozenKerbals.Values)
                {
                    if (frozenInfo == null || string.IsNullOrEmpty(frozenInfo.Name)) continue;
                    if (!IsStockAstronautCrewNameForAcCount(frozenInfo.Name)) continue;
                    if (RosterRotationState.Records.TryGetValue(frozenInfo.Name, out var rec) &&
                        rec != null && rec.Retired)
                        continue;
                    names.Add(frozenInfo.Name);
                }

                foreach (var kvp in RosterRotationState.Records)
                {
                    if (kvp.Value != null && kvp.Value.DeepFreezeActive &&
                        !kvp.Value.Retired && kvp.Value.DeathUT <= 0 &&
                        IsStockAstronautCrewNameForAcCount(kvp.Key))
                        names.Add(kvp.Key);
                }

                return names.Count;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("acpatch.countassigned.fail", "[EAC] ACPatch: CountAssignedCrew failed; returning 0.", ex);
                return 0;
            }
        }

        private static void FixAssignedBadge(GameObject acGo)
        {
            try
            {
                if (acGo == null) return;
                Transform tab = FindDeepChild(acGo.transform, "Tab Assigned");
                if (tab == null) return;
                SetBadgeText(tab, CountAssignedCrew(), "Assigned");
            }
            catch (Exception ex)
            {
                RRLog.Warn("[RosterRotation] ACPatch: FixAssignedBadge failed: " + ex.Message);
            }
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
                    if (!IsStockAstronautCrew(pcm)) continue;

                    RosterRotationState.Records.TryGetValue(pcm.name, out var rec);
                    bool frozen = RosterRotationKSCUI.IsDeepFreezeFrozen(pcm) || (rec != null && rec.DeepFreezeActive);
                    if (frozen) continue;

                    if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                        pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing)
                    {
                        names.Add(pcm.name);
                        continue;
                    }

                    if (rec != null && rec.DeathUT > 0)
                        names.Add(pcm.name);
                }

                foreach (var kvp in RosterRotationState.Records)
                {
                    if (kvp.Value != null && kvp.Value.DeathUT > 0 && !kvp.Value.DeepFreezeActive)
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
                    var p = ReflectionUtils.FindProperty(c.GetType(), "text");
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
                    if (!IsStockAstronautCrew(pcm)) continue;
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

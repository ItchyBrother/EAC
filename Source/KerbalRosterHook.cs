using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    /// <summary>
    /// Hooks KerbalRoster active-crew count methods so "retired" Kerbals (tracked by our save Records)
    /// do NOT consume Astronaut Complex capacity.
    ///
    /// IMPORTANT: We intentionally do NOT filter roster lists/enumerables here anymore because that can
    /// break stock UI tabs (Assigned/Lost/etc). This hook only adjusts int "active crew count" methods.
    /// </summary>
    public static class KerbalRosterHook
    {
        private const string LOGP = "[RosterRotation] KerbalRosterHook: ";
        private static bool _applied;

        public static void Apply(Harmony h)
        {
            if (_applied) return;
            _applied = true;

            try
            {
                if (h == null)
                {
                    RRLog.WarnOnce("kr.nullharmony", LOGP + "Apply called with null Harmony instance.");
                    return;
                }

                var t = typeof(KerbalRoster);
                var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                int patchedCounts = 0;

                foreach (var m in methods)
                {
                    if (m == null) continue;
                    if (m.IsAbstract) continue;
                    if (m.ReturnType != typeof(int)) continue;

                    string name = (m.Name ?? "").ToLowerInvariant();

                    // Tight filter: only "active crew count" style methods/properties
                    // Examples in the wild: GetActiveCrewCount, get_ActiveCrewCount, ActiveCrewCount, etc.
                    bool looksLikeActiveCount =
                        name.Contains("active") &&
                        (name.Contains("crew") || name.Contains("kerbal")) &&
                        name.Contains("count");

                    if (!looksLikeActiveCount) continue;

                    var postfix = new HarmonyMethod(typeof(KerbalRosterHookPatches),
                        nameof(KerbalRosterHookPatches.Postfix_ActiveCount));

                    h.Patch(m, postfix: postfix);
                    patchedCounts++;
                }

                RRLog.Info(LOGP + "patched active-count methods=" + patchedCounts);
                if (patchedCounts == 0)
                {
                    // Diagnostic: help identify method names on this KSP build without spamming the log.
                    RRLog.WarnOnce("kr.nopatch", LOGP + "No active-count methods patched. Active/count int methods present:");
                    int shown = 0;
                    foreach (var m in methods)
                    {
                        if (m == null) continue;
                        if (m.ReturnType != typeof(int)) continue;
                        var n = (m.Name ?? "").ToLowerInvariant();
                        if (!n.Contains("count")) continue;
                        if (!(n.Contains("active") || n.Contains("crew") || n.Contains("kerbal"))) continue;
                        RRLog.Verbose(LOGP + "  candidate: " + m.Name);
                        if (++shown >= 25) { RRLog.Verbose(LOGP + "  ... (truncated)"); break; }
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error(LOGP + "Apply failed: " + ex);
            }
        }
    }

    internal static class KerbalRosterHookPatches
    {
        private const string LOGP = "[RosterRotation] KerbalRosterHook: ";
        private static bool _loggedFirstAdjust;

        public static void Postfix_ActiveCount(KerbalRoster __instance, MethodBase __originalMethod, ref int __result)
        {
            try
            {
                if (__result <= 0) return;

                int retired = CountRetiredCrewInRoster(__instance);
                if (retired <= 0) return;

                int fixedCount = __result - retired;
                if (fixedCount < 0) fixedCount = 0;

                if (fixedCount == __result) return;

                if (!_loggedFirstAdjust)
                {
                    _loggedFirstAdjust = true;
                    string mn = __originalMethod?.Name ?? "<unknown>";
                    RRLog.VerboseOnce("kr.adjust." + mn, LOGP + $"{mn} adjusted {__result} -> {fixedCount} (retired={retired})");
                }

                __result = fixedCount;
            }
            catch (Exception ex)
            {
                RRLog.Error(LOGP + "Postfix_ActiveCount failed: " + ex);
            }
        }

        private static int CountRetiredCrewInRoster(KerbalRoster roster)
        {
            try
            {
                if (roster == null) return 0;

                // Use reflection to be resilient across KSP versions (field vs property).
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                object crewObj = null;

                var p = roster.GetType().GetProperty("Crew", flags);
                if (p != null) crewObj = p.GetValue(roster, null);

                if (crewObj == null)
                {
                    var f = roster.GetType().GetField("Crew", flags);
                    if (f != null) crewObj = f.GetValue(roster);
                }

                var crew = crewObj as IEnumerable;
                if (crew == null) return 0;

                int n = 0;
                foreach (var o in crew)
                {
                    var k = o as ProtoCrewMember;
                    if (k == null) continue;

                    // Mirror what the AC cap generally considers "active":
                    // not applicants, not dead/missing.
                    if (k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                    if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead) continue;
                    if (k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;

                    if (RosterRotationState.Records.TryGetValue(k.name, out var rec) &&
                        rec != null && rec.Retired)
                    {
                        n++;
                    }
                }

                return n;
            }
            catch
            {
                return 0;
            }
        }
    }
}

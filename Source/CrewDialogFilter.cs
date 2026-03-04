using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    // Backup patch: filters common crew-list containers on CrewAssignmentDialog refresh.
    [HarmonyPatch]
    public static class Patch_FilterCrewAssignmentDialog
    {
        static Type TargetType() => AccessTools.TypeByName("CrewAssignmentDialog");

        static MethodBase TargetMethod()
        {
            var t = TargetType();
            if (t == null) return null;

            return AccessTools.Method(t, "RefreshCrewLists")
                ?? AccessTools.Method(t, "Refresh")
                ?? AccessTools.Method(t, "Show");
        }

        static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                if (HighLogic.LoadedScene != GameScenes.EDITOR) return;

                double nowUT = Planetarium.GetUniversalTime();

                var instType = __instance.GetType();
                var fields = instType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                int totalRemoved = 0;

                foreach (var f in fields)
                {
                    if (f == null) continue;
                    var ft = f.FieldType;
                    if (ft == null) continue;

                    if (ft == typeof(List<ProtoCrewMember>))
                    {
                        var list = f.GetValue(__instance) as List<ProtoCrewMember>;
                        if (list == null) continue;
                        int before = list.Count;
                        list.RemoveAll(k => ShouldHide(k, nowUT));
                        totalRemoved += (before - list.Count);
                        continue;
                    }

                    if (ft == typeof(ProtoCrewMember[]))
                    {
                        var arr = f.GetValue(__instance) as ProtoCrewMember[];
                        if (arr == null || arr.Length == 0) continue;
                        int before = arr.Length;
                        var tmp = new List<ProtoCrewMember>(arr);
                        tmp.RemoveAll(k => ShouldHide(k, nowUT));
                        if (tmp.Count != before && !f.IsInitOnly)
                            f.SetValue(__instance, tmp.ToArray());
                        totalRemoved += (before - tmp.Count);
                        continue;
                    }

                    if (typeof(IEnumerable<ProtoCrewMember>).IsAssignableFrom(ft))
                    {
                        var enumerable = f.GetValue(__instance) as IEnumerable<ProtoCrewMember>;
                        if (enumerable == null) continue;
                        var tmp = new List<ProtoCrewMember>();
                        int before = 0;
                        foreach (var k in enumerable) { tmp.Add(k); before++; }
                        if (before == 0) continue;

                        tmp.RemoveAll(k => ShouldHide(k, nowUT));
                        if (tmp.Count != before && !f.IsInitOnly)
                            f.SetValue(__instance, tmp);

                        totalRemoved += (before - tmp.Count);
                        continue;
                    }
                }

                if (totalRemoved > 0)
                    RRLog.Verbose($"[RosterRotation] Crew dialog filtered {totalRemoved} retired/unavailable kerbals.");
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] Crew dialog filter failed: {ex}");
            }
        }

        private static bool ShouldHide(ProtoCrewMember k, double nowUT)
        {
            if (k == null) return false;
            if (k.type == ProtoCrewMember.KerbalType.Applicant) return false;

            if (RosterRotationState.Records.TryGetValue(k.name, out var rec) && rec != null && rec.Retired)
                return true;

            if (k.inactive && k.inactiveTimeEnd > nowUT)
                return true;

            if (CrewRandRAdapter.IsOnVacationByName(k.name, nowUT))
                return true;

            return false;
        }
    }
}

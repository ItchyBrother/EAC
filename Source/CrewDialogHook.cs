using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    public static class CrewDialogHook
    {
        public static void Apply(Harmony h)
        {
            try
            {
                var asm = AssemblyLoader.loadedAssemblies
                    .Select(a => a.assembly)
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

                if (asm == null)
                {
                    RRLog.Error("[RosterRotation] CrewDialogHook: Assembly-CSharp not found.");
                    return;
                }

                // Find ANY type whose FullName contains CrewAssignmentDialog
                var t = asm.GetTypes().FirstOrDefault(x =>
                {
                    var n = x.FullName ?? x.Name;
                    return n.IndexOf("CrewAssignmentDialog", StringComparison.OrdinalIgnoreCase) >= 0;
                });

                if (t == null)
                {
                    RRLog.Error("[RosterRotation] CrewDialogHook: no *CrewAssignmentDialog* type found.");
                    return;
                }

                RRLog.Verbose($"[RosterRotation] CrewDialogHook: using type {t.FullName}");

                // Patch likely lifecycle/refresh methods IF they exist
                // Awake/Start removed.
                PatchIfExists(h, t, "Show");
                PatchIfExists(h, t, "OnEnable");
                PatchIfExists(h, t, "Refresh");
                PatchIfExists(h, t, "RefreshCrewLists");
                PatchIfExists(h, t, "UpdateCrewLists");
                PatchIfExists(h, t, "RebuildCrewLists");

                RRLog.Verbose("[RosterRotation] CrewDialogHook: applied (retired should be removed from editor crew lists).");
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] CrewDialogHook.Apply failed: {ex}");
            }
        }

        private static void PatchIfExists(Harmony h, Type t, string methodName)
        {
            var m = AccessTools.Method(t, methodName);
            if (m == null) return;

            var postfix = new HarmonyMethod(typeof(CrewDialogHookPatches), nameof(CrewDialogHookPatches.Postfix));
            h.Patch(m, postfix: postfix);

            RRLog.Verbose($"[RosterRotation] CrewDialogHook: patched {t.Name}.{methodName}()");
        }
    }

    internal static class CrewDialogHookPatches
    {
        public static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                if (HighLogic.LoadedScene != GameScenes.EDITOR) return;

                double nowUT = Planetarium.GetUniversalTime();

                var instType = __instance.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                int totalRemoved = 0;

                foreach (var f in instType.GetFields(flags))
                {
                    if (f == null) continue;
                    var ft = f.FieldType;
                    if (ft == null) continue;

                    // List<ProtoCrewMember>
                    if (ft == typeof(List<ProtoCrewMember>))
                    {
                        var list = f.GetValue(__instance) as List<ProtoCrewMember>;
                        if (list == null) continue;
                        int before = list.Count;
                        list.RemoveAll(k => ShouldHide(k, nowUT));
                        totalRemoved += (before - list.Count);
                        continue;
                    }

                    // ProtoCrewMember[]
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

                    // Any IEnumerable<ProtoCrewMember> we can write back
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
                    RRLog.Verbose($"[RosterRotation] CrewDialogHook removed {totalRemoved} kerbals from crew dialog.");
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
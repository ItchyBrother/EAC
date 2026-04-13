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
                // Use the shared cache — avoids a redundant AssemblyLoader scan and
                // ensures GetTypes() is only called once across all hooks at startup.
                var asm = KspAssemblyCache.GetAssembly();

                if (asm == null)
                {
                    RRLog.Error("[RosterRotation] CrewDialogHook: Assembly-CSharp not found.");
                    return;
                }

                // Find ANY type whose FullName contains CrewAssignmentDialog
                var t = KspAssemblyCache.GetAllTypes().FirstOrDefault(x =>
                {
                    if (x == null) return false;
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
        // Cached after the first Postfix call that has a live dialog instance.
        // CrewAssignmentDialog's type hierarchy never changes at runtime, so the
        // GetFields() walk only needs to happen once — every subsequent call reuses
        // these lists directly, eliminating the repeated reflection cost.
        private static List<FieldInfo> _cachedCrewListFields;       // fields typed List<ProtoCrewMember>
        private static List<FieldInfo> _cachedCrewArrayFields;      // fields typed ProtoCrewMember[]
        private static List<FieldInfo> _cachedCrewEnumerableFields; // other IEnumerable<ProtoCrewMember> fields

        public static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                if (HighLogic.LoadedScene != GameScenes.EDITOR) return;

                // Build the field cache the first time we have a live dialog instance.
                if (_cachedCrewListFields == null)
                    BuildFieldCache(__instance.GetType());

                double nowUT = Planetarium.GetUniversalTime();
                int totalRemoved = 0;

                foreach (var f in _cachedCrewListFields)
                {
                    try
                    {
                        var list = f.GetValue(__instance) as List<ProtoCrewMember>;
                        if (list == null) continue;
                        int before = list.Count;
                        list.RemoveAll(k => ShouldHide(k, nowUT));
                        totalRemoved += before - list.Count;
                    }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("CrewDialogHook.List:" + f.Name, "Suppressed exception scrubbing crew list field", ex); }
                }

                foreach (var f in _cachedCrewArrayFields)
                {
                    try
                    {
                        var arr = f.GetValue(__instance) as ProtoCrewMember[];
                        if (arr == null || arr.Length == 0) continue;
                        int before = arr.Length;
                        var tmp = new List<ProtoCrewMember>(arr);
                        tmp.RemoveAll(k => ShouldHide(k, nowUT));
                        if (tmp.Count != before && !f.IsInitOnly)
                            f.SetValue(__instance, tmp.ToArray());
                        totalRemoved += before - tmp.Count;
                    }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("CrewDialogHook.Array:" + f.Name, "Suppressed exception scrubbing crew array field", ex); }
                }

                foreach (var f in _cachedCrewEnumerableFields)
                {
                    try
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
                        totalRemoved += before - tmp.Count;
                    }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("CrewDialogHook.Enumerable:" + f.Name, "Suppressed exception scrubbing crew enumerable field", ex); }
                }

                if (totalRemoved > 0)
                    RRLog.Verbose($"[RosterRotation] CrewDialogHook removed {totalRemoved} kerbals from crew dialog.");
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] Crew dialog filter failed: {ex}");
            }
        }

        /// <summary>
        /// Walks the full type hierarchy of the dialog once, sorting every
        /// ProtoCrewMember-bearing field into one of three cached lists.
        /// Called exactly once; all subsequent Postfix calls use the cache.
        /// </summary>
        private static void BuildFieldCache(Type dialogType)
        {
            _cachedCrewListFields       = new List<FieldInfo>();
            _cachedCrewArrayFields      = new List<FieldInfo>();
            _cachedCrewEnumerableFields = new List<FieldInfo>();

            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var t = dialogType; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (f == null) continue;
                    var ft = f.FieldType;
                    if (ft == null) continue;

                    if (ft == typeof(List<ProtoCrewMember>))
                        _cachedCrewListFields.Add(f);
                    else if (ft == typeof(ProtoCrewMember[]))
                        _cachedCrewArrayFields.Add(f);
                    else if (typeof(IEnumerable<ProtoCrewMember>).IsAssignableFrom(ft))
                        _cachedCrewEnumerableFields.Add(f);
                }
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

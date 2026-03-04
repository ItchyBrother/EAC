// RosterRotation - LaunchBlocker
// Prevents launching a vessel if any crewed kerbal is retired or on R&R vacation.
// NOTE: The RosterRotationHarmonyBootstrap class was removed from this file.
//       It lives exclusively in HarmonyPatches.cs to avoid a duplicate-type compile error.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    [HarmonyPatch]
    public static class Patch_BlockLaunchIfCrewUnavailable
    {
        // PatchAll needs this
        static MethodBase TargetMethod() => FindTarget();

        public static MethodBase FindTarget()
        {
            var t = typeof(EditorLogic);
            var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var m in methods)
            {
                string n = (m.Name ?? "").ToLowerInvariant();
                if (!n.Contains("launch")) continue;

                var ps = m.GetParameters();
                if (ps.Length != 0) continue;

                if (m.ReturnType == typeof(void) || m.ReturnType == typeof(bool))
                    return m;
            }

            return AccessTools.Method(t, "OnLaunchClicked") ?? AccessTools.Method(t, "onLaunchClicked");
        }

        static bool Prefix(EditorLogic __instance)
        {
            try
            {
                if (__instance == null) return true;

                var ship = __instance.ship;
                if (ship == null) return true;

                double nowUT = Planetarium.GetUniversalTime();
                var crew = GetShipCrew(ship);

                foreach (var pcm in crew)
                {
                    if (pcm == null) continue;
                    if (pcm.type == ProtoCrewMember.KerbalType.Applicant) continue;

                    if (RosterRotationState.Records.TryGetValue(pcm.name, out var rec) && rec.Retired)
                    {
                        ShowBlocked($"Unable to launch: {pcm.name} is retired.");
                        return false;
                    }

                    if (CrewRandRAdapter.IsOnVacationByName(pcm.name, nowUT))
                    {
                        if (CrewRandRAdapter.TryGetVacationUntilByName(pcm.name, out var untilUT) && untilUT > nowUT)
                            ShowBlocked($"Unable to launch: {pcm.name} not available (R&R {Format(untilUT - nowUT)}).");
                        else
                            ShowBlocked($"Unable to launch: {pcm.name} not available (R&R).");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] Launch block check failed: {ex}");
                return true;
            }

            return true;
        }

        private static List<ProtoCrewMember> GetShipCrew(ShipConstruct ship)
        {
            var result = new List<ProtoCrewMember>();
            if (ship?.parts == null) return result;
            foreach (var p in ship.parts)
            {
                if (p?.protoModuleCrew == null) continue;
                foreach (var pcm in p.protoModuleCrew)
                    if (pcm != null) result.Add(pcm);
            }
            return result;
        }

        private static void ShowBlocked(string msg)
        {
            RRLog.Verbose($"[RosterRotation] {msg}");
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                "RosterRotation_LaunchBlocked", "Launch blocked", msg,
                "OK", true, HighLogic.UISkin);
        }

        private static string Format(double seconds)
        {
            if (seconds < 0) seconds = 0;
            double days = seconds / 21600.0;
            if (days >= 1) return $"{days:0.0}d";
            double hours = seconds / 3600.0;
            if (hours >= 1) return $"{hours:0.0}h";
            return $"{seconds / 60.0:0}m";
        }
    }
}

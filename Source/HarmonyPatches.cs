using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RosterRotationHarmonyBootstrap : MonoBehaviour
    {
        private const string VER = "EAC v 1.1";

        private void Start()
        {
            try
            {
                RRLog.Info(VER + " starting...");
                var h = new Harmony("RosterRotation.Patches");
                h.PatchAll(Assembly.GetExecutingAssembly());

                ApplyHook(h, "RosterRotation.KerbalRosterHook", "Apply");
                ApplyHook(h, "RosterRotation.CrewDialogHook", "Apply");
                ApplyHook(h, "RosterRotation.AstronautComplexHook", "Apply");
                ApplyHook(h, "RosterRotation.AstronautComplexACPatch", "Apply");

                RRLog.Info(VER + " hooks applied.");
            }
            catch (Exception ex) { RRLog.Error("Harmony bootstrap failed: " + ex); }
        }

        private static void ApplyHook(Harmony h, string fullTypeName, string methodName)
        {
            try
            {
                var t = Type.GetType(fullTypeName);
                if (t == null)
                {
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (a == null) continue;
                        try { t = a.GetType(fullTypeName); } catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("HarmonyPatches.cs:41", "Suppressed exception in HarmonyPatches.cs:41", ex); }
                        if (t != null) break;
                    }
                }
                if (t == null) return;

                var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) return;

                m.Invoke(null, new object[] { h });
            }
            catch (Exception ex) { RRLog.Error("Failed applying hook " + fullTypeName + "." + methodName + ": " + ex); }
        }
    }
}

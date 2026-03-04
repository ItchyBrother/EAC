using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RosterRotationHarmonyBootstrap : MonoBehaviour
    {
        private const string LOGP = "[RosterRotation] ";
        private const string VER = "HarmonyPatches v2026-02-23c";

        private void Start()
        {
            try
            {
                RRLog.Info(LOGP + VER + " starting...");

                var h = new Harmony("RosterRotation.Patches");
                h.PatchAll(Assembly.GetExecutingAssembly());
                RRLog.Verbose(LOGP + "Harmony PatchAll applied.");

                // Reflection-based hooks (not [HarmonyPatch] classes)
                ApplyHook(h, "RosterRotation.KerbalRosterHook",          "Apply");
                ApplyHook(h, "RosterRotation.CrewDialogHook",             "Apply");
                ApplyHook(h, "RosterRotation.AstronautComplexHook",       "Apply");
                ApplyHook(h, "RosterRotation.AstronautComplexACPatch",    "Apply");

                RRLog.Info(LOGP + VER + " hooks applied.");
            }
            catch (Exception ex)
            {
                RRLog.Error(LOGP + "Harmony bootstrap failed: " + ex);
            }
        }

        private static void ApplyHook(Harmony h, string fullTypeName, string methodName)
        {
            try
            {
                var t = FindType(fullTypeName);
                if (t == null)
                {
                    RRLog.Verbose(LOGP + "Hook type not found: " + fullTypeName);
                    return;
                }

                var m = t.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null)
                {
                    RRLog.Verbose(LOGP + "Hook method not found: " + fullTypeName + "." + methodName);
                    return;
                }

                RRLog.Verbose(LOGP + "Applying hook: " + fullTypeName + "." + methodName);
                m.Invoke(null, new object[] { h });
                RRLog.Verbose(LOGP + "Applied hook: " + fullTypeName + "." + methodName);
            }
            catch (Exception ex)
            {
                RRLog.Error(LOGP + "Failed applying hook " + fullTypeName + "." + methodName + ": " + ex);
            }
        }

        private static Type FindType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;

            var asms = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var a in asms)
            {
                if (a == null) continue;
                try
                {
                    t = a.GetType(fullName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}

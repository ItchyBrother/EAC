using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    public static class AstronautComplexWatcher
    {
        public static bool IsOpen { get; internal set; }
    }

    public static class AstronautComplexHook
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
                    RRLog.Error("[RosterRotation] AstronautComplexHook: Assembly-CSharp not found.");
                    return;
                }

                // In your log, this was the working type:
                // AstronautComplexFacility
                var t = asm.GetTypes().FirstOrDefault(x => x.Name == "AstronautComplexFacility");
                if (t == null)
                {
                    // fallback: name contains Astronaut + Complex
                    t = asm.GetTypes().FirstOrDefault(x =>
                    {
                        var n = x.FullName ?? x.Name;
                        return n.IndexOf("Astronaut", StringComparison.OrdinalIgnoreCase) >= 0 &&
                               n.IndexOf("Complex", StringComparison.OrdinalIgnoreCase) >= 0 &&
                               typeof(MonoBehaviour).IsAssignableFrom(x);
                    });
                }

                if (t == null)
                {
                    //RRLog.Error("[RosterRotation] AstronautComplexHook: no Astronaut Complex type found.");
                    return;
                }

                //RRLog.Verbose($"[RosterRotation] AstronautComplexHook: using type {t.FullName}");

                PatchIfExists(h, t, "Start", nameof(AstronautComplexHookPatches.Open_Postfix));
                PatchIfExists(h, t, "OnDestroy", nameof(AstronautComplexHookPatches.Close_Postfix));
                PatchIfExists(h, t, "OnDisable", nameof(AstronautComplexHookPatches.Close_Postfix));
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] AstronautComplexHook.Apply failed: {ex}");
            }
        }

        private static void PatchIfExists(Harmony h, Type targetType, string methodName, string postfixName)
        {
            var m = AccessTools.Method(targetType, methodName);
            if (m == null) return;

            var postfix = new HarmonyMethod(typeof(AstronautComplexHookPatches), postfixName);
            h.Patch(m, postfix: postfix);
            //RRLog.Verbose($"[RosterRotation] AstronautComplexHook: patched {targetType.Name}.{methodName}()");
        }
    }

    internal static class AstronautComplexHookPatches
    {
        // We can't safely set IsOpen=true immediately because this type may exist at KSC startup.
        // So we delay one frame and confirm the AstronautComplex UI actually exists in Resources.
        public static void Open_Postfix()
        {
            AstronautComplexProbeRunner.RequestOpenCheck();
        }

        public static void Close_Postfix()
        {
            if (AstronautComplexWatcher.IsOpen)
            {
                AstronautComplexWatcher.IsOpen = false;
                //RRLog.Verbose("[RosterRotation] AstronautComplexWatcher: IsOpen=False");
            }
        }
    }

    /// <summary>
    /// Small runner MonoBehaviour used to do a delayed "is the AC UI actually present?" check.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class AstronautComplexProbeRunner : MonoBehaviour
    {
        private static AstronautComplexProbeRunner _instance;
        private bool _pendingOpenCheck;

        private void Awake() => _instance = this;

        public static void RequestOpenCheck()
        {
            if (_instance != null)
                _instance._pendingOpenCheck = true;
        }

        private void Update()
        {
            if (!_pendingOpenCheck) return;
            _pendingOpenCheck = false;

            // Confirm AC UI exists (even if inactive)
            bool found = Resources.FindObjectsOfTypeAll<GameObject>()
                .Any(go => go != null && go.name != null &&
                           go.name.IndexOf("AstronautComplex", StringComparison.OrdinalIgnoreCase) >= 0);

            if (found && !AstronautComplexWatcher.IsOpen)
            {
                AstronautComplexWatcher.IsOpen = true;
                //RRLog.Verbose("[RosterRotation] AstronautComplexWatcher: IsOpen=True");
            }
        }
    }
}
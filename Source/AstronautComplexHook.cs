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
                // Use the shared cache — avoids a redundant AssemblyLoader scan since
                // GetAllTypes() (called below) already resolves and caches the assembly.
                var asm = KspAssemblyCache.GetAssembly();

                if (asm == null)
                {
                    RRLog.Error("[EAC] AstronautComplexHook: Assembly-CSharp not found.");
                    return;
                }

                // Patch 1: AstronautComplexFacility (the KSC building object).
                // Uses KspAssemblyCache so GetTypes() is only called once across all hooks.
                // The two-pass search is collapsed into a single LINQ pass: exact name first,
                // then fuzzy fallback — same logic, zero extra scans.
                var facilityType = KspAssemblyCache.GetAllTypes().FirstOrDefault(x =>
                {
                    if (x == null) return false;
                    if (x.Name == "AstronautComplexFacility") return true;
                    var n = x.FullName ?? x.Name;
                    return n.IndexOf("Astronaut",  StringComparison.OrdinalIgnoreCase) >= 0 &&
                           n.IndexOf("Complex",    StringComparison.OrdinalIgnoreCase) >= 0 &&
                           n.IndexOf("Facility",   StringComparison.OrdinalIgnoreCase) >= 0 &&
                           typeof(MonoBehaviour).IsAssignableFrom(x);
                });

                if (facilityType != null)
                {
                    PatchIfExists(h, facilityType, "Start", nameof(AstronautComplexHookPatches.Open_Postfix));
                    PatchIfExists(h, facilityType, "OnDestroy", nameof(AstronautComplexHookPatches.Close_Postfix));
                    PatchIfExists(h, facilityType, "OnDisable", nameof(AstronautComplexHookPatches.Close_Postfix));
                }

                // Patch 2: KSP.UI.Screens.AstronautComplex (the dialog UI)
                // This is what actually opens/closes when the player clicks the building.
                // Patching it ensures ACOpenCache.Invalidate() fires immediately on dialog
                // open/close, eliminating the 0-3 second button appearance delay.
                var dialogType = asm.GetType("KSP.UI.Screens.AstronautComplex");
                if (dialogType != null)
                {
                    PatchIfExists(h, dialogType, "Start", nameof(AstronautComplexHookPatches.Open_Postfix));
                    PatchIfExists(h, dialogType, "Awake", nameof(AstronautComplexHookPatches.Open_Postfix));
                    PatchIfExists(h, dialogType, "OnDestroy", nameof(AstronautComplexHookPatches.Close_Postfix));
                    PatchIfExists(h, dialogType, "OnDisable", nameof(AstronautComplexHookPatches.Close_Postfix));
                    RRLog.Verbose("[EAC] AstronautComplexHook: patched AC dialog UI lifecycle.");
                }
                else
                {
                    RRLog.Warn("[EAC] AstronautComplexHook: KSP.UI.Screens.AstronautComplex not found — button may appear with delay.");
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"[EAC] AstronautComplexHook.Apply failed: {ex}");
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
            ACOpenCache.Invalidate();
            AstronautComplexProbeRunner.RequestOpenCheck();
        }

        public static void Close_Postfix()
        {
            ACOpenCache.Invalidate();
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
        private static Type _acType;
        private static bool _typeSearched;
        private bool _pendingOpenCheck;

        private void Awake() => _instance = this;

        public static void RequestOpenCheck()
        {
            if (_instance != null)
                _instance._pendingOpenCheck = true;
        }

        private static bool HasActiveAstronautComplexScreen()
        {
            try
            {
                if (!_typeSearched)
                {
                    _typeSearched = true;
                    var asm = AssemblyLoader.loadedAssemblies
                        .Select(a => a.assembly)
                        .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                    if (asm != null)
                        _acType = asm.GetType("KSP.UI.Screens.AstronautComplex");
                }

                if (_acType == null) return false;

                var all = Resources.FindObjectsOfTypeAll(_acType);
                foreach (var obj in all)
                {
                    var mb = obj as MonoBehaviour;
                    if (mb != null && mb.isActiveAndEnabled && mb.gameObject != null && mb.gameObject.activeInHierarchy)
                        return true;
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("AstronautComplexHook.cs:150", "Suppressed exception in AstronautComplexHook.cs:150", ex); }

            return false;
        }

        private void Update()
        {
            if (!_pendingOpenCheck) return;
            _pendingOpenCheck = false;

            bool found = HasActiveAstronautComplexScreen();
            if (AstronautComplexWatcher.IsOpen != found)
            {
                AstronautComplexWatcher.IsOpen = found;
                ACOpenCache.Invalidate();
                //RRLog.Verbose($"[RosterRotation] AstronautComplexWatcher: IsOpen={found}");
            }
        }
    }
}
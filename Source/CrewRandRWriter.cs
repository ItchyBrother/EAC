using System;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    public static class CrewRandRWriter
    {
        // 60 Kerbin years by default. (Kerbin year ≈ 9,201,600 seconds)
        public const double KerbinYearSeconds = 9201600.0;

        public static bool TrySetVacationUntil(string kerbalName, double untilUT)
        {
            try
            {
                var asm = AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a?.name != null &&
                                         a.name.IndexOf("crewrandr", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?.assembly;

                if (asm == null) return false;

                // Find CrewRandRRoster
                var rosterType = asm.GetTypes().FirstOrDefault(t => (t.FullName ?? "").Contains("CrewRandRRoster"));
                if (rosterType == null) return false;

                var instProp = rosterType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var rosterInst = instProp?.GetValue(null, null);
                if (rosterInst == null) return false;

                var extProp = rosterType.GetProperty("ExtDataSet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var extSet = extProp?.GetValue(rosterInst, null) as IEnumerable;
                if (extSet == null) return false;

                foreach (var ext in extSet)
                {
                    if (ext == null) continue;
                    var extType = ext.GetType();

                    // Match ProtoReference -> ProtoCrewMember
                    var protoRef =
                        extType.GetField("ProtoReference", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ext)
                        ?? extType.GetProperty("ProtoReference", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ext, null);

                    if (protoRef is ProtoCrewMember pcm && pcm.name == kerbalName)
                    {
                        // Find a likely vacation end field/property and set it.
                        // Prefer names with "vac" + ("end"/"until"/"return"/"next")
                        if (TrySetBestNumericMember(ext, extType, untilUT))
                            return true;

                        // If we can't identify a member to set, fail
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] CrewRandRWriter.TrySetVacationUntil failed: {ex}");
            }

            return false;
        }

        private static bool TrySetBestNumericMember(object ext, Type extType, double untilUT)
        {
            // fields first
            foreach (var f in extType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType != typeof(double) && f.FieldType != typeof(float)) continue;

                string n = (f.Name ?? "").ToLowerInvariant();
                bool nameMatch = n.Contains("vac") && (n.Contains("end") || n.Contains("until") || n.Contains("return") || n.Contains("next"));
                if (!nameMatch) continue;

                if (f.FieldType == typeof(float)) f.SetValue(ext, (float)untilUT);
                else f.SetValue(ext, untilUT);

                return true;
            }

            // properties next
            foreach (var p in extType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!p.CanWrite) continue;
                if (p.PropertyType != typeof(double) && p.PropertyType != typeof(float)) continue;

                string n = (p.Name ?? "").ToLowerInvariant();
                bool nameMatch = n.Contains("vac") && (n.Contains("end") || n.Contains("until") || n.Contains("return") || n.Contains("next"));
                if (!nameMatch) continue;

                if (p.PropertyType == typeof(float)) p.SetValue(ext, (float)untilUT, null);
                else p.SetValue(ext, untilUT, null);

                return true;
            }

            return false;
        }
    }
}
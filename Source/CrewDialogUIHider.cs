using System;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    internal static class CrewDialogUIHider
    {
        // Common member names found on UI row/view-model components
        private static readonly string[] MemberNames =
        {
            "crewMember", "CrewMember", "kerbal", "Kerbal", "pcm", "PCM",
            "protoCrewMember", "ProtoCrewMember", "member", "Member"
        };

        public static int HideRetiredRows(MonoBehaviour dialog)
        {
            int hidden = 0;

            try
            {
                // Search ALL MonoBehaviours under the dialog's UI tree (including inactive objects)
                var comps = dialog.GetComponentsInChildren<MonoBehaviour>(true);

                foreach (var c in comps)
                {
                    if (c == null) continue;
                    if (ReferenceEquals(c, dialog)) continue;

                    var pcm = TryExtractPCM(c);
                    if (pcm == null) continue;

                    // Ignore applicants
                    if (pcm.type == ProtoCrewMember.KerbalType.Applicant) continue;

                    // Hide only retired
                    if (!RosterRotationState.Records.TryGetValue(pcm.name, out var rec) || !rec.Retired)
                        continue;

                    // Disable the row GameObject ONLY (local to this dialog instance)
                    if (c.gameObject != null && c.gameObject.activeSelf)
                    {
                        c.gameObject.SetActive(false);
                        hidden++;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.Error($"[RosterRotation] CrewDialogUIHider failed: {ex}");
            }

            return hidden;
        }

        private static ProtoCrewMember TryExtractPCM(object obj)
        {
            var ot = obj.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Try common names first
            foreach (var n in MemberNames)
            {
                var f = ot.GetField(n, flags);
                if (f != null)
                {
                    try
                    {
                        var v = f.GetValue(obj);
                        if (v is ProtoCrewMember pcm) return pcm;
                    }
                    catch { }
                }

                var p = ot.GetProperty(n, flags);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        var v = p.GetValue(obj, null);
                        if (v is ProtoCrewMember pcm) return pcm;
                    }
                    catch { }
                }
            }

            // Last resort: any field/property of type ProtoCrewMember
            foreach (var f in ot.GetFields(flags))
            {
                if (f.FieldType != typeof(ProtoCrewMember)) continue;
                try { return f.GetValue(obj) as ProtoCrewMember; } catch { }
            }

            foreach (var p in ot.GetProperties(flags))
            {
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (p.PropertyType != typeof(ProtoCrewMember)) continue;
                try { return p.GetValue(obj, null) as ProtoCrewMember; } catch { }
            }

            return null;
        }
    }
}
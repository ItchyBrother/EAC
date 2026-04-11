using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RosterRotation
{
    /// <summary>
    /// Prevents retired kerbals from appearing in the VAB/SPH crew assignment dialog.
    ///
    /// While in the editor, retired kerbals have their ProtoCrewMember.type temporarily
    /// set to Unowned.  KerbalRoster.Crew only yields type==Crew kerbals, so the stock
    /// CrewAssignmentDialog never sees them.  On editor exit (and before any save) the
    /// original type is restored.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorCrewRetiredHider : MonoBehaviour
    {
        private const string LOGP = "[RosterRotation] EditorHider: ";

        // Static so the hidden state survives across the brief OnDestroy → re-Awake cycle
        // that can happen during scene reloads. OnDestroy always calls RestoreRetiredKerbals()
        // which clears the dictionary before any save, so there is no risk of a retired
        // kerbal's type being permanently left as Unowned across sessions.
        private static readonly Dictionary<string, ProtoCrewMember.KerbalType> _hiddenKerbals =
            new Dictionary<string, ProtoCrewMember.KerbalType>();

        private float _nextEnforce;
        private const float ENFORCE_INTERVAL = 1.0f;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Start()
        {
            HideRetiredKerbals();
            GameEvents.onGameStateSave.Add(OnBeforeSave);
        }

        private void Update()
        {
            if (Time.time < _nextEnforce) return;
            _nextEnforce = Time.time + ENFORCE_INTERVAL;

            HideRetiredKerbals();
            ScrubCrewDialog();
        }

        private void OnDestroy()
        {
            GameEvents.onGameStateSave.Remove(OnBeforeSave);
            RestoreRetiredKerbals();
        }

        // ─── Save protection ─────────────────────────────────────────────────

        private void OnBeforeSave(ConfigNode node)
        {
            RestoreRetiredKerbals();
            StartCoroutine(ReHideAfterSave());
        }

        private IEnumerator ReHideAfterSave()
        {
            yield return null;
            yield return null;
            HideRetiredKerbals();
        }

        // ─── Primary: Unowned type-swap ──────────────────────────────────────

        private static int HideRetiredKerbals()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return 0;

            // Fast path: no EAC records means no retired kerbals to process.
            if (RosterRotationState.Records.Count == 0) return 0;

            int count = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                ProtoCrewMember k;
                try { k = roster[i]; } catch { continue; }
                if (k == null) continue;

                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (rec == null || !rec.Retired) continue;
                if (k.rosterStatus != ProtoCrewMember.RosterStatus.Available) continue;

                if (k.type == ProtoCrewMember.KerbalType.Unowned)
                {
                    if (!_hiddenKerbals.ContainsKey(k.name))
                        _hiddenKerbals[k.name] = ProtoCrewMember.KerbalType.Crew;
                    continue;
                }

                _hiddenKerbals[k.name] = k.type;
                k.type = ProtoCrewMember.KerbalType.Unowned;
                count++;
            }

            return count;
        }

        private static int RestoreRetiredKerbals()
        {
            if (_hiddenKerbals.Count == 0) return 0;

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return 0;

            int count = 0;

            foreach (var kvp in _hiddenKerbals)
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    ProtoCrewMember k;
                    try { k = roster[i]; } catch { continue; }
                    if (k == null || k.name != kvp.Key) continue;

                    if (k.type == ProtoCrewMember.KerbalType.Unowned)
                    {
                        k.type = kvp.Value;
                        count++;
                    }
                    break;
                }
            }

            _hiddenKerbals.Clear();
            return count;
        }

        // ─── Secondary: Dialog list scrubbing ────────────────────────────────

        // Cached after the first successful scrub. CrewAssignmentDialog's type
        // hierarchy never changes at runtime, so we only need to walk it once.
        // This eliminates the repeated GetFields() reflection cost from every
        // 1-second tick while the dialog is open.
        private static List<FieldInfo> _cachedCrewListFields;   // fields typed List<ProtoCrewMember>
        private static List<FieldInfo> _cachedCrewArrayFields;  // fields typed ProtoCrewMember[]

        private static int ScrubCrewDialog()
        {
            try
            {
                object dialog = FindCrewDialogInstance();
                if (dialog == null) return 0;

                return ScrubRetiredFromObject(dialog);
            }
            catch { return 0; }
        }

        private static int ScrubRetiredFromObject(object obj)
        {
            if (obj == null) return 0;

            // Build the field cache the first time we have a live dialog instance.
            // After this point the lists are reused directly — no more GetFields()
            // on every tick.
            if (_cachedCrewListFields == null)
                BuildScrubFieldCache(obj.GetType());

            int total = 0;

            foreach (var field in _cachedCrewListFields)
            {
                try
                {
                    var list = field.GetValue(obj) as List<ProtoCrewMember>;
                    if (list == null || list.Count == 0) continue;
                    int before = list.Count;
                    list.RemoveAll(IsRetired);
                    total += before - list.Count;
                }
                catch (Exception ex) { RRLog.VerboseExceptionOnce("EditorCrewRetiredHider.ScrubList:" + field.Name, "Suppressed exception scrubbing crew list field", ex); }
            }

            foreach (var field in _cachedCrewArrayFields)
            {
                try
                {
                    var arr = field.GetValue(obj) as ProtoCrewMember[];
                    if (arr == null || arr.Length == 0) continue;
                    int before = arr.Length;
                    var filtered = new List<ProtoCrewMember>(arr);
                    filtered.RemoveAll(IsRetired);
                    if (filtered.Count != before && !field.IsInitOnly)
                    {
                        field.SetValue(obj, filtered.ToArray());
                        total += before - filtered.Count;
                    }
                }
                catch (Exception ex) { RRLog.VerboseExceptionOnce("EditorCrewRetiredHider.ScrubArray:" + field.Name, "Suppressed exception scrubbing crew array field", ex); }
            }

            return total;
        }

        /// <summary>
        /// Walks the full type hierarchy of the dialog once, collecting every field
        /// typed as List&lt;ProtoCrewMember&gt; or ProtoCrewMember[].  Results are stored
        /// in the static caches and reused on all subsequent ticks.
        /// </summary>
        private static void BuildScrubFieldCache(Type dialogType)
        {
            _cachedCrewListFields  = new List<FieldInfo>();
            _cachedCrewArrayFields = new List<FieldInfo>();

            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var t = dialogType; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(flags))
                {
                    if (field == null) continue;

                    if (field.FieldType == typeof(List<ProtoCrewMember>))
                        _cachedCrewListFields.Add(field);
                    else if (field.FieldType == typeof(ProtoCrewMember[]))
                        _cachedCrewArrayFields.Add(field);
                }
            }
        }

        private static bool IsRetired(ProtoCrewMember k)
        {
            if (k == null) return false;
            if (k.type == ProtoCrewMember.KerbalType.Applicant) return false;
            return RosterRotationState.Records.TryGetValue(k.name, out var rec) && rec != null && rec.Retired;
        }

        // ─── Dialog instance finding ─────────────────────────────────────────

        private static Type _cachedDialogType;
        private static FieldInfo _cachedInstanceField;
        private static PropertyInfo _cachedInstanceProp;

        private static object FindCrewDialogInstance()
        {
            try
            {
                if (_cachedDialogType == null)
                    _cachedDialogType = FindCrewDialogType();
                if (_cachedDialogType == null) return null;

                if (_cachedInstanceProp != null)
                {
                    try { var v = _cachedInstanceProp.GetValue(null, null); if (v != null) return v; }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EditorCrewRetiredHider.cs:216", "Suppressed exception in EditorCrewRetiredHider.cs:216", ex); }
                }

                if (_cachedInstanceField != null)
                {
                    try { var v = _cachedInstanceField.GetValue(null); if (v != null) return v; }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EditorCrewRetiredHider.cs:222", "Suppressed exception in EditorCrewRetiredHider.cs:222", ex); }
                }

                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var prop in _cachedDialogType.GetProperties(sf))
                {
                    if (!prop.CanRead) continue;
                    if (!_cachedDialogType.IsAssignableFrom(prop.PropertyType) &&
                        prop.PropertyType != typeof(object)) continue;
                    try
                    {
                        var v = prop.GetValue(null, null);
                        if (v != null) { _cachedInstanceProp = prop; return v; }
                    }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EditorCrewRetiredHider.cs:237", "Suppressed exception in EditorCrewRetiredHider.cs:237", ex); }
                }

                foreach (var field in _cachedDialogType.GetFields(sf))
                {
                    if (!_cachedDialogType.IsAssignableFrom(field.FieldType) &&
                        field.FieldType != typeof(object)) continue;
                    try
                    {
                        var v = field.GetValue(null);
                        if (v != null) { _cachedInstanceField = field; return v; }
                    }
                    catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EditorCrewRetiredHider.cs:249", "Suppressed exception in EditorCrewRetiredHider.cs:249", ex); }
                }

                if (typeof(UnityEngine.Object).IsAssignableFrom(_cachedDialogType))
                {
                    var obj = UnityEngine.Object.FindObjectOfType(_cachedDialogType);
                    if (obj != null) return obj;
                }
            }
            catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EditorCrewRetiredHider.cs:258", "Suppressed exception in EditorCrewRetiredHider.cs:258", ex); }

            return null;
        }

        private static Type FindCrewDialogType()
        {
            foreach (var la in AssemblyLoader.loadedAssemblies)
            {
                if (la?.assembly == null) continue;
                if (la.assembly.GetName().Name != "Assembly-CSharp") continue;

                try
                {
                    foreach (var t in la.assembly.GetTypes())
                    {
                        if (t == null) continue;
                        var name = t.FullName ?? t.Name;
                        if (name.IndexOf("CrewAssignmentDialog", StringComparison.OrdinalIgnoreCase) >= 0)
                            return t;
                    }
                }
                catch (global::System.Exception ex) { RRLog.VerboseExceptionOnce("EditorCrewRetiredHider.cs:280", "Suppressed exception in EditorCrewRetiredHider.cs:280", ex); }
            }

            return null;
        }
    }
}

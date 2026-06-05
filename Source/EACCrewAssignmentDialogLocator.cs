using System;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    /// <summary>
    /// Reflection-only stock editor crew panel/dialog detection. Kept separate
    /// from the advisor window so future auto-fill investigation cannot leak
    /// back into recommendation scoring.
    /// </summary>
    internal static class EACCrewAssignmentDialogLocator
    {
        private static Type _cachedDialogType;
        private static FieldInfo _cachedInstanceField;
        private static PropertyInfo _cachedInstanceProp;

        internal static bool IsCrewEditorPanelSelected()
        {
            try
            {
                if (HighLogic.LoadedScene != GameScenes.EDITOR) return false;

                var editorType = typeof(EditorLogic);
                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags inf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                object editor = null;
                var fetchField = editorType.GetField("fetch", sf);
                if (fetchField != null)
                    editor = fetchField.GetValue(null);

                if (editor == null)
                {
                    var fetchProp = editorType.GetProperty("fetch", sf);
                    if (fetchProp != null && fetchProp.CanRead)
                        editor = fetchProp.GetValue(null, null);
                }

                if (editor == null) return false;

                object screen = null;
                var screenField = editor.GetType().GetField("editorScreen", inf);
                if (screenField != null)
                    screen = screenField.GetValue(editor);

                if (screen == null)
                {
                    var screenProp = editor.GetType().GetProperty("editorScreen", inf);
                    if (screenProp != null && screenProp.CanRead)
                        screen = screenProp.GetValue(editor, null);
                }

                if (screen == null) return false;
                string name = screen.ToString();
                return !string.IsNullOrEmpty(name) && name.IndexOf("Crew", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACCrewAssignmentDialogLocator.IsCrewEditorPanelSelected", "Suppressed exception checking editor crew panel", ex);
                return false;
            }
        }

        internal static bool IsCrewAssignmentDialogOpen()
        {
            object dialog = FindCrewDialogInstance();
            if (dialog == null) return false;

            var component = dialog as Component;
            if (component != null && component.gameObject != null)
                return component.gameObject.activeInHierarchy;

            var go = dialog as GameObject;
            if (go != null) return go.activeInHierarchy;

            return true;
        }

        internal static object FindCrewDialogInstance()
        {
            try
            {
                if (_cachedDialogType == null)
                    _cachedDialogType = FindCrewDialogType();
                if (_cachedDialogType == null) return null;

                if (_cachedInstanceProp != null)
                {
                    try { var v = _cachedInstanceProp.GetValue(null, null); if (v != null) return v; }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("EACCrewAssignmentDialogLocator.InstanceProp", "Suppressed exception reading crew dialog instance property", ex); }
                }

                if (_cachedInstanceField != null)
                {
                    try { var v = _cachedInstanceField.GetValue(null); if (v != null) return v; }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("EACCrewAssignmentDialogLocator.InstanceField", "Suppressed exception reading crew dialog instance field", ex); }
                }

                const BindingFlags sf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var prop in _cachedDialogType.GetProperties(sf))
                {
                    if (!prop.CanRead) continue;
                    if (!_cachedDialogType.IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(object)) continue;
                    try
                    {
                        var v = prop.GetValue(null, null);
                        if (v != null) { _cachedInstanceProp = prop; return v; }
                    }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("EACCrewAssignmentDialogLocator.FindProp", "Suppressed exception scanning crew dialog instance property", ex); }
                }

                foreach (var field in _cachedDialogType.GetFields(sf))
                {
                    if (!_cachedDialogType.IsAssignableFrom(field.FieldType) && field.FieldType != typeof(object)) continue;
                    try
                    {
                        var v = field.GetValue(null);
                        if (v != null) { _cachedInstanceField = field; return v; }
                    }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("EACCrewAssignmentDialogLocator.FindField", "Suppressed exception scanning crew dialog instance field", ex); }
                }

                if (typeof(UnityEngine.Object).IsAssignableFrom(_cachedDialogType))
                {
                    var obj = UnityEngine.Object.FindObjectOfType(_cachedDialogType);
                    if (obj != null) return obj;
                }
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("EACCrewAssignmentDialogLocator.FindDialog", "Suppressed exception finding crew assignment dialog", ex); }

            return null;
        }

        private static Type FindCrewDialogType()
        {
            try
            {
                var types = KspAssemblyCache.GetAllTypes();
                if (types != null)
                {
                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        var name = t.FullName ?? t.Name;
                        if (name.IndexOf("CrewAssignmentDialog", StringComparison.OrdinalIgnoreCase) >= 0)
                            return t;
                    }
                }
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("EACCrewAssignmentDialogLocator.FindDialogType", "Suppressed exception finding crew assignment dialog type", ex); }

            return null;
        }
    }
}

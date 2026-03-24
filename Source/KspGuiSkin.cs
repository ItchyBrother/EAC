using UnityEngine;
using KSP;

namespace RosterRotation
{
    internal static class KspGuiSkin
    {
        public static GUISkin Current
        {
            get
            {
                return HighLogic.Skin ?? GUI.skin;
            }
        }

        public static GUIStyle Window
        {
            get
            {
                GUISkin skin = Current;
                return skin != null ? skin.window : GUIStyle.none;
            }
        }

        public static GUIStyle Label
        {
            get
            {
                GUISkin skin = Current;
                return skin != null ? skin.label : GUIStyle.none;
            }
        }

        public static GUIStyle Button
        {
            get
            {
                GUISkin skin = Current;
                return skin != null ? skin.button : GUIStyle.none;
            }
        }

        public static GUIStyle Box
        {
            get
            {
                GUISkin skin = Current;
                return skin != null ? skin.box : GUIStyle.none;
            }
        }

        public static GUIStyle HorizontalSlider
        {
            get
            {
                GUISkin skin = Current;
                return skin != null ? skin.horizontalSlider : GUIStyle.none;
            }
        }
    }
}

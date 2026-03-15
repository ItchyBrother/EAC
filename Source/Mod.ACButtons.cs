// EAC - Mod.ACButtons
// Extracted small UI bridge types from Mod.cs.

using UnityEngine;
using KSP;

namespace RosterRotation
{
    // ── AC "Astronaut Management" button ────────────────────────────────────────
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class RosterRotationACButtons : MonoBehaviour
    {
        private GUIStyle _boldBtn;

        private void OnGUI()
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return;
            if (!ACOpenCache.IsOpen) return;

            if (_boldBtn == null)
            {
                _boldBtn = new GUIStyle(GUI.skin.button);
                _boldBtn.fontStyle = FontStyle.Bold;
                _boldBtn.fontSize = 16;
                _boldBtn.wordWrap = false;
            }

            float W = Screen.width, H = Screen.height;
            float btnW = 220f, btnH = 34f;
            float x = W * 0.44f - btnW * 0.5f;
            float y = H * 0.070f - btnH * 0.5f;

            if (GUI.Button(new Rect(x, y, btnW, btnH), "Astronaut Management", _boldBtn))
                RosterRotationKSCUIBridge.RequestOverlay(RosterRotationKSCUIBridge.AcOverlayOpen);
        }
    }

    // ── Bridge ──────────────────────────────────────────────────────────────────
    public static class RosterRotationKSCUIBridge
    {
        public const int AcOverlayNone = 0;
        public const int AcOverlayOpen = 1;
        public const int AcOverlayApplicants = 1;
        public const int AcOverlayTraining = 1;
        public const int AcOverlayForceRetire = 1;

        private static volatile int _pendingOverlay = AcOverlayNone;
        public static void RequestOverlay(int which) => _pendingOverlay = which;
        public static int ConsumeOverlay()
        {
            int v = _pendingOverlay;
            _pendingOverlay = AcOverlayNone;
            return v;
        }
    }
}

// EAC - DeepFreeze compatibility: scene monitors.
// Keeps DeepFreeze polling and reconciliation outside the large core files.

using KSP;
using UnityEngine;

namespace RosterRotation.DeepFreeze
{
    internal abstract class EACDeepFreezeSceneMonitor : MonoBehaviour
    {
        private const float PollSeconds = 5f;
        private float _nextPollRT;

        private void Start()
        {
            PollNow("DeepFreeze monitor start");
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextPollRT) return;
            PollNow("DeepFreeze monitor poll");
        }

        private void PollNow(string saveReason)
        {
            _nextPollRT = Time.realtimeSinceStartup + PollSeconds;
            if (RosterRotationKSCUI.ReconcileDeepFreezeForCurrentRoster())
                SaveScheduler.RequestSave(saveReason);
        }
    }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    internal sealed class EACDeepFreezeSpaceCentreMonitor : EACDeepFreezeSceneMonitor { }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    internal sealed class EACDeepFreezeFlightMonitor : EACDeepFreezeSceneMonitor { }
}

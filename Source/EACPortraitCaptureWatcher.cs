using UnityEngine;
using KSP;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class EACPortraitCaptureWatcher : MonoBehaviour
    {
        private const float PollIntervalSeconds = 0.5f;   // Poll every 0.5s so we catch the RenderTexture quickly
        private const float RetryWindowSeconds = 20.0f;   // Wide window — retries until RenderTexture appears
        private const float StartupDelaySeconds = 1.25f;  // Start early; Texture2D placeholders are now rejected
        private const float VesselChangeDelaySeconds = 0.75f; // Same — retry loop handles the wait

        private static EACPortraitCaptureWatcher _instance;

        private float _nextAttemptTime;
        private float _retryUntilTime;
        private bool _eventsHooked;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
        }

        private void Start()
        {
            if (_instance != this)
                return;

            Log("Watcher Start in scene=" + HighLogic.LoadedScene);

            HookEvents();
            if (RosterRotationState.PortraitCaptureEnabled)
                ScheduleRetry(StartupDelaySeconds, "Start");
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            UnhookEvents();
        }

        private void Update()
        {
            if (_instance != this)
                return;

            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            if (!RosterRotationState.PortraitCaptureEnabled)
            {
                StopRetryWindow();
                return;
            }

            if (_retryUntilTime <= 0f)
                return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextAttemptTime)
                return;

            if (now > _retryUntilTime)
            {
                StopRetryWindow();
                return;
            }

            _nextAttemptTime = now + PollIntervalSeconds;
            TryCaptureForActiveVessel();
        }

        private void OnFlightReady()
        {
            if (!RosterRotationState.PortraitCaptureEnabled)
            {
                StopRetryWindow();
                return;
            }

            ScheduleRetry(StartupDelaySeconds, "onFlightReady");
        }

        private void OnVesselChange(Vessel vessel)
        {
            if (!RosterRotationState.PortraitCaptureEnabled)
            {
                StopRetryWindow();
                return;
            }

            if (vessel != null && FlightGlobals.ActiveVessel != null && vessel.id != FlightGlobals.ActiveVessel.id)
                return;

            ScheduleRetry(VesselChangeDelaySeconds, "onVesselChange");
        }

        private void OnVesselLoaded(Vessel vessel)
        {
            if (!RosterRotationState.PortraitCaptureEnabled)
            {
                StopRetryWindow();
                return;
            }

            if (vessel != null && FlightGlobals.ActiveVessel != null && vessel.id != FlightGlobals.ActiveVessel.id)
                return;

            ScheduleRetry(VesselChangeDelaySeconds, "onVesselLoaded");
        }

        private void OnVesselWasModified(Vessel vessel)
        {
            if (!RosterRotationState.PortraitCaptureEnabled)
            {
                StopRetryWindow();
                return;
            }

            if (vessel == null || FlightGlobals.ActiveVessel == null || vessel.id != FlightGlobals.ActiveVessel.id)
                return;

            ScheduleRetry(VesselChangeDelaySeconds, "onVesselWasModified");
        }

        private void HookEvents()
        {
            if (_eventsHooked)
                return;

            _eventsHooked = true;
            GameEvents.onFlightReady.Add(OnFlightReady);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselLoaded.Add(OnVesselLoaded);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
        }

        private void UnhookEvents()
        {
            if (!_eventsHooked)
                return;

            _eventsHooked = false;
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselLoaded.Remove(OnVesselLoaded);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
        }

        private void ScheduleRetry(float initialDelay, string reason)
        {
            if (!RosterRotationState.PortraitCaptureEnabled)
            {
                StopRetryWindow();
                return;
            }

            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (!VesselNeedsCapture(vessel))
            {
                StopRetryWindow();
                return;
            }

            float now = Time.realtimeSinceStartup;
            float delay = Mathf.Max(0.1f, initialDelay);

            if (_nextAttemptTime <= 0f || _nextAttemptTime > now + delay)
                _nextAttemptTime = now + delay;

            float desiredUntil = now + Mathf.Max(delay + 1f, RetryWindowSeconds);
            if (_retryUntilTime < desiredUntil)
                _retryUntilTime = desiredUntil;

            Log("ScheduleRetry reason=" + reason + " next=" + _nextAttemptTime.ToString("F2") + " retryUntil=" + _retryUntilTime.ToString("F2"));
        }

        private void TryCaptureForActiveVessel()
        {
            if (!RosterRotationState.PortraitCaptureEnabled)
            {
                StopRetryWindow();
                return;
            }

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (!VesselNeedsCapture(vessel))
            {
                StopRetryWindow();
                return;
            }

            var crew = vessel.GetVesselCrew();
            if (crew == null || crew.Count == 0)
            {
                StopRetryWindow();
                return;
            }

            Log("TryCaptureForActiveVessel vessel=" + vessel.vesselName + " crewCount=" + crew.Count + " mode=visibleUI");

            bool missingAny = false;
            bool capturedAny = false;

            for (int i = 0; i < crew.Count; i++)
            {
                ProtoCrewMember pcm = crew[i];
                if (pcm == null || string.IsNullOrEmpty(pcm.name))
                    continue;

                bool hasCache = HallOfHistoryWindow.HasCapturedPortraitCache(pcm.name);
                Log("crew=" + pcm.name + " hasCache=" + hasCache);
                if (hasCache)
                    continue;

                missingAny = true;

                bool captured = HallOfHistoryWindow.TryCapturePortraitCache(pcm);
                Log("captureAttempt crew=" + pcm.name + " result=" + captured);
                if (captured)
                {
                    capturedAny = true;
                    Log("Cached portrait for " + pcm.name);
                }
            }

            if (!missingAny || !VesselNeedsCapture(vessel))
            {
                Log("All crew already cached; stopping retry window");
                StopRetryWindow();
                return;
            }

            if (capturedAny)
            {
                _nextAttemptTime = Time.realtimeSinceStartup + 1.5f;
                _retryUntilTime = Time.realtimeSinceStartup + 4f;
            }
        }


        private static void Log(string message)
        {
            if (!RosterRotationState.VerboseLogging)
                return;

            Debug.Log("[EAC.PortraitCapture] " + message);
        }

        private void StopRetryWindow()
        {
            _retryUntilTime = 0f;
            _nextAttemptTime = 0f;
        }

        private static bool VesselNeedsCapture(Vessel vessel)
        {
            if (!RosterRotationState.PortraitCaptureEnabled)
                return false;

            if (vessel == null || vessel.isEVA)
                return false;

            var crew = vessel.GetVesselCrew();
            if (crew == null || crew.Count == 0)
                return false;

            for (int i = 0; i < crew.Count; i++)
            {
                ProtoCrewMember pcm = crew[i];
                if (pcm == null || string.IsNullOrEmpty(pcm.name))
                    continue;

                if (!HallOfHistoryWindow.HasCapturedPortraitCache(pcm.name))
                    return true;
            }

            return false;
        }
    }
}

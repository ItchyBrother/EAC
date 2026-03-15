using System;
using System.Collections.Generic;
using UnityEngine;
using KSP;

namespace RosterRotation
{
    internal sealed class SaveSchedulerRunner : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        private void Update()
        {
            SaveScheduler.Tick();
        }
    }

    internal static class SaveScheduler
    {
        private static SaveSchedulerRunner _runner;
        private static bool _savePending;
        private static int _framesUntilSave;
        private static float _earliestRealtime;
        private static bool _syncStateFromGameParams;
        private static readonly HashSet<string> _pendingReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void EnsureRunner()
        {
            if (_runner != null)
                return;

            try
            {
                var go = new GameObject("EAC.SaveScheduler");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _runner = go.AddComponent<SaveSchedulerRunner>();
            }
            catch (Exception ex)
            {
                RRLog.Warn("[EAC] Failed to create save scheduler runner: " + ex.Message);
            }
        }

        public static void RequestSave(string reason, int delayFrames = 1, float minDelaySeconds = 0.10f, bool syncStateFromGameParams = false)
        {
            EnsureRunner();

            if (_savePending)
            {
                _framesUntilSave = Math.Min(_framesUntilSave, Math.Max(0, delayFrames));
                _earliestRealtime = Math.Min(_earliestRealtime, Time.realtimeSinceStartup + Math.Max(0f, minDelaySeconds));
            }
            else
            {
                _savePending = true;
                _framesUntilSave = Math.Max(0, delayFrames);
                _earliestRealtime = Time.realtimeSinceStartup + Math.Max(0f, minDelaySeconds);
            }

            _syncStateFromGameParams |= syncStateFromGameParams;
            if (!string.IsNullOrEmpty(reason))
                _pendingReasons.Add(reason);
        }

        public static void RequestImmediateSave(string reason, bool syncStateFromGameParams = false)
        {
            if (!string.IsNullOrEmpty(reason))
                _pendingReasons.Add(reason);
            _syncStateFromGameParams |= syncStateFromGameParams;
            FlushPending(immediateReasonOverride: reason);
        }

        internal static void Tick()
        {
            if (!_savePending)
                return;

            if (_framesUntilSave > 0)
            {
                _framesUntilSave--;
                return;
            }

            if (Time.realtimeSinceStartup < _earliestRealtime)
                return;

            FlushPending();
        }

        public static void FlushPending(string immediateReasonOverride = null)
        {
            string reason = !string.IsNullOrEmpty(immediateReasonOverride)
                ? immediateReasonOverride
                : (_pendingReasons.Count > 0 ? string.Join(", ", _pendingReasons) : "unspecified EAC state change");

            bool syncState = _syncStateFromGameParams;

            _savePending = false;
            _framesUntilSave = 0;
            _earliestRealtime = 0f;
            _syncStateFromGameParams = false;
            _pendingReasons.Clear();

            TrySave(reason, syncState);
        }

        private static void TrySave(string reason, bool syncStateFromGameParams)
        {
            try
            {
                if (HighLogic.CurrentGame == null || string.IsNullOrEmpty(HighLogic.SaveFolder))
                {
                    RRLog.VerboseOnce("save-scheduler-no-game", "[EAC] Skipping persistent save because no current game/save folder is available.");
                    return;
                }

                if (syncStateFromGameParams && !EACGameSettings.TryApplyToStateFromGameParams())
                    RRLog.Warn("[EAC] SaveScheduler could not mirror game parameters into state before saving.");

                RRLog.Verbose("[EAC] Saving persistent.sfs (" + reason + ").");
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            }
            catch (Exception ex)
            {
                RRLog.Error("[EAC] SaveScheduler failed while saving persistent.sfs (" + reason + "): " + ex);
            }
        }
    }
}

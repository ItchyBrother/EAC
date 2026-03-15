// EAC - Mod.SettingsPersistence
// Extracted settings persistence hook from Mod.cs.

using UnityEngine;
using KSP;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class EACSettingsPersistenceHook : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(this);
            SaveScheduler.EnsureRunner();
            GameEvents.OnGameSettingsApplied.Add(OnGameSettingsApplied);
        }

        private void OnDestroy()
        {
            GameEvents.OnGameSettingsApplied.Remove(OnGameSettingsApplied);
        }

        private void OnGameSettingsApplied()
        {
            SaveScheduler.RequestSave("settings applied", delayFrames: 2, syncStateFromGameParams: true);
        }
    }
}

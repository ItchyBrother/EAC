namespace RosterRotation
{
    internal static class EACExternalModDetector
    {
        internal static bool IsEarnYourStripesInstalled()
        {
            return EACOptionalModRegistry.IsAssemblyLoaded("EarnYourStripes");
        }

        internal static bool IsCrewRandRInstalled()
        {
            return EACOptionalModRegistry.IsAssemblyLoaded("CrewRandR", "CrewQueueTwo");
        }
    }
}

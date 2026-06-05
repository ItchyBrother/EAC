using System;
using System.Linq;

namespace RosterRotation
{
    internal static class EACExternalModDetector
    {
        private static bool _searchedEarnYourStripes;
        private static bool _earnYourStripesInstalled;
        private static bool _searchedCrewRandR;
        private static bool _crewRandRInstalled;

        internal static bool IsEarnYourStripesInstalled()
        {
            if (_searchedEarnYourStripes) return _earnYourStripesInstalled;
            _searchedEarnYourStripes = true;

            try
            {
                _earnYourStripesInstalled = AssemblyLoader.loadedAssemblies.Any(a =>
                    a != null &&
                    ((a.name != null && a.name.IndexOf("EarnYourStripes", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     (a.assembly != null && a.assembly.GetName().Name.IndexOf("EarnYourStripes", StringComparison.OrdinalIgnoreCase) >= 0)));
            }
            catch
            {
                _earnYourStripesInstalled = false;
            }

            return _earnYourStripesInstalled;
        }

        internal static bool IsCrewRandRInstalled()
        {
            if (_searchedCrewRandR) return _crewRandRInstalled;
            _searchedCrewRandR = true;

            try
            {
                _crewRandRInstalled = AssemblyLoader.loadedAssemblies.Any(a =>
                    a != null &&
                    ((a.name != null && a.name.IndexOf("CrewRandR", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     (a.name != null && a.name.IndexOf("CrewQueueTwo", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     (a.assembly != null && a.assembly.GetName().Name.IndexOf("CrewRandR", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     (a.assembly != null && a.assembly.GetName().Name.IndexOf("CrewQueueTwo", StringComparison.OrdinalIgnoreCase) >= 0)));
            }
            catch
            {
                _crewRandRInstalled = false;
            }

            return _crewRandRInstalled;
        }
    }
}

// EAC - Enhanced Astronaut Complex - KspAssemblyCache.cs
// Caches the result of Assembly-CSharp.GetTypes() so multiple hooks that all need
// to scan the assembly only pay the cost once per game session.
//
// Assembly-CSharp in KSP 1.12 contains ~10,000+ types.  Each GetTypes() call on
// the main thread takes ~100-300 ms.  The previous code called it 3-4 times in
// sequence during startup, causing the visible Space Center lag.

using System;
using System.Linq;
using System.Reflection;

namespace RosterRotation
{
    /// <summary>
    /// Lazily loads and caches all types from Assembly-CSharp.
    /// Thread-safe for reading after the first call; call from the main thread only.
    /// </summary>
    internal static class KspAssemblyCache
    {
        private static Type[]    _allTypes;
        private static Assembly  _cachedAssembly;

        /// <summary>
        /// Returns Assembly-CSharp, or null if not found.
        /// Result is cached after the first successful lookup.
        /// </summary>
        internal static Assembly GetAssembly()
        {
            if (_cachedAssembly != null) return _cachedAssembly;

            _cachedAssembly = AssemblyLoader.loadedAssemblies
                .Select(la => la.assembly)
                .FirstOrDefault(a => a != null && a.GetName().Name == "Assembly-CSharp");

            return _cachedAssembly;
        }

        /// <summary>
        /// Returns every type in Assembly-CSharp.  The first call does the expensive
        /// GetTypes() scan; every subsequent call returns the cached array instantly.
        /// </summary>
        internal static Type[] GetAllTypes()
        {
            if (_allTypes != null) return _allTypes;

            Assembly asm = GetAssembly();
            if (asm == null)
            {
                RRLog.Warn("[EAC] KspAssemblyCache: Assembly-CSharp not found.");
                return new Type[0];
            }

            try
            {
                _allTypes = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Partial load — take whatever types did resolve.
                _allTypes = ex.Types ?? new Type[0];
                RRLog.Warn("[EAC] KspAssemblyCache: GetTypes() partial load (" +
                           (_allTypes.Length) + " types resolved). " + ex.Message);
            }
            catch (Exception ex)
            {
                RRLog.Error("[EAC] KspAssemblyCache: GetTypes() failed: " + ex);
                _allTypes = new Type[0];
            }

            RRLog.Verbose("[EAC] KspAssemblyCache: cached " + _allTypes.Length +
                          " types from Assembly-CSharp.");
            return _allTypes;
        }
    }
}

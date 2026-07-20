using System;
using System.Collections.Generic;
using System.Reflection;

namespace RosterRotation
{
    /// <summary>
    /// Shared cache for optional-mod assembly and type discovery.
    /// Keeps every adapter from rescanning AssemblyLoader/AppDomain independently.
    /// </summary>
    internal static class EACOptionalModRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Assembly> AssemblyCache =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> MissingAssemblies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Type> TypeCache =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly HashSet<string> MissingTypes =
            new HashSet<string>(StringComparer.Ordinal);

        internal static bool IsAssemblyLoaded(params string[] candidateNames)
        {
            return FindAssembly(candidateNames) != null;
        }

        internal static Assembly FindAssembly(params string[] candidateNames)
        {
            if (candidateNames == null || candidateNames.Length == 0)
                return null;

            string key = BuildKey(candidateNames);
            lock (Sync)
            {
                Assembly cached;
                if (AssemblyCache.TryGetValue(key, out cached))
                    return cached;
                if (MissingAssemblies.Contains(key))
                    return null;
            }

            Assembly result = null;
            try
            {
                foreach (var loaded in AssemblyLoader.loadedAssemblies)
                {
                    if (loaded == null) continue;

                    string loaderName = loaded.name ?? string.Empty;
                    string assemblyName = string.Empty;
                    try
                    {
                        if (loaded.assembly != null)
                            assemblyName = loaded.assembly.GetName().Name ?? string.Empty;
                    }
                    catch { }

                    if (MatchesAny(loaderName, candidateNames) || MatchesAny(assemblyName, candidateNames))
                    {
                        result = loaded.assembly;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseWarn("[EAC] Optional-mod assembly lookup failed: " + ex.Message);
            }

            lock (Sync)
            {
                if (result != null)
                    AssemblyCache[key] = result;
                else
                    MissingAssemblies.Add(key);
            }

            return result;
        }

        internal static Type FindType(string fullName, params string[] preferredAssemblyNames)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            string key = fullName + "|" + BuildKey(preferredAssemblyNames);
            lock (Sync)
            {
                Type cached;
                if (TypeCache.TryGetValue(key, out cached))
                    return cached;
                if (MissingTypes.Contains(key))
                    return null;
            }

            Type result = null;
            Assembly preferred = FindAssembly(preferredAssemblyNames);
            if (preferred != null)
            {
                try { result = preferred.GetType(fullName, false); }
                catch { }
            }

            if (result == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == null) continue;
                    try
                    {
                        result = assembly.GetType(fullName, false);
                        if (result != null) break;
                    }
                    catch { }
                }
            }

            lock (Sync)
            {
                if (result != null)
                    TypeCache[key] = result;
                else
                    MissingTypes.Add(key);
            }

            return result;
        }

        private static bool MatchesAny(string value, string[] candidateNames)
        {
            if (string.IsNullOrEmpty(value) || candidateNames == null)
                return false;

            foreach (string candidate in candidateNames)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                if (value.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string BuildKey(string[] names)
        {
            if (names == null || names.Length == 0)
                return string.Empty;
            return string.Join("\u001f", names);
        }
    }
}

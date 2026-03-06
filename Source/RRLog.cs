using System;
using System.Collections.Generic;
using UnityEngine;

namespace RosterRotation
{
    internal static class RRLog
    {
        private const string Prefix = "[EAC] ";

        private static readonly string[] LegacyPrefixes = { "[EAC]", "[RosterRotation]" };
        private static readonly HashSet<string> _once = new HashSet<string>();

        private static string P(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return Prefix.TrimEnd();
            string core = msg;
            foreach (var p in LegacyPrefixes)
            {
                if (core.StartsWith(p, StringComparison.Ordinal))
                {
                    core = core.Substring(p.Length).TrimStart();
                    break;
                }
            }
            return string.IsNullOrEmpty(core) ? Prefix.TrimEnd() : (Prefix + core);
        }

        internal static bool VerboseEnabled => RosterRotationState.VerboseLogging;

        internal static void Info(string msg) => Debug.Log(P(msg));
        internal static void Warn(string msg) => Debug.LogWarning(P(msg));
        internal static void Error(string msg) => Debug.LogError(P(msg));

        internal static void Verbose(string msg)
        {
            if (VerboseEnabled) Debug.Log(P(msg));
        }

        internal static void WarnOnce(string key, string msg)
        {
            if (_once.Add("W:" + (key ?? msg ?? ""))) Warn(msg);
        }

        internal static void VerboseOnce(string key, string msg)
        {
            if (!VerboseEnabled) return;
            if (_once.Add("V:" + (key ?? msg ?? ""))) Verbose(msg);
        }
    }
}

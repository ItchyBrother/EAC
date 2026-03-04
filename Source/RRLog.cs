using System;
using System.Collections.Generic;
using UnityEngine;

namespace RosterRotation
{
    /// <summary>
    /// Centralized logging with:
    ///   - Optional verbose logging (RosterRotationState.VerboseLogging)
    ///   - One-time logging helpers to prevent spam
    ///   - Consistent [EAC] prefix without double-prefixing
    /// </summary>
    internal static class RRLog
    {
        // Public-facing mod tag used in KSP.log.
        private const string Prefix = "[EAC] ";

        // Backwards-compatible prefixes that may still exist in some message strings.
        private static readonly string[] LegacyPrefixes =
        {
            "[EAC]",
            "[RosterRotation]",
        };

        private static readonly HashSet<string> _once = new HashSet<string>();

        private static string P(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return Prefix.TrimEnd();

            // Strip any existing known prefix to prevent double-prefixing
            // and to ensure logs consistently show the new mod tag.
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
            if (!VerboseEnabled) return;
            Debug.Log(P(msg));
        }

        internal static void InfoOnce(string key, string msg)
        {
            if (key == null) key = msg ?? "";
            if (_once.Add("I:" + key)) Info(msg);
        }

        internal static void WarnOnce(string key, string msg)
        {
            if (key == null) key = msg ?? "";
            if (_once.Add("W:" + key)) Warn(msg);
        }

        internal static void VerboseOnce(string key, string msg)
        {
            if (!VerboseEnabled) return;
            if (key == null) key = msg ?? "";
            if (_once.Add("V:" + key)) Verbose(msg);
        }
    }
}

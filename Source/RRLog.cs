using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    internal static class RRLog
    {
        private const string Prefix = "[EAC] ";

        private static readonly string[] LegacyPrefixes = { "[EAC]", "[RosterRotation]" };
        private static readonly HashSet<string> _once = new HashSet<string>();
        private static readonly object _fileLock = new object();
        private static string _logFilePath;
        private static string _purgeLogFilePath;

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

        private static string Timestamp() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + "Z";

        private static string EnsureDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                Directory.CreateDirectory(path);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string ResolvePluginDataDir()
        {
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    string pluginData = Path.GetFullPath(Path.Combine(asmDir, "..", "PluginData"));
                    if (EnsureDirectory(pluginData) != null) return pluginData;
                    if (EnsureDirectory(asmDir) != null) return asmDir;
                }
            }
            catch { }

            try
            {
                string root = KSPUtil.ApplicationRootPath;
                if (!string.IsNullOrEmpty(root))
                {
                    string fallback = Path.Combine(root, "GameData", "EAC", "PluginData");
                    if (EnsureDirectory(fallback) != null) return fallback;
                }
            }
            catch { }

            return EnsureDirectory(Environment.CurrentDirectory);
        }

        private static string ResolveKspLogPath()
        {
            try
            {
                string root = KSPUtil.ApplicationRootPath;
                if (string.IsNullOrEmpty(root)) return null;
                return Path.Combine(root, "KSP.log");
            }
            catch
            {
                return null;
            }
        }

        private static string GetGeneralLogPath()
        {
            if (!string.IsNullOrEmpty(_logFilePath)) return _logFilePath;
            _logFilePath = ResolveKspLogPath();
            if (!string.IsNullOrEmpty(_logFilePath)) return _logFilePath;

            string dir = ResolvePluginDataDir();
            _logFilePath = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "EAC_Debug.log");
            return _logFilePath;
        }

        private static string GetPurgeLogPath()
        {
            if (!string.IsNullOrEmpty(_purgeLogFilePath)) return _purgeLogFilePath;
            string dir = ResolvePluginDataDir();
            _purgeLogFilePath = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "EAC_PurgeLog.txt");
            return _purgeLogFilePath;
        }

        private static void AppendLine(string path, string line)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(line)) return;
            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch
            {
                // Avoid recursive logging failures.
            }
        }

        private static void WriteGeneral(string level, string msg)
        {
            string formatted = P(msg);
            AppendLine(GetGeneralLogPath(), $"{Timestamp()} [{level}] {formatted}");
        }

        internal static void AuditPurge(string msg)
        {
            string formatted = P(msg);
            AppendLine(GetPurgeLogPath(), $"{Timestamp()} {formatted}");
        }

        internal static string PurgeLogPath => GetPurgeLogPath();
        internal static string GeneralLogPath => GetGeneralLogPath();
        internal static bool VerboseEnabled => RosterRotationState.VerboseLogging;

        private static string FormatExceptionMessage(string msg, Exception ex)
        {
            if (ex == null) return msg;
            return string.IsNullOrEmpty(msg) ? ex.ToString() : (msg + ": " + ex);
        }

        internal static void Info(string msg)
        {
            string formatted = P(msg);
            Debug.Log(formatted);
            WriteGeneral("INFO", msg);
        }

        internal static void Warn(string msg)
        {
            string formatted = P(msg);
            Debug.LogWarning(formatted);
            WriteGeneral("WARN", msg);
        }

        internal static void Error(string msg)
        {
            string formatted = P(msg);
            Debug.LogError(formatted);
            WriteGeneral("ERROR", msg);
        }

        internal static void Verbose(string msg)
        {
            if (!VerboseEnabled) return;
            string formatted = P(msg);
            Debug.Log(formatted);
            WriteGeneral("VERBOSE", msg);
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

        internal static void WarnException(string msg, Exception ex)
        {
            Warn(FormatExceptionMessage(msg, ex));
        }

        internal static void ErrorException(string msg, Exception ex)
        {
            Error(FormatExceptionMessage(msg, ex));
        }

        internal static void VerboseException(string msg, Exception ex)
        {
            if (!VerboseEnabled) return;
            Verbose(FormatExceptionMessage(msg, ex));
        }

        internal static void WarnExceptionOnce(string key, string msg, Exception ex)
        {
            if (_once.Add("WX:" + (key ?? msg ?? ""))) WarnException(msg, ex);
        }

        internal static void ErrorExceptionOnce(string key, string msg, Exception ex)
        {
            if (_once.Add("EX:" + (key ?? msg ?? ""))) ErrorException(msg, ex);
        }

        internal static void VerboseExceptionOnce(string key, string msg, Exception ex)
        {
            if (!VerboseEnabled) return;
            if (_once.Add("VX:" + (key ?? msg ?? ""))) VerboseException(msg, ex);
        }
    }
}

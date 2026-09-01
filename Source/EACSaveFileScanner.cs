using System;
using System.Collections.Generic;
using System.IO;

namespace RosterRotation
{
    /// <summary>
    /// Lightweight scanner for values stored in KSP .sfs text files. This avoids
    /// loading full ConfigNode trees during conservative external-data cleanup passes.
    /// </summary>
    internal static class EACSaveFileScanner
    {
        /// <summary>
        /// Collects assignments named valueName from every .sfs under saveRoot.
        /// When nodeName is supplied, only values directly inside that ConfigNode are
        /// collected. A null/empty nodeName scans matching assignments anywhere.
        /// </summary>
        internal static bool TryCollectValues(
            string saveRoot,
            string nodeName,
            string valueName,
            HashSet<string> values,
            out string failure)
        {
            failure = null;
            if (values == null)
            {
                failure = "destination set is null";
                return false;
            }
            if (string.IsNullOrEmpty(valueName))
            {
                failure = "value name is empty";
                return false;
            }
            if (string.IsNullOrEmpty(saveRoot) || !Directory.Exists(saveRoot))
                return true;

            string[] saveFiles;
            try
            {
                saveFiles = Directory.GetFiles(saveRoot, "*.sfs", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                return false;
            }

            bool scoped = !string.IsNullOrEmpty(nodeName);
            for (int i = 0; i < saveFiles.Length; i++)
            {
                try
                {
                    bool waitingForBrace = false;
                    bool inTargetNode = false;
                    int depth = 0;

                    foreach (string rawLine in File.ReadLines(saveFiles[i]))
                    {
                        string line = rawLine != null ? rawLine.Trim() : string.Empty;

                        if (!scoped)
                        {
                            string found;
                            if (TryReadAssignment(line, valueName, out found) && !string.IsNullOrEmpty(found))
                                values.Add(found);
                            continue;
                        }

                        if (!inTargetNode && !waitingForBrace &&
                            string.Equals(line, nodeName, StringComparison.Ordinal))
                        {
                            waitingForBrace = true;
                            continue;
                        }

                        if (waitingForBrace)
                        {
                            if (line == "{")
                            {
                                inTargetNode = true;
                                depth = 1;
                            }
                            waitingForBrace = false;
                            continue;
                        }

                        if (!inTargetNode) continue;

                        if (line == "{")
                        {
                            depth++;
                            continue;
                        }

                        if (line == "}")
                        {
                            depth--;
                            if (depth <= 0) inTargetNode = false;
                            continue;
                        }

                        if (depth != 1) continue;
                        string value;
                        if (TryReadAssignment(line, valueName, out value) && !string.IsNullOrEmpty(value))
                            values.Add(value);
                    }
                }
                catch (Exception ex)
                {
                    failure = Path.GetFileName(saveFiles[i]) + ": " + ex.Message;
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadAssignment(string line, string valueName, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(line)) return false;
            int equals = line.IndexOf('=');
            if (equals <= 0) return false;

            string key = line.Substring(0, equals).Trim();
            if (!string.Equals(key, valueName, StringComparison.Ordinal)) return false;

            value = line.Substring(equals + 1).Trim();
            return true;
        }
    }
}

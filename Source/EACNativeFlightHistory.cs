using System;
using System.Collections.Generic;
using System.Globalization;

namespace RosterRotation
{
    /// <summary>
    /// EAC-owned per-Kerbal flight history. This deliberately does not depend on the
    /// optional FlightTracker mod; that integration remains a separate bridge.
    /// </summary>
    internal static class EACNativeFlightHistory
    {
        internal static void RecordRecovery(
            ProtoCrewMember pcm,
            RosterRotationState.KerbalRecord rec,
            Vessel vessel,
            string fallbackVesselName,
            string fallbackFlightId,
            double nowUT,
            double missionSeconds,
            int flightNumber)
        {
            if (pcm == null || rec == null) return;

            missionSeconds = Math.Max(0, missionSeconds);
            string flightId = vessel != null && vessel.id != Guid.Empty
                ? vessel.id.ToString("N")
                : (fallbackFlightId ?? "");

            // Recovery can be observed through both live-vessel and proto-vessel events.
            // The coordinator normally de-duplicates those paths; retain a per-record guard
            // as a second line of defense for unusual mod/event ordering.
            if (!string.IsNullOrEmpty(flightId))
            {
                for (int i = rec.FlightHistory.Count - 1; i >= 0; i--)
                {
                    var prior = rec.FlightHistory[i];
                    if (prior == null) continue;
                    if (string.Equals(prior.FlightId, flightId, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            FlightMetadata metadata = ReadFlightMetadata(pcm, vessel);
            string vesselName = vessel != null && !string.IsNullOrEmpty(vessel.vesselName)
                ? vessel.vesselName
                : (fallbackVesselName ?? "");

            var entry = new RosterRotationState.FlightRecord
            {
                FlightId = flightId,
                FlightNumber = flightNumber,
                VesselName = vesselName,
                FlightType = metadata.FlightType,
                BodyName = metadata.BodyName,
                StartUT = missionSeconds > 0 ? Math.Max(0, nowUT - missionSeconds) : 0,
                EndUT = nowUT,
                DurationUT = missionSeconds
            };

            rec.FlightHistory.Add(entry);
            rec.TotalTrackedFlightUT += missionSeconds;
            // Also import any stock CAREER_LOG accomplishments now visible for this
            // mission. This is conservative/deduplicated and supplies the Hall service
            // record with Orbit/EVA/PlantFlag/etc. without inventing metadata.
            EACCareerHistory.SyncCurrentFlightLog(pcm, rec, nowUT);
            EACCareerHistory.SyncCareerLog(pcm, rec, nowUT, "EACRecovery");
            RRLog.Verbose("[EAC] Native flight history recorded for " + pcm.name
                + ": flight=" + flightNumber
                + ", type=" + entry.FlightType
                + ", vessel=" + entry.VesselName
                + ", duration=" + missionSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s");
        }

        private sealed class FlightMetadata
        {
            internal string FlightType = "Surface";
            internal string BodyName = "";
        }

        private static FlightMetadata ReadFlightMetadata(ProtoCrewMember pcm, Vessel vessel)
        {
            var metadata = new FlightMetadata();
            bool hasAir = false;
            bool hasSpace = false;
            bool hasEva = false;
            string lastBody = "";

            try
            {
                ConfigNode kerbalNode = new ConfigNode("KERBAL");
                pcm.Save(kerbalNode);

                // FLIGHT_LOG is the current mission log. Prefer it so an earlier orbit in
                // CAREER_LOG cannot turn a later aircraft recovery into a "Spaceflight".
                bool readCurrent = ReadLogNode(
                    kerbalNode.GetNode("FLIGHT_LOG") ?? kerbalNode.GetNode("flightLog"),
                    null, ref hasAir, ref hasSpace, ref hasEva, ref lastBody);

                // Some recovery paths archive/clear FLIGHT_LOG before EAC sees the PCM.
                // In that case, inspect only the newest numeric flight id in CAREER_LOG.
                if (!readCurrent)
                {
                    ConfigNode career = kerbalNode.GetNode("CAREER_LOG") ?? kerbalNode.GetNode("careerLog");
                    string latestFlightId = FindLatestFlightId(career);
                    ReadLogNode(career, latestFlightId, ref hasAir, ref hasSpace, ref hasEva, ref lastBody);
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EACNativeFlightHistory.ReadFlightMetadata",
                    "Suppressed exception while reading current flight log.", ex);
            }

            if (vessel != null)
            {
                try
                {
                    if (vessel.mainBody != null && !string.IsNullOrEmpty(vessel.mainBody.bodyName))
                        lastBody = vessel.mainBody.bodyName;

                    switch (vessel.situation)
                    {
                        case Vessel.Situations.ORBITING:
                        case Vessel.Situations.SUB_ORBITAL:
                        case Vessel.Situations.ESCAPING:
                            hasSpace = true;
                            break;
                        case Vessel.Situations.FLYING:
                            hasAir = true;
                            break;
                    }

                    if (vessel.vesselType == VesselType.Plane)
                        hasAir = true;
                    if (vessel.vesselType == VesselType.EVA)
                        hasEva = true;
                }
                catch (Exception ex)
                {
                    RRLog.VerboseExceptionOnce("EACNativeFlightHistory.ReadFlightMetadata.Vessel",
                        "Suppressed exception while reading recovery vessel metadata.", ex);
                }
            }

            metadata.BodyName = lastBody ?? "";
            metadata.FlightType = hasSpace ? "Spaceflight" : hasAir ? "Air" : hasEva ? "EVA" : "Surface";
            return metadata;
        }

        private static string FindLatestFlightId(ConfigNode logNode)
        {
            if (logNode == null) return null;
            int latest = int.MinValue;
            string latestText = null;
            foreach (ConfigNode.Value value in logNode.values)
            {
                if (value == null || string.IsNullOrEmpty(value.name) || string.IsNullOrEmpty(value.value)) continue;
                int id;
                if (!int.TryParse(value.name, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) continue;
                if (id < latest) continue;
                latest = id;
                latestText = value.name;
            }
            return latestText;
        }

        private static bool ReadLogNode(
            ConfigNode logNode,
            string onlyFlightId,
            ref bool hasAir,
            ref bool hasSpace,
            ref bool hasEva,
            ref string lastBody)
        {
            if (logNode == null) return false;
            bool readAny = false;

            foreach (ConfigNode.Value value in logNode.values)
            {
                if (value == null || string.IsNullOrEmpty(value.value)) continue;
                if (string.Equals(value.name, "flight", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value.name, "flights", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(onlyFlightId) &&
                    !string.Equals(value.name, onlyFlightId, StringComparison.OrdinalIgnoreCase))
                    continue;

                readAny = true;
                string[] parts = value.value.Split(new[] { ',' }, 2);
                string kind = parts.Length > 0 ? parts[0].Trim() : "";
                string body = parts.Length > 1 ? parts[1].Trim() : "";
                if (!string.IsNullOrEmpty(body)) lastBody = body;

                if (kind.Equals("Orbit", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("Suborbit", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("SubOrbital", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("Escape", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("Flyby", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("ReturnFromFlyBy", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("ReturnFromOrbit", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("ReturnFromSurface", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("PlantFlag", StringComparison.OrdinalIgnoreCase))
                {
                    hasSpace = true;
                    continue;
                }

                // KSP also uses "Flight,<Body>" as a mission-start marker, and
                // "Launch" is shared by rockets and aircraft. Neither is enough on its
                // own to call the mission atmospheric; the live Vessel's situation/type
                // provides that signal while orbital log entries establish spaceflight.

                if (kind.Equals("ExitVessel", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("BoardVessel", StringComparison.OrdinalIgnoreCase))
                    hasEva = true;
            }
            return readAny;
        }
    }
}

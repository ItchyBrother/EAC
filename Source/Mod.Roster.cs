// EAC - Enhanced Astronaut Complex - Mod.Roster.cs
// Partial class: roster row building, status strings, crew list caches,
// and vessel-name lookup (with cached proto-vessel reflection).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RosterRotation
{
    public partial class RosterRotationKSCUI
    {
        // ── Roster cache fields ────────────────────────────────────────────────
        private float _lastRosterRowsCacheRT         = -10f;
        private Tab   _cachedRosterRowsTab           = (Tab)(-1);
        private List<RosterRowData> _cachedRosterRows = new List<RosterRowData>();

        private float _lastApplicantsCacheRT = -10f;
        private List<ProtoCrewMember> _cachedApplicants = new List<ProtoCrewMember>();

        private float _lastTrainingCandidatesCacheRT = -10f;
        private List<ProtoCrewMember> _cachedTrainingCandidates = new List<ProtoCrewMember>();

        private float _lastRetireRowsCacheRT = -10f;
        private List<RosterRowData> _cachedRetireRows = new List<RosterRowData>();

        // ── Cached proto-vessel type reflection ────────────────────────────────
        // Resolved once on first use; eliminates repeated GetMethod/GetField calls
        // inside TryGetAssignedVesselName's proto-vessel fallback path.
        private static Type         _cachedProtoVesselType;
        private static MethodInfo   _cachedProtoGetVesselCrewMethod;
        private static FieldInfo    _cachedProtoVesselNameField;
        private static PropertyInfo _cachedProtoVesselNameProp;

        // ── Cache accessors ────────────────────────────────────────────────────

        private List<RosterRowData> GetRosterRowsCached(KerbalRoster roster, double now)
        {
            float rt = Time.realtimeSinceStartup;
            if (_cachedRosterRowsTab == _tab && rt - _lastRosterRowsCacheRT < UiCacheSeconds)
                return _cachedRosterRows;

            _lastRosterRowsCacheRT = rt;
            _cachedRosterRowsTab   = _tab;
            _cachedRosterRows      = BuildRosterRows(roster, now, _tab);
            return _cachedRosterRows;
        }

        private List<ProtoCrewMember> GetApplicantsCached(KerbalRoster roster)
        {
            float rt = Time.realtimeSinceStartup;
            if (rt - _lastApplicantsCacheRT < UiCacheSeconds) return _cachedApplicants;

            _lastApplicantsCacheRT = rt;
            _cachedApplicants = roster.Applicants.Where(k => k != null).ToList();
            _cachedApplicants.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return _cachedApplicants;
        }

        private List<ProtoCrewMember> GetTrainingCandidatesCached(KerbalRoster roster)
        {
            float rt = Time.realtimeSinceStartup;
            if (rt - _lastTrainingCandidatesCacheRT < UiCacheSeconds) return _cachedTrainingCandidates;

            _lastTrainingCandidatesCacheRT = rt;
            _cachedTrainingCandidates      = BuildTrainingCandidates(roster);
            return _cachedTrainingCandidates;
        }

        private List<RosterRowData> GetRetireRowsCached(KerbalRoster roster, double now)
        {
            float rt = Time.realtimeSinceStartup;
            if (rt - _lastRetireRowsCacheRT < UiCacheSeconds) return _cachedRetireRows;

            _lastRetireRowsCacheRT = rt;
            _cachedRetireRows      = new List<RosterRowData>();

            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                    k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r?.Retired == true || r?.DeathUT > 0) continue;

                var row = new RosterRowData
                {
                    Kerbal             = k,
                    Record             = r,
                    Status             = BuildStatusString(k, r, now, r != null && r.LastFlightUT > 0, false),
                    AgeText            = GetAgeDisplay(r, now),
                    DisplayFlights     = GetDisplayedFlights(k, r),
                    InTrainingLockout  = r != null && r.TrainingEndUT > 0
                                         && (now - r.TrainingEndUT) < RosterRotationState.YearSeconds
                };
                _cachedRetireRows.Add(row);
            }
            _cachedRetireRows.Sort((a, b) => string.Compare(a.Kerbal.name, b.Kerbal.name, StringComparison.Ordinal));
            return _cachedRetireRows;
        }

        // ── Row building ───────────────────────────────────────────────────────

        private List<RosterRowData> BuildRosterRows(KerbalRoster roster, double now, Tab tab)
        {
            var rows = new List<RosterRowData>();

            foreach (var k in roster.Kerbals())
            {
                if (k == null) continue;
                bool applicant = k.type == ProtoCrewMember.KerbalType.Applicant;
                if (tab == Tab.Applicants) { if (!applicant) continue; }
                else if (applicant) continue;

                RosterRotationState.Records.TryGetValue(k.name, out var r);
                bool retired    = r != null && r.Retired;
                bool hasFlown   = r != null && r.LastFlightUT > 0;
                bool onVacation = CrewRandRAdapter.IsOnVacationByName(k.name, now);
                bool onMission  = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned;
                bool inTraining = r != null && r.Training != TrainingType.None && k.inactive && k.inactiveTimeEnd > now;
                bool isLost     = k.rosterStatus == ProtoCrewMember.RosterStatus.Dead
                               || k.rosterStatus == ProtoCrewMember.RosterStatus.Missing
                               || (r != null && r.DeathUT > 0);

                switch (tab)
                {
                    case Tab.Active:   if (isLost || retired || onVacation || onMission || inTraining) continue; break;
                    case Tab.Assigned: if (isLost || retired || !onMission) continue; break;
                    case Tab.RandR:    if (isLost || retired || !onVacation) continue; break;
                    case Tab.Retired:  if (!retired || isLost) continue; break;
                    case Tab.Lost:     if (!isLost) continue; break;
                }

                int displayFlights = GetDisplayedFlights(k, r);

                rows.Add(new RosterRowData
                {
                    Kerbal            = k,
                    Record            = r,
                    Retired           = retired,
                    HasFlown          = hasFlown || displayFlights > 0,
                    IsLost            = isLost,
                    IsAssigned        = onMission,
                    Status            = BuildStatusString(k, r, now, hasFlown, retired),
                    AgeText           = GetAgeDisplay(r, now),
                    DisplayFlights    = displayFlights,
                    EffectiveStars    = retired ? RosterRotationState.GetRetiredEffectiveStars(k, r, now) : 0,
                    InTrainingLockout = r != null && r.TrainingEndUT > 0
                                        && (now - r.TrainingEndUT) < RosterRotationState.YearSeconds
                });
            }

            rows.Sort((a, b) => string.Compare(a.Kerbal.name, b.Kerbal.name, StringComparison.Ordinal));
            return rows;
        }

        private List<ProtoCrewMember> BuildTrainingCandidates(KerbalRoster roster)
        {
            var list = new List<ProtoCrewMember>();
            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (k.inactive || k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                    k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                if (k.experienceLevel >= 3f) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (r?.Retired == true || r?.DeathUT > 0) continue;
                list.Add(k);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return list;
        }

        // ── Display helpers ────────────────────────────────────────────────────

        private static string GetAgeDisplay(RosterRotationState.KerbalRecord record, double now)
        {
            if (!RosterRotationState.AgingEnabled || record == null || record.LastAgedYears < 0)
                return "";
            int age = RosterRotationState.GetKerbalAge(record, now);
            return age >= 0 ? $"Age {age}" : "";
        }

        private string BuildStatusString(ProtoCrewMember k, RosterRotationState.KerbalRecord r,
                                         double now, bool hasFlown, bool retired)
        {
            if (r != null && r.DeathUT > 0)
            {
                int    age      = RosterRotationState.GetKerbalAge(r, r.DeathUT);
                string ageStr   = age >= 0 ? $"Age {age}, " : "";
                string dateStr  = RosterRotationState.FormatGameDateYD(r.DeathUT);
                bool   retDeath = r.RetiredUT > 0 && r.DeathUT >= r.RetiredUT - 1;
                if (r.DiedOnMission) return $"Died on mission {ageStr}{dateStr}";
                return retDeath ? $"Died {ageStr}{dateStr}" : $"K.I.A. {ageStr}{dateStr}";
            }
            if (retired)
            {
                int eff = RosterRotationState.GetRetiredEffectiveStars(k, r, now);
                return $"RETIRED L{eff} ({RosterRotationState.FormatTimeAgo(r.RetiredUT, now)})";
            }
            if (r != null && r.Training != TrainingType.None && k.inactive && k.inactiveTimeEnd > now)
            {
                double rem = k.inactiveTimeEnd - now;
                return $"In {TrainingLabel(r.Training, r.TrainingTargetLevel)}  {RosterRotationState.FormatCountdown(rem)}";
            }
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
            {
                return TryGetAssignedVesselName(k, out var vn) ? "ASSIGNED: " + vn : "ASSIGNED";
            }
            if (k.inactive && k.inactiveTimeEnd > now)
                return $"INACTIVE ({RosterRotationState.FormatCountdown(k.inactiveTimeEnd - now)})";
            if (CrewRandRAdapter.TryGetVacationUntilByName(k.name, out var vacUntil) && vacUntil > now)
                return $"R&R ({FormatTime(vacUntil - now)})";
            return "AVAILABLE";
        }

        // ── Vessel-name lookup (proto-vessel reflection cached) ────────────────

        private static bool TryGetAssignedVesselName(ProtoCrewMember k, out string vesselName)
        {
            vesselName = null;
            if (k == null || string.IsNullOrEmpty(k.name)) return false;

            // Phase 1: FlightGlobals.Vessels — fast, no reflection needed.
            try
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null) continue;
                    try
                    {
                        var crew = vessel.GetVesselCrew();
                        if (crew == null) continue;
                        if (crew.Any(c => c != null && string.Equals(c.name, k.name, StringComparison.Ordinal)))
                        {
                            vesselName = vessel.vesselName;
                            return !string.IsNullOrEmpty(vesselName);
                        }
                    }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("Roster.VesselName.Live:" + vessel?.id, "Suppressed", ex); }
                }
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("Roster.VesselName.LiveOuter", "Suppressed", ex); }

            // Phase 2: protoVessels fallback — reflection resolved once and cached.
            try
            {
                var protoVessels = HighLogic.CurrentGame?.flightState?.protoVessels as IEnumerable;
                if (protoVessels == null) return false;

                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var pv in protoVessels)
                {
                    if (pv == null) continue;
                    try
                    {
                        // Resolve the proto vessel type's reflection members once and reuse.
                        // In practice all ProtoVessel instances share the same type, but we
                        // guard with a type identity check to be safe across KSP versions.
                        var pvType = pv.GetType();
                        if (!ReferenceEquals(pvType, _cachedProtoVesselType))
                        {
                            _cachedProtoVesselType         = pvType;
                            _cachedProtoGetVesselCrewMethod = pvType.GetMethod("GetVesselCrew", bf);
                            _cachedProtoVesselNameField     = pvType.GetField("vesselName", bf);
                            _cachedProtoVesselNameProp      = pvType.GetProperty("vesselName", bf);
                        }

                        var crewRaw = _cachedProtoGetVesselCrewMethod?.Invoke(pv, null) as IEnumerable;
                        if (crewRaw == null) continue;

                        bool found = false;
                        foreach (var crewObj in crewRaw)
                        {
                            var crew = crewObj as ProtoCrewMember;
                            if (crew == null) continue;
                            if (!string.Equals(crew.name, k.name, StringComparison.Ordinal)) continue;
                            found = true;
                            break;
                        }
                        if (!found) continue;

                        vesselName = _cachedProtoVesselNameField?.GetValue(pv) as string;
                        if (string.IsNullOrEmpty(vesselName))
                            vesselName = _cachedProtoVesselNameProp?.GetValue(pv, null) as string;

                        return !string.IsNullOrEmpty(vesselName);
                    }
                    catch (Exception ex) { RRLog.VerboseExceptionOnce("Roster.VesselName.Proto", "Suppressed", ex); }
                }
            }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("Roster.VesselName.ProtoOuter", "Suppressed", ex); }

            return false;
        }
    }
}

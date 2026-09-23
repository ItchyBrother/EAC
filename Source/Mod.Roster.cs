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
        private static Type         _cachedProtoVesselType;
        private static MethodInfo   _cachedProtoGetVesselCrewMethod;
        private static FieldInfo    _cachedProtoVesselNameField;
        private static PropertyInfo _cachedProtoVesselNameProp;

        // KSP's stock Astronaut Complex manages actual astronaut crew only.
        // Contract passengers (Tourist) and Unowned kerbals can still exist in the
        // roster and can be returned by roster.Kerbals(), but they should not appear
        // in EAC's astronaut-management tabs or count against AC-style totals.
        private static bool IsAstronautComplexManagedCrew(ProtoCrewMember k,
            RosterRotationState.KerbalRecord rec = null)
        {
            if (k == null) return false;
            if (k.type == ProtoCrewMember.KerbalType.Crew) return true;

            // Preserve visibility for older EAC retired records that may have been
            // hidden by temporarily changing the Kerbal type away from Crew.
            if (rec != null && rec.Retired &&
                (rec.OriginalType == ProtoCrewMember.KerbalType.Crew || rec.OriginalType == 0))
                return true;

            return false;
        }

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
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (!IsAstronautComplexManagedCrew(k, r)) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                    k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                if (IsDeepFreezeFrozen(k) || (r != null && r.DeepFreezeActive)) continue;
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
                RosterRotationState.Records.TryGetValue(k.name, out var r);

                bool applicant = k.type == ProtoCrewMember.KerbalType.Applicant;
                if (tab == Tab.Applicants) { if (!applicant) continue; }
                else
                {
                    if (applicant) continue;
                    if (!IsAstronautComplexManagedCrew(k, r)) continue;
                }
                bool retired    = r != null && r.Retired;
                bool hasFlown   = r != null && r.LastFlightUT > 0;
                bool onVacation = CrewRandRAdapter.IsOnVacationByName(k.name, now);
                bool frozen     = IsDeepFreezeFrozen(k) || (r != null && r.DeepFreezeActive);
                bool onMission  = k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned || frozen;
                bool inTraining = r != null && r.Training != TrainingType.None && k.inactive && k.inactiveTimeEnd > now;
                bool isLost     = !frozen &&
                                  (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead
                               ||  k.rosterStatus == ProtoCrewMember.RosterStatus.Missing
                               || (r != null && r.DeathUT > 0));

                switch (tab)
                {
                    case Tab.Active:   if (isLost || retired || frozen || onVacation || onMission || inTraining) continue; break;
                    case Tab.Assigned: if (isLost || retired || !onMission) continue; break;
                    case Tab.RandR:    if (isLost || retired || frozen || !onVacation) continue; break;
                    case Tab.Retired:  if (!retired || isLost || frozen) continue; break;
                    case Tab.Lost:     if (!isLost) continue; break;
                }

                int displayFlights = GetDisplayedFlights(k, r);

                rows.Add(new RosterRowData
                {
                    Kerbal            = k,
                    Record            = r,
                    DisplayName       = k.name,
                    DisplayTrait      = k.trait,
                    DisplayLevel      = (int)k.experienceLevel,
                    Retired           = retired,
                    HasFlown          = hasFlown || displayFlights > 0,
                    IsLost            = isLost,
                    IsAssigned        = onMission,
                    Status            = BuildStatusString(k, r, now, hasFlown, retired),
                    // Lost rows already include age-at-death in the status text,
                    // so leave the live/current age column blank for that tab.
                    AgeText           = isLost ? "" : GetAgeDisplay(r, now),
                    DisplayFlights    = displayFlights,
                    EffectiveStars    = retired ? RosterRotationState.GetRetiredEffectiveStars(k, r, now) : 0,
                    InTrainingLockout = r != null && r.TrainingEndUT > 0
                                        && (now - r.TrainingEndUT) < RosterRotationState.YearSeconds
                });
            }

            // The stock Astronaut Complex integration already injects cold rows,
            // but the EAC Space Center window is built independently here. Merge
            // the small cold index directly so archived Kerbals remain visible
            // without recreating live ProtoCrewMember objects.
            if (tab == Tab.Retired || tab == Tab.Lost)
            {
                string coldReason = tab == Tab.Retired ? "retired" : "lost";
                var liveNames = new HashSet<string>(
                    rows.Where(x => x != null && !string.IsNullOrEmpty(x.DisplayName))
                        .Select(x => x.DisplayName),
                    StringComparer.Ordinal);

                foreach (var archived in EACRosterArchive.GetColdArchivedEntries(coldReason))
                {
                    if (archived == null || string.IsNullOrEmpty(archived.Name)) continue;
                    if (liveNames.Contains(archived.Name)) continue;

                    RosterRotationState.KerbalRecord archivedRecord;
                    RosterRotationState.Records.TryGetValue(archived.Name, out archivedRecord);

                    bool archivedLost = string.Equals(coldReason, "lost", StringComparison.OrdinalIgnoreCase);
                    int archivedFlights = GetDisplayedFlights(null, archivedRecord);

                    rows.Add(new RosterRowData
                    {
                        Kerbal            = null,
                        Record            = archivedRecord,
                        ColdArchived      = true,
                        DisplayName       = archived.Name,
                        DisplayTrait      = string.IsNullOrEmpty(archived.Trait) ? "Unknown" : archived.Trait,
                        DisplayLevel      = Math.Max(0, archived.ExperienceLevel),
                        Retired           = !archivedLost,
                        HasFlown          = archivedFlights > 0 || (archivedRecord != null && archivedRecord.LastFlightUT > 0),
                        IsLost            = archivedLost,
                        IsAssigned        = false,
                        Status            = BuildColdArchivedStatus(archived, archivedRecord, now),
                        AgeText           = archivedLost ? "" : GetAgeDisplay(archivedRecord, now),
                        DisplayFlights    = archivedFlights,
                        EffectiveStars    = 0,
                        InTrainingLockout = false
                    });
                }
            }

            rows.Sort((a, b) => string.Compare(
                a != null ? a.DisplayName : null,
                b != null ? b.DisplayName : null,
                StringComparison.Ordinal));
            return rows;
        }

        private static string BuildColdArchivedStatus(
            EACRosterArchive.ColdRosterEntrySummary archived,
            RosterRotationState.KerbalRecord record,
            double now)
        {
            if (archived == null) return "ARCHIVED";

            if (string.Equals(archived.Reason, "lost", StringComparison.OrdinalIgnoreCase))
            {
                double deathUT = record != null && record.DeathUT > 0
                    ? record.DeathUT
                    : archived.DeathUT;

                if (deathUT > 0)
                {
                    int age = record != null ? RosterRotationState.GetKerbalAge(record, deathUT) : -1;
                    string ageStr = age >= 0 ? $"Age {age}, " : "";
                    string dateStr = RosterRotationState.FormatGameDateYD(deathUT);
                    bool diedOnMission = record != null ? record.DiedOnMission : archived.DiedOnMission;

                    if (diedOnMission)
                        return $"Died on mission {ageStr}{dateStr} (Archive)";

                    bool retiredDeath =
                        (record != null && record.RetiredUT > 0 && deathUT >= record.RetiredUT - 1)
                        || (record == null && archived.RetiredUT > 0 && deathUT >= archived.RetiredUT - 1);

                    return retiredDeath
                        ? $"Died {ageStr}{dateStr} (Archive)"
                        : $"K.I.A. {ageStr}{dateStr} (Archive)";
                }

                return "LOST (Archive)";
            }

            double retiredUT = record != null && record.RetiredUT > 0
                ? record.RetiredUT
                : archived.RetiredUT;
            string ago = retiredUT > 0
                ? RosterRotationState.FormatTimeAgo(retiredUT, now)
                : "date unknown";

            return $"RETIRED L0 ({ago}) — No Recall (Archive)";
        }

        private List<ProtoCrewMember> BuildTrainingCandidates(KerbalRoster roster)
        {
            var list = new List<ProtoCrewMember>();
            double now = Planetarium.GetUniversalTime();

            if (CleanupStaleTrainingRecords(roster))
                SaveScheduler.RequestSave("cleanup stale training records");

            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                RosterRotationState.Records.TryGetValue(k.name, out var r);
                if (!IsAstronautComplexManagedCrew(k, r)) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) continue;
                if (IsDeepFreezeFrozen(k) || (r != null && r.DeepFreezeActive)) continue;
                if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                    k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) continue;
                if (k.experienceLevel >= 3f) continue;

                if (r != null && (r.Retired || r.DeathUT > 0)) continue;
                if (r != null && RecoveryLeaveService.IsEacRecoveryActive(k, r, now)) continue;
                if (HasGraduationExam(r)) continue;

                if (r != null && r.Training != TrainingType.None)
                {
                    // Do not clear completed-but-not-yet-processed training here.
                    // The training candidate list can be rebuilt before the periodic
                    // CheckTrainingCompletion() pass runs after a time warp.  Clearing
                    // the record here races that completion pass and can erase the
                    // ExperienceUpgrade state before EAC marks the final exam pending,
                    // which makes L1/L2/L3 exams never appear after training completes.
                    continue;
                }

                if (k.inactive && k.inactiveTimeEnd > now) continue;
                list.Add(k);
            }

            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            return list;
        }

        /// <summary>
        /// Aggressively cleans any training data left behind for Kerbals that are no longer valid active crew.
        /// This catches Kerbals removed from roster.Crew as well as entries left behind as Applicant,
        /// Tourist, Unowned, Dead, or Missing after dismissal or other roster transitions.
        /// </summary>
        private bool CleanupStaleTrainingRecords(KerbalRoster roster)
        {
            if (roster == null) return false;

            var validTrainingCrewNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in roster.Crew)
            {
                if (IsValidTrainingRosterCrew(k))
                    validTrainingCrewNames.Add(k.name);
            }

            bool anyCleaned = false;
            List<string> staleTrainingKeys = null;

            foreach (var kvp in RosterRotationState.Records)
            {
                var rec = kvp.Value;
                if (rec == null || rec.Training == TrainingType.None) continue;

                if (!validTrainingCrewNames.Contains(kvp.Key))
                {
                    if (staleTrainingKeys == null) staleTrainingKeys = new List<string>();
                    staleTrainingKeys.Add(kvp.Key);
                }
            }

            if (staleTrainingKeys != null)
            {
                for (int i = 0; i < staleTrainingKeys.Count; i++)
                {
                    string key = staleTrainingKeys[i];
                    RosterRotationState.KerbalRecord rec;
                    if (!RosterRotationState.Records.TryGetValue(key, out rec)) continue;
                    if (rec == null || rec.Training == TrainingType.None) continue;

                    RRLog.Verbose($"[EAC] CleanupStaleTrainingRecords: removing leftover training data for non-active Kerbal: {key}");
                    ClearTrainingState(rec);
                    anyCleaned = true;
                }
            }

            if (anyCleaned)
            {
                _lastTrainingCandidatesCacheRT = -10f;
                InvalidateUICaches();
            }

            return anyCleaned;
        }

        private static bool IsValidTrainingRosterCrew(ProtoCrewMember k)
        {
            if (k == null || string.IsNullOrEmpty(k.name)) return false;
            if (k.type != ProtoCrewMember.KerbalType.Crew) return false;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return false;
            return true;
        }

        private static void ClearTrainingState(RosterRotationState.KerbalRecord rec)
        {
            if (rec == null) return;
            rec.Training = TrainingType.None;
            rec.TrainingTargetLevel = 0;
            rec.TrainingEndUT = 0;
            ClearGraduationExamState(rec);
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
            if (IsDeepFreezeFrozen(k) || (r != null && r.DeepFreezeActive))
            {
                if (TryGetDeepFreezeVesselName(k.name, out var frozenVessel))
                    return "FROZEN: " + frozenVessel;
                if (r != null && !string.IsNullOrEmpty(r.DeepFreezeLastKnownVesselName))
                    return "FROZEN: " + r.DeepFreezeLastKnownVesselName;
                return "FROZEN";
            }
            if (retired)
            {
                int eff = RosterRotationState.GetRetiredEffectiveStars(k, r, now);
                return $"RETIRED L{eff} ({RosterRotationState.FormatTimeAgo(r.RetiredUT, now)})";
            }
            if (r != null && r.GraduationExamActive)
                return $"Final exam active → L{r.GraduationExamTargetLevel}";
            if (r != null && r.GraduationExamPending)
                return $"Final exam ready → L{r.GraduationExamTargetLevel}";
            if (r != null)
            {
                double recoveryUntil = RecoveryLeaveService.GetEacRecoveryUntilUT(k, r, now);
                if (recoveryUntil > now)
                    return $"In recovery ({RosterRotationState.FormatCountdown(recoveryUntil - now)})";
            }
            if (r != null && r.Training != TrainingType.None)
            {
                double trainingEndUT = EACKerbalState.GetTrainingEndUT(k, r);
                if (trainingEndUT > now)
                {
                    double rem = trainingEndUT - now;
                    return $"In {TrainingLabel(r.Training, r.TrainingTargetLevel)}  {RosterRotationState.FormatCountdown(rem)}";
                }
                return $"In {TrainingLabel(r.Training, r.TrainingTargetLevel)}";
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
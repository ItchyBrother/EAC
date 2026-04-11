// EAC - Enhanced Astronaut Complex - Mod.Aging.cs
// Partial class: kerbal aging, natural/morale retirement, in-mission death,
// and vessel crew detachment for death processing.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens;

namespace RosterRotation
{
    public partial class RosterRotationKSCUI
    {
        // ── Mission-death probability tuning ───────────────────────────────────
        // These constants govern the per-day chance of a kerbal dying while on a
        // mission past their retirement age. Adjust carefully — small changes have
        // large compounding effects over long missions.
        private const double MissionDeathBaseDailyChance   = 0.000015; // ~0.0015% baseline per day
        private const double MissionDeathMaxDailyChance    = 0.25;     // hard cap: 25% per day
        private const double MissionDeathMaxStressFactor   = 3.0;      // maximum stress multiplier
        private const double MissionDeathStressMissionDays = 120.0;    // mission length (days) to reach max stress
        private const double MissionDeathAgeFactorScale    = 0.5;      // scale applied to (years past retirement)²

        // ── Aging / retirement loop ────────────────────────────────────────────

        private void CheckAgingAndRetirement()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;
            double nowUT  = Planetarium.GetUniversalTime();
            double yearSec = RosterRotationState.YearSeconds;
            bool anyDirty = false;

            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (rec.LastAgedYears < 0) continue;
                if (rec.DeathUT > 0 && !rec.Retired) continue;

                int currentAge = RosterRotationState.GetKerbalAge(rec, nowUT);
                if (currentAge < 0) continue;

                if (MissionTimeTracker.SyncKerbal(k, nowUT)) anyDirty = true;

                double effectiveRetireUT = rec.NaturalRetirementUT + rec.RetirementDelayYears * yearSec;

                if (!rec.Retired)
                {
                    if (CheckAssignedMissionDeath(k, rec, nowUT, currentAge, effectiveRetireUT))
                        anyDirty = true;
                    if (rec.DeathUT > 0) continue;
                }

                if (rec.Retired)
                {
                    if (rec.DeathUT <= 0)
                    {
                        if (CheckRetiredDeath(k, rec, nowUT, currentAge)) anyDirty = true;
                        if (rec.DeathUT <= 0 && RetiredKerbalCleanupService.IsRetiredPurgeDue(k, rec, nowUT))
                        {
                            if (RetiredKerbalCleanupService.RequestAutoCleanupSave(k.name)) anyDirty = true;
                        }
                    }
                    continue;
                }

                if (currentAge <= rec.LastAgedYears) continue;
                rec.LastAgedYears = currentAge;
                anyDirty = true;

                if (k.rosterStatus != ProtoCrewMember.RosterStatus.Dead &&
                    k.rosterStatus != ProtoCrewMember.RosterStatus.Missing)
                {
                    RosterRotationState.PostNotification(
                        EACNotificationType.Birthday, "Birthday — " + k.name,
                        k.name + " turns " + currentAge + " today! (" + RosterRotationState.FormatGameDate(nowUT) + ")",
                        MessageSystemButton.MessageButtonColor.GREEN,
                        MessageSystemButton.ButtonIcons.MESSAGE, 5f);
                }

                if (nowUT >= effectiveRetireUT)
                {
                    if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                    {
                        rec.RetirementScheduled   = true;
                        rec.RetirementScheduledUT = nowUT;
                        RosterRotationState.PostNotification(EACNotificationType.Retirement,
                            $"Retirement Pending — {k.name}",
                            $"{k.name} has reached retirement age but will serve until mission end.",
                            MessageSystemButton.MessageButtonColor.YELLOW, MessageSystemButton.ButtonIcons.ALERT);
                    }
                    else
                    {
                        FireRetirement(k, rec, nowUT, "reached retirement age");
                    }
                    continue;
                }

                if (!rec.RetirementWarned && (effectiveRetireUT - nowUT) < yearSec)
                {
                    rec.RetirementWarned = true;
                    RosterRotationState.PostNotification(EACNotificationType.Retirement,
                        $"Retirement Warning — {k.name}",
                        $"{k.name} is approaching retirement age and may retire within the year.",
                        MessageSystemButton.MessageButtonColor.YELLOW, MessageSystemButton.ButtonIcons.ALERT);
                }

                int stars = (int)k.experienceLevel;
                if (stars >= 4) continue;
                double pRetire = MoraleRetireProbability(k, rec, nowUT, stars);
                if (pRetire <= 0) continue;
                if (UnityEngine.Random.value < pRetire)
                {
                    if (!rec.RetirementWarned)
                    {
                        rec.RetirementWarned = true;
                        RosterRotationState.PostNotification(EACNotificationType.Retirement,
                            $"Retirement Warning — {k.name}",
                            $"{k.name} is considering retirement.",
                            MessageSystemButton.MessageButtonColor.YELLOW, MessageSystemButton.ButtonIcons.ALERT);
                    }
                    else
                    {
                        if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
                        {
                            rec.RetirementScheduled   = true;
                            rec.RetirementScheduledUT = nowUT;
                        }
                        else
                        {
                            FireRetirement(k, rec, nowUT, "decided to retire");
                        }
                    }
                }
            }

            if (anyDirty)
                SaveScheduler.RequestSave("aging and retirement");
        }

        private static double MoraleRetireProbability(
            ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT, int stars)
        {
            double yearSec       = RosterRotationState.YearSeconds;
            double inactiveYears = rec != null && rec.LastFlightUT > 0 ? (nowUT - rec.LastFlightUT) / yearSec : 0;
            int    displayedFlights = GetDisplayedFlights(k, rec);
            return CareerRules.CalculateMoraleRetireProbability(stars, inactiveYears, displayedFlights);
        }

        private void FireRetirement(ProtoCrewMember k, RosterRotationState.KerbalRecord rec,
            double nowUT, string reason)
        {
            if (string.IsNullOrEmpty(rec.OriginalTrait)) rec.OriginalTrait = k.trait;
            rec.OriginalType         = k.type;
            rec.Retired              = true;
            rec.RetiredUT            = nowUT;
            rec.ExperienceAtRetire   = (int)k.experienceLevel;
            rec.RetirementWarned     = false;
            rec.RetirementScheduled  = false;
            k.inactive        = true;
            k.inactiveTimeEnd = nowUT + RosterRotationState.YearSeconds * 1000.0;
            RosterRotationState.InvalidateRetiredCache();
            RetiredKerbalCleanupService.ResetAutoCleanupRequest(k.name);

            int    frAge  = RosterRotationState.GetKerbalAge(rec, nowUT);
            string frAgeS = frAge >= 0 ? $" at age {frAge}" : "";
            RosterRotationState.PostNotification(EACNotificationType.Retirement,
                $"Retired — {k.name}",
                $"{k.name} has {reason} and entered retirement{frAgeS}. ({RosterRotationState.FormatGameDate(nowUT)})",
                MessageSystemButton.MessageButtonColor.ORANGE, MessageSystemButton.ButtonIcons.MESSAGE);
            InvalidateUICaches();
            _pendingForceRefresh = true;
        }

        private bool CheckRetiredDeath(ProtoCrewMember k, RosterRotationState.KerbalRecord rec,
            double nowUT, int currentAge)
        {
            if (currentAge <= rec.LastAgedYears) return false;
            rec.LastAgedYears = currentAge;

            int    minAge = RosterRotationState.RetiredDeathAgeMin;
            double pDeath;
            if      (currentAge >= minAge + 30) pDeath = 0.30;
            else if (currentAge >= minAge + 20) pDeath = 0.14;
            else if (currentAge >= minAge + 10) pDeath = 0.06;
            else if (currentAge >= minAge)      pDeath = 0.02;
            else                                return true;

            if (UnityEngine.Random.value >= pDeath) return true;

            rec.DeathUT          = nowUT;
            rec.DiedOnMission    = false;
            rec.PendingMissionDeath = false;
            try { k.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch (Exception ex) { RRLog.VerboseExceptionOnce("Aging.CheckRetiredDeath.SetDead", "Suppressed", ex); }

            if (RosterRotationState.DeathNotificationsEnabled)
                RosterRotationState.PostNotification(EACNotificationType.Death,
                    $"Deceased — {k.name}",
                    $"{k.name} has passed away at age {currentAge}. ({RosterRotationState.FormatGameDate(nowUT)})",
                    MessageSystemButton.MessageButtonColor.RED, MessageSystemButton.ButtonIcons.ALERT, 12f);

            InvalidateUICaches();
            _pendingForceRefresh = true;
            return true;
        }

        // ── Mission-death check ────────────────────────────────────────────────

        private bool CheckAssignedMissionDeath(ProtoCrewMember k, RosterRotationState.KerbalRecord rec,
            double nowUT, int currentAge, double effectiveRetireUT)
        {
            if (k == null || rec == null) return false;
            if (k.rosterStatus != ProtoCrewMember.RosterStatus.Assigned) return false;
            if (rec.DeathUT > 0) return false;

            bool forceTest = RosterRotationState.DebugForceMissionDeath;
            if (!forceTest && !RosterRotationState.MissionDeathEnabled) return false;
            if (!forceTest && nowUT < effectiveRetireUT) return false;

            if (rec.MissionStartUT <= 0 || rec.MissionStartUT > nowUT)
                rec.MissionStartUT = nowUT;

            double daySec       = RosterRotationState.DaySeconds;
            double lastCheckUT  = rec.LastMissionDeathCheckUT > 0 ? rec.LastMissionDeathCheckUT : rec.MissionStartUT;
            double riskStartUT  = forceTest ? lastCheckUT : Math.Max(lastCheckUT, effectiveRetireUT);
            double eligibleElapsedUT = nowUT - riskStartUT;
            if (!forceTest && eligibleElapsedUT < daySec) return false;

            int    elapsedDays          = forceTest ? 1 : Math.Max(1, (int)Math.Floor(eligibleElapsedUT / daySec));
            double missionDays          = rec.MissionStartUT > 0 ? Math.Max(0.0, (nowUT - rec.MissionStartUT) / daySec) : 0.0;
            double yearsPastRetirement  = forceTest ? 0.0 : Math.Max(0.0, (nowUT - effectiveRetireUT) / RosterRotationState.YearSeconds);

            // Named constants replace the raw magic numbers that were here previously.
            double ageFactor   = 1.0 + yearsPastRetirement * yearsPastRetirement * MissionDeathAgeFactorScale;
            double stressFactor = 1.0 + Math.Min(MissionDeathMaxStressFactor, missionDays / MissionDeathStressMissionDays);
            double dailyChance = Math.Min(MissionDeathMaxDailyChance, MissionDeathBaseDailyChance * ageFactor * stressFactor);
            double rollChance  = forceTest ? 1.0 : 1.0 - Math.Pow(Math.Max(0.0, 1.0 - dailyChance), elapsedDays);
            float  roll        = UnityEngine.Random.value;

            rec.LastMissionDeathCheckUT = nowUT;

            if (RRLog.VerboseEnabled)
            {
                RRLog.Verbose(
                    $"[EAC] Mission death check for {k.name}: ForceTest={forceTest}, Age={currentAge}" +
                    $", YearsPastRetirement={yearsPastRetirement:F2}, MissionDays={missionDays:F1}" +
                    $", ElapsedDays={elapsedDays}, DailyChance={dailyChance:P4}" +
                    $", RollChance={rollChance:P4}, Roll={roll:F4}");
            }

            if (!forceTest && roll >= rollChance) return true;
            if (forceTest) RosterRotationState.DebugForceMissionDeath = false;

            string vesselName;
            if (!TryDetachKerbalFromAssignedVessel(k, out vesselName))
            {
                RRLog.Error("[EAC] Mission death could not detach " + (k?.name ?? "<null>") + " from assigned vessel; aborting death transition.");
                return false;
            }

            rec.DeathUT             = nowUT;
            rec.DiedOnMission       = true;
            rec.PendingMissionDeath = true;
            rec.RetirementScheduled = false;
            rec.MissionStartUT      = 0;
            rec.LastMissionDeathCheckUT = 0;
            RetiredKerbalCleanupService.ResetAutoCleanupRequest(k.name);

            try { k.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch (Exception ex) { RRLog.VerboseExceptionOnce("Aging.MissionDeath.SetDead1", "Suppressed", ex); }

            SaveScheduler.RequestImmediateSave("assigned mission death");
            try { if (k.rosterStatus != ProtoCrewMember.RosterStatus.Dead) k.rosterStatus = ProtoCrewMember.RosterStatus.Dead; } catch (Exception ex) { RRLog.VerboseExceptionOnce("Aging.MissionDeath.SetDead2", "Suppressed", ex); }

            if (RosterRotationState.DeathNotificationsEnabled)
            {
                string causeText = forceTest
                    ? $"[TEST] {k.name} was marked dead on mission."
                    : $"{k.name} has died on mission at age {currentAge} after {missionDays:F0} days in space.";

                RosterRotationState.PostNotification(
                    EACNotificationType.Death,
                    $"Deceased on mission — {k.name}",
                    causeText + $" ({RosterRotationState.FormatGameDate(nowUT)})",
                    MessageSystemButton.MessageButtonColor.RED,
                    MessageSystemButton.ButtonIcons.ALERT, 12f);
            }

            InvalidateUICaches();
            _pendingForceRefresh = true;
            return true;
        }

        // ── Vessel crew detachment helpers ─────────────────────────────────────

        private static bool TryDetachKerbalFromAssignedVessel(ProtoCrewMember k, out string vesselName)
        {
            vesselName = null;
            if (k == null || string.IsNullOrEmpty(k.name)) return false;

            try
            {
                var protoVessels = HighLogic.CurrentGame?.flightState?.protoVessels as IEnumerable;
                if (protoVessels == null) return false;

                foreach (var pv in protoVessels)
                {
                    if (pv == null) continue;
                    bool foundCrew = false;
                    try
                    {
                        var pvType  = pv.GetType();
                        var flags   = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        var getCrew = pvType.GetMethod("GetVesselCrew", flags);
                        var crewRaw = getCrew?.Invoke(pv, null) as IEnumerable;
                        if (crewRaw == null) continue;

                        foreach (var crewObj in crewRaw)
                        {
                            var    crew     = crewObj as ProtoCrewMember;
                            string crewName = crew?.name;
                            if (string.IsNullOrEmpty(crewName) && crewObj != null)
                            {
                                var ct = crewObj.GetType();
                                crewName = ct.GetField("name", flags)?.GetValue(crewObj) as string;
                                if (string.IsNullOrEmpty(crewName))
                                    crewName = ct.GetProperty("name", flags)?.GetValue(crewObj, null) as string;
                            }
                            if (!string.Equals(crewName, k.name, StringComparison.Ordinal)) continue;
                            foundCrew = true;
                            break;
                        }
                        if (!foundCrew) continue;

                        vesselName = pvType.GetField("vesselName", flags)?.GetValue(pv) as string;
                        if (string.IsNullOrEmpty(vesselName))
                            vesselName = pvType.GetProperty("vesselName", flags)?.GetValue(pv, null) as string;

                        object partsObj = pvType.GetField("protoPartSnapshots", flags)?.GetValue(pv)
                                       ?? pvType.GetProperty("protoPartSnapshots", flags)?.GetValue(pv, null);
                        var parts = partsObj as IEnumerable;
                        if (parts == null) return false;

                        bool removed = false;
                        int  removedConfigCrew = 0, removedCachedCrew = 0;

                        foreach (var partObj in parts)
                        {
                            if (partObj == null) continue;
                            var pt = partObj.GetType();

                            object crewListObj = pt.GetField("protoModuleCrew", flags)?.GetValue(partObj)
                                              ?? pt.GetProperty("protoModuleCrew", flags)?.GetValue(partObj, null);
                            var list = crewListObj as IList;
                            if (list != null && list.Count > 0)
                            {
                                for (int i = list.Count - 1; i >= 0; --i)
                                {
                                    var    crewEntry = list[i];
                                    var    crew      = crewEntry as ProtoCrewMember;
                                    string crewName  = crew?.name;
                                    if (string.IsNullOrEmpty(crewName) && crewEntry != null)
                                    {
                                        var ct = crewEntry.GetType();
                                        crewName = ct.GetField("name", flags)?.GetValue(crewEntry) as string;
                                        if (string.IsNullOrEmpty(crewName))
                                        {
                                            var prop = ct.GetProperty("name", flags);
                                            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                                                crewName = prop.GetValue(crewEntry, null) as string;
                                        }
                                    }
                                    if (!string.Equals(crewName, k.name, StringComparison.Ordinal)) continue;
                                    list.RemoveAt(i);
                                    removed = true;
                                }
                            }

                            removedCachedCrew += RemoveKerbalFromCrewLikeLists(partObj, k.name);

                            foreach (var field in pt.GetFields(flags))
                            {
                                if (field == null || field.FieldType != typeof(ConfigNode)) continue;
                                var node = field.GetValue(partObj) as ConfigNode;
                                if (node == null) continue;
                                removedConfigCrew += RemoveKerbalFromConfigNodeCrewValues(node, k.name);
                            }
                            foreach (var prop in pt.GetProperties(flags))
                            {
                                if (prop == null || prop.PropertyType != typeof(ConfigNode) || !prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                                ConfigNode node = null;
                                try { node = prop.GetValue(partObj, null) as ConfigNode; } catch (Exception ex) { RRLog.VerboseExceptionOnce("Aging.Detach.PropNode", "Suppressed", ex); }
                                if (node == null) continue;
                                removedConfigCrew += RemoveKerbalFromConfigNodeCrewValues(node, k.name);
                            }
                        }

                        removedCachedCrew += RemoveKerbalFromCrewLikeLists(pv, k.name);
                        TryRefreshProtoVesselCrewCaches(pv);

                        if (removed || removedConfigCrew > 0 || removedCachedCrew > 0)
                        {
                            RRLog.Verbose("[EAC] Detached " + k.name + " from vessel " +
                                (string.IsNullOrEmpty(vesselName) ? "<unknown>" : vesselName) +
                                " for mission death processing. protoModuleCrewRemoved=" + removed +
                                ", configCrewValuesRemoved=" + removedConfigCrew +
                                ", cachedCrewEntriesRemoved=" + removedCachedCrew + ".");
                            return true;
                        }
                        return false;
                    }
                    catch (Exception ex)
                    {
                        RRLog.Error("[EAC] Failed detaching " + k.name + " from assigned vessel: " + ex);
                        if (foundCrew) return false;
                    }
                }
            }
            catch (Exception ex) { RRLog.Error("[EAC] Exception while scanning assigned vessels for " + k.name + ": " + ex); }

            return false;
        }

        private static int RemoveKerbalFromConfigNodeCrewValues(ConfigNode node, string kerbalName)
        {
            if (node == null || string.IsNullOrEmpty(kerbalName)) return 0;

            int  removed      = 0;
            var  keepCrew     = new List<string>();
            bool touchedCrewValues = false;

            foreach (ConfigNode.Value value in node.values)
            {
                if (value == null) continue;
                if (!string.Equals(value.name, "crew", StringComparison.OrdinalIgnoreCase)) continue;
                touchedCrewValues = true;
                if (string.Equals(value.value, kerbalName, StringComparison.Ordinal)) { removed++; continue; }
                keepCrew.Add(value.value);
            }

            if (touchedCrewValues)
            {
                node.RemoveValues("crew");
                for (int i = 0; i < keepCrew.Count; i++) node.AddValue("crew", keepCrew[i]);
            }

            foreach (ConfigNode child in node.nodes)
                removed += RemoveKerbalFromConfigNodeCrewValues(child, kerbalName);

            return removed;
        }

        private static int RemoveKerbalFromCrewLikeLists(object owner, string kerbalName)
        {
            if (owner == null || string.IsNullOrEmpty(kerbalName)) return 0;

            int  removed = 0;
            var  flags   = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var  type    = owner.GetType();

            foreach (var field in type.GetFields(flags))
            {
                if (field == null) continue;
                if (field.FieldType == typeof(string)) continue;
                if (!typeof(IList).IsAssignableFrom(field.FieldType)) continue;
                if (field.Name.IndexOf("crew", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var list = field.GetValue(owner) as IList;
                if (list == null || list.Count == 0) continue;

                for (int i = list.Count - 1; i >= 0; --i)
                {
                    var entry = list[i];
                    if (entry == null) continue;

                    string entryName = entry as string;
                    var    pcm       = entry as ProtoCrewMember;
                    if (string.IsNullOrEmpty(entryName) && pcm != null) entryName = pcm.name;
                    if (string.IsNullOrEmpty(entryName))
                    {
                        var et = entry.GetType();
                        entryName = et.GetField("name", flags)?.GetValue(entry) as string;
                        if (string.IsNullOrEmpty(entryName))
                        {
                            var prop = et.GetProperty("name", flags);
                            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                                entryName = prop.GetValue(entry, null) as string;
                        }
                    }

                    if (!string.Equals(entryName, kerbalName, StringComparison.Ordinal)) continue;
                    list.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        private static void TryRefreshProtoVesselCrewCaches(object protoVessel)
        {
            if (protoVessel == null) return;
            try
            {
                var flags  = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var pvType = protoVessel.GetType();

                int totalCrew = 0, crewedParts = 0;
                object partsObj = pvType.GetField("protoPartSnapshots", flags)?.GetValue(protoVessel)
                               ?? pvType.GetProperty("protoPartSnapshots", flags)?.GetValue(protoVessel, null);
                var parts = partsObj as IEnumerable;
                if (parts != null)
                {
                    foreach (var partObj in parts)
                    {
                        if (partObj == null) continue;
                        var    pt         = partObj.GetType();
                        object crewListObj = pt.GetField("protoModuleCrew", flags)?.GetValue(partObj)
                                          ?? pt.GetProperty("protoModuleCrew", flags)?.GetValue(partObj, null);
                        int count = (crewListObj as IList)?.Count ?? 0;
                        if (count > 0) { totalCrew += count; crewedParts++; }
                    }
                }

                var vesselCrewField = pvType.GetField("vesselCrew", flags);
                if (vesselCrewField != null && vesselCrewField.FieldType == typeof(int))
                    vesselCrewField.SetValue(protoVessel, totalCrew);
                var vesselCrewProp = pvType.GetProperty("vesselCrew", flags);
                if (vesselCrewProp != null && vesselCrewProp.CanWrite && vesselCrewProp.PropertyType == typeof(int))
                    vesselCrewProp.SetValue(protoVessel, totalCrew, null);

                var crewedPartsField = pvType.GetField("crewedParts", flags);
                if (crewedPartsField != null && crewedPartsField.FieldType == typeof(int))
                    crewedPartsField.SetValue(protoVessel, crewedParts);
                var crewedPartsProp = pvType.GetProperty("crewedParts", flags);
                if (crewedPartsProp != null && crewedPartsProp.CanWrite && crewedPartsProp.PropertyType == typeof(int))
                    crewedPartsProp.SetValue(protoVessel, crewedParts, null);

                object vesselRef = pvType.GetField("vesselRef", flags)?.GetValue(protoVessel)
                               ?? pvType.GetProperty("vesselRef", flags)?.GetValue(protoVessel, null);
                if (vesselRef != null)
                {
                    var vt = vesselRef.GetType();
                    vt.GetMethod("CrewListSetDirty", flags)?.Invoke(vesselRef, null);
                    var crewWasModified = vt.GetMethod("CrewWasModified",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { vt }, null);
                    crewWasModified?.Invoke(null, new[] { vesselRef });
                }
            }
            catch (Exception ex) { RRLog.Verbose("[EAC] Failed refreshing proto vessel crew caches: " + ex.Message); }
        }
    }
}

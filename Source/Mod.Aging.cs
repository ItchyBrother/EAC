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

        private const BindingFlags AgingReflectionFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class ProtoVesselDetachAccessors
        {
            public MethodInfo   GetVesselCrewMethod;
            public FieldInfo    VesselNameField;
            public PropertyInfo VesselNameProperty;
            public FieldInfo    ProtoPartSnapshotsField;
            public PropertyInfo ProtoPartSnapshotsProperty;
        }

        private sealed class ProtoPartDetachAccessors
        {
            public FieldInfo    ProtoModuleCrewField;
            public PropertyInfo ProtoModuleCrewProperty;
        }

        private sealed class ConfigNodeMemberAccessors
        {
            public FieldInfo[]    Fields;
            public PropertyInfo[] Properties;
        }

        private sealed class ProtoVesselRefreshAccessors
        {
            public FieldInfo    VesselCrewField;
            public PropertyInfo VesselCrewProperty;
            public FieldInfo    CrewedPartsField;
            public PropertyInfo CrewedPartsProperty;
            public FieldInfo    VesselRefField;
            public PropertyInfo VesselRefProperty;
        }

        private sealed class VesselRefreshAccessors
        {
            public MethodInfo CrewListSetDirtyMethod;
            public MethodInfo CrewWasModifiedMethod;
        }

        private static readonly Dictionary<Type, ProtoVesselDetachAccessors> _detachProtoVesselAccessorsByType
            = new Dictionary<Type, ProtoVesselDetachAccessors>();
        private static readonly Dictionary<Type, ProtoPartDetachAccessors> _detachProtoPartAccessorsByType
            = new Dictionary<Type, ProtoPartDetachAccessors>();
        private static readonly Dictionary<Type, FieldInfo[]> _crewLikeFieldsByOwnerType
            = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, ConfigNodeMemberAccessors> _configNodeMembersByOwnerType
            = new Dictionary<Type, ConfigNodeMemberAccessors>();
        private static readonly Dictionary<Type, ProtoVesselRefreshAccessors> _refreshProtoVesselAccessorsByType
            = new Dictionary<Type, ProtoVesselRefreshAccessors>();
        private static readonly Dictionary<Type, VesselRefreshAccessors> _refreshVesselAccessorsByType
            = new Dictionary<Type, VesselRefreshAccessors>();

        private static readonly FieldInfo[]    EmptyFieldInfos    = new FieldInfo[0];
        private static readonly PropertyInfo[] EmptyPropertyInfos = new PropertyInfo[0];

        // ── Aging / retirement loop ────────────────────────────────────────────

        private void CheckAgingAndRetirement()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;
            double nowUT  = Planetarium.GetUniversalTime();
            double yearSec = RosterRotationState.YearSeconds;
            double daySec  = RosterRotationState.DaySeconds;
            bool anyDirty = false;

            foreach (var k in roster.Crew)
            {
                if (k == null || k.type == ProtoCrewMember.KerbalType.Applicant) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;

                if (ReconcileDeepFreezeState(k, rec, nowUT)) anyDirty = true;
                if (rec.DeepFreezeActive || IsDeepFreezeFrozen(k)) continue;

                if (rec.LastAgedYears < 0) continue;
                if (rec.DeathUT > 0 && !rec.Retired) continue;

                int currentAge = RosterRotationState.GetKerbalAge(rec, nowUT);
                if (currentAge < 0) continue;

                if (MissionTimeTracker.SyncKerbal(k, nowUT)) anyDirty = true;

                double effectiveRetireUT = rec.NaturalRetirementUT + rec.RetirementDelayYears * yearSec;

                if (!rec.Retired)
                {
                    if (CheckAssignedMissionDeath(k, rec, nowUT, currentAge, effectiveRetireUT, yearSec, daySec))
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
                double pRetire = MoraleRetireProbability(k, rec, nowUT, stars, yearSec);
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
            ProtoCrewMember k, RosterRotationState.KerbalRecord rec, double nowUT, int stars, double yearSec)
        {
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
            RosterRotationState.NoteCertifiedLevel(rec, (int)k.experienceLevel);
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
            double nowUT, int currentAge, double effectiveRetireUT, double yearSec, double daySec)
        {
            if (k == null || rec == null) return false;
            if (rec.DeepFreezeActive || IsDeepFreezeFrozen(k)) return false;
            if (k.rosterStatus != ProtoCrewMember.RosterStatus.Assigned) return false;
            if (rec.DeathUT > 0) return false;

            bool forceTest = RosterRotationState.DebugForceMissionDeath;
            if (!forceTest && !RosterRotationState.MissionDeathEnabled) return false;
            if (!forceTest && nowUT < effectiveRetireUT) return false;

            if (rec.MissionStartUT <= 0 || rec.MissionStartUT > nowUT)
                rec.MissionStartUT = nowUT;

            double lastCheckUT  = rec.LastMissionDeathCheckUT > 0 ? rec.LastMissionDeathCheckUT : rec.MissionStartUT;
            double riskStartUT  = forceTest ? lastCheckUT : Math.Max(lastCheckUT, effectiveRetireUT);
            double eligibleElapsedUT = nowUT - riskStartUT;
            if (!forceTest && eligibleElapsedUT < daySec) return false;

            int    elapsedDays          = forceTest ? 1 : Math.Max(1, (int)Math.Floor(eligibleElapsedUT / daySec));
            double missionDays          = rec.MissionStartUT > 0 ? Math.Max(0.0, (nowUT - rec.MissionStartUT) / daySec) : 0.0;
            double yearsPastRetirement  = forceTest ? 0.0 : Math.Max(0.0, (nowUT - effectiveRetireUT) / yearSec);

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

        private static ProtoVesselDetachAccessors GetProtoVesselDetachAccessors(Type type)
        {
            if (type == null) return null;

            if (_detachProtoVesselAccessorsByType.TryGetValue(type, out var cached))
                return cached;

            var accessors = new ProtoVesselDetachAccessors
            {
                GetVesselCrewMethod       = type.GetMethod("GetVesselCrew", AgingReflectionFlags),
                VesselNameField           = type.GetField("vesselName", AgingReflectionFlags),
                VesselNameProperty        = type.GetProperty("vesselName", AgingReflectionFlags),
                ProtoPartSnapshotsField   = type.GetField("protoPartSnapshots", AgingReflectionFlags),
                ProtoPartSnapshotsProperty = type.GetProperty("protoPartSnapshots", AgingReflectionFlags)
            };

            _detachProtoVesselAccessorsByType[type] = accessors;
            return accessors;
        }

        private static ProtoPartDetachAccessors GetProtoPartDetachAccessors(Type type)
        {
            if (type == null) return null;

            if (_detachProtoPartAccessorsByType.TryGetValue(type, out var cached))
                return cached;

            var accessors = new ProtoPartDetachAccessors
            {
                ProtoModuleCrewField    = type.GetField("protoModuleCrew", AgingReflectionFlags),
                ProtoModuleCrewProperty = type.GetProperty("protoModuleCrew", AgingReflectionFlags)
            };

            _detachProtoPartAccessorsByType[type] = accessors;
            return accessors;
        }

        private static FieldInfo[] GetCrewLikeFields(Type type)
        {
            if (type == null) return new FieldInfo[0];

            if (_crewLikeFieldsByOwnerType.TryGetValue(type, out var cached))
                return cached;

            var matches = new List<FieldInfo>();
            foreach (var field in type.GetFields(AgingReflectionFlags))
            {
                if (field == null) continue;
                if (field.FieldType == typeof(string)) continue;
                if (!typeof(IList).IsAssignableFrom(field.FieldType)) continue;
                if (field.Name.IndexOf("crew", StringComparison.OrdinalIgnoreCase) < 0) continue;
                matches.Add(field);
            }

            cached = matches.ToArray();
            _crewLikeFieldsByOwnerType[type] = cached;
            return cached;
        }

        private static ConfigNodeMemberAccessors GetConfigNodeMembers(Type type)
        {
            if (type == null)
            {
                return new ConfigNodeMemberAccessors
                {
                    Fields     = EmptyFieldInfos,
                    Properties = EmptyPropertyInfos
                };
            }

            if (_configNodeMembersByOwnerType.TryGetValue(type, out var cached))
                return cached;

            var fields = new List<FieldInfo>();
            foreach (var field in type.GetFields(AgingReflectionFlags))
            {
                if (field == null) continue;
                if (field.FieldType != typeof(ConfigNode)) continue;
                fields.Add(field);
            }

            var properties = new List<PropertyInfo>();
            foreach (var prop in type.GetProperties(AgingReflectionFlags))
            {
                if (prop == null) continue;
                if (prop.PropertyType != typeof(ConfigNode)) continue;
                if (!prop.CanRead) continue;
                if (prop.GetIndexParameters().Length != 0) continue;
                properties.Add(prop);
            }

            cached = new ConfigNodeMemberAccessors
            {
                Fields     = fields.Count > 0 ? fields.ToArray() : EmptyFieldInfos,
                Properties = properties.Count > 0 ? properties.ToArray() : EmptyPropertyInfos
            };
            _configNodeMembersByOwnerType[type] = cached;
            return cached;
        }

        private static ProtoVesselRefreshAccessors GetProtoVesselRefreshAccessors(Type type)
        {
            if (type == null) return null;

            if (_refreshProtoVesselAccessorsByType.TryGetValue(type, out var cached))
                return cached;

            var vesselCrewField = type.GetField("vesselCrew", AgingReflectionFlags);
            var vesselCrewProp  = type.GetProperty("vesselCrew", AgingReflectionFlags);
            var crewedPartsField = type.GetField("crewedParts", AgingReflectionFlags);
            var crewedPartsProp  = type.GetProperty("crewedParts", AgingReflectionFlags);

            cached = new ProtoVesselRefreshAccessors
            {
                VesselCrewField     = vesselCrewField != null && vesselCrewField.FieldType == typeof(int) ? vesselCrewField : null,
                VesselCrewProperty  = vesselCrewProp != null && vesselCrewProp.CanWrite && vesselCrewProp.PropertyType == typeof(int) ? vesselCrewProp : null,
                CrewedPartsField    = crewedPartsField != null && crewedPartsField.FieldType == typeof(int) ? crewedPartsField : null,
                CrewedPartsProperty = crewedPartsProp != null && crewedPartsProp.CanWrite && crewedPartsProp.PropertyType == typeof(int) ? crewedPartsProp : null,
                VesselRefField      = type.GetField("vesselRef", AgingReflectionFlags),
                VesselRefProperty   = type.GetProperty("vesselRef", AgingReflectionFlags)
            };

            _refreshProtoVesselAccessorsByType[type] = cached;
            return cached;
        }

        private static VesselRefreshAccessors GetVesselRefreshAccessors(Type type)
        {
            if (type == null) return null;

            if (_refreshVesselAccessorsByType.TryGetValue(type, out var cached))
                return cached;

            cached = new VesselRefreshAccessors
            {
                CrewListSetDirtyMethod = type.GetMethod("CrewListSetDirty", AgingReflectionFlags),
                CrewWasModifiedMethod  = type.GetMethod("CrewWasModified",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { type }, null)
            };

            _refreshVesselAccessorsByType[type] = cached;
            return cached;
        }

        private static bool TryGetValue(FieldInfo field, object owner, out object value)
        {
            value = null;
            if (field == null || owner == null) return false;
            try { value = field.GetValue(owner); return true; }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("Aging.Reflection.FieldValue", "Suppressed", ex); return false; }
        }

        private static bool TryGetValue(PropertyInfo prop, object owner, out object value)
        {
            value = null;
            if (prop == null || owner == null || !prop.CanRead || prop.GetIndexParameters().Length != 0) return false;
            try { value = prop.GetValue(owner, null); return true; }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("Aging.Reflection.PropertyValue", "Suppressed", ex); return false; }
        }

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
                        var pvType    = pv.GetType();
                        var pvAccessors = GetProtoVesselDetachAccessors(pvType);
                        var crewRaw     = pvAccessors?.GetVesselCrewMethod?.Invoke(pv, null) as IEnumerable;
                        if (crewRaw == null) continue;

                        foreach (var crewObj in crewRaw)
                        {
                            var    crew     = crewObj as ProtoCrewMember;
                            string crewName = crew?.name;
                            if (string.IsNullOrEmpty(crewName) && crewObj != null)
                            {
                                var ct = crewObj.GetType();
                                crewName = ct.GetField("name", AgingReflectionFlags)?.GetValue(crewObj) as string;
                                if (string.IsNullOrEmpty(crewName))
                                    crewName = ct.GetProperty("name", AgingReflectionFlags)?.GetValue(crewObj, null) as string;
                            }
                            if (!string.Equals(crewName, k.name, StringComparison.Ordinal)) continue;
                            foundCrew = true;
                            break;
                        }
                        if (!foundCrew) continue;

                        vesselName = pvAccessors?.VesselNameField?.GetValue(pv) as string;
                        if (string.IsNullOrEmpty(vesselName))
                            vesselName = pvAccessors?.VesselNameProperty?.GetValue(pv, null) as string;

                        object partsObj = pvAccessors?.ProtoPartSnapshotsField?.GetValue(pv)
                                       ?? pvAccessors?.ProtoPartSnapshotsProperty?.GetValue(pv, null);
                        var parts = partsObj as IEnumerable;
                        if (parts == null) return false;

                        bool removed = false;
                        int  removedConfigCrew = 0, removedCachedCrew = 0;

                        foreach (var partObj in parts)
                        {
                            if (partObj == null) continue;
                            var pt = partObj.GetType();

                            var partAccessors = GetProtoPartDetachAccessors(pt);
                            object crewListObj = partAccessors?.ProtoModuleCrewField?.GetValue(partObj)
                                              ?? partAccessors?.ProtoModuleCrewProperty?.GetValue(partObj, null);
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
                                        crewName = ct.GetField("name", AgingReflectionFlags)?.GetValue(crewEntry) as string;
                                        if (string.IsNullOrEmpty(crewName))
                                        {
                                            var prop = ct.GetProperty("name", AgingReflectionFlags);
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

                            var configMembers = GetConfigNodeMembers(pt);
                            foreach (var field in configMembers.Fields)
                            {
                                object value;
                                if (!TryGetValue(field, partObj, out value)) continue;
                                var node = value as ConfigNode;
                                if (node == null) continue;
                                removedConfigCrew += RemoveKerbalFromConfigNodeCrewValues(node, k.name);
                            }
                            foreach (var prop in configMembers.Properties)
                            {
                                object value;
                                if (!TryGetValue(prop, partObj, out value)) continue;
                                var node = value as ConfigNode;
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
            var  type    = owner.GetType();

            foreach (var field in GetCrewLikeFields(type))
            {
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
                        entryName = et.GetField("name", AgingReflectionFlags)?.GetValue(entry) as string;
                        if (string.IsNullOrEmpty(entryName))
                        {
                            var prop = et.GetProperty("name", AgingReflectionFlags);
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
                var pvType          = protoVessel.GetType();
                var detachAccessors = GetProtoVesselDetachAccessors(pvType);
                var refreshAccessors = GetProtoVesselRefreshAccessors(pvType);

                int totalCrew = 0, crewedParts = 0;

                object partsObj = null;
                object value;
                if (detachAccessors != null && TryGetValue(detachAccessors.ProtoPartSnapshotsField, protoVessel, out value))
                    partsObj = value;
                if (partsObj == null && detachAccessors != null && TryGetValue(detachAccessors.ProtoPartSnapshotsProperty, protoVessel, out value))
                    partsObj = value;

                var parts = partsObj as IEnumerable;
                if (parts != null)
                {
                    foreach (var partObj in parts)
                    {
                        if (partObj == null) continue;

                        var partAccessors = GetProtoPartDetachAccessors(partObj.GetType());
                        object crewListObj = null;
                        if (partAccessors != null && TryGetValue(partAccessors.ProtoModuleCrewField, partObj, out value))
                            crewListObj = value;
                        if (crewListObj == null && partAccessors != null && TryGetValue(partAccessors.ProtoModuleCrewProperty, partObj, out value))
                            crewListObj = value;

                        var crewList = crewListObj as IList;
                        int count = crewList != null ? crewList.Count : 0;
                        if (count > 0)
                        {
                            totalCrew += count;
                            crewedParts++;
                        }
                    }
                }

                if (refreshAccessors != null)
                {
                    if (refreshAccessors.VesselCrewField != null)
                        refreshAccessors.VesselCrewField.SetValue(protoVessel, totalCrew);
                    if (refreshAccessors.VesselCrewProperty != null)
                        refreshAccessors.VesselCrewProperty.SetValue(protoVessel, totalCrew, null);

                    if (refreshAccessors.CrewedPartsField != null)
                        refreshAccessors.CrewedPartsField.SetValue(protoVessel, crewedParts);
                    if (refreshAccessors.CrewedPartsProperty != null)
                        refreshAccessors.CrewedPartsProperty.SetValue(protoVessel, crewedParts, null);

                    object vesselRef = null;
                    if (TryGetValue(refreshAccessors.VesselRefField, protoVessel, out value))
                        vesselRef = value;
                    if (vesselRef == null && TryGetValue(refreshAccessors.VesselRefProperty, protoVessel, out value))
                        vesselRef = value;

                    if (vesselRef != null)
                    {
                        var vesselAccessors = GetVesselRefreshAccessors(vesselRef.GetType());
                        if (vesselAccessors != null)
                        {
                            vesselAccessors.CrewListSetDirtyMethod?.Invoke(vesselRef, null);
                            vesselAccessors.CrewWasModifiedMethod?.Invoke(null, new[] { vesselRef });
                        }
                    }
                }
            }
            catch (Exception ex) { RRLog.Verbose("[EAC] Failed refreshing proto vessel crew caches: " + ex.Message); }
        }
    }
}

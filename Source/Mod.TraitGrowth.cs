// EAC - Enhanced Astronaut Complex - Mod.TraitGrowth.cs
// Partial class: courage/stupidity trait growth applied on mission recovery and training completion.

using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace RosterRotation
{
    public partial class RosterRotationKSCUI
    {
        // ── Tuning constants ───────────────────────────────────────────────────
        private const float TraitGrowthCourageCap   = 0.90f;
        private const float TraitGrowthStupidityFloor = 0.10f;

        // Veteran growth (applied on mission recovery for L3+ kerbals)
        private const float TraitGrowthVeteranBaseChance              = 0.25f;
        private const float TraitGrowthVeteranFlightBonusPerFlight    = 0.01f;
        private const float TraitGrowthVeteranFlightBonusCap          = 0.10f;
        private const float TraitGrowthVeteranHourBonusPerTenHours    = 0.01f;
        private const float TraitGrowthVeteranHourBonusCap            = 0.10f;
        private const float TraitGrowthVeteranTotalBonusCap           = 0.15f;
        private const float TraitGrowthVeteranDelta                   = 0.02f;

        // ── Public entry points ────────────────────────────────────────────────

        public static void TryApplyVeteranTraitGrowthOnRecovery(
            ProtoCrewMember k, RosterRotationState.KerbalRecord rec, Vessel vessel, double nowUT)
        {
            if (!RosterRotationState.TraitGrowthEnabled) return;
            if (k == null || rec == null) return;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return;
            if (rec.Retired) return;
            if (k.type != ProtoCrewMember.KerbalType.Crew) return;
            if (k.experienceLevel < 3)
            {
                if (RRLog.VerboseEnabled)
                    RRLog.Verbose($"[EAC] Trait growth (veteran) skipped for {k.name}: experienceLevel={k.experienceLevel} (< 3).");
                return;
            }

            int eacFlights = Math.Max(0, rec.Flights);
            int ftFlights; string ftFlightsReason;
            bool ftFlightsAvailable = TryGetFlightTrackerFlights(k.name, out ftFlights, out ftFlightsReason);
            int flights = ftFlightsAvailable ? Math.Max(0, ftFlights) : eacFlights;

            double ftHours; string ftHoursReason;
            bool ftHoursAvailable   = TryGetFlightTrackerRecordedHours(k.name, out ftHours, out ftHoursReason);
            double currentMissionHours = vessel != null ? Math.Max(0.0, vessel.missionTime / 3600.0) : 0.0;
            double totalHours       = ftHoursAvailable ? Math.Max(0.0, ftHours) : currentMissionHours;

            float flightBonus  = Mathf.Min(TraitGrowthVeteranFlightBonusCap, flights * TraitGrowthVeteranFlightBonusPerFlight);
            float hourBonus    = Mathf.Min(TraitGrowthVeteranHourBonusCap, (float)(Math.Floor(totalHours / 10.0) * TraitGrowthVeteranHourBonusPerTenHours));
            float serviceBonus = Mathf.Min(TraitGrowthVeteranTotalBonusCap, flightBonus + hourBonus);
            float courageChance   = Mathf.Clamp01((TraitGrowthVeteranBaseChance + serviceBonus) * Mathf.Clamp01(1f - k.courage));
            float stupidityChance = Mathf.Clamp01((TraitGrowthVeteranBaseChance + serviceBonus) * Mathf.Clamp01(k.stupidity));

            ApplyTraitGrowthRolls(
                k, courageChance, stupidityChance,
                TraitGrowthVeteranDelta, TraitGrowthVeteranDelta,
                "Veteran service",
                $"[EAC] Trait growth (veteran) for {k.name}: EACFlights={eacFlights}, " +
                $"FTFlights={(ftFlightsAvailable ? ftFlights.ToString() : "unavailable:" + (ftFlightsReason ?? "unknown"))}, " +
                $"FTHours={(ftHoursAvailable ? ftHours.ToString("F2") : "unavailable:" + (ftHoursReason ?? "unknown"))}, " +
                $"CurrentMissionHours={currentMissionHours:F2}, TotalHours={totalHours:F2}, " +
                $"HoursSource={(ftHoursAvailable ? "FlightTracker" : "CurrentMission")}, " +
                $"CourageChance={courageChance:P1}, StupidityChance={stupidityChance:P1}",
                nowUT);
        }

        public static void TryApplyTrainingTraitGrowth(
            ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int targetLevel, double nowUT)
        {
            if (!RosterRotationState.TraitGrowthEnabled) return;
            if (k == null || rec == null) return;
            if (targetLevel < 1 || targetLevel > 3) return;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return;

            float baseChance, delta;
            switch (targetLevel)
            {
                case 1:  baseChance = 0.15f; delta = 0.010f; break;
                case 2:  baseChance = 0.25f; delta = 0.015f; break;
                default: baseChance = 0.35f; delta = 0.020f; break;
            }

            float courageChance   = Mathf.Clamp01(baseChance * Mathf.Clamp01(1f - k.courage));
            float stupidityChance = Mathf.Clamp01(baseChance * Mathf.Clamp01(k.stupidity));

            ApplyTraitGrowthRolls(
                k, courageChance, stupidityChance, delta, delta,
                $"Level {targetLevel} training",
                $"[EAC] Trait growth (training) for {k.name}: TargetLevel={targetLevel}, " +
                $"CourageChance={courageChance:P1}, StupidityChance={stupidityChance:P1}",
                nowUT);
        }

        // ── Implementation ─────────────────────────────────────────────────────

        private static void ApplyTraitGrowthRolls(
            ProtoCrewMember k,
            float courageChance, float stupidityChance,
            float courageDelta,  float stupidityDelta,
            string sourceLabel,  string verbosePrefix,
            double nowUT)
        {
            if (k == null) return;

            float oldCourage   = Mathf.Clamp01(k.courage);
            float oldStupidity = Mathf.Clamp01(k.stupidity);
            float courageRoll   = UnityEngine.Random.value;
            float stupidityRoll = UnityEngine.Random.value;

            bool courageSuccess   = courageRoll   < courageChance   && oldCourage   < TraitGrowthCourageCap;
            bool stupiditySuccess = stupidityRoll < stupidityChance && oldStupidity > TraitGrowthStupidityFloor;

            float newCourage   = courageSuccess   ? Mathf.Min(TraitGrowthCourageCap,     oldCourage   + courageDelta)   : oldCourage;
            float newStupidity = stupiditySuccess ? Mathf.Max(TraitGrowthStupidityFloor, oldStupidity - stupidityDelta) : oldStupidity;

            if (RRLog.VerboseEnabled)
            {
                RRLog.Verbose(verbosePrefix +
                    $", CourageRoll={courageRoll:F3}, StupidityRoll={stupidityRoll:F3}" +
                    $", CourageSuccess={courageSuccess}, StupiditySuccess={stupiditySuccess}" +
                    $", OldCourage={oldCourage:P0}, NewCourage={newCourage:P0}" +
                    $", OldStupidity={oldStupidity:P0}, NewStupidity={newStupidity:P0}");
            }

            if (!courageSuccess && !stupiditySuccess) return;

            try { k.courage   = newCourage;   } catch (Exception ex) { RRLog.VerboseExceptionOnce("TraitGrowth.Courage",   "Suppressed", ex); }
            try { k.stupidity = newStupidity; } catch (Exception ex) { RRLog.VerboseExceptionOnce("TraitGrowth.Stupidity", "Suppressed", ex); }

            var changes = new List<string>();
            if (courageSuccess)   changes.Add($"Courage {oldCourage:P0} to {newCourage:P0}");
            if (stupiditySuccess) changes.Add($"Stupidity {oldStupidity:P0} to {newStupidity:P0}");

            RosterRotationState.PostNotification(
                EACNotificationType.Training,
                $"Trait Growth — {k.name}",
                $"{k.name} showed growth from {sourceLabel}: {string.Join(", ", changes.ToArray())}. ({RosterRotationState.FormatGameDate(nowUT)})",
                MessageSystemButton.MessageButtonColor.GREEN,
                MessageSystemButton.ButtonIcons.COMPLETE, 6f);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RosterRotation
{
    /// <summary>
    /// Generates replacement new-save starting crews using EAC's configured gender
    /// and class filters. Dialog ownership remains in EACStartingCrewSetupController.
    /// </summary>
    internal static class EACStartingCrewService
    {
        internal static bool GenerateReplacementStartingCrew(int count)
        {
            if (EACVeteranService.IsDelegatedToEarnYourStripes) return false;
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            count = Math.Max(1, Math.Min(10, count));
            var existing = roster.Crew.Where(k => k != null).ToList();
            foreach (var k in existing)
            {
                try { roster.Remove(k); }
                catch (Exception ex) { RRLog.Warn("[EAC] Could not remove starting Kerbal " + k.name + ": " + ex.Message); }
            }

            int generated = 0;
            foreach (string requiredTrait in GetRequiredStartingCrewTraits(count))
            {
                if (generated >= count) break;
                if (GenerateStartingKerbal(roster, requiredTrait) != null)
                    generated++;
            }

            int guard = 0;
            while (generated < count && guard++ < 300)
            {
                if (GenerateStartingKerbal(roster, null) != null)
                    generated++;
            }

            if (generated < count)
                RRLog.Warn("[EAC] Generated only " + generated.ToString(CultureInfo.InvariantCulture)
                    + " of " + count.ToString(CultureInfo.InvariantCulture)
                    + " requested starting Kerbals. Check gender/class filters.");

            SaveScheduler.RequestSave("EAC replacement starting crew");
            return generated > 0;
        }

        private static IEnumerable<string> GetRequiredStartingCrewTraits(int count)
        {
            var traits = new List<string>();
            if (RosterRotationState.EACStartingCrewAllowPilots) traits.Add("Pilot");
            if (RosterRotationState.EACStartingCrewAllowEngineers) traits.Add("Engineer");
            if (RosterRotationState.EACStartingCrewAllowScientists) traits.Add("Scientist");

            // If the user selects enough crew slots for every selected class,
            // guarantee at least one Kerbal from each selected class. This gives
            // Pilot/Engineer/Scientist when all classes are enabled and count >= 3.
            if (count >= traits.Count)
                return traits;

            return Enumerable.Empty<string>();
        }

        private static ProtoCrewMember GenerateStartingKerbal(KerbalRoster roster, string forcedTrait)
        {
            if (roster == null) return null;

            forcedTrait = NormalizeStartingCrewTrait(forcedTrait);

            for (int attempts = 0; attempts < 300; attempts++)
            {
                ProtoCrewMember pcm = null;
                try { pcm = roster.GetNewKerbal(); }
                catch (Exception ex)
                {
                    RRLog.Warn("[EAC] Could not generate starting Kerbal: " + ex.Message);
                    return null;
                }

                if (pcm == null) continue;

                if (!IsAllowedStartingCrewGender(pcm.gender))
                {
                    try { roster.Remove(pcm); } catch { /* ignore rejected generated crew */ }
                    continue;
                }

                if (!string.IsNullOrEmpty(forcedTrait))
                {
                    // Try to force the class first, but do not trust reflection alone.
                    // In some KSP builds/mod stacks the public trait string can be
                    // backed by another member. If the visible trait does not become
                    // the required class, reject this generated Kerbal and keep trying
                    // until the required Pilot/Engineer/Scientist slot is truly filled.
                    EACKerbalMemberAccess.TryWriteString(pcm, forcedTrait, "trait", "Trait");
                    if (!TraitEquals(pcm.trait, forcedTrait))
                    {
                        try { roster.Remove(pcm); } catch { /* ignore rejected generated crew */ }
                        continue;
                    }
                }

                if (!IsAllowedStartingCrewClass(pcm.trait))
                {
                    try { roster.Remove(pcm); } catch { /* ignore rejected generated crew */ }
                    continue;
                }

                EACKerbalMemberAccess.WriteBool(pcm, false, "veteran", "Veteran", "isVeteran", "IsVeteran");
                EACKerbalMemberAccess.WriteBool(pcm, false, "isBadass", "IsBadass", "badass", "Badass");
                EACSuitService.TryApplySuit(pcm, RosterRotationState.EACDefaultSuit);
                RosterRotationState.ReplaceWithFreshKerbalRecord(pcm.name, pcm);
                RRLog.Info("[EAC] Generated starting Kerbal: " + pcm.name + " (" + pcm.trait + ")");
                return pcm;
            }

            if (!string.IsNullOrEmpty(forcedTrait))
                RRLog.Warn("[EAC] Could not generate required starting crew trait: " + forcedTrait);

            return null;
        }

        private static string NormalizeStartingCrewTrait(string trait)
        {
            if (string.IsNullOrEmpty(trait)) return null;
            if (TraitEquals(trait, "Pilot")) return "Pilot";
            if (TraitEquals(trait, "Engineer")) return "Engineer";
            if (TraitEquals(trait, "Scientist")) return "Scientist";
            return trait;
        }

        private static bool TraitEquals(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllowedStartingCrewClass(string trait)
        {
            if (string.IsNullOrEmpty(trait)) return true;
            if (string.Equals(trait, "Pilot", StringComparison.OrdinalIgnoreCase)) return RosterRotationState.EACStartingCrewAllowPilots;
            if (string.Equals(trait, "Engineer", StringComparison.OrdinalIgnoreCase)) return RosterRotationState.EACStartingCrewAllowEngineers;
            if (string.Equals(trait, "Scientist", StringComparison.OrdinalIgnoreCase)) return RosterRotationState.EACStartingCrewAllowScientists;
            return false;
        }

        private static bool IsAllowedStartingCrewGender(ProtoCrewMember.Gender gender)
        {
            if (gender == ProtoCrewMember.Gender.Female) return RosterRotationState.EACStartingCrewAllowFemales;
            if (gender == ProtoCrewMember.Gender.Male) return RosterRotationState.EACStartingCrewAllowMales;
            return true;
        }
    }
}

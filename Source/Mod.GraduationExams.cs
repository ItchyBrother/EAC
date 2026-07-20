// EAC - Enhanced Astronaut Complex - Mod.GraduationExams
// Optional Contract Configurator final exam support for level training.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens;
using Contracts;
using Contracts.Parameters;

namespace RosterRotation
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class EACGraduationExamWatcher : MonoBehaviour
    {
        private static bool _subscribed;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Subscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private static void Subscribe()
        {
            if (_subscribed) return;

            // Do not subscribe directly to GameEvents.Contract.* here.  In some KSP/CC
            // builds these contract events are not safe to hook at Startup.Instantly and
            // can throw during AddonLoader.  The optional EAC_CCBridge behaviour is
            // attached to every EAC final exam contract and forwards offer/accept/
            // abandon/complete lifecycle callbacks to EAC, which is the path we need.
            RRLog.Info("[EAC] Graduation exam watcher active; Contract Configurator lifecycle callbacks are provided by EAC_CCBridge.");
            _subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
        }
    }

    public partial class RosterRotationKSCUI
    {
        // Called by the optional EAC_CCBridge assembly so Contract Configurator
        // diagnostics obey the in-game EAC General -> Debug -> Verbose UI logs toggle.
        public static void LogContractConfiguratorVerbose(string message)
        {
            RRLog.Verbose(message);
        }

        public static void LogContractConfiguratorWarning(string message)
        {
            RRLog.VerboseWarn(message);
        }

        private static readonly HashSet<string> _completedGraduationContractsProcessed = new HashSet<string>();
        private static readonly HashSet<string> _graduationAssetProvisionMessagesLogged = new HashSet<string>();

        private static bool HasGraduationExam(RosterRotationState.KerbalRecord rec)
        {
            return rec != null
                   && (rec.GraduationExamPending || rec.GraduationExamActive)
                   && rec.GraduationExamTargetLevel >= 1
                   && rec.GraduationExamTargetLevel <= 3;
        }

        private static bool CanOfferGraduationExam(ProtoCrewMember k, RosterRotationState.KerbalRecord rec)
        {
            if (k == null || rec == null) return false;
            if (!RosterRotationState.FinalExamContractsEnabled) return false;
            if (!EACContractConfiguratorBridge.IsAvailable) return false;
            if (!rec.GraduationExamPending) return false;
            if (rec.GraduationExamActive &&
                EACContractConfiguratorBridge.HasCurrentGraduationContract(rec.GraduationExamContractGuid, rec.GraduationExamContractType))
                return false;
            if (rec.GraduationExamTargetLevel < 1 || rec.GraduationExamTargetLevel > 3) return false;
            if (rec.Retired || rec.DeathUT > 0) return false;
            if (k.type != ProtoCrewMember.KerbalType.Crew) return false;
            if (k.inactive) return false;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) return false;
            if (k.rosterStatus == ProtoCrewMember.RosterStatus.Dead ||
                k.rosterStatus == ProtoCrewMember.RosterStatus.Missing) return false;
            return true;
        }

        private static bool ShouldRequireGraduationExam(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int targetLevel)
        {
            if (!RosterRotationState.FinalExamContractsEnabled) return false;
            if (k == null || rec == null) return false;
            if (targetLevel < 1 || targetLevel > 3) return false;

            // Final exams are deliberately a soft optional dependency. If CC is missing,
            // the original EAC training flow continues and the Kerbal is awarded normally.
            return EACContractConfiguratorBridge.IsAvailable;
        }

        private static void MarkGraduationExamPending(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int targetLevel, double nowUT)
        {
            if (k == null || rec == null) return;

            RosterRotationState.EnsureKerbalIdentity(rec);
            rec.GraduationExamPending = true;
            rec.GraduationExamActive = false;
            rec.GraduationExamTargetLevel = targetLevel;
            rec.GraduationExamContractGuid = "";
            rec.GraduationExamContractType = EACContractConfiguratorBridge.GetContractTypeName(k, targetLevel);
            rec.GraduationExamId = "";
            rec.GraduationExamReadyUT = nowUT;
            rec.TrainingEndUT = nowUT;

            RRLog.Verbose("[EAC] Graduation exam pending: kerbal=" + k.name +
                       " trait=" + k.trait +
                       " targetLevel=" + targetLevel +
                       " contractType=" + rec.GraduationExamContractType +
                       " readyUT=" + nowUT.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                       " cc=" + EACContractConfiguratorBridge.AvailabilitySummary);

            if (k.inactive && nowUT >= k.inactiveTimeEnd)
                k.inactive = false;

            RosterRotationState.PostNotification(
                EACNotificationType.Training,
                $"Final Exam Ready - {k.name}",
                $"{k.name} has completed Level {targetLevel} training. Offer the final exam contract to graduate. ({RosterRotationState.FormatGameDate(nowUT)})",
                MessageSystemButton.MessageButtonColor.ORANGE,
                MessageSystemButton.ButtonIcons.ALERT,
                8f);
        }

        private static void GrantTrainingLevel(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int targetLevel, double nowUT)
        {
            if (k == null || rec == null) return;
            if (targetLevel < 1 || targetLevel > 3) return;

            RRLog.Verbose("[EAC] Granting training level without CC final exam: kerbal=" + k.name +
                       " targetLevel=" + targetLevel +
                       " previousStockLevel=" + ((int)k.experienceLevel) +
                       " previousGranted=" + rec.GrantedLevel);

            try { k.experienceLevel = targetLevel; }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("Mod.GraduationExams.Grant.SetLevel", "Suppressed", ex); }

            RosterRotationState.NoteCertifiedLevel(rec, targetLevel);

            try { k.careerLog.AddEntry("Training" + targetLevel, "Kerbin"); }
            catch (Exception ex) { RRLog.VerboseExceptionOnce("Mod.GraduationExams.Grant.CareerLog", "Suppressed", ex); }

            if (RosterRotationState.AgingEnabled && rec.NaturalRetirementUT > 0)
            {
                rec.RetirementDelayYears += targetLevel;
                rec.RetirementWarned = false;
            }

            TryApplyTrainingTraitGrowth(k, rec, targetLevel, nowUT);

            RosterRotationState.PostNotification(
                EACNotificationType.Training,
                $"Training Complete - {k.name}",
                $"{k.name} has completed Level {targetLevel} training and is ready for duty. ({RosterRotationState.FormatGameDate(nowUT)})",
                MessageSystemButton.MessageButtonColor.GREEN,
                MessageSystemButton.ButtonIcons.COMPLETE,
                6f);
        }

        private static bool GraduationExamAwardAlreadyVisible(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int targetLevel)
        {
            if (targetLevel < 1 || targetLevel > 3) return false;
            if (k != null && (int)k.experienceLevel >= targetLevel) return true;
            if (rec != null && rec.GrantedLevel >= targetLevel) return true;
            return false;
        }

        private static bool CompleteGraduationExamFromObservedLevel(ProtoCrewMember k, RosterRotationState.KerbalRecord rec, int targetLevel, double nowUT)
        {
            if (k == null || rec == null) return false;
            if (targetLevel < 1 || targetLevel > 3) return false;

            int observedStockLevel = k != null ? (int)k.experienceLevel : -1;
            RRLog.Verbose("[EAC] Graduation reconciliation check: kerbal=" + k.name +
                       " targetLevel=" + targetLevel +
                       " stockLevel=" + observedStockLevel +
                       " grantedLevel=" + rec.GrantedLevel +
                       " pending=" + rec.GraduationExamPending +
                       " active=" + rec.GraduationExamActive +
                       " contractGuid=" + (rec.GraduationExamContractGuid ?? "") +
                       " contractType=" + (rec.GraduationExamContractType ?? ""));

            if (!GraduationExamAwardAlreadyVisible(k, rec, targetLevel))
            {
                RRLog.Verbose("[EAC] Graduation reconciliation deferred: CC contract completed but stock XP/level is not visible yet for " +
                           k.name + " targetLevel=" + targetLevel +
                           " stockLevel=" + observedStockLevel +
                           " grantedLevel=" + rec.GrantedLevel +
                           ". This is expected when Contract Configurator has not refreshed the roster yet; EAC will retry later and will not grant the level itself.");
                return false;
            }

            int previousGranted = rec.GrantedLevel;
            RosterRotationState.NoteCertifiedLevel(rec, targetLevel);

            if (k.inactive && nowUT >= k.inactiveTimeEnd)
                k.inactive = false;

            // CC awarded the stock XP/level.  EAC only reconciles its own state and
            // applies EAC side effects that used to happen at graduation time.
            if (previousGranted < targetLevel)
            {
                if (RosterRotationState.AgingEnabled && rec.NaturalRetirementUT > 0)
                {
                    rec.RetirementDelayYears += targetLevel;
                    rec.RetirementWarned = false;
                }

                TryApplyTrainingTraitGrowth(k, rec, targetLevel, nowUT);
            }

            RRLog.Verbose("[EAC] Graduation reconciliation succeeded: kerbal=" + k.name +
                       " targetLevel=" + targetLevel +
                       " stockLevel=" + ((int)k.experienceLevel) +
                       " previousGranted=" + previousGranted +
                       " newGranted=" + rec.GrantedLevel +
                       " traitGrowthApplied=" + (previousGranted < targetLevel));

            RecordGraduationExamUsed(EACContractConfiguratorBridge.NormalizeTraitForContract(k.trait), targetLevel,
                !string.IsNullOrEmpty(rec.GraduationExamId) ? rec.GraduationExamId : rec.GraduationExamContractType);

            ClearGraduationExamState(rec);

            RosterRotationState.PostNotification(
                EACNotificationType.Training,
                $"Final Exam Complete - {k.name}",
                $"{k.name} has passed the final exam and graduated to Level {targetLevel}. ({RosterRotationState.FormatGameDate(nowUT)})",
                MessageSystemButton.MessageButtonColor.GREEN,
                MessageSystemButton.ButtonIcons.COMPLETE,
                6f);

            return true;
        }

        private static void ClearGraduationExamState(RosterRotationState.KerbalRecord rec)
        {
            if (rec == null) return;
            RRLog.Verbose("[EAC] Clearing graduation exam state: targetLevel=" + rec.GraduationExamTargetLevel +
                       " pending=" + rec.GraduationExamPending +
                       " active=" + rec.GraduationExamActive +
                       " contractGuid=" + (rec.GraduationExamContractGuid ?? "") +
                       " contractType=" + (rec.GraduationExamContractType ?? ""));
            rec.GraduationExamPending = false;
            rec.GraduationExamActive = false;
            rec.GraduationExamTargetLevel = 0;
            rec.GraduationExamContractGuid = "";
            rec.GraduationExamContractType = "";
            rec.GraduationExamId = "";
            rec.GraduationExamReadyUT = 0;

            string cleanupMessage;
            if (EACContractConfiguratorBridge.TryCleanupGraduationExamAssetsIfUnused(out cleanupMessage) && !string.IsNullOrEmpty(cleanupMessage))
                RRLog.Verbose("[EAC] " + cleanupMessage);
        }


        private bool ResolveUnavailableGraduationExams()
        {
            // If final exams are disabled in EAC settings, or Contract Configurator is
            // no longer available, any saved pending/active exam records must be
            // migrated back to the normal EAC training-award path. Otherwise those
            // Kerbals stay hidden from the training candidate list by HasGraduationExam().
            bool graduationExamsUsable = RosterRotationState.FinalExamContractsEnabled &&
                                         EACContractConfiguratorBridge.IsAvailable;
            if (graduationExamsUsable)
                return false;

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            bool anyResolved = false;
            double nowUT = Planetarium.GetUniversalTime();
            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (!HasGraduationExam(rec)) continue;
                if (rec.GraduationExamTargetLevel < 1 || rec.GraduationExamTargetLevel > 3) continue;

                if (!anyResolved)
                {
                    string reason = !RosterRotationState.FinalExamContractsEnabled
                        ? "final exam contracts are disabled in EAC settings"
                        : "Contract Configurator final exams are not available: " + EACContractConfiguratorBridge.AvailabilitySummary;
                    RRLog.VerboseWarn("[EAC] Resolving saved graduation exam state because " + reason +
                               ". Pending/active final exams will be completed through the legacy EAC training award path.");
                }

                GrantTrainingLevel(k, rec, rec.GraduationExamTargetLevel, nowUT);
                ClearGraduationExamState(rec);
                anyResolved = true;
            }

            if (anyResolved)
            {
                InvalidateUICaches();
                SaveScheduler.RequestSave("final exams resolved by legacy training path");
            }

            return anyResolved;
        }

        private bool OfferGraduationExamContract(ProtoCrewMember k, RosterRotationState.KerbalRecord rec)
        {
            if (!CanOfferGraduationExam(k, rec))
            {
                ScreenMessages.PostScreenMessage("Final exam contract is not available for this Kerbal.", 4f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }

            int targetLevel = rec.GraduationExamTargetLevel;
            string selectedContractType = EACContractConfiguratorBridge.SelectContractTypeName(k, targetLevel);
            string selectedExamId = EACContractConfiguratorBridge.GetExamIdForContractType(selectedContractType);
            RRLog.Verbose("[EAC] Graduation exam offer requested: kerbal=" + k.name +
                       " trait=" + k.trait +
                       " targetLevel=" + targetLevel +
                       " selectedContractType=" + selectedContractType +
                       " examId=" + selectedExamId +
                       " cc=" + EACContractConfiguratorBridge.AvailabilitySummary);
            rec.GraduationExamPending = true;
            rec.GraduationExamActive = false;
            rec.GraduationExamContractGuid = EACContractConfiguratorBridge.RequestedOfferMarker;
            rec.GraduationExamContractType = selectedContractType;
            rec.GraduationExamId = selectedExamId;

            string contractGuid;
            string failureReason;
            if (!EACContractConfiguratorBridge.TryStartGraduationExam(k, targetLevel, selectedContractType, out contractGuid, out failureReason))
            {
                rec.GraduationExamContractGuid = "";
                ScreenMessages.PostScreenMessage("Could not offer final exam: " + failureReason, 6f, ScreenMessageStyle.UPPER_CENTER);
                RRLog.VerboseWarn("[EAC] Could not offer final exam contract for " + k.name + ": " + failureReason);
                return false;
            }

            // The contract is now in Contract Configurator's pending Mission Control list.
            // Mark it active/offered in EAC so we do not create duplicate offers before the
            // player accepts it from Mission Control.
            rec.GraduationExamActive = true;
            rec.GraduationExamContractGuid = string.IsNullOrEmpty(contractGuid) ? EACContractConfiguratorBridge.RequestedOfferMarker : contractGuid;
            rec.GraduationExamId = selectedExamId;

            RRLog.Verbose("[EAC] Graduation exam offer stored: kerbal=" + k.name +
                       " targetLevel=" + targetLevel +
                       " contractGuid=" + rec.GraduationExamContractGuid +
                       " contractType=" + rec.GraduationExamContractType +
                       " examId=" + rec.GraduationExamId +
                       " pending=" + rec.GraduationExamPending +
                       " active=" + rec.GraduationExamActive);

            ScreenMessages.PostScreenMessage($"Final exam offered for {k.name}. Open Mission Control to accept it.", 6f, ScreenMessageStyle.UPPER_CENTER);
            InvalidateUICaches();
            ACPatches.ForceRefresh();
            SaveScheduler.RequestSave("final exam offered");
            return true;
        }



        internal static void ProvisionActiveGraduationExamAssets()
        {
            if (HighLogic.CurrentGame == null || !RosterRotationState.FinalExamContractsEnabled)
                return;
            if (!EACContractConfiguratorBridge.IsAvailable)
                return;

            var roster = HighLogic.CurrentGame.CrewRoster;
            if (roster == null) return;

            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (!HasGraduationExam(rec) || !rec.GraduationExamActive) continue;

                string assetMessage;
                bool ok = EACContractConfiguratorBridge.TryProvisionGraduationExamAssets(k, rec.GraduationExamTargetLevel, out assetMessage);
                LogGraduationAssetProvisionMessageOnce(k.name, ok, assetMessage);
            }
        }

        private static void LogGraduationAssetProvisionMessageOnce(string kerbalName, bool success, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            string key = (success ? "ok:" : "fail:") + (kerbalName ?? "") + ":" + message;
            if (_graduationAssetProvisionMessagesLogged.Contains(key)) return;
            _graduationAssetProvisionMessagesLogged.Add(key);

            if (success) RRLog.Verbose("[EAC] " + message);
            else RRLog.VerboseWarn("[EAC] " + message);
        }

        private bool DrawGraduationExamRows(KerbalRoster roster, double now, bool compact)
        {
            if (!RosterRotationState.FinalExamContractsEnabled || roster == null)
                return false;

            GUILayout.Label("Final Exams");
            if (!EACContractConfiguratorBridge.IsAvailable)
            {
                GUILayout.Label("  Contract Configurator not detected; EAC will award completed training normally.");
                return false;
            }

            bool any = false;
            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (HasGraduationExam(rec) && GraduationExamAwardAlreadyVisible(k, rec, rec.GraduationExamTargetLevel))
                {
                    TryReconcileGraduationExamAwardForKerbal(k);
                    if (!RosterRotationState.Records.TryGetValue(k.name, out rec)) continue;
                }
                if (!HasGraduationExam(rec)) continue;
                if (rec.DeathUT > 0 || rec.Retired) continue;

                any = true;
                bool activeContractExists = rec.GraduationExamActive &&
                    EACContractConfiguratorBridge.HasCurrentGraduationContract(rec.GraduationExamContractGuid, rec.GraduationExamContractType);
                if (activeContractExists)
                {
                    string assetMessage;
                    bool provisioned = EACContractConfiguratorBridge.TryProvisionGraduationExamAssets(k, rec.GraduationExamTargetLevel, out assetMessage);
                    LogGraduationAssetProvisionMessageOnce(k.name, provisioned, assetMessage);
                }
                string state = activeContractExists ? "Contract offered/active" : (rec.GraduationExamActive ? "Offer missing" : "Ready");
                string level = rec.GraduationExamTargetLevel > 0 ? "L" + rec.GraduationExamTargetLevel : "L?";

                GUILayout.BeginHorizontal();
                GUILayout.Label(k.name, GUILayout.Width(compact ? 180 : 150));
                GUILayout.Label(k.trait, GUILayout.Width(90));
                GUILayout.Label(level, GUILayout.Width(45));
                GUILayout.Label(state, GUILayout.Width(compact ? 130 : 150));
                GUILayout.FlexibleSpace();

                bool canOffer = CanOfferGraduationExam(k, rec);
                string examLabel = activeContractExists
                    ? EACContractConfiguratorBridge.GetShortExamDisplayName(rec.GraduationExamContractType)
                    : "Auto";
                GUILayout.Label("Exam: " + examLabel, GUILayout.Width(compact ? 150 : 190));

                GUI.enabled = canOffer;
                string btn = activeContractExists ? "Contract Listed" : (rec.GraduationExamActive ? "Retry Offer" : "Offer Final Exam");
                if (GUILayout.Button(btn, GUILayout.Width(compact ? 130 : 150)) && canOffer)
                    OfferGraduationExamContract(k, rec);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            if (!any) GUILayout.Label("  None.");
            return any;
        }

        public static bool TryPrepareGraduationExamContractFromCC(Contract contract, string trait, int targetLevel)
        {
            return TryPrepareGraduationExamContractFromCC(contract, trait, targetLevel, "", true);
        }

        public static bool TryPrepareGraduationExamContractFromCC(Contract contract, string trait, int targetLevel, string examId, bool excludeRecentlyUsed)
        {
            // Contract Configurator can evaluate REQUIREMENT nodes more than once: once
            // while generating the offered contract, and again later while maintaining
            // the pending/offered list used by Mission Control.  The first pass should
            // consume EAC's explicit "requested offer" marker.  Later passes must still
            // return true for the already-generated personalized contract, otherwise CC
            // quietly removes/hides the pending contract before the player can see it.
            if (contract != null)
            {
                string existingKerbal = EACContractConfiguratorBridge.GetUniqueDataString(contract, "eacKerbal");
                string existingKerbalIdentity = EACContractConfiguratorBridge.GetUniqueDataString(contract, "eacKerbalIdentity");
                int existingLevel = EACContractConfiguratorBridge.GetUniqueDataInt(contract, "eacTargetLevel");
                if (!string.IsNullOrEmpty(existingKerbal) && ExistingGraduationExamContractStillValid(existingKerbal, existingKerbalIdentity, existingLevel, trait, targetLevel))
                    return true;
            }

            ProtoCrewMember kerbal;
            RosterRotationState.KerbalRecord rec;
            int resolvedLevel;
            if (!TryFindPendingGraduationExamForOffer(trait, targetLevel, examId, excludeRecentlyUsed, out kerbal, out rec, out resolvedLevel))
                return false;

            if (contract != null)
            {
                string actualContractType;
                EACContractConfiguratorBridge.IsEacGraduationContract(contract, out actualContractType);
                string expectedType = !string.IsNullOrEmpty(actualContractType)
                    ? actualContractType
                    : (!string.IsNullOrEmpty(rec.GraduationExamContractType)
                        ? rec.GraduationExamContractType
                        : EACContractConfiguratorBridge.GetContractTypeName(kerbal, resolvedLevel));
                EACContractConfiguratorBridge.TrySetUniqueDataForContract(contract, "eacKerbal", kerbal.name);
                EACContractConfiguratorBridge.TrySetUniqueDataForContract(contract, "eacKerbalIdentity", RosterRotationState.EnsureKerbalIdentity(rec));
                EACContractConfiguratorBridge.TrySetUniqueDataForContract(contract, "eacTrait", EACContractConfiguratorBridge.NormalizeTraitForContract(kerbal.trait));
                EACContractConfiguratorBridge.TrySetUniqueDataForContract(contract, "eacTargetLevel", resolvedLevel);
                string resolvedExamId = EACContractConfiguratorBridge.NormalizeExamId(!string.IsNullOrEmpty(examId) ? examId : expectedType);
                EACContractConfiguratorBridge.TrySetUniqueDataForContract(contract, "eacExamId", resolvedExamId);
                EACContractConfiguratorBridge.TryApplyPersonalizedText(contract, kerbal, resolvedLevel);
                rec.GraduationExamContractType = expectedType;
                rec.GraduationExamId = resolvedExamId;
            }

            return true;
        }

        private static bool ExistingGraduationExamContractStillValid(string kerbalName, string kerbalIdentityKey, int contractLevel, string requiredTrait, int requiredLevel)
        {
            if (string.IsNullOrEmpty(kerbalName)) return false;

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            string normalizedRequiredTrait = EACContractConfiguratorBridge.NormalizeTraitForContract(requiredTrait);
            foreach (var k in roster.Crew)
            {
                if (k == null || !string.Equals(k.name, kerbalName, StringComparison.Ordinal)) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) return false;
                if (!RosterRotationState.KerbalIdentityMatches(rec, kerbalIdentityKey)) return false;
                if (!HasGraduationExam(rec)) return false;
                if (!rec.GraduationExamPending) return false;

                int level = contractLevel >= 1 && contractLevel <= 3 ? contractLevel : rec.GraduationExamTargetLevel;
                if (requiredLevel >= 1 && requiredLevel <= 3 && level != requiredLevel) return false;
                if (rec.GraduationExamTargetLevel >= 1 && rec.GraduationExamTargetLevel <= 3 && level != rec.GraduationExamTargetLevel) return false;

                string kerbalTrait = EACContractConfiguratorBridge.NormalizeTraitForContract(k.trait);
                if (!string.IsNullOrEmpty(normalizedRequiredTrait) &&
                    !string.Equals(normalizedRequiredTrait, kerbalTrait, StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            }

            return false;
        }


        public static bool TryReconcileGraduationExamAwardForKerbal(ProtoCrewMember k)
        {
            if (!RosterRotationState.FinalExamContractsEnabled)
                return false;
            if (k == null) return false;
            if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) return false;
            if (!HasGraduationExam(rec)) return false;

            int targetLevel = rec.GraduationExamTargetLevel;
            if (targetLevel < 1 || targetLevel > 3) return false;
            if (!GraduationExamAwardAlreadyVisible(k, rec, targetLevel)) return false;

            RRLog.Verbose("[EAC] Reconciled Contract Configurator final exam award for " + k.name +
                       " -> L" + targetLevel + " (observed stock level " + (int)k.experienceLevel +
                       ", EAC granted level " + rec.GrantedLevel + ")");

            bool reconciled = CompleteGraduationExamFromObservedLevel(k, rec, targetLevel, Planetarium.GetUniversalTime());
            if (reconciled)
            {
                RRLog.Verbose("[EAC] Graduation exam roster-scan reconciliation complete: kerbal=" + k.name +
                           " targetLevel=" + targetLevel);
                RefreshGraduationExamViews();
                SaveScheduler.RequestSave("final exam award reconciled");
            }
            else
            {
                RRLog.Verbose("[EAC] Graduation exam roster-scan reconciliation deferred: kerbal=" + k.name +
                           " targetLevel=" + targetLevel +
                           ". Stock XP/level is not visible yet; EAC will retry after the roster refreshes.");
            }
            return reconciled;
        }

        public static void TryReconcileGraduationExamAwards()
        {
            if (!RosterRotationState.FinalExamContractsEnabled)
                return;

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return;

            bool anyReconciled = false;
            double nowUT = Planetarium.GetUniversalTime();

            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var rec)) continue;
                if (!HasGraduationExam(rec)) continue;

                int targetLevel = rec.GraduationExamTargetLevel;
                if (targetLevel < 1 || targetLevel > 3) continue;
                if (!GraduationExamAwardAlreadyVisible(k, rec, targetLevel)) continue;

                RRLog.Verbose("[EAC] Reconciled Contract Configurator final exam award for " + k.name +
                           " -> L" + targetLevel + " (observed stock level " + (int)k.experienceLevel +
                           ", EAC granted level " + rec.GrantedLevel + ")");
                if (CompleteGraduationExamFromObservedLevel(k, rec, targetLevel, nowUT))
                    anyReconciled = true;
            }

            if (anyReconciled)
            {
                RefreshGraduationExamViews();
                SaveScheduler.RequestSave("final exam award reconciled");
            }
        }

        private static bool AnyGraduationExamAwaitingAward()
        {
            try
            {
                foreach (var entry in RosterRotationState.Records)
                {
                    if (HasGraduationExam(entry.Value))
                        return true;
                }
            }
            catch { }
            return false;
        }

        public static void TryRebindGraduationAwardToKerbal(Contract contract, string kerbalName)
        {
            if (contract == null || string.IsNullOrEmpty(kerbalName)) return;

            try
            {
                var roster = HighLogic.CurrentGame?.CrewRoster;
                if (roster == null) return;

                ProtoCrewMember kerbal = null;
                foreach (var k in roster.Crew)
                {
                    if (k != null && string.Equals(k.name, kerbalName, StringComparison.Ordinal))
                    {
                        kerbal = k;
                        break;
                    }
                }

                if (kerbal != null)
                {
                    string expectedIdentity = EACContractConfiguratorBridge.GetUniqueDataString(contract, "eacKerbalIdentity");
                    if (RosterRotationState.Records.TryGetValue(kerbal.name, out var rec) &&
                        !RosterRotationState.KerbalIdentityMatches(rec, expectedIdentity))
                    {
                        RRLog.VerboseWarn("[EAC] Not rebinding final exam award because the EAC Kerbal identity no longer matches: " + kerbal.name);
                        return;
                    }
                    EACContractConfiguratorBridge.TryBindGraduationContractToKerbal(contract, kerbal);
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.RebindAwardOnComplete", "Suppressed", ex);
            }
        }

        public static bool HandleGraduationContractCompletedForKerbal(string kerbalName, int targetLevel, string contractTypeName, string contractId)
        {
            return HandleGraduationContractCompletedForKerbal(kerbalName, targetLevel, contractTypeName, contractId, "", "");
        }

        public static bool HandleGraduationContractCompletedForKerbal(string kerbalName, int targetLevel, string contractTypeName, string contractId, string examId)
        {
            return HandleGraduationContractCompletedForKerbal(kerbalName, targetLevel, contractTypeName, contractId, examId, "");
        }

        public static bool HandleGraduationContractCompletedForKerbal(string kerbalName, int targetLevel, string contractTypeName, string contractId, string examId, string kerbalIdentityKey)
        {
            if (string.IsNullOrEmpty(kerbalName))
                return false;

            if (!string.IsNullOrEmpty(contractId) && _completedGraduationContractsProcessed.Contains(contractId))
                return true;

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null)
                return false;

            ProtoCrewMember kerbal = null;
            foreach (var k in roster.Crew)
            {
                if (k != null && string.Equals(k.name, kerbalName, StringComparison.Ordinal))
                {
                    kerbal = k;
                    break;
                }
            }

            if (kerbal == null)
            {
                RRLog.VerboseWarn("[EAC] Graduation exam completion named Kerbal not found in roster: " + kerbalName);
                return false;
            }

            RosterRotationState.KerbalRecord rec;
            if (!RosterRotationState.Records.TryGetValue(kerbal.name, out rec) || !HasGraduationExam(rec))
                return false;
            if (!RosterRotationState.KerbalIdentityMatches(rec, kerbalIdentityKey))
            {
                RRLog.VerboseWarn("[EAC] Ignoring completed final exam for " + kerbal.name + " because the EAC Kerbal identity does not match the pending exam record.");
                return false;
            }

            int resolvedLevel = targetLevel >= 1 && targetLevel <= 3 ? targetLevel : rec.GraduationExamTargetLevel;
            if (resolvedLevel < 1 || resolvedLevel > 3)
                return false;

            string normalizedContractType = EACContractConfiguratorBridge.NormalizeContractTypeName(contractTypeName);
            string resolvedExamId = EACContractConfiguratorBridge.NormalizeExamId(!string.IsNullOrEmpty(examId) ? examId : normalizedContractType);
            if (!string.IsNullOrEmpty(resolvedExamId)) rec.GraduationExamId = resolvedExamId;
            string expectedContractType = EACContractConfiguratorBridge.NormalizeContractTypeName(rec.GraduationExamContractType);
            if (!string.IsNullOrEmpty(normalizedContractType) && !string.IsNullOrEmpty(expectedContractType) &&
                !string.Equals(normalizedContractType, expectedContractType, StringComparison.Ordinal))
            {
                int parsedLevel = EACContractConfiguratorBridge.TryParseTargetLevel(normalizedContractType);
                string parsedTrait = EACContractConfiguratorBridge.TryParseTrait(normalizedContractType);
                string kerbalTrait = EACContractConfiguratorBridge.NormalizeTraitForContract(kerbal.trait);
                if ((parsedLevel >= 1 && parsedLevel <= 3 && parsedLevel != resolvedLevel) ||
                    (!string.IsNullOrEmpty(parsedTrait) && !string.Equals(parsedTrait, kerbalTrait, StringComparison.OrdinalIgnoreCase)))
                {
                    RRLog.VerboseWarn("[EAC] Ignoring completed final exam for " + kerbal.name + " because contract type '" + normalizedContractType + "' does not match pending EAC exam '" + expectedContractType + "'.");
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(contractId))
                _completedGraduationContractsProcessed.Add(contractId);

            RRLog.Verbose("[EAC] Graduation exam completion observed for " + kerbal.name + " -> L" + resolvedLevel +
                       (string.IsNullOrEmpty(normalizedContractType) ? "" : " (" + normalizedContractType + ")") +
                       "; waiting for Contract Configurator XP award if needed.");
            bool reconciled = CompleteGraduationExamFromObservedLevel(kerbal, rec, resolvedLevel, Planetarium.GetUniversalTime());
            if (reconciled)
            {
                RRLog.Verbose("[EAC] Graduation exam bridge-callback reconciliation complete: kerbal=" + kerbal.name +
                           " targetLevel=" + resolvedLevel +
                           " contractId=" + contractId +
                           " contractType=" + contractTypeName);
                RefreshGraduationExamViews();
                SaveScheduler.RequestSave("final exam award reconciled");
            }
            else
            {
                RRLog.Verbose("[EAC] Graduation exam bridge-callback reconciliation deferred after CC completion: kerbal=" + kerbal.name +
                           " targetLevel=" + resolvedLevel +
                           " contractId=" + contractId +
                           " contractType=" + contractTypeName +
                           ". Stock XP/level is not visible yet; EAC will retry after the roster refreshes.");
            }
            return reconciled;
        }

        public static void HandleGraduationContractOffered(Contract contract)
        {
            MarkGraduationContractOfferedOrAccepted(contract, false);
        }

        public static void HandleGraduationContractAccepted(Contract contract)
        {
            if (MarkGraduationContractOfferedOrAccepted(contract, true))
            {
                ProtoCrewMember kerbal;
                RosterRotationState.KerbalRecord rec;
                int targetLevel;
                string contractTypeName;
                if (EACContractConfiguratorBridge.IsEacGraduationContract(contract, out contractTypeName) &&
                    TryResolveGraduationExamRecord(contract, contractTypeName, out kerbal, out rec, out targetLevel))
                {
                    string assetMessage;
                    bool ok = EACContractConfiguratorBridge.TryProvisionGraduationExamAssets(kerbal, targetLevel, out assetMessage);
                    LogGraduationAssetProvisionMessageOnce(kerbal.name, ok, assetMessage);
                }
            }
        }

        private static bool MarkGraduationContractOfferedOrAccepted(Contract contract, bool accepted)
        {
            string contractTypeName;
            if (!EACContractConfiguratorBridge.IsEacGraduationContract(contract, out contractTypeName))
                return false;

            ProtoCrewMember kerbal;
            RosterRotationState.KerbalRecord rec;
            int targetLevel;
            if (!TryResolveGraduationExamRecord(contract, contractTypeName, out kerbal, out rec, out targetLevel))
            {
                RRLog.VerboseWarn("[EAC] Graduation exam " + (accepted ? "accepted" : "offered") + " but could not be matched to a pending Kerbal record: " + contractTypeName + " title='" + EACContractConfiguratorBridge.GetContractTitle(contract) + "'");
                return false;
            }

            rec.GraduationExamPending = true;
            rec.GraduationExamActive = true;
            rec.GraduationExamTargetLevel = targetLevel;
            rec.GraduationExamContractGuid = EACContractConfiguratorBridge.GetContractIdentifier(contract);
            rec.GraduationExamContractType = contractTypeName;
            string uniqueExamId = EACContractConfiguratorBridge.GetUniqueDataString(contract, "eacExamId");
            rec.GraduationExamId = EACContractConfiguratorBridge.NormalizeExamId(!string.IsNullOrEmpty(uniqueExamId) ? uniqueExamId : contractTypeName);
            EACContractConfiguratorBridge.TryApplyPersonalizedText(contract, kerbal, targetLevel);
            EACContractConfiguratorBridge.TryBindGraduationContractToKerbal(contract, kerbal);

            RRLog.Verbose("[EAC] Graduation exam " + (accepted ? "accepted" : "offered") + " for " + kerbal.name + " -> L" + targetLevel + " via " + contractTypeName + " examId=" + rec.GraduationExamId);
            RefreshGraduationExamViews();
            SaveScheduler.RequestSave(accepted ? "final exam accepted" : "final exam offered");
            return true;
        }

        private static bool TryFindPendingGraduationExamForOffer(string trait, int targetLevel, string examId, bool excludeRecentlyUsed,
            out ProtoCrewMember kerbal, out RosterRotationState.KerbalRecord rec, out int resolvedLevel)
        {
            kerbal = null;
            rec = null;
            resolvedLevel = 0;

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            string normalizedTrait = EACContractConfiguratorBridge.NormalizeTraitForContract(trait);
            int matches = 0;

            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var r)) continue;
                if (!HasGraduationExam(r)) continue;
                if (!r.GraduationExamPending) continue;
                if (targetLevel >= 1 && targetLevel <= 3 && r.GraduationExamTargetLevel != targetLevel) continue;

                string kerbalTrait = EACContractConfiguratorBridge.NormalizeTraitForContract(k.trait);
                if (!string.IsNullOrEmpty(normalizedTrait) &&
                    !string.Equals(normalizedTrait, kerbalTrait, StringComparison.OrdinalIgnoreCase))
                    continue;

                string candidateExamId = EACContractConfiguratorBridge.NormalizeExamId(!string.IsNullOrEmpty(examId) ? examId : EACContractConfiguratorBridge.GetContractTypeName(k, r.GraduationExamTargetLevel));
                if (!IsGraduationExamAllowedForOffer(kerbalTrait, r.GraduationExamTargetLevel, candidateExamId, excludeRecentlyUsed))
                    continue;

                string expectedType = !string.IsNullOrEmpty(r.GraduationExamContractType)
                    ? r.GraduationExamContractType
                    : EACContractConfiguratorBridge.GetContractTypeName(k, r.GraduationExamTargetLevel);
                if (!string.Equals(r.GraduationExamContractGuid, EACContractConfiguratorBridge.RequestedOfferMarker, StringComparison.Ordinal))
                    continue;
                if (r.GraduationExamActive && EACContractConfiguratorBridge.HasCurrentGraduationContract(r.GraduationExamContractGuid, expectedType))
                    continue;

                matches++;
                kerbal = k;
                rec = r;
                resolvedLevel = r.GraduationExamTargetLevel;

                if (matches > 1)
                {
                    RRLog.VerboseWarn("[EAC] More than one matching final exam offer was requested for " + normalizedTrait + " L" + targetLevel + "; Contract Configurator will wait until only one request is active.");
                    return false;
                }
            }

            return matches == 1 && resolvedLevel >= 1 && resolvedLevel <= 3;
        }

        public static void HandleGraduationContractCompleted(Contract contract)
        {
            ProcessCompletedGraduationExamContract(contract, true);
        }

        public static void HandleGraduationContractAbandonedFromBridge(Contract contract)
        {
            HandleGraduationContractAbandoned(contract);
        }

        private static bool ProcessCompletedGraduationExamContract(Contract contract, bool logUnmatched)
        {
            string contractTypeName;
            if (!EACContractConfiguratorBridge.IsEacGraduationContract(contract, out contractTypeName))
                return false;

            string completedContractId = EACContractConfiguratorBridge.GetContractIdentifier(contract);
            if (!string.IsNullOrEmpty(completedContractId) && _completedGraduationContractsProcessed.Contains(completedContractId))
            {
                RRLog.Verbose("[EAC] Graduation exam completion already processed: contractId=" + completedContractId +
                           " type=" + contractTypeName +
                           " title='" + EACContractConfiguratorBridge.GetContractTitle(contract) + "'");
                return true;
            }

            RRLog.Verbose("[EAC] Graduation exam contract completed by CC: contractId=" + completedContractId +
                       " type=" + contractTypeName +
                       " title='" + EACContractConfiguratorBridge.GetContractTitle(contract) + "'");

            ProtoCrewMember kerbal;
            RosterRotationState.KerbalRecord rec;
            int targetLevel;
            if (!TryResolveGraduationExamRecord(contract, contractTypeName, out kerbal, out rec, out targetLevel))
            {
                if (logUnmatched)
                    RRLog.VerboseWarn("[EAC] Completed graduation exam contract could not be matched to a pending EAC Kerbal record: " + contractTypeName + " title='" + EACContractConfiguratorBridge.GetContractTitle(contract) + "'");
                return false;
            }

            if (!string.IsNullOrEmpty(completedContractId))
                _completedGraduationContractsProcessed.Add(completedContractId);

            RRLog.Verbose("[EAC] Graduation exam completion observed for " + kerbal.name + " -> L" + targetLevel + " via " + contractTypeName +
                       "; reconciling against the stock level awarded by Contract Configurator.");
            bool reconciled = CompleteGraduationExamFromObservedLevel(kerbal, rec, targetLevel, Planetarium.GetUniversalTime());
            if (reconciled)
            {
                RRLog.Verbose("[EAC] Graduation exam reconciliation complete: kerbal=" + kerbal.name +
                           " targetLevel=" + targetLevel +
                           " contractId=" + completedContractId +
                           " contractType=" + contractTypeName);
                RefreshGraduationExamViews();
                SaveScheduler.RequestSave("final exam award reconciled");
            }
            else
            {
                RRLog.Verbose("[EAC] Graduation exam reconciliation deferred after CC completion: kerbal=" + kerbal.name +
                           " targetLevel=" + targetLevel +
                           " contractId=" + completedContractId +
                           " contractType=" + contractTypeName +
                           ". Stock XP/level is not visible yet; EAC will retry after the roster refreshes.");
            }
            return reconciled;
        }

        public static void HandleGraduationContractAbandoned(Contract contract)
        {
            string contractTypeName;
            if (!EACContractConfiguratorBridge.IsEacGraduationContract(contract, out contractTypeName))
                return;

            ProtoCrewMember kerbal;
            RosterRotationState.KerbalRecord rec;
            int targetLevel;
            if (!TryResolveGraduationExamRecord(contract, contractTypeName, out kerbal, out rec, out targetLevel))
                return;

            RRLog.Verbose("[EAC] Graduation exam abandoned/reset: kerbal=" + kerbal.name +
                       " targetLevel=" + targetLevel +
                       " contractType=" + contractTypeName +
                       " title='" + EACContractConfiguratorBridge.GetContractTitle(contract) + "'");

            string abandonedExamId = EACContractConfiguratorBridge.GetUniqueDataString(contract, "eacExamId");
            if (string.IsNullOrEmpty(abandonedExamId)) abandonedExamId = rec.GraduationExamId;
            if (string.IsNullOrEmpty(abandonedExamId)) abandonedExamId = contractTypeName;
            RecordGraduationExamUsed(EACContractConfiguratorBridge.NormalizeTraitForContract(kerbal.trait), targetLevel, abandonedExamId);

            rec.GraduationExamPending = true;
            rec.GraduationExamActive = false;
            rec.GraduationExamContractGuid = "";
            rec.GraduationExamContractType = contractTypeName;
            rec.GraduationExamId = "";
            rec.GraduationExamTargetLevel = targetLevel;

            RosterRotationState.PostNotification(
                EACNotificationType.Training,
                $"Final Exam Reset - {kerbal.name}",
                $"{kerbal.name}'s Level {targetLevel} final exam contract ended before completion. You may offer another exam contract.",
                MessageSystemButton.MessageButtonColor.ORANGE,
                MessageSystemButton.ButtonIcons.ALERT,
                6f);

            RefreshGraduationExamViews();
            SaveScheduler.RequestSave("final exam reset");
        }

        private static void RefreshGraduationExamViews()
        {
            try
            {
                RosterRotationKSCUI ui = RosterRotationKSCUI.Instance;
                if (ui != null)
                {
                    ui.InvalidateUICaches();

                    // KSP/CC can update the ProtoCrewMember level and then rebuild the
                    // Astronaut Complex / EAC training UI on a later frame.  Do one
                    // immediate refresh plus two delayed refreshes so newly-awarded
                    // exam levels show without requiring a scene reload.
                    try
                    {
                        ui.StartCoroutine(ui.RefreshGraduationExamViewsAfterDelay(0.5f));
                        ui.StartCoroutine(ui.RefreshGraduationExamViewsAfterDelay(2.0f));
                    }
                    catch (Exception ex)
                    {
                        RRLog.VerboseExceptionOnce("Mod.GraduationExams.RefreshViews.StartCoroutine", "Suppressed", ex);
                    }
                }
                ACPatches.ForceRefresh();
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("Mod.GraduationExams.RefreshViews", "Suppressed", ex);
            }
        }

        private IEnumerator RefreshGraduationExamViewsAfterDelay(float delaySeconds)
        {
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);

            try
            {
                InvalidateUICaches();
                ACPatches.ForceRefresh();
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("Mod.GraduationExams.RefreshViews.Delayed", "Suppressed", ex);
            }
        }

        private static bool TryResolveGraduationExamRecord(Contract contract, string contractTypeName,
            out ProtoCrewMember kerbal, out RosterRotationState.KerbalRecord rec, out int targetLevel)
        {
            kerbal = null;
            rec = null;
            targetLevel = 0;

            string contractId = EACContractConfiguratorBridge.GetContractIdentifier(contract);
            string uniqueKerbalName = EACContractConfiguratorBridge.GetUniqueDataString(contract, "eacKerbal");
            string uniqueKerbalIdentity = EACContractConfiguratorBridge.GetUniqueDataString(contract, "eacKerbalIdentity");
            int uniqueTargetLevel = EACContractConfiguratorBridge.GetUniqueDataInt(contract, "eacTargetLevel");
            int typeTargetLevel = EACContractConfiguratorBridge.TryParseTargetLevel(contractTypeName);
            string normalizedTypeName = EACContractConfiguratorBridge.NormalizeContractTypeName(contractTypeName);

            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            if (!string.IsNullOrEmpty(uniqueKerbalName) &&
                TryGetPendingGraduationExamForKerbal(roster, uniqueKerbalName, uniqueKerbalIdentity, uniqueTargetLevel, normalizedTypeName,
                    out kerbal, out rec, out targetLevel))
            {
                return true;
            }

            // Strongest save-safe match: the pending EAC record knows the CC contract type and target level.
            if (TryGetPendingGraduationExamByContractIdOrType(roster, contractId, normalizedTypeName, typeTargetLevel,
                out kerbal, out rec, out targetLevel))
            {
                return true;
            }

            // Last-resort fallback: if exactly one Kerbal has a pending/active exam matching this completed
            // contract's level/trait, award it. This covers CC contracts that completed correctly but lost
            // runtime uniqueData/GUID details during save/load or scene transitions.
            if (TryGetSolePendingGraduationExamForCompletedContract(roster, normalizedTypeName, typeTargetLevel,
                out kerbal, out rec, out targetLevel))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetPendingGraduationExamForKerbal(KerbalRoster roster, string kerbalName, string kerbalIdentityKey, int hintedLevel, string normalizedTypeName,
            out ProtoCrewMember kerbal, out RosterRotationState.KerbalRecord rec, out int targetLevel)
        {
            kerbal = null;
            rec = null;
            targetLevel = 0;
            if (roster == null || string.IsNullOrEmpty(kerbalName)) return false;

            foreach (var k in roster.Crew)
            {
                if (k == null || !string.Equals(k.name, kerbalName, StringComparison.Ordinal)) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var r)) return false;
                if (!RosterRotationState.KerbalIdentityMatches(r, kerbalIdentityKey)) return false;
                if (!HasGraduationExam(r)) return false;

                int resolvedLevel = r.GraduationExamTargetLevel;
                if ((resolvedLevel < 1 || resolvedLevel > 3) && hintedLevel >= 1 && hintedLevel <= 3)
                    resolvedLevel = hintedLevel;
                if (resolvedLevel < 1 || resolvedLevel > 3) return false;

                string expectedType = EACContractConfiguratorBridge.NormalizeContractTypeName(r.GraduationExamContractType);
                if (!string.IsNullOrEmpty(normalizedTypeName) && !string.IsNullOrEmpty(expectedType) &&
                    !string.Equals(normalizedTypeName, expectedType, StringComparison.Ordinal))
                {
                    return false;
                }

                kerbal = k;
                rec = r;
                targetLevel = resolvedLevel;
                return true;
            }

            return false;
        }

        private static bool TryGetPendingGraduationExamByContractIdOrType(KerbalRoster roster, string contractId, string normalizedTypeName, int typeTargetLevel,
            out ProtoCrewMember kerbal, out RosterRotationState.KerbalRecord rec, out int targetLevel)
        {
            kerbal = null;
            rec = null;
            targetLevel = 0;
            if (roster == null) return false;

            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var r)) continue;
                if (!HasGraduationExam(r)) continue;

                string expectedType = EACContractConfiguratorBridge.NormalizeContractTypeName(r.GraduationExamContractType);
                bool idMatch = !string.IsNullOrEmpty(contractId) &&
                               !string.IsNullOrEmpty(r.GraduationExamContractGuid) &&
                               string.Equals(contractId, r.GraduationExamContractGuid, StringComparison.Ordinal);
                bool typeMatch = !string.IsNullOrEmpty(normalizedTypeName) &&
                                 !string.IsNullOrEmpty(expectedType) &&
                                 string.Equals(normalizedTypeName, expectedType, StringComparison.Ordinal);
                bool levelMatch = typeTargetLevel <= 0 || r.GraduationExamTargetLevel == typeTargetLevel;

                if (!idMatch && !(typeMatch && levelMatch)) continue;

                kerbal = k;
                rec = r;
                targetLevel = r.GraduationExamTargetLevel;
                return targetLevel >= 1 && targetLevel <= 3;
            }

            return false;
        }

        private static bool TryGetSolePendingGraduationExamForCompletedContract(KerbalRoster roster, string normalizedTypeName, int typeTargetLevel,
            out ProtoCrewMember kerbal, out RosterRotationState.KerbalRecord rec, out int targetLevel)
        {
            kerbal = null;
            rec = null;
            targetLevel = 0;
            if (roster == null) return false;

            string contractTrait = EACContractConfiguratorBridge.TryParseTrait(normalizedTypeName);
            int matches = 0;

            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!RosterRotationState.Records.TryGetValue(k.name, out var r)) continue;
                if (!HasGraduationExam(r)) continue;
                if (typeTargetLevel >= 1 && typeTargetLevel <= 3 && r.GraduationExamTargetLevel != typeTargetLevel) continue;

                string expectedType = EACContractConfiguratorBridge.NormalizeContractTypeName(r.GraduationExamContractType);
                string expectedTrait = EACContractConfiguratorBridge.TryParseTrait(expectedType);
                string kerbalTrait = EACContractConfiguratorBridge.NormalizeTraitForContract(k.trait);

                if (!string.IsNullOrEmpty(normalizedTypeName) && !string.IsNullOrEmpty(expectedType) &&
                    !string.Equals(normalizedTypeName, expectedType, StringComparison.Ordinal))
                {
                    // Allow trait+level matching when the record type string is stale/missing, but do not
                    // award a Pilot exam to an Engineer/Scientist record.
                    if (!string.IsNullOrEmpty(contractTrait) && !string.Equals(contractTrait, kerbalTrait, StringComparison.Ordinal))
                        continue;
                    if (!string.IsNullOrEmpty(expectedTrait) && !string.Equals(contractTrait, expectedTrait, StringComparison.Ordinal))
                        continue;
                }

                matches++;
                kerbal = k;
                rec = r;
                targetLevel = r.GraduationExamTargetLevel;
                if (matches > 1)
                {
                    kerbal = null;
                    rec = null;
                    targetLevel = 0;
                    return false;
                }
            }

            return matches == 1 && targetLevel >= 1 && targetLevel <= 3;
        }

        public static bool IsGraduationExamAllowedForOffer(string trait, int targetLevel, string examId, bool excludeRecentlyUsed)
        {
            if (!excludeRecentlyUsed) return true;
            examId = EACContractConfiguratorBridge.NormalizeExamId(examId);
            if (string.IsNullOrEmpty(examId)) return true;

            trait = EACContractConfiguratorBridge.NormalizeTraitForContract(trait);
            if (targetLevel < 1 || targetLevel > 3) return true;

            List<string> history = GetGraduationExamHistoryForTraitLevel(trait, targetLevel);
            if (!history.Contains(examId, StringComparer.OrdinalIgnoreCase)) return true;

            // Avoid soft-locks: when the available pool is exhausted, allow any exam
            // except the one that was completed most recently. If only one exam exists,
            // allow it.
            List<string> candidates = EACContractConfiguratorBridge.GetGraduationExamCandidateIds(trait, targetLevel);
            if (candidates.Count <= 1) return true;

            string last = history.Count > 0 ? history[history.Count - 1] : string.Empty;
            bool allUsed = candidates.All(c => history.Contains(EACContractConfiguratorBridge.NormalizeExamId(c), StringComparer.OrdinalIgnoreCase));
            if (allUsed)
                return !string.Equals(examId, last, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        public static List<string> GetGraduationExamHistoryForTraitLevel(string trait, int targetLevel)
        {
            trait = EACContractConfiguratorBridge.NormalizeTraitForContract(trait);
            string prefix = BuildGraduationExamHistoryPrefix(trait, targetLevel);
            var result = new List<string>();
            foreach (string entry in ParseGraduationExamHistoryEntries())
            {
                if (entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string examId = entry.Substring(prefix.Length);
                    if (!string.IsNullOrEmpty(examId)) result.Add(examId);
                }
            }
            return result;
        }

        public static string GetLastGraduationExamId(string trait, int targetLevel)
        {
            List<string> history = GetGraduationExamHistoryForTraitLevel(trait, targetLevel);
            return history.Count > 0 ? history[history.Count - 1] : string.Empty;
        }

        public static void ReduceGraduationExamHistoryToLast(string trait, int targetLevel, string lastExamId)
        {
            trait = EACContractConfiguratorBridge.NormalizeTraitForContract(trait);
            lastExamId = EACContractConfiguratorBridge.NormalizeExamId(lastExamId);
            string prefix = BuildGraduationExamHistoryPrefix(trait, targetLevel);
            var entries = ParseGraduationExamHistoryEntries()
                .Where(e => !e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!string.IsNullOrEmpty(lastExamId))
                entries.Add(prefix + lastExamId);
            RosterRotationState.GraduationExamHistory = string.Join(";", entries.ToArray());
        }

        private static void RecordGraduationExamUsed(string trait, int targetLevel, string examId)
        {
            trait = EACContractConfiguratorBridge.NormalizeTraitForContract(trait);
            examId = EACContractConfiguratorBridge.NormalizeExamId(examId);
            if (string.IsNullOrEmpty(trait) || targetLevel < 1 || targetLevel > 3 || string.IsNullOrEmpty(examId))
                return;

            string prefix = BuildGraduationExamHistoryPrefix(trait, targetLevel);
            string entry = prefix + examId;
            var entries = ParseGraduationExamHistoryEntries()
                .Where(e => !string.Equals(e, entry, StringComparison.OrdinalIgnoreCase))
                .ToList();
            entries.Add(entry);

            // Keep the save node small while preserving enough history for rotation.
            int samePairCount = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (!entries[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                samePairCount++;
                if (samePairCount > 32)
                    entries.RemoveAt(i);
            }

            RosterRotationState.GraduationExamHistory = string.Join(";", entries.ToArray());
            RRLog.Verbose("[EAC] Recorded recently used final exam for rotation: trait=" + trait +
                       " targetLevel=" + targetLevel +
                       " examId=" + examId);
        }

        private static string BuildGraduationExamHistoryPrefix(string trait, int targetLevel)
        {
            return (trait ?? string.Empty).Trim() + "|L" + targetLevel + "|";
        }

        private static List<string> ParseGraduationExamHistoryEntries()
        {
            var result = new List<string>();
            string raw = RosterRotationState.GraduationExamHistory ?? string.Empty;
            foreach (string part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = part.Trim();
                if (!string.IsNullOrEmpty(entry)) result.Add(entry);
            }
            return result;
        }
    }

    internal static class EACContractConfiguratorBridge
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const string ContractPrefix = "EAC.Graduation.";
        public const string RequestedOfferMarker = "EAC_REQUESTED_OFFER";

        private static readonly string[] T38ExperimentalPartNames =
        {
            "Mark2Cockpit",
            "airplaneTail",
            "winglet3",
            "miniFuselage",
            "miniJetEngine",
            "R8winglet",
            "standardNoseCone",
            "deltaWing",
            "StandardCtrlSrf",
            "IntakeRadialLong",
            "SmallGearBay",
            "telescopicLadder",
            "noseConeAdapter",
            "avionicsNoseCone"
        };

        private static readonly string[] EngineerScienceRoverExperimentalPartNames =
        {
            "roverBody.v2",
            "roverWheel2",
            "smallCargoContainer",
            "GooExperiment",
            "sensorGravimeter",
            "sensorBarometer",
            "sensorThermometer",
            "sensorAccelerometer",
            "seatExternalCmd",
            "strutCube",
            "batteryPack",
            "solarPanels5",
            "probeCoreOcto2.v2"
        };

        private static Assembly _ccAssembly;
        private static Type _contractTypeType;
        private static Type _configuredContractType;
        private static Type _graduationAwardBehaviourType;
        private static bool _resolved;
        private static bool _availabilityLogged;

        public static bool IsContractConfiguratorInstalled
        {
            get
            {
                EnsureResolved();
                return _ccAssembly != null;
            }
        }

        public static bool IsBridgeLoaded
        {
            get
            {
                EnsureResolved();
                return _graduationAwardBehaviourType != null;
            }
        }

        public static string AvailabilitySummary
        {
            get
            {
                EnsureResolved();
                return BuildAvailabilitySummaryNoResolve();
            }
        }

        private static string BuildAvailabilitySummaryNoResolve()
        {
            if (_ccAssembly == null)
                return "Contract Configurator not detected";
            if (_contractTypeType == null || _configuredContractType == null)
                return "Contract Configurator detected but required CC types were not resolved";
            if (_graduationAwardBehaviourType == null)
                return "Contract Configurator detected but EAC_CCBridge.dll / EACGraduationAward was not loaded";
            return "Contract Configurator final exams available";
        }

        private static void LogAvailabilityOnce()
        {
            if (_availabilityLogged) return;
            _availabilityLogged = true;
            RRLog.Info("[EAC] Contract Configurator bridge status: " + BuildAvailabilitySummaryNoResolve());
        }

        public static bool IsAvailable
        {
            get
            {
                EnsureResolved();
                return _ccAssembly != null && _contractTypeType != null && _configuredContractType != null && _graduationAwardBehaviourType != null;
            }
        }

        public static string GetContractTypeName(ProtoCrewMember kerbal, int targetLevel)
        {
            string trait = NormalizeTrait(kerbal != null ? kerbal.trait : "Pilot");
            int clampedLevel = Math.Max(1, Math.Min(3, targetLevel));
            return ContractPrefix + trait + ".Level" + clampedLevel;
        }

        public static List<string> GetGraduationExamCandidateTypeNamesForTraitLevel(string trait, int targetLevel)
        {
            return GetGraduationExamCandidateTypeNames(trait, targetLevel);
        }

        public static string GetShortExamDisplayName(string contractTypeName)
        {
            contractTypeName = NormalizeContractTypeName(contractTypeName);
            if (string.IsNullOrEmpty(contractTypeName)) return "Auto";

            int lastDot = contractTypeName.LastIndexOf('.');
            string name = lastDot >= 0 && lastDot + 1 < contractTypeName.Length
                ? contractTypeName.Substring(lastDot + 1)
                : contractTypeName;
            if (string.IsNullOrEmpty(name)) return contractTypeName;

            var chars = new List<char>();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(name[i - 1]) && !char.IsUpper(name[i - 1]))
                    chars.Add(' ');
                chars.Add(c);
            }
            return new string(chars.ToArray());
        }

        public static string SelectContractTypeName(ProtoCrewMember kerbal, int targetLevel)
        {
            string trait = NormalizeTrait(kerbal != null ? kerbal.trait : "Pilot");
            int clampedLevel = Math.Max(1, Math.Min(3, targetLevel));
            List<string> candidates = GetGraduationExamCandidateTypeNames(trait, clampedLevel);
            if (candidates.Count == 0)
                return ContractPrefix + trait + ".Level" + clampedLevel;

            List<string> eligible = FilterCandidateTypeNamesByHistory(trait, clampedLevel, candidates);
            if (eligible.Count == 0) eligible = candidates;

            List<string> unreserved = FilterCandidateTypeNamesByCurrentAssignments(trait, clampedLevel, eligible, kerbal);
            if (unreserved.Count > 0)
                eligible = unreserved;

            int index = 0;
            if (eligible.Count > 1)
            {
                try
                {
                    int kerbalHash = kerbal != null && !string.IsNullOrEmpty(kerbal.name) ? kerbal.name.GetHashCode() : 0;
                    int tickHash = unchecked((int)DateTime.UtcNow.Ticks);
                    int utHash = 0;
                    try { utHash = unchecked((int)Planetarium.GetUniversalTime()); } catch { }
                    var rng = new System.Random(unchecked(tickHash ^ kerbalHash ^ (clampedLevel << 8) ^ utHash ^ Guid.NewGuid().GetHashCode()));
                    index = rng.Next(eligible.Count);
                }
                catch
                {
                    try { index = UnityEngine.Random.Range(0, eligible.Count); }
                    catch { index = 0; }
                }
            }

            string selected = eligible[Math.Max(0, Math.Min(eligible.Count - 1, index))];
            RRLog.Verbose("[EAC] Graduation exam selected: trait=" + trait +
                       " targetLevel=" + clampedLevel +
                       " kerbal=" + (kerbal != null ? kerbal.name : "") +
                       " selected=" + selected +
                       " candidates=" + string.Join(",", candidates.ToArray()) +
                       " historyEligible=" + string.Join(",", eligible.ToArray()));
            return selected;
        }

        public static List<string> GetGraduationExamCandidateIds(string trait, int targetLevel)
        {
            return GetGraduationExamCandidateTypeNames(trait, targetLevel)
                .Select(GetExamIdForContractType)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> FilterCandidateTypeNamesByCurrentAssignments(string trait, int targetLevel, List<string> candidates, ProtoCrewMember kerbalBeingOffered)
        {
            if (candidates == null || candidates.Count <= 1)
                return candidates ?? new List<string>();

            string normalizedTrait = NormalizeTrait(trait);
            var reserved = new HashSet<string>(StringComparer.Ordinal);
            KerbalRoster roster = null;
            try { roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null; } catch { }

            foreach (var entry in RosterRotationState.Records)
            {
                var rec = entry.Value;
                if (rec == null) continue;
                if (rec.GraduationExamTargetLevel != targetLevel) continue;
                if (!rec.GraduationExamPending && !rec.GraduationExamActive) continue;
                if (string.IsNullOrEmpty(rec.GraduationExamContractType)) continue;

                // A just-completed-training record starts with the generic base type
                // (EAC.Graduation.<Trait>.Level<N>).  That is not an actual reserved
                // variant, so ignore it for simultaneous-offer rotation.
                string reservedType = NormalizeContractTypeName(rec.GraduationExamContractType);
                string baseName = ContractPrefix + normalizedTrait + ".Level" + Math.Max(1, Math.Min(3, targetLevel));
                if (string.Equals(reservedType, baseName, StringComparison.Ordinal)) continue;

                if (kerbalBeingOffered != null && string.Equals(entry.Key, kerbalBeingOffered.name, StringComparison.Ordinal))
                    continue;

                ProtoCrewMember other = null;
                if (roster != null)
                {
                    foreach (var crew in roster.Crew)
                    {
                        if (crew != null && string.Equals(crew.name, entry.Key, StringComparison.Ordinal))
                        {
                            other = crew;
                            break;
                        }
                    }
                }
                if (other != null)
                {
                    string otherTrait = NormalizeTrait(other.trait);
                    if (!string.Equals(otherTrait, normalizedTrait, StringComparison.Ordinal))
                        continue;
                }

                reserved.Add(reservedType);
            }

            if (reserved.Count == 0) return candidates;

            var unreserved = candidates
                .Where(c => !reserved.Contains(NormalizeContractTypeName(c)))
                .ToList();

            // Avoid soft-locks: if all variants are already active/offered, allow the
            // full eligible pool again.  Otherwise, prefer an unreserved variant so
            // several Kerbals ready at the same time do not all get the same exam.
            return unreserved.Count > 0 ? unreserved : candidates;
        }

        private static List<string> FilterCandidateTypeNamesByHistory(string trait, int targetLevel, List<string> candidates)
        {
            if (candidates == null || candidates.Count <= 1)
                return candidates ?? new List<string>();

            List<string> history = RosterRotationKSCUI.GetGraduationExamHistoryForTraitLevel(trait, targetLevel);
            if (history.Count == 0) return candidates;

            var eligible = candidates
                .Where(c => !history.Contains(GetExamIdForContractType(c), StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (eligible.Count > 0) return eligible;

            string last = history[history.Count - 1];
            eligible = candidates
                .Where(c => !string.Equals(GetExamIdForContractType(c), last, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (eligible.Count > 0)
            {
                RosterRotationKSCUI.ReduceGraduationExamHistoryToLast(trait, targetLevel, last);
                return eligible;
            }

            return candidates;
        }

        private static List<string> GetGraduationExamCandidateTypeNames(string trait, int targetLevel)
        {
            EnsureResolved();
            trait = NormalizeTrait(trait);
            targetLevel = Math.Max(1, Math.Min(3, targetLevel));
            string baseName = ContractPrefix + trait + ".Level" + targetLevel;
            string variantPrefix = baseName + ".";
            var names = new List<string>();

            if (_contractTypeType != null)
            {
                try
                {
                    PropertyInfo prop = _contractTypeType.GetProperty("AllValidContractTypeNames", PublicStatic);
                    var enumerable = prop != null ? prop.GetValue(null, null) as IEnumerable : null;
                    if (enumerable != null)
                    {
                        foreach (object item in enumerable)
                        {
                            string name = NormalizeContractTypeName(item as string);
                            if (string.IsNullOrEmpty(name)) continue;
                            if (string.Equals(name, baseName, StringComparison.Ordinal) || name.StartsWith(variantPrefix, StringComparison.Ordinal))
                                names.Add(name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    RRLog.VerboseWarn("[EAC] Could not enumerate Contract Configurator final exam types: " + ex.Message);
                }
            }

            if (_contractTypeType != null && names.Count == 0 && GetContractType(baseName) != null)
                names.Add(baseName);

            names = names
                .Distinct(StringComparer.Ordinal)
                .Where(name => IsContractTypeAllowedByEacPreconditions(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            return names;
        }

        private static bool IsContractTypeAllowedByEacPreconditions(string contractTypeName)
        {
            contractTypeName = NormalizeContractTypeName(contractTypeName);
            // The former L1 Bail-Out exam is now configured as a stock-friendly emergency landing/recovery exam.
            // Do not suppress it based on Astronaut Complex EVA unlocks; the cfg owns the objective design.
            return true;
        }

        public static string GetExamIdForContractType(string contractTypeName)
        {
            return NormalizeExamId(NormalizeContractTypeName(contractTypeName));
        }

        public static string NormalizeExamId(string examId)
        {
            if (string.IsNullOrEmpty(examId)) return string.Empty;
            return examId.Trim().Replace(';', '_').Replace('|', '_');
        }


        public static bool TryStartGraduationExam(ProtoCrewMember kerbal, int targetLevel, out string contractId, out string failureReason)
        {
            return TryStartGraduationExam(kerbal, targetLevel, SelectContractTypeName(kerbal, targetLevel), out contractId, out failureReason);
        }

        public static bool TryStartGraduationExam(ProtoCrewMember kerbal, int targetLevel, string requestedContractTypeName, out string contractId, out string failureReason)
        {
            contractId = "";
            failureReason = "";

            if (kerbal == null)
            {
                failureReason = "no Kerbal selected";
                return false;
            }
            if (!IsAvailable)
            {
                failureReason = "Contract Configurator or EAC_CCBridge.dll is not installed or not loaded";
                return false;
            }

            string contractTypeName = NormalizeContractTypeName(requestedContractTypeName);
            if (string.IsNullOrEmpty(contractTypeName))
                contractTypeName = SelectContractTypeName(kerbal, targetLevel);
            object contractType = GetContractType(contractTypeName);
            if (contractType == null)
            {
                failureReason = "missing CC contract type " + contractTypeName + " (install EAC_Graduation.cfg under GameData/EAC/Contracts)";
                return false;
            }

            string preAssetMessage;
            if (!TryProvisionGraduationExamAssets(kerbal, targetLevel, out preAssetMessage))
            {
                failureReason = preAssetMessage;
                return false;
            }
            if (!string.IsNullOrEmpty(preAssetMessage))
                RRLog.Verbose("[EAC] " + preAssetMessage);

            // Generate this specific contract type through Contract Configurator's own
            // private preloader generator, then put the generated ConfiguredContract into
            // ContractPreLoader's pending list.  Merely resetting CC's cooldowns and hoping
            // for the normal random generation pass is not deterministic enough for a
            // button-driven EAC workflow.
            TryResetContractGenerationCooldown(contractType);

            object configuredContract;
            Contract stockContract;
            if (!TryGenerateConfiguredContractViaPreLoader(contractType, out configuredContract, out stockContract, out failureReason))
            {
                failureReason = failureReason + " Pending before/after request: " + GetPreLoaderPendingSummary();
                return false;
            }

            TrySetContractText(configuredContract, kerbal, targetLevel);
            TrySetUniqueData(configuredContract, "eacKerbal", kerbal.name);
            if (RosterRotationState.Records.TryGetValue(kerbal.name, out var eacRecForIdentity))
                TrySetUniqueData(configuredContract, "eacKerbalIdentity", RosterRotationState.EnsureKerbalIdentity(eacRecForIdentity));
            TrySetUniqueData(configuredContract, "eacTrait", NormalizeTrait(kerbal.trait));
            TrySetUniqueData(configuredContract, "eacTargetLevel", targetLevel);
            TrySetUniqueData(configuredContract, "eacExamId", GetExamIdForContractType(contractTypeName));
            TryBindGraduationContractToKerbal(stockContract, kerbal);

            if (!TryRegisterAsOffered(stockContract, configuredContract, out failureReason))
            {
                failureReason = failureReason + " Pending after failed register: " + GetPreLoaderPendingSummary();
                return false;
            }

            // Give CC/KSP UI one more nudge; if Mission Control is already open it will
            // refresh from ContractPreLoader.PendingContracts(), and if it is opened later
            // the pending list is already populated.
            TryForceContractGenerationPass();

            contractId = GetContractIdentifier(stockContract);
            RRLog.Verbose("[EAC] Offered graduation exam contract via CC preloader: " + GetContractTitle(stockContract) +
                       " state=" + stockContract.ContractState +
                       " id=" + contractId +
                       " pending=" + GetPreLoaderPendingSummary());
            return true;
        }

        public static bool IsEacGraduationContract(Contract contract, out string contractTypeName)
        {
            contractTypeName = GetContractTypeName(contract);
            return !string.IsNullOrEmpty(contractTypeName) &&
                   contractTypeName.StartsWith(ContractPrefix, StringComparison.Ordinal);
        }

        public static bool TryProvisionGraduationExamAssets(ProtoCrewMember kerbal, int targetLevel, out string message)
        {
            message = "";
            if (kerbal == null)
                return true;

            string trait = NormalizeTrait(kerbal.trait);
            if (string.Equals(trait, "Pilot", StringComparison.OrdinalIgnoreCase))
            {
                return TryProvisionCraftAndExperimentalParts(
                    "T-38-EAC-L1.craft",
                    "T-38-EAC-L1.craft",
                    T38ExperimentalPartNames,
                    "T-38-EAC-L1 craft",
                    "Level " + targetLevel + " pilot exam",
                    out message);
            }

            if (string.Equals(trait, "Engineer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trait, "Scientist", StringComparison.OrdinalIgnoreCase))
            {
                // Level 3 Engineer/Scientist final exams are now lab/scenario contracts.
                // Contract Configurator provides the T-38 and all exam-specific assets.
                // EAC should not copy the old rover craft for those exams.
                if (targetLevel >= 3)
                    return true;

                return TryProvisionCraftAndExperimentalParts(
                    "EAC-Engineer-Science-Rover-L1.craft",
                    "EAC-Engineer-Science-Rover-L1.craft",
                    EngineerScienceRoverExperimentalPartNames,
                    "EAC Engineer/Science Rover L1 craft",
                    "Level " + targetLevel + " " + trait.ToLowerInvariant() + " exam",
                    out message);
            }

            return true;
        }

        public static bool TryCleanupGraduationExamAssetsIfUnused(out string message)
        {
            message = "";
            try
            {
                if (AnyGraduationExamPendingOrActive("Pilot") ||
                    AnyGraduationExamPendingOrActive("Engineer") ||
                    AnyGraduationExamPendingOrActive("Scientist"))
                    return true;

                string saveFolder;
                List<string> sphDirs = GetCurrentSaveSphDirectories(out saveFolder);
                if (sphDirs.Count == 0) return true;

                int deleted = 0;
                foreach (string sphDir in sphDirs.Distinct())
                {
                    if (string.IsNullOrEmpty(sphDir)) continue;
                    deleted += TryDeleteCraftFromDirectory(sphDir, "T-38-EAC-L1.craft", ref message) ? 1 : 0;
                    if (!string.IsNullOrEmpty(message)) return false;
                    deleted += TryDeleteCraftFromDirectory(sphDir, "EAC-Engineer-Science-Rover-L1.craft", ref message) ? 1 : 0;
                    if (!string.IsNullOrEmpty(message)) return false;
                }

                if (deleted > 0)
                    message = "Removed EAC final-exam craft from the current save because no final exams remain pending or active.";
                return true;
            }
            catch (Exception ex)
            {
                message = "could not evaluate EAC final exam craft cleanup: " + ex.Message;
                return false;
            }
        }

        private static bool TryDeleteCraftFromDirectory(string sphDir, string fileName, ref string message)
        {
            string path = System.IO.Path.Combine(sphDir, fileName);
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = "could not remove EAC exam craft from " + path + ": " + ex.Message;
            }
            return false;
        }

        private static bool AnyGraduationExamPendingOrActive(string trait)
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (roster == null) return false;

            trait = NormalizeTrait(trait);
            foreach (var k in roster.Crew)
            {
                if (k == null) continue;
                if (!string.Equals(NormalizeTrait(k.trait), trait, StringComparison.Ordinal)) continue;
                RosterRotationState.KerbalRecord rec;
                if (!RosterRotationState.Records.TryGetValue(k.name, out rec) || rec == null) continue;
                if (rec.GraduationExamTargetLevel >= 1 && rec.GraduationExamTargetLevel <= 3 &&
                    (rec.GraduationExamPending || rec.GraduationExamActive))
                    return true;
            }
            return false;
        }

        private static bool TryProvisionCraftAndExperimentalParts(string sourceFileName, string destinationFileName, string[] partNames, string craftDisplayName, string examDisplayName, out string message)
        {
            message = "";

            string sourcePath = TryFindEacCraftFile(sourceFileName);
            if (string.IsNullOrEmpty(sourcePath) || !System.IO.File.Exists(sourcePath))
            {
                message = sourceFileName + " is missing. Expected it under GameData/EAC/Craft, next to the EAC plugin, or in the build source GameData/EAC/Craft folder.";
                return false;
            }

            string saveFolder;
            List<string> sphDirs = GetCurrentSaveSphDirectories(out saveFolder);
            if (sphDirs.Count == 0)
            {
                message = "could not determine the current save's SPH craft folder; HighLogic.SaveFolder='" + (saveFolder ?? "") + "'";
                return false;
            }

            bool copiedAny = false;
            bool alreadyPresent = false;
            List<string> failures = new List<string>();
            string lastDestination = "";

            foreach (string sphDir in sphDirs.Distinct())
            {
                if (string.IsNullOrEmpty(sphDir)) continue;
                string destinationPath = System.IO.Path.Combine(sphDir, destinationFileName);
                lastDestination = destinationPath;

                try
                {
                    if (!System.IO.Directory.Exists(sphDir))
                        System.IO.Directory.CreateDirectory(sphDir);

                    bool needsCopy = !System.IO.File.Exists(destinationPath) ||
                                     System.IO.File.GetLastWriteTimeUtc(sourcePath) > System.IO.File.GetLastWriteTimeUtc(destinationPath) ||
                                     new System.IO.FileInfo(sourcePath).Length != new System.IO.FileInfo(destinationPath).Length;
                    if (needsCopy)
                    {
                        System.IO.File.Copy(sourcePath, destinationPath, true);
                        copiedAny = true;
                    }
                    else
                    {
                        alreadyPresent = true;
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(destinationPath + " (" + ex.Message + ")");
                }
            }

            if (!copiedAny && !alreadyPresent && failures.Count > 0)
            {
                message = "could not copy " + sourceFileName + " into the current save. Tried: " + string.Join("; ", failures.ToArray());
                return false;
            }

            string unlockMessage;
            bool unlocked = TryUnlockExperimentalParts(partNames, craftDisplayName, out unlockMessage);

            if (copiedAny)
                message = "Copied " + craftDisplayName + " into the current save's SPH folder: " + lastDestination;
            else
                message = craftDisplayName + " is already present in the current save's SPH folder: " + lastDestination;

            if (!string.IsNullOrEmpty(unlockMessage))
                message += "; " + unlockMessage + " for the " + examDisplayName;

            return unlocked;
        }

        private static bool TryUnlockExperimentalParts(string[] partNames, string craftDisplayName, out string message)
        {
            message = "";

            try
            {
                if (ResearchAndDevelopment.Instance == null || partNames == null || partNames.Length == 0)
                    return true;

                int unlocked = 0;
                List<string> missing = new List<string>();

                foreach (string partName in partNames)
                {
                    AvailablePart part = null;
                    try
                    {
                        part = PartLoader.LoadedPartsList.FirstOrDefault(p => p != null && string.Equals(p.name, partName, StringComparison.Ordinal));
                    }
                    catch { }

                    if (part == null)
                    {
                        missing.Add(partName);
                        continue;
                    }

                    try
                    {
                        ProtoTechNode techNode = ResearchAndDevelopment.Instance.GetTechState(part.TechRequired);
                        if (techNode == null || techNode.state != RDTech.State.Available)
                        {
                            ResearchAndDevelopment.AddExperimentalPart(part);
                            unlocked++;
                        }
                    }
                    catch (Exception ex)
                    {
                        message = "could not unlock experimental part '" + partName + "' for " + craftDisplayName + ": " + ex.Message;
                        return false;
                    }
                }

                if (missing.Count > 0)
                    message = craftDisplayName + " copied, but these part names were not found for experimental unlock: " + string.Join(", ", missing.ToArray());
                else if (unlocked > 0)
                    message = "temporarily unlocked " + unlocked + " parts";
                else
                    message = "parts are already available or experimentally unlocked";

                return missing.Count == 0;
            }
            catch (Exception ex)
            {
                message = "could not unlock experimental parts for " + craftDisplayName + ": " + ex.Message;
                return false;
            }
        }

        private static List<string> GetCurrentSaveSphDirectories(out string saveFolder)
        {
            saveFolder = "";
            var dirs = new List<string>();

            try { saveFolder = HighLogic.SaveFolder; }
            catch { saveFolder = ""; }

            if (string.IsNullOrEmpty(saveFolder))
                return dirs;

            // HighLogic.SaveFolder is normally just the save folder name, for example "Code".
            // The correct KSP craft location is always under the saves directory:
            //     <KSP root>/saves/<SaveFolder>/Ships/SPH
            // Do not create <KSP root>/<SaveFolder>/Ships/SPH; that is outside the normal KSP save tree.
            string root = "";
            try { root = KSPUtil.ApplicationRootPath ?? ""; } catch { root = ""; }
            AddSaveSphDirectory(dirs, root, saveFolder);

            // Fallback for unusual launcher/current-directory cases. This still only targets
            // the standard saves/<SaveFolder>/Ships/SPH layout.
            try
            {
                string cwd = System.IO.Directory.GetCurrentDirectory();
                if (!string.Equals(System.IO.Path.GetFullPath(cwd), System.IO.Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                    AddSaveSphDirectory(dirs, cwd, saveFolder);
            }
            catch { }

            return dirs;
        }

        private static void AddSaveSphDirectory(List<string> dirs, string root, string saveFolder)
        {
            if (dirs == null || string.IsNullOrEmpty(root) || string.IsNullOrEmpty(saveFolder)) return;

            try
            {
                string sphDir;

                if (System.IO.Path.IsPathRooted(saveFolder))
                {
                    // Defensive support for a rooted SaveFolder value. In that uncommon case,
                    // treat it as the actual save folder path and append Ships/SPH.
                    sphDir = System.IO.Path.Combine(saveFolder, System.IO.Path.Combine("Ships", "SPH"));
                }
                else
                {
                    string normalizedSaveFolder = saveFolder.Trim().Trim(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                    // If a modded launcher ever supplies "saves/<name>" instead of just "<name>",
                    // do not add another saves segment.
                    string savePrefix = "saves" + System.IO.Path.DirectorySeparatorChar;
                    string altSavePrefix = "saves" + System.IO.Path.AltDirectorySeparatorChar;
                    if (normalizedSaveFolder.StartsWith(savePrefix, StringComparison.OrdinalIgnoreCase) ||
                        normalizedSaveFolder.StartsWith(altSavePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        sphDir = System.IO.Path.Combine(System.IO.Path.Combine(root, normalizedSaveFolder), System.IO.Path.Combine("Ships", "SPH"));
                    }
                    else
                    {
                        sphDir = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(root, "saves"), normalizedSaveFolder), System.IO.Path.Combine("Ships", "SPH"));
                    }
                }

                AddDistinctPath(dirs, sphDir);
            }
            catch { }
        }

        private static void AddDistinctPath(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrEmpty(path)) return;
            try { path = System.IO.Path.GetFullPath(path); } catch { }
            foreach (string existing in paths)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            paths.Add(path);
        }

        private static string TryFindEacCraftFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";

            var attempts = new List<string>();
            AddGameDataAttempt(attempts, "EAC/Craft/" + fileName);

            try
            {
                string asmDir = System.IO.Path.GetDirectoryName(typeof(EACContractConfiguratorBridge).Assembly.Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    AddDistinctPath(attempts, System.IO.Path.Combine(System.IO.Path.Combine(asmDir, ".."), System.IO.Path.Combine("Craft", fileName)));
                    AddDistinctPath(attempts, System.IO.Path.Combine(asmDir, System.IO.Path.Combine("Craft", fileName)));
                    AddDistinctPath(attempts, System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(asmDir, ".."), "EAC"), System.IO.Path.Combine("Craft", fileName)));
                }
            }
            catch { }

            foreach (string path in attempts)
            {
                try
                {
                    if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                        return path;
                }
                catch { }
            }

            return "";
        }

        private static void AddGameDataAttempt(List<string> attempts, string gameDataRelativePath)
        {
            if (attempts == null || string.IsNullOrEmpty(gameDataRelativePath)) return;
            string relative = gameDataRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);

            try
            {
                string root = KSPUtil.ApplicationRootPath ?? "";
                if (!string.IsNullOrEmpty(root))
                    AddDistinctPath(attempts, System.IO.Path.Combine(System.IO.Path.Combine(root, "GameData"), relative));
            }
            catch { }

            try
            {
                string cwd = System.IO.Directory.GetCurrentDirectory();
                if (!string.IsNullOrEmpty(cwd))
                    AddDistinctPath(attempts, System.IO.Path.Combine(System.IO.Path.Combine(cwd, "GameData"), relative));
            }
            catch { }
        }

        public static string GetContractIdentifier(Contract contract)
        {
            return contract == null ? "" : GetObjectIdentifier(contract);
        }

        public static string GetUniqueDataString(Contract contract, string key)
        {
            if (contract == null || string.IsNullOrEmpty(key)) return "";
            try
            {
                object dictObj = GetMemberValue(contract, "uniqueData");
                var dict = dictObj as IDictionary;
                if (dict == null || !dict.Contains(key)) return "";
                object value = dict[key];
                return value != null ? value.ToString() : "";
            }
            catch { return ""; }
        }

        public static int GetUniqueDataInt(Contract contract, string key)
        {
            int value;
            return int.TryParse(GetUniqueDataString(contract, key), out value) ? value : 0;
        }
        public static bool TrySetUniqueDataForContract(Contract contract, string key, object value)
        {
            if (contract == null || string.IsNullOrEmpty(key)) return false;
            try
            {
                object dictObj = GetMemberValue(contract, "uniqueData");
                var dict = dictObj as IDictionary;
                if (dict == null) return false;
                dict[key] = value;
                return true;
            }
            catch { return false; }
        }

        public static void TryApplyPersonalizedText(Contract contract, ProtoCrewMember kerbal, int targetLevel)
        {
            if (contract == null || kerbal == null) return;
            TrySetContractText(contract, kerbal, targetLevel);
        }


        public static IEnumerable<Contract> EnumerateCompletedContracts()
        {
            object system = GetContractSystemInstance();
            if (system == null) yield break;

            IEnumerable finished = GetMemberValue(system, "ContractsFinished") as IEnumerable;
            if (finished == null) yield break;

            foreach (object item in finished)
            {
                var contract = item as Contract;
                if (contract == null) continue;
                if (contract.ContractState == Contract.State.Completed)
                    yield return contract;
            }
        }

        public static bool HasCurrentGraduationContract(string contractId, string contractTypeName)
        {
            object system = GetContractSystemInstance();
            if (system == null) return false;

            string expectedId = contractId ?? "";
            string expectedType = NormalizeContractTypeName(contractTypeName);

            try
            {
                IEnumerable contracts = GetMemberValue(system, "Contracts") as IEnumerable;
                if (contracts != null)
                {
                    foreach (object item in contracts)
                    {
                        var contract = item as Contract;
                        if (contract == null) continue;
                        if (contract.ContractState != Contract.State.Offered &&
                            contract.ContractState != Contract.State.Active)
                            continue;

                        if (ContractMatchesIdOrType(contract, expectedId, expectedType))
                            return true;
                    }
                }

                foreach (Contract contract in EnumeratePreLoaderPendingContracts())
                {
                    if (contract == null || contract.ContractState != Contract.State.Offered) continue;
                    if (ContractMatchesIdOrType(contract, expectedId, expectedType))
                        return true;
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.HasCurrentGraduationContract", "Suppressed", ex);
            }

            return false;
        }

        public static string GetContractTitle(Contract contract)
        {
            if (contract == null) return "";
            try
            {
                object titleObj = GetMemberValue(contract, "Title") ?? GetMemberValue(contract, "title");
                string title = titleObj as string;
                if (!string.IsNullOrEmpty(title)) return title;
            }
            catch { }

            try
            {
                MethodInfo method = contract.GetType().GetMethod("GetTitle", AnyInstance);
                if (method != null)
                {
                    string title = method.Invoke(contract, null) as string;
                    if (!string.IsNullOrEmpty(title)) return title;
                }
            }
            catch { }

            return "";
        }

        public static string NormalizeContractTypeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            name = name.Trim();
            if (name.StartsWith("EACGraduation.", StringComparison.Ordinal))
                name = name.Substring("EACGraduation.".Length);
            return name;
        }

        public static int TryParseTargetLevel(string contractTypeName)
        {
            contractTypeName = NormalizeContractTypeName(contractTypeName);
            if (string.IsNullOrEmpty(contractTypeName)) return 0;

            int idx = contractTypeName.LastIndexOf(".Level", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;

            string tail = contractTypeName.Substring(idx + ".Level".Length);
            int end = 0;
            while (end < tail.Length && char.IsDigit(tail[end])) end++;
            if (end <= 0) return 0;
            int level;
            return int.TryParse(tail.Substring(0, end), out level) ? level : 0;
        }

        public static string TryParseTrait(string contractTypeName)
        {
            contractTypeName = NormalizeContractTypeName(contractTypeName);
            if (string.IsNullOrEmpty(contractTypeName)) return "";

            string prefix = ContractPrefix;
            if (!contractTypeName.StartsWith(prefix, StringComparison.Ordinal)) return "";
            string rest = contractTypeName.Substring(prefix.Length);
            int dot = rest.IndexOf('.');
            return dot > 0 ? NormalizeTrait(rest.Substring(0, dot)) : "";
        }

        public static string NormalizeTraitForContract(string trait)
        {
            return NormalizeTrait(trait);
        }

        private static void EnsureResolved()
        {
            if (_resolved) return;
            _resolved = true;

            try
            {
                _ccAssembly = EACOptionalModRegistry.FindAssembly("ContractConfigurator");

                if (_ccAssembly == null)
                {
                    LogAvailabilityOnce();
                    return;
                }

                _contractTypeType = _ccAssembly.GetType("ContractConfigurator.ContractType", false);
                _configuredContractType = _ccAssembly.GetType("ContractConfigurator.ConfiguredContract", false);
                _graduationAwardBehaviourType = FindType("RosterRotation.ContractConfiguratorIntegration.EACGraduationAward");
                LogAvailabilityOnce();
            }
            catch (Exception ex)
            {
                RRLog.VerboseWarn("[EAC] Contract Configurator bridge resolution failed: " + ex.Message);
                _ccAssembly = null;
                _contractTypeType = null;
                _configuredContractType = null;
                _graduationAwardBehaviourType = null;
            }
        }

        private static object GetContractType(string contractTypeName)
        {
            MethodInfo getContractType = _contractTypeType.GetMethod("GetContractType", PublicStatic, null, new[] { typeof(string) }, null);
            return getContractType != null ? getContractType.Invoke(null, new object[] { contractTypeName }) : null;
        }

        private static string GetContractTypeName(Contract contract)
        {
            EnsureResolved();
            if (contract == null) return "";

            try
            {
                if (_configuredContractType != null)
                {
                    MethodInfo method = _configuredContractType.GetMethod("contractTypeName", PublicStatic, null, new[] { typeof(Contract) }, null);
                    if (method != null)
                    {
                        string name = method.Invoke(null, new object[] { contract }) as string;
                        if (!string.IsNullOrEmpty(name)) return NormalizeContractTypeName(name);
                    }
                }
            }
            catch { }

            try
            {
                object subType = GetMemberValue(contract, "subType") ?? GetMemberValue(contract, "subtype");
                string name = subType as string;
                if (!string.IsNullOrEmpty(name)) return NormalizeContractTypeName(name);
            }
            catch { }

            try
            {
                object contractType = GetMemberValue(contract, "contractType");
                object typeName = contractType != null ? GetMemberValue(contractType, "name") : null;
                string name = typeName as string;
                if (!string.IsNullOrEmpty(name)) return NormalizeContractTypeName(name);
            }
            catch { }

            // Fallback for completed contracts where CC metadata was not exposed through reflection.
            string title = GetContractTitle(contract);
            string parsed = TryParseContractTypeFromTitle(title);
            return NormalizeContractTypeName(parsed);
        }

        private static void TrySetContractText(object configuredContract, ProtoCrewMember kerbal, int targetLevel)
        {
            string trait = NormalizeTrait(kerbal.trait);
            string contractType = GetContractTypeName(configuredContract as Contract);
            string existingTitle = GetContractTitle(configuredContract as Contract);
            string existingSynopsis = GetStringMember(configuredContract, "synopsis");
            string existingDescription = GetStringMember(configuredContract, "description");
            string existingCompletedMessage = GetStringMember(configuredContract, "completedMessage");

            string scenarioTitle = GetScenarioDisplayName(contractType);
            if (string.IsNullOrEmpty(scenarioTitle))
                scenarioTitle = ExtractScenarioTitle(existingTitle, trait, targetLevel);

            string suffix = string.IsNullOrEmpty(scenarioTitle) ? "" : ": " + scenarioTitle;
            string title = kerbal.name + "'s " + trait + " Level " + targetLevel + " Final Exam" + suffix;

            // Contract Configurator should own the contract body text.  EAC only
            // personalizes the assigned Kerbal's name and leaves the configured
            // description/synopsis/objectives alone.
            string synopsis = PersonalizeConfiguredContractText(existingSynopsis, kerbal.name, trait, targetLevel);
            string description = PersonalizeConfiguredContractText(existingDescription, kerbal.name, trait, targetLevel);

            if (string.IsNullOrEmpty(description))
                description = kerbal.name + " has completed EAC classroom training and now needs a practical final exam to graduate to Level " + targetLevel + ".";
            if (string.IsNullOrEmpty(synopsis))
                synopsis = "Complete the Contract Configurator objectives for " + kerbal.name + "'s assigned EAC final exam.";

            string completed = !string.IsNullOrEmpty(existingCompletedMessage)
                ? PersonalizeConfiguredContractText(existingCompletedMessage, kerbal.name, trait, targetLevel)
                : kerbal.name + " has passed the EAC final exam.";

            TrySetMember(configuredContract, "title", title);
            TrySetMember(configuredContract, "synopsis", synopsis);
            TrySetMember(configuredContract, "description", description);
            TrySetMember(configuredContract, "completedMessage", completed);
        }

        private static string GetStringMember(object target, string memberName)
        {
            try
            {
                object value = GetMemberValue(target, memberName);
                return value as string ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string ExtractScenarioTitle(string existingTitle, string trait, int targetLevel)
        {
            if (string.IsNullOrEmpty(existingTitle)) return string.Empty;
            int colon = existingTitle.IndexOf(':');
            if (colon >= 0 && colon + 1 < existingTitle.Length)
                return existingTitle.Substring(colon + 1).Trim();

            string prefix = trait + " Level " + targetLevel + " Final Exam";
            int idx = existingTitle.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string tail = existingTitle.Substring(idx + prefix.Length).Trim(' ', '-', ':');
                if (!string.IsNullOrEmpty(tail)) return tail;
            }
            return string.Empty;
        }

        private static string StripContractTitlePrefix(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Trim();
        }

        private static string PersonalizeConfiguredContractText(string text, string kerbalName, string trait, int targetLevel)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string value = text;
            value = RemoveLegacyAssignedSetupText(value, kerbalName);
            value = value.Replace("the assigned Kerbal", kerbalName);
            value = value.Replace("assigned Kerbal", kerbalName);
            value = value.Replace("{kerbal}", kerbalName);
            value = value.Replace("{KERBAL}", kerbalName);
            value = value.Replace("$KERBAL", kerbalName);
            value = value.Replace("$kerbal", kerbalName);
            value = value.Replace("@KERBAL", kerbalName);
            value = value.Replace("@kerbal", kerbalName);
            return CollapseContractWhitespace(value);
        }

        private static string RemoveLegacyAssignedSetupText(string text, string kerbalName)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string value = text;
            const string legacyStart = "After accepting this exam, EAC will copy";
            const string legacyEnd = "then launch at KSC.";

            int guard = 0;
            while (guard++ < 10)
            {
                int start = value.IndexOf(legacyStart, StringComparison.Ordinal);
                if (start < 0) break;

                int end = value.IndexOf(legacyEnd, start, StringComparison.Ordinal);
                if (end < 0) break;

                int removeEnd = end + legacyEnd.Length;
                string candidate = value.Substring(start, removeEnd - start);
                bool oldRoverSetup = candidate.IndexOf("rover", StringComparison.OrdinalIgnoreCase) >= 0
                                     && candidate.IndexOf("Spaceplane Hangar", StringComparison.Ordinal) >= 0;
                if (!oldRoverSetup) break;

                value = value.Remove(start, removeEnd - start);
            }

            return value;
        }

        private static string CollapseContractWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string value = text.Trim();
            while (value.IndexOf("  ", StringComparison.Ordinal) >= 0)
                value = value.Replace("  ", " ");
            value = value.Replace(" .", ".");
            value = value.Replace(" ,", ",");
            value = value.Replace(" ;", ";");
            value = value.Replace(" :", ":");
            return value.Trim();
        }

        private static string GetAssignedExamSetupText(string kerbalName, string trait, int targetLevel)
        {
            return "Complete the Contract Configurator objectives for " + kerbalName + "'s assigned EAC final exam.";
        }

        private static string GetAssignedScenarioInstructionText(string contractTypeName)
        {
            contractTypeName = NormalizeContractTypeName(contractTypeName);
            if (contractTypeName.EndsWith(".TestCircuit", StringComparison.Ordinal)) return "Fly the local KSC circuit, land back on Kerbin, then recover the T-38.";
            if (contractTypeName.EndsWith(".Bailout", StringComparison.Ordinal)) return "Climb to a safe handling altitude, return to Kerbin, land safely, stop, and recover the T-38.";
            if (contractTypeName.EndsWith(".HighAltitude", StringComparison.Ordinal)) return "Fly the high-altitude handling block, return to Kerbin, land, and recover the T-38.";
            if (contractTypeName.EndsWith(".StallRecovery", StringComparison.Ordinal)) return "At a safe altitude, slow below the stall threshold, recover to stable flight, land, and recover the T-38.";
            if (contractTypeName.EndsWith(".AbortTakeoff", StringComparison.Ordinal)) return "Accelerate on the runway, briefly rotate or lift off, settle back down, stop safely, and recover the T-38.";

            if (contractTypeName.EndsWith(".FieldInspection", StringComparison.Ordinal)) return "For the inspection, leave the command seat on EVA, walk around/visually inspect the rover, board again if desired, then recover the rover test article; recovering only the EVA Kerbal is not enough.";
            if (contractTypeName.EndsWith(".BatteryInstall", StringComparison.Ordinal)) return "Use EVA construction to remove the Z-100 battery from the rover cargo container and attach it to the outside of the rover, then recover the rover test article.";
            if (contractTypeName.EndsWith(".SolarPanelInstall", StringComparison.Ordinal)) return "Use EVA construction to remove the OX-STAT solar panel from the rover cargo container and attach it to the outside of the rover, then recover the rover test article.";
            if (contractTypeName.EndsWith(".RemovePart", StringComparison.Ordinal)) return "Use EVA construction to remove the cubic strut training part from the rover, then recover the rover test article.";
            if (contractTypeName.EndsWith(".ParachuteInspection", StringComparison.Ordinal)) return "Leave the command seat on EVA, walk around/visually inspect the cubic strut on the rover, board again if desired, then recover the rover test article; recovering only the EVA Kerbal is not enough.";

            if (contractTypeName.EndsWith(".KSCSurvey", StringComparison.Ordinal)) return "Run the required science reading and recover the rover or the vessel carrying that science data.";
            if (contractTypeName.EndsWith(".MysteryGoo", StringComparison.Ordinal)) return "Run the Mystery Goo observation and recover the rover with the Goo data still aboard. Do not remove the data to EVA unless you recover the vessel carrying that data.";
            if (contractTypeName.EndsWith(".InstrumentCalibration", StringComparison.Ordinal)) return "Run the temperature and pressure scans, then recover the rover or the vessel carrying those science results.";
            if (contractTypeName.EndsWith(".AtmosphericData", StringComparison.Ordinal)) return "Run the pressure scan, then recover the rover or the vessel carrying that science result.";
            if (contractTypeName.EndsWith(".ShorelineExpedition", StringComparison.Ordinal)) return "Drive to Kerbin Shores, take the temperature scan there, and recover the rover or the vessel carrying that science result. The live biome checkbox can toggle while driving; the recovered Shores data is what matters.";

            if (contractTypeName.EndsWith(".IslandOutAndBack", StringComparison.Ordinal)) return "Fly to the Island Airfield, land and slow down there, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".IslandLowPass", StringComparison.Ordinal)) return "Make a safe low pass over the Island Airfield, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".CoastalNavigation", StringComparison.Ordinal)) return "Fly the north and south KSC coastal checkpoints, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".OffshoreEmergencyReturn", StringComparison.Ordinal)) return "Fly offshore east of KSC, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".CrosswindPattern", StringComparison.Ordinal)) return "Fly the wider KSC crosswind-pattern checkpoint, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".ScientistIslandAirlift", StringComparison.Ordinal)) return "Carry a scientist passenger, land at the Island Airfield, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".EngineerIslandAirlift", StringComparison.Ordinal)) return "Carry an engineer passenger, land at the Island Airfield, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".ScientistSurveyFlight", StringComparison.Ordinal)) return "Carry a scientist passenger through the high-altitude survey checkpoint, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".EngineerSystemsFlight", StringComparison.Ordinal)) return "Carry an engineer passenger through the Island low-pass systems checkpoint, then return to KSC and land safely.";
            if (contractTypeName.EndsWith(".MixedCrewCheckride", StringComparison.Ordinal)) return "Carry either a scientist or engineer passenger through the Island checkride, then return to KSC and land safely.";

            if (contractTypeName.EndsWith(".RoverRangeInspection", StringComparison.Ordinal)) return "Drive the rover at least 500 m from the start point, then recover the rover test article.";
            if (contractTypeName.EndsWith(".BatteryFieldService", StringComparison.Ordinal)) return "Install the Z-100 battery from cargo, drive at least 200 m, then recover the rover test article.";
            if (contractTypeName.EndsWith(".SolarFieldService", StringComparison.Ordinal)) return "Install the OX-STAT solar panel from cargo, drive at least 200 m, then recover the rover test article.";
            if (contractTypeName.EndsWith(".StructuralRemovalDrill", StringComparison.Ordinal)) return "Remove the cubic strut from the rover, then recover the rover test article.";
            if (contractTypeName.EndsWith(".InstrumentBayInspection", StringComparison.Ordinal)) return "Drive at least 300 m while keeping the rover intact, then recover the rover test article.";
            if (contractTypeName.EndsWith(".ExtendedRoverTraverse", StringComparison.Ordinal)) return "Drive the rover at least 1000 m from the start point, then recover the rover test article.";
            if (contractTypeName.EndsWith(".PowerAndMobilityService", StringComparison.Ordinal)) return "Install the battery and solar panel from cargo, drive at least 300 m, then recover the rover test article.";
            if (contractTypeName.EndsWith(".StructuralServiceTraverse", StringComparison.Ordinal)) return "Remove the cubic strut, drive at least 300 m, then recover the rover test article.";
            if (contractTypeName.EndsWith(".AntennaInstallationRelay", StringComparison.Ordinal)) return "Install the Communotron antenna from cargo, drive at least 300 m, then recover the rover test article.";
            if (contractTypeName.EndsWith(".FullFieldService", StringComparison.Ordinal)) return "Install the battery, solar panel, and antenna from cargo, then recover the rover test article.";

            if (contractTypeName.EndsWith(".ThermometerRoverSurvey", StringComparison.Ordinal)) return "Drive the rover at least 250 m, take a temperature scan, and recover the rover or vessel carrying the data.";
            if (contractTypeName.EndsWith(".GooFieldSurvey", StringComparison.Ordinal)) return "Drive the rover at least 250 m, observe Mystery Goo, and recover the rover or vessel carrying the data.";
            if (contractTypeName.EndsWith(".PressureFieldSurvey", StringComparison.Ordinal)) return "Drive the rover at least 250 m, take a pressure scan, and recover the rover or vessel carrying the data.";
            if (contractTypeName.EndsWith(".GravityCalibration", StringComparison.Ordinal)) return "Run the gravimeter and recover the rover or vessel carrying the data.";
            if (contractTypeName.EndsWith(".SeismicMotionCalibration", StringComparison.Ordinal)) return "Run the accelerometer/seismic scan and recover the rover or vessel carrying the data.";
            if (contractTypeName.EndsWith(".MultiInstrumentSurvey", StringComparison.Ordinal)) return "Run the temperature, pressure, and Mystery Goo experiments, then recover the rover or vessel carrying the data.";
            if (contractTypeName.EndsWith(".RoverScienceTraverse", StringComparison.Ordinal)) return "Drive the rover at least 500 m, run the temperature and pressure scans, then recover the rover or vessel carrying the data.";
            if (contractTypeName.EndsWith(".AdvancedInstrumentPackage", StringComparison.Ordinal)) return "Run the gravimeter and accelerometer/seismic scan, then recover the rover or vessel carrying the data.";
            if (contractTypeName.EndsWith(".ShorelineResearchRun", StringComparison.Ordinal)) return "Drive to Kerbin Shores, take the temperature scan there, and recover the rover or vessel carrying the data. The live biome checkbox can toggle while driving; the recovered Shores data is what matters.";
            if (contractTypeName.EndsWith(".FullKSCResearchPackage", StringComparison.Ordinal)) return "Run the full rover science package and recover the rover or vessel carrying the data.";
            return string.Empty;
        }

        private static string GetScenarioDisplayName(string contractTypeName)
        {
            contractTypeName = NormalizeContractTypeName(contractTypeName);
            if (string.IsNullOrEmpty(contractTypeName)) return "";
            if (contractTypeName.EndsWith(".TestCircuit", StringComparison.Ordinal)) return "KSC Test Circuit";
            if (contractTypeName.EndsWith(".Bailout", StringComparison.Ordinal)) return "Emergency Landing";
            if (contractTypeName.EndsWith(".HighAltitude", StringComparison.Ordinal)) return "High-Altitude Handling";
            if (contractTypeName.EndsWith(".StallRecovery", StringComparison.Ordinal)) return "Stall Recovery";
            if (contractTypeName.EndsWith(".AbortTakeoff", StringComparison.Ordinal)) return "Abort Takeoff";
            if (contractTypeName.EndsWith(".FieldInspection", StringComparison.Ordinal)) return "Field Inspection";
            if (contractTypeName.EndsWith(".BatteryInstall", StringComparison.Ordinal)) return "Battery Installation";
            if (contractTypeName.EndsWith(".SolarPanelInstall", StringComparison.Ordinal)) return "Solar Panel Installation";
            if (contractTypeName.EndsWith(".RemovePart", StringComparison.Ordinal)) return "Remove-and-Recover Part Drill";
            if (contractTypeName.EndsWith(".ParachuteInspection", StringComparison.Ordinal)) return "Cubic Strut Inspection";
            if (contractTypeName.EndsWith(".KSCSurvey", StringComparison.Ordinal)) return "KSC Science Survey";
            if (contractTypeName.EndsWith(".MysteryGoo", StringComparison.Ordinal)) return "Mystery Goo Familiarization";
            if (contractTypeName.EndsWith(".InstrumentCalibration", StringComparison.Ordinal)) return "Instrument Calibration Survey";
            if (contractTypeName.EndsWith(".AtmosphericData", StringComparison.Ordinal)) return "Atmospheric Data Flight";
            if (contractTypeName.EndsWith(".ShorelineExpedition", StringComparison.Ordinal)) return "Shoreline Sample Expedition";
            if (contractTypeName.EndsWith(".IslandOutAndBack", StringComparison.Ordinal)) return "Island Airfield Out-and-Back";
            if (contractTypeName.EndsWith(".IslandLowPass", StringComparison.Ordinal)) return "Island Airfield Low Pass";
            if (contractTypeName.EndsWith(".CoastalNavigation", StringComparison.Ordinal)) return "KSC Coastal Navigation";
            if (contractTypeName.EndsWith(".OffshoreEmergencyReturn", StringComparison.Ordinal)) return "Offshore Emergency Return";
            if (contractTypeName.EndsWith(".CrosswindPattern", StringComparison.Ordinal)) return "KSC Crosswind Pattern";
            if (contractTypeName.EndsWith(".ScientistIslandAirlift", StringComparison.Ordinal)) return "Scientist Island Airlift";
            if (contractTypeName.EndsWith(".EngineerIslandAirlift", StringComparison.Ordinal)) return "Engineer Island Airlift";
            if (contractTypeName.EndsWith(".ScientistSurveyFlight", StringComparison.Ordinal)) return "Scientist Survey Flight";
            if (contractTypeName.EndsWith(".EngineerSystemsFlight", StringComparison.Ordinal)) return "Engineer Systems Flight";
            if (contractTypeName.EndsWith(".MixedCrewCheckride", StringComparison.Ordinal)) return "Mixed-Crew Checkride";
            if (contractTypeName.EndsWith(".RoverRangeInspection", StringComparison.Ordinal)) return "Rover Range Inspection";
            if (contractTypeName.EndsWith(".BatteryFieldService", StringComparison.Ordinal)) return "Battery Field Service";
            if (contractTypeName.EndsWith(".SolarFieldService", StringComparison.Ordinal)) return "Solar Field Service";
            if (contractTypeName.EndsWith(".StructuralRemovalDrill", StringComparison.Ordinal)) return "Structural Removal Drill";
            if (contractTypeName.EndsWith(".InstrumentBayInspection", StringComparison.Ordinal)) return "Instrument Bay Inspection";
            if (contractTypeName.EndsWith(".ExtendedRoverTraverse", StringComparison.Ordinal)) return "Extended Rover Traverse";
            if (contractTypeName.EndsWith(".PowerAndMobilityService", StringComparison.Ordinal)) return "Power and Mobility Service";
            if (contractTypeName.EndsWith(".StructuralServiceTraverse", StringComparison.Ordinal)) return "Structural Service Traverse";
            if (contractTypeName.EndsWith(".AntennaInstallationRelay", StringComparison.Ordinal)) return "Antenna Installation Relay";
            if (contractTypeName.EndsWith(".FullFieldService", StringComparison.Ordinal)) return "Full Field Service";
            if (contractTypeName.EndsWith(".ThermometerRoverSurvey", StringComparison.Ordinal)) return "Thermometer Rover Survey";
            if (contractTypeName.EndsWith(".GooFieldSurvey", StringComparison.Ordinal)) return "Mystery Goo Field Survey";
            if (contractTypeName.EndsWith(".PressureFieldSurvey", StringComparison.Ordinal)) return "Pressure Field Survey";
            if (contractTypeName.EndsWith(".GravityCalibration", StringComparison.Ordinal)) return "Gravity Calibration";
            if (contractTypeName.EndsWith(".SeismicMotionCalibration", StringComparison.Ordinal)) return "Seismic/Motion Calibration";
            if (contractTypeName.EndsWith(".MultiInstrumentSurvey", StringComparison.Ordinal)) return "Multi-Instrument Survey";
            if (contractTypeName.EndsWith(".RoverScienceTraverse", StringComparison.Ordinal)) return "Rover Science Traverse";
            if (contractTypeName.EndsWith(".AdvancedInstrumentPackage", StringComparison.Ordinal)) return "Advanced Instrument Package";
            if (contractTypeName.EndsWith(".ShorelineResearchRun", StringComparison.Ordinal)) return "Shoreline Research Run";
            if (contractTypeName.EndsWith(".FullKSCResearchPackage", StringComparison.Ordinal)) return "Full KSC Research Package";
            return "";
        }

        private static void TrySetUniqueData(object configuredContract, string key, object value)
        {
            try
            {
                object dictObj = GetMemberValue(configuredContract, "uniqueData");
                var dict = dictObj as IDictionary;
                if (dict != null) dict[key] = value;
            }
            catch { }
        }


        public static void TryBindGraduationContractToKerbal(Contract stockContract, ProtoCrewMember kerbal)
        {
            if (stockContract == null || kerbal == null) return;
            if (RosterRotationState.Records.TryGetValue(kerbal.name, out var recForIdentity))
            {
                TrySetUniqueData(stockContract, "eacKerbal", kerbal.name);
                TrySetUniqueData(stockContract, "eacKerbalIdentity", RosterRotationState.EnsureKerbalIdentity(recForIdentity));
            }
            TryAddAssignedKerbalPresentParameter(stockContract, kerbal);
            TryBindSpecificKerbalToHasCrewParameters(stockContract, kerbal);
            TryBindSpecificKerbalToRecoverKerbalParameters(stockContract, kerbal);
            TryBindSpecificKerbalToAwardExperienceBehaviour(stockContract, kerbal);
        }

        private static void TryAddAssignedKerbalPresentParameter(Contract stockContract, ProtoCrewMember kerbal)
        {
            if (stockContract == null || kerbal == null) return;

            try
            {
                // This binder runs when CC generates the contract, again when EAC marks it
                // offered, and again when it is accepted.  Contract.GetAllDescendents() is
                // not reliable for freshly-added top-level parameters in all KSP/CC states,
                // so keep a small CC uniqueData marker as the primary idempotency guard.
                string injectedFor = GetUniqueDataString(stockContract, "eacAssignedKerbalPresentInjected");
                if (string.Equals(injectedFor, kerbal.name, StringComparison.Ordinal))
                    return;

                foreach (object existing in EnumerateContractParameters(stockContract))
                {
                    if (IsAssignedKerbalPresentParameter(existing, kerbal.name))
                    {
                        TrySetUniqueData(stockContract, "eacAssignedKerbalPresentInjected", kerbal.name);
                        return;
                    }
                }

                Type parameterType = FindType("RosterRotation.ContractConfiguratorIntegration.EACAssignedKerbalPresent");
                if (parameterType == null)
                {
                    RRLog.VerboseWarn("[EAC] EACAssignedKerbalPresent parameter type was not found; final exam will rely on existing HasCrew/AwardExperience binding.");
                    return;
                }

                object parameterObj = null;
                ConstructorInfo ctor = parameterType.GetConstructor(AnyInstance, null, new[] { typeof(string), typeof(string) }, null);
                if (ctor != null)
                    parameterObj = ctor.Invoke(new object[] { kerbal.name, "Correct Kerbal assigned: " + kerbal.name });
                else
                    parameterObj = Activator.CreateInstance(parameterType, true);

                ContractParameter parameter = parameterObj as ContractParameter;
                if (parameter == null)
                {
                    RRLog.VerboseWarn("[EAC] EACAssignedKerbalPresent could not be created as a ContractParameter.");
                    return;
                }

                parameter.ID = "EACAssignedKerbalPresent";
                TrySetMember(parameter, "assignedKerbalName", kerbal.name);
                TrySetMember(parameter, "title", "Correct Kerbal assigned: " + kerbal.name);
                stockContract.AddParameter(parameter);
                TrySetUniqueData(stockContract, "eacAssignedKerbalPresentInjected", kerbal.name);
                RRLog.Verbose("[EAC] Added final exam correct-Kerbal check for " + kerbal.name + ".");
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.AddAssignedKerbalParameter", "Suppressed", ex);
            }
        }

        private static bool IsAssignedKerbalPresentParameter(object parameter, string kerbalName)
        {
            if (parameter == null) return false;

            Type type = parameter.GetType();
            string fullName = type.FullName ?? string.Empty;
            if (string.Equals(fullName, "RosterRotation.ContractConfiguratorIntegration.EACAssignedKerbalPresent", StringComparison.Ordinal))
                return true;

            string id = GetMemberValue(parameter, "ID") as string;
            if (!string.IsNullOrEmpty(id) && id.IndexOf("EACAssignedKerbalPresent", StringComparison.Ordinal) >= 0)
                return true;

            string title = GetMemberValue(parameter, "title") as string;
            if (string.IsNullOrEmpty(title))
                title = GetMemberValue(parameter, "Title") as string;

            if (!string.IsNullOrEmpty(title) &&
                title.IndexOf("Correct Kerbal assigned", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return string.IsNullOrEmpty(kerbalName) ||
                       title.IndexOf(kerbalName, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private static void TryBindSpecificKerbalToRecoverKerbalParameters(Contract stockContract, ProtoCrewMember kerbal)
        {
            if (stockContract == null || kerbal == null) return;

            try
            {
                object ccKerbal = TryCreateExperienceKerbal(kerbal);
                if (ccKerbal == null)
                {
                    RRLog.VerboseWarn("[EAC] Could not create a Contract Configurator Kerbal wrapper for " + kerbal.name + "; RecoverKerbal parameters will remain unbound.");
                    return;
                }

                Type kerbalType = ccKerbal.GetType();
                Type listType = typeof(List<>).MakeGenericType(kerbalType);
                var typedList = (IList)Activator.CreateInstance(listType);
                typedList.Add(ccKerbal);

                int bound = 0;
                foreach (object parameter in EnumerateContractParameters(stockContract))
                {
                    if (parameter == null) continue;
                    Type parameterType = parameter.GetType();
                    if (!string.Equals(parameterType.FullName, "ContractConfigurator.Parameters.RecoverKerbal", StringComparison.Ordinal))
                        continue;

                    bool changed = TryBindRecoverKerbalMember(parameter, ccKerbal, typedList);
                    changed |= TrySetMember(parameter, "kerbal", typedList);
                    changed |= TrySetMember(parameter, "kerbals", typedList);
                    TrySetMember(parameter, "title", "Recover " + kerbal.name + " safely");
                    TryInvoke(parameter, "CreateDelegates");

                    if (changed) bound++;
                }

                if (bound > 0)
                    RRLog.Verbose("[EAC] Bound Contract Configurator RecoverKerbal to " + kerbal.name + " on " + bound + " parameter(s).");
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.BindRecoverKerbal", "Suppressed", ex);
            }
        }

        private static bool TryBindRecoverKerbalMember(object parameter, object ccKerbal, IList typedKerbalList)
        {
            if (parameter == null || ccKerbal == null || typedKerbalList == null) return false;
            bool bound = false;
            Type kerbalType = ccKerbal.GetType();
            Type listType = typedKerbalList.GetType();

            try
            {
                for (Type t = parameter.GetType(); t != null; t = t.BaseType)
                {
                    foreach (FieldInfo field in t.GetFields(AnyInstance))
                    {
                        if (field.Name.IndexOf("kerbal", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (field.FieldType.IsAssignableFrom(listType))
                        {
                            field.SetValue(parameter, typedKerbalList);
                            bound = true;
                        }
                        else if (field.FieldType.IsAssignableFrom(kerbalType))
                        {
                            field.SetValue(parameter, ccKerbal);
                            bound = true;
                        }
                    }

                    foreach (PropertyInfo prop in t.GetProperties(AnyInstance))
                    {
                        if (!prop.CanWrite) continue;
                        if (prop.Name.IndexOf("kerbal", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (prop.PropertyType.IsAssignableFrom(listType))
                        {
                            prop.SetValue(parameter, typedKerbalList, null);
                            bound = true;
                        }
                        else if (prop.PropertyType.IsAssignableFrom(kerbalType))
                        {
                            prop.SetValue(parameter, ccKerbal, null);
                            bound = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.BindRecoverKerbalMember", "Suppressed", ex);
            }

            return bound;
        }

        private static bool ShouldBindHasCrewAsAssignedTrainee(object parameter)
        {
            if (parameter == null) return false;

            string id = GetMemberValue(parameter, "ID") as string;
            if (!string.IsNullOrEmpty(id))
            {
                if (string.Equals(id, "HasCrew", StringComparison.OrdinalIgnoreCase))
                    return true;

                // Passenger checks should be allowed to remain stock CC HasCrew objectives.
                if (id.IndexOf("Passenger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Scientist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    id.IndexOf("Engineer", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            string title = GetMemberValue(parameter, "title") as string;
            if (string.IsNullOrEmpty(title))
                title = GetMemberValue(parameter, "Title") as string;

            if (!string.IsNullOrEmpty(title))
            {
                if (title.IndexOf("passenger", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                if (title.IndexOf("assigned", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    title.IndexOf("trainee", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (title.IndexOf("Carry ", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    title.IndexOf(" aboard", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            // Be conservative.  Unknown HasCrew parameters are probably contract objectives,
            // not the EAC trainee identity binding target.
            return false;
        }

        private static void TryBindSpecificKerbalToHasCrewParameters(Contract stockContract, ProtoCrewMember kerbal)
        {
            if (stockContract == null || kerbal == null) return;

            try
            {
                object ccKerbal = TryCreateExperienceKerbal(kerbal);
                if (ccKerbal == null)
                {
                    RRLog.VerboseWarn("[EAC] Could not create a Contract Configurator Kerbal wrapper for " + kerbal.name + "; final exam contract will still be tracked by EAC metadata.");
                    return;
                }

                Type kerbalType = ccKerbal.GetType();
                int bound = 0;
                foreach (object parameter in EnumerateContractParameters(stockContract))
                {
                    if (parameter == null) continue;
                    Type parameterType = parameter.GetType();
                    if (!string.Equals(parameterType.FullName, "ContractConfigurator.Parameters.HasCrew", StringComparison.Ordinal))
                        continue;

                    // Only personalize the trainee crew check.  Other HasCrew parameters
                    // may be normal Contract Configurator objectives, such as Scientist or
                    // Engineer passenger checks in Level 3 pilot exams.  Those should stay
                    // cfg-driven and continue to evaluate trait/minCrew normally.
                    if (!ShouldBindHasCrewAsAssignedTrainee(parameter))
                        continue;

                    Type listType = typeof(List<>).MakeGenericType(kerbalType);
                    var typedList = (IList)Activator.CreateInstance(listType);
                    typedList.Add(ccKerbal);

                    if (!TryBindKerbalList(parameter, typedList))
                    {
                        TrySetMember(parameter, "kerbal", typedList);
                        TrySetMember(parameter, "kerbals", typedList);
                    }

                    TrySetMember(parameter, "trait", null);
                    TrySetMember(parameter, "minExperience", 0);
                    TrySetMember(parameter, "maxExperience", 5);
                    TrySetMember(parameter, "minCrew", 0);
                    TrySetMember(parameter, "title", "Carry " + kerbal.name + " aboard");
                    TryInvoke(parameter, "CreateDelegates");
                    bound++;
                }

                if (bound == 0)
                    RRLog.Verbose("[EAC] No trainee-specific Contract Configurator HasCrew parameter was found to bind for " + kerbal.name + "; continuing with AwardExperience binding.");
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.BindSpecificKerbal", "Suppressed", ex);
            }
        }

        private static void TryBindSpecificKerbalToAwardExperienceBehaviour(Contract stockContract, ProtoCrewMember kerbal)
        {
            if (stockContract == null || kerbal == null) return;

            try
            {
                object ccKerbal = TryCreateExperienceKerbal(kerbal);
                if (ccKerbal == null)
                {
                    RRLog.VerboseWarn("[EAC] Could not create a Contract Configurator Kerbal wrapper for " + kerbal.name + "; AwardExperience will fall back to the completed vessel parameter crew.");
                    return;
                }

                Type kerbalType = ccKerbal.GetType();
                Type kerbalListType = typeof(List<>).MakeGenericType(kerbalType);
                var kerbalList = (IList)Activator.CreateInstance(kerbalListType);
                kerbalList.Add(ccKerbal);

                int bound = 0;
                foreach (object behaviour in EnumerateContractBehaviours(stockContract))
                {
                    if (behaviour == null) continue;
                    if (!string.Equals(behaviour.GetType().FullName, "ContractConfigurator.Behaviour.AwardExperience", StringComparison.Ordinal))
                        continue;

                    TrySetMember(behaviour, "kerbals", kerbalList);

                    // The cfg may include parameter=... so Contract Configurator can load the
                    // behaviour before EAC personalizes the offered contract.  Once personalized,
                    // prefer the exact Kerbal and aggressively clear every parameter/crew fallback.
                    // CC AwardExperience builds a cached crew list from completed parameters; if that
                    // fallback remains active, any passenger aboard the active vessel can receive the
                    // exam level along with the trainee.
                    TryClearAwardExperienceFallbackCollections(behaviour);
                    bound++;
                }

                if (bound > 0)
                    RRLog.Verbose("[EAC] Bound Contract Configurator AwardExperience to " + kerbal.name + " on " + bound + " behaviour(s).");
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.BindAwardExperience", "Suppressed", ex);
            }
        }

        private static void TryClearAwardExperienceFallbackCollections(object behaviour)
        {
            if (behaviour == null) return;

            TrySetMember(behaviour, "crew", new List<ProtoCrewMember>());
            TrySetMember(behaviour, "parameter", new List<string>());
            TrySetMember(behaviour, "parameters", new List<string>());
            TrySetMember(behaviour, "excludedKerbals", new List<string>());

            try
            {
                for (Type t = behaviour.GetType(); t != null; t = t.BaseType)
                {
                    foreach (FieldInfo field in t.GetFields(AnyInstance))
                    {
                        TryClearAwardExperienceFallbackMember(behaviour, field.Name, field.FieldType, value => field.SetValue(behaviour, value));
                    }

                    foreach (PropertyInfo prop in t.GetProperties(AnyInstance))
                    {
                        if (!prop.CanWrite || prop.GetIndexParameters().Length != 0) continue;
                        TryClearAwardExperienceFallbackMember(behaviour, prop.Name, prop.PropertyType, value => prop.SetValue(behaviour, value, null));
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.ClearAwardExperienceFallbacks", "Suppressed", ex);
            }
        }

        private static void TryClearAwardExperienceFallbackMember(object behaviour, string memberName, Type memberType, Action<object> setter)
        {
            if (behaviour == null || string.IsNullOrEmpty(memberName) || memberType == null || setter == null) return;

            bool isParameterFallback = memberName.IndexOf("parameter", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isCrewFallback = memberName.IndexOf("crew", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isParameterFallback && !isCrewFallback) return;

            try
            {
                if (memberType == typeof(string))
                {
                    setter(string.Empty);
                    return;
                }

                if (typeof(IList).IsAssignableFrom(memberType))
                {
                    object list = null;
                    if (memberType.IsInterface || memberType.IsAbstract)
                        list = new ArrayList();
                    else
                        list = Activator.CreateInstance(memberType);
                    setter(list);
                    return;
                }

                if (memberType.IsGenericType)
                {
                    Type genericDef = memberType.GetGenericTypeDefinition();
                    if (genericDef == typeof(List<>))
                    {
                        setter(Activator.CreateInstance(memberType));
                        return;
                    }
                    if (genericDef == typeof(HashSet<>))
                    {
                        setter(Activator.CreateInstance(memberType));
                        return;
                    }
                }

                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(memberType) && memberType != typeof(string))
                {
                    Type elementType = typeof(string);
                    if (memberType.IsArray)
                    {
                        elementType = memberType.GetElementType() ?? typeof(string);
                        setter(Array.CreateInstance(elementType, 0));
                        return;
                    }
                }
            }
            catch { }
        }

        private static IEnumerable<object> EnumerateContractBehaviours(Contract stockContract)
        {
            if (stockContract == null) yield break;

            object behavioursObj = GetMemberValue(stockContract, "behaviours") ?? GetMemberValue(stockContract, "contractBehaviours");
            var enumerable = behavioursObj as IEnumerable;
            if (enumerable == null) yield break;

            foreach (object item in enumerable)
                yield return item;
        }

        private static bool TryBindKerbalList(object parameter, IList typedKerbalList)
        {
            if (parameter == null || typedKerbalList == null) return false;
            bool bound = false;
            try
            {
                Type listType = typedKerbalList.GetType();
                for (Type t = parameter.GetType(); t != null; t = t.BaseType)
                {
                    foreach (FieldInfo field in t.GetFields(AnyInstance))
                    {
                        if (!field.FieldType.IsAssignableFrom(listType)) continue;
                        if (field.Name.IndexOf("kerbal", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        field.SetValue(parameter, typedKerbalList);
                        bound = true;
                    }

                    foreach (PropertyInfo prop in t.GetProperties(AnyInstance))
                    {
                        if (!prop.CanWrite) continue;
                        if (!prop.PropertyType.IsAssignableFrom(listType)) continue;
                        if (prop.Name.IndexOf("kerbal", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        prop.SetValue(parameter, typedKerbalList, null);
                        bound = true;
                    }
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.BindKerbalList", "Suppressed", ex);
            }
            return bound;
        }

        private static IEnumerable<object> EnumerateContractParameters(Contract contract)
        {
            if (contract == null) yield break;

            IEnumerable all = null;
            try
            {
                MethodInfo method = typeof(Contract).GetMethods(AnyInstance)
                    .FirstOrDefault(m => m.Name == "GetAllDescendents" && m.GetParameters().Length == 0);
                if (method != null) all = method.Invoke(contract, null) as IEnumerable;
            }
            catch { }

            if (all == null) yield break;
            foreach (object item in all)
                yield return item;
        }

        private static object TryCreateExperienceKerbal(ProtoCrewMember crew)
        {
            if (crew == null) return null;

            // Contract Configurator's Kerbal wrapper moved namespace in current builds.
            // Prefer the current type, but keep the older names for compatibility with
            // older CC/KSP-RO assemblies.
            Type kerbalType = FindType("ContractConfigurator.Kerbal") ??
                              FindType("ContractConfigurator.ExpressionParser.Kerbal") ??
                              FindType("ContractConfigurator.ExpressionParser.Wrappers.Kerbal");
            if (kerbalType == null) return null;

            foreach (ConstructorInfo ctor in kerbalType.GetConstructors(AnyInstance))
            {
                ParameterInfo[] args = ctor.GetParameters();

                try
                {
                    if (args.Length == 1 && args[0].ParameterType.IsInstanceOfType(crew))
                    {
                        object result = ctor.Invoke(new object[] { crew });
                        TryPopulateExperienceKerbalWrapper(result, crew);
                        return result;
                    }

                    if (args.Length == 1 && args[0].ParameterType == typeof(string))
                    {
                        object result = ctor.Invoke(new object[] { crew.name });
                        TryPopulateExperienceKerbalWrapper(result, crew);
                        return result;
                    }

                    if (args.Length == 0)
                    {
                        object result = ctor.Invoke(null);
                        TryPopulateExperienceKerbalWrapper(result, crew);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    RRLog.VerboseExceptionOnce("EAC.CCBridge.CreateKerbalWrapper.Ctor." + kerbalType.FullName, "Suppressed", ex);
                }
            }

            try
            {
                object created = Activator.CreateInstance(kerbalType, true);
                TryPopulateExperienceKerbalWrapper(created, crew);
                return created;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.CreateKerbalWrapper.Default." + kerbalType.FullName, "Suppressed", ex);
                return null;
            }
        }

        private static void TryPopulateExperienceKerbalWrapper(object wrapper, ProtoCrewMember crew)
        {
            if (wrapper == null || crew == null) return;

            // Current Contract Configurator exposes pcm as a read-only property backed
            // by _pcm.  Set both names so this works across old and new wrappers.
            TrySetMember(wrapper, "_pcm", crew);
            TrySetMember(wrapper, "pcm", crew);
            TrySetMember(wrapper, "name", crew.name);
            TrySetMember(wrapper, "Name", crew.name);
        }

        private static Type FindType(string fullName)
        {
            return EACOptionalModRegistry.FindType(fullName, "EAC_CCBridge");
        }

        private static object GetContractSystemInstance()
        {
            try
            {
                PropertyInfo pi = typeof(ContractSystem).GetProperty("Instance", PublicStatic);
                if (pi != null)
                {
                    object system = pi.GetValue(null, null);
                    if (system != null) return system;
                }

                FieldInfo fi = typeof(ContractSystem).GetField("Instance", PublicStatic);
                if (fi != null) return fi.GetValue(null);
            }
            catch { }

            return null;
        }

        private static string TryParseContractTypeFromTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";

            string normalized = title.Replace("'s", " ");
            string trait = "";
            if (normalized.IndexOf("Pilot", StringComparison.OrdinalIgnoreCase) >= 0) trait = "Pilot";
            else if (normalized.IndexOf("Engineer", StringComparison.OrdinalIgnoreCase) >= 0) trait = "Engineer";
            else if (normalized.IndexOf("Scientist", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.IndexOf("Science", StringComparison.OrdinalIgnoreCase) >= 0) trait = "Scientist";

            int level = 0;
            for (int i = 1; i <= 3; i++)
            {
                if (normalized.IndexOf("Level " + i, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalized.IndexOf("Level" + i, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalized.IndexOf("L" + i, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    level = i;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(trait) && level >= 1 && level <= 3)
                return ContractPrefix + trait + ".Level" + level;
            return "";
        }

        private static bool TryGenerateConfiguredContractViaPreLoader(object contractType, out object configuredContract, out Contract stockContract, out string failureReason)
        {
            configuredContract = null;
            stockContract = null;
            failureReason = "";

            object preLoader = GetContractPreLoaderInstance();
            if (preLoader == null)
            {
                failureReason = "Contract Configurator preloader is unavailable. Return to the Space Center or Mission Control and retry.";
                return false;
            }

            try { TryInvoke(preLoader, "ResetGenerationFailure"); } catch { }

            MethodInfo generate = null;
            try
            {
                generate = preLoader.GetType().GetMethod("GenerateContract", AnyInstance, null, new[] { _contractTypeType }, null);
            }
            catch { }

            if (generate == null)
            {
                failureReason = "Contract Configurator preloader GenerateContract API was not found";
                return false;
            }

            IEnumerable generated;
            try
            {
                generated = generate.Invoke(preLoader, new[] { contractType }) as IEnumerable;
            }
            catch (Exception ex)
            {
                failureReason = "Contract Configurator could not generate the exam contract: " + Unwrap(ex).Message;
                return false;
            }

            if (generated == null)
            {
                failureReason = "Contract Configurator did not return a contract generator";
                return false;
            }

            try
            {
                IEnumerator enumerator = generated.GetEnumerator();
                int guard = 0;
                while (guard++ < 128 && enumerator.MoveNext())
                {
                    object candidate = enumerator.Current;
                    if (candidate == null) continue;
                    if (!_configuredContractType.IsInstanceOfType(candidate)) continue;

                    configuredContract = candidate;
                    stockContract = candidate as Contract;
                    if (stockContract == null)
                    {
                        failureReason = "Contract Configurator generated an exam object that is not a stock Contract";
                        configuredContract = null;
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                failureReason = "Contract Configurator contract generator failed: " + Unwrap(ex).Message;
                return false;
            }

            failureReason = "Contract Configurator did not generate an offered exam. Check the EACGraduation group, active/offered contract limits, and CC requirement errors in KSP.log.";
            return false;
        }

        private static bool TryCreateConfiguredContractInstance(out object configuredContract, out Contract stockContract, out string failureReason)
        {
            configuredContract = null;
            stockContract = null;
            failureReason = "";

            try
            {
                // This mirrors Contract Configurator's ContractPreLoader path.  Using
                // Contract.Generate instead of Activator.CreateInstance gives stock KSP a
                // properly seeded/registered Contract object that Mission Control can display.
                int seed = unchecked((int)(DateTime.UtcNow.Ticks & 0x7fffffff));
                stockContract = Contract.Generate(_configuredContractType, Contract.ContractPrestige.Trivial, seed, Contract.State.Withdrawn);
                configuredContract = stockContract;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.GenerateConfiguredContract", "Falling back to Activator.CreateInstance", ex);
            }

            if (configuredContract == null)
            {
                try
                {
                    configuredContract = Activator.CreateInstance(_configuredContractType);
                    stockContract = configuredContract as Contract;
                }
                catch (Exception ex)
                {
                    failureReason = "could not create ConfiguredContract: " + ex.Message;
                    return false;
                }
            }

            if (stockContract == null)
            {
                failureReason = "created CC contract was not a stock Contract";
                return false;
            }

            return true;
        }

        private static bool TryMeetBasicRequirements(object contractType, object configuredContract, out string failureReason)
        {
            failureReason = "";
            if (contractType == null || configuredContract == null)
                return true;

            try
            {
                MethodInfo method = _contractTypeType.GetMethod("MeetBasicRequirements", AnyInstance, null, new[] { _configuredContractType }, null);
                if (method == null) return true;

                bool ok = (bool)method.Invoke(contractType, new[] { configuredContract });
                if (!ok)
                {
                    failureReason = "Contract Configurator rejected the exam requirements. Check active contract limits, the EAC Graduation Exams contract group, and whether another exam of this type is already offered/active.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                failureReason = "Contract Configurator requirement check failed: " + Unwrap(ex).Message;
                return false;
            }

            return true;
        }

        private static bool TryRegisterAsOffered(Contract stockContract, object configuredContract, out string failureReason)
        {
            failureReason = "";
            if (stockContract == null)
            {
                failureReason = "created CC contract was not a stock Contract";
                return false;
            }

            object preLoader = GetContractPreLoaderInstance();
            if (preLoader == null)
            {
                failureReason = "Contract Configurator preloader is unavailable. Return to the Space Center or Mission Control and retry.";
                return false;
            }

            if (stockContract.ContractState != Contract.State.Offered &&
                !TrySetContractState(stockContract, Contract.State.Offered))
            {
                failureReason = "contract was created but could not enter Offered state";
                return false;
            }

            TrySetMember(configuredContract, "preLoaded", true);

            if (!TryAddToPreLoaderContracts(preLoader, stockContract))
            {
                failureReason = "could not add the final exam to Contract Configurator's Mission Control pending-contract list";
                return false;
            }

            try { GameEvents.Contract.onOffered.Fire(stockContract); } catch { }
            try { GameEvents.Contract.onContractsListChanged.Fire(); } catch { }

            RRLog.Verbose("[EAC] Offered graduation exam contract in Mission Control pending list: " + GetContractTitle(stockContract) +
                       " state=" + stockContract.ContractState +
                       " id=" + GetContractIdentifier(stockContract));
            return true;
        }

        private static string GetPreLoaderPendingSummary()
        {
            try
            {
                int total = 0;
                int eac = 0;
                List<string> titles = new List<string>();
                foreach (Contract c in EnumeratePreLoaderPendingContracts())
                {
                    if (c == null) continue;
                    total++;
                    string typeName;
                    if (IsEacGraduationContract(c, out typeName))
                    {
                        eac++;
                        titles.Add(GetContractTitle(c) + " [" + typeName + ", " + c.ContractState + "]");
                    }
                }
                return "total=" + total + ", eac=" + eac + (titles.Count > 0 ? ", titles=" + string.Join(" | ", titles.ToArray()) : "");
            }
            catch (Exception ex)
            {
                return "unavailable (" + ex.Message + ")";
            }
        }

        private static void TryForceContractGenerationPass()
        {
            try
            {
                Type preLoaderType = FindType("ContractConfigurator.ContractPreLoader");
                if (preLoaderType == null) return;

                MethodInfo force = preLoaderType.GetMethod("ForceContractGenerationPass", PublicStatic, null, Type.EmptyTypes, null);
                if (force != null) force.Invoke(null, null);

                object preLoader = GetContractPreLoaderInstance();
                if (preLoader != null)
                {
                    TryInvoke(preLoader, "ResetGenerationFailure");
                    TrySetMember(preLoader, "contractEnumerator", null);
                    TrySetNumericMember(preLoader, "lastGenerationFailure", -100.0);
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.ForceGeneration", "Suppressed", ex);
            }
        }

        private static void TryResetContractGenerationCooldown(object contractType)
        {
            if (contractType == null) return;
            TrySetNumericMember(contractType, "failedGenerationAttempts", 0);
            TrySetNumericMember(contractType, "lastGenerationFailure", -100.0);
        }

        private static bool TrySetNumericMember(object instance, string name, double value)
        {
            if (instance == null || string.IsNullOrEmpty(name)) return false;
            try
            {
                for (Type t = instance.GetType(); t != null; t = t.BaseType)
                {
                    PropertyInfo p = t.GetProperty(name, AnyInstance);
                    if (p != null && p.CanWrite)
                    {
                        object converted = Convert.ChangeType(value, p.PropertyType);
                        p.SetValue(instance, converted, null);
                        return true;
                    }

                    FieldInfo f = t.GetField(name, AnyInstance);
                    if (f != null)
                    {
                        object converted = Convert.ChangeType(value, f.FieldType);
                        f.SetValue(instance, converted);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static object GetContractPreLoaderInstance()
        {
            EnsureResolved();
            try
            {
                Type preLoaderType = FindType("ContractConfigurator.ContractPreLoader");
                if (preLoaderType == null) return null;

                PropertyInfo pi = preLoaderType.GetProperty("Instance", PublicStatic);
                if (pi != null)
                {
                    object instance = pi.GetValue(null, null);
                    if (instance != null) return instance;
                }

                FieldInfo fi = preLoaderType.GetField("Instance", PublicStatic);
                return fi != null ? fi.GetValue(null) : null;
            }
            catch { return null; }
        }

        private static IEnumerable<Contract> EnumeratePreLoaderPendingContracts()
        {
            object preLoader = GetContractPreLoaderInstance();
            if (preLoader == null) yield break;

            IEnumerable pending = null;
            try
            {
                MethodInfo method = preLoader.GetType().GetMethod("PendingContracts", AnyInstance, null, Type.EmptyTypes, null);
                if (method != null)
                    pending = method.Invoke(preLoader, null) as IEnumerable;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.PendingContracts", "Suppressed", ex);
            }

            if (pending == null) yield break;
            foreach (object item in pending)
            {
                var contract = item as Contract;
                if (contract != null) yield return contract;
            }
        }

        private static bool TryAddToPreLoaderContracts(object preLoader, Contract stockContract)
        {
            if (preLoader == null || stockContract == null) return false;

            try
            {
                for (Type t = preLoader.GetType(); t != null; t = t.BaseType)
                {
                    FieldInfo field = t.GetField("contracts", AnyInstance);
                    if (field == null) continue;

                    var list = field.GetValue(preLoader) as IList;
                    if (list == null) continue;

                    if (!list.Contains(stockContract))
                        list.Add(stockContract);
                    return true;
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.AddPendingContract", "Suppressed", ex);
            }

            return false;
        }

        private static bool ContractMatchesIdOrType(Contract contract, string expectedId, string expectedType)
        {
            if (contract == null) return false;

            string id = GetContractIdentifier(contract);
            if (!string.IsNullOrEmpty(expectedId) &&
                string.Equals(id, expectedId, StringComparison.Ordinal))
                return true;

            string type = NormalizeContractTypeName(GetContractTypeName(contract));
            return !string.IsNullOrEmpty(expectedType) &&
                   string.Equals(type, expectedType, StringComparison.Ordinal);
        }

        private static bool IsContractOfferedOrActive(Contract contract)
        {
            return contract != null &&
                   (contract.ContractState == Contract.State.Offered ||
                    contract.ContractState == Contract.State.Active);
        }

        private static bool IsContractActive(Contract contract)
        {
            return contract != null && contract.ContractState == Contract.State.Active;
        }

        private static bool TrySetContractState(Contract contract, Contract.State state)
        {
            if (contract == null) return false;

            // ConfiguredContract exposes a public new ContractState setter that calls SetState().
            if (TrySetMember(contract, "ContractState", state))
                return contract.ContractState == state;

            try
            {
                for (Type t = contract.GetType(); t != null; t = t.BaseType)
                {
                    MethodInfo method = t.GetMethods(AnyInstance)
                        .FirstOrDefault(m => m.Name == "SetState" &&
                                             m.GetParameters().Length == 1 &&
                                             m.GetParameters()[0].ParameterType == typeof(Contract.State));
                    if (method == null) continue;
                    method.Invoke(contract, new object[] { state });
                    return contract.ContractState == state;
                }
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.SetContractState", "Suppressed", ex);
            }

            return false;
        }

        private static bool IsInContractsCollection(object contractSystem, Contract stockContract)
        {
            if (contractSystem == null || stockContract == null) return false;
            try
            {
                object contracts = GetMemberValue(contractSystem, "Contracts");
                var list = contracts as IList;
                return list != null && list.Contains(stockContract);
            }
            catch { return false; }
        }

        private static bool TryAddToContractsCollection(object contractSystem, Contract stockContract)
        {
            try
            {
                object contracts = GetMemberValue(contractSystem, "Contracts");
                var list = contracts as IList;
                if (list != null && !list.Contains(stockContract))
                {
                    list.Add(stockContract);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool TryInvoke(object instance, string methodName, object arg = null)
        {
            return TryInvokeBoolish(instance, methodName, arg);
        }

        private static bool TryInvokeBoolish(object instance, string methodName, object arg = null)
        {
            if (instance == null || string.IsNullOrEmpty(methodName)) return false;
            try
            {
                MethodInfo method = null;
                for (Type t = instance.GetType(); t != null && method == null; t = t.BaseType)
                {
                    method = t.GetMethods(AnyInstance)
                        .FirstOrDefault(m => m.Name == methodName &&
                                             m.GetParameters().Length == (arg == null ? 0 : 1) &&
                                             (arg == null || m.GetParameters()[0].ParameterType.IsInstanceOfType(arg)));
                }

                if (method == null) return false;
                object result = method.Invoke(instance, arg == null ? null : new[] { arg });
                return method.ReturnType == typeof(bool) ? (bool)result : true;
            }
            catch (Exception ex)
            {
                RRLog.VerboseExceptionOnce("EAC.CCBridge.Invoke." + methodName, "Suppressed", ex);
                return false;
            }
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null || string.IsNullOrEmpty(name)) return null;
            for (Type t = instance.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name, AnyInstance);
                if (p != null) return p.GetValue(instance, null);
                FieldInfo f = t.GetField(name, AnyInstance);
                if (f != null) return f.GetValue(instance);
            }
            return null;
        }

        private static bool TrySetMember(object instance, string name, object value)
        {
            if (instance == null || string.IsNullOrEmpty(name)) return false;
            try
            {
                for (Type t = instance.GetType(); t != null; t = t.BaseType)
                {
                    PropertyInfo p = t.GetProperty(name, AnyInstance);
                    if (p != null && p.CanWrite)
                    {
                        p.SetValue(instance, value, null);
                        return true;
                    }

                    FieldInfo f = t.GetField(name, AnyInstance);
                    if (f != null)
                    {
                        f.SetValue(instance, value);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static string GetObjectIdentifier(object instance)
        {
            string[] names = { "ContractGuid", "contractGuid", "Guid", "guid", "ID", "id", "MissionSeed", "missionSeed" };
            foreach (string name in names)
            {
                try
                {
                    object value = GetMemberValue(instance, name);
                    if (value == null) continue;
                    string s = value.ToString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                catch { }
            }
            return instance.GetHashCode().ToString();
        }

        private static string NormalizeTrait(string trait)
        {
            if (string.IsNullOrEmpty(trait)) return "Pilot";
            if (string.Equals(trait, "Science", StringComparison.OrdinalIgnoreCase)) return "Scientist";
            if (string.Equals(trait, "Scientist", StringComparison.OrdinalIgnoreCase)) return "Scientist";
            if (string.Equals(trait, "Engineer", StringComparison.OrdinalIgnoreCase)) return "Engineer";
            if (string.Equals(trait, "Pilot", StringComparison.OrdinalIgnoreCase)) return "Pilot";
            return trait.Replace(" ", "");
        }

        private static Exception Unwrap(Exception ex)
        {
            var tie = ex as TargetInvocationException;
            return tie != null && tie.InnerException != null ? tie.InnerException : ex;
        }
    }
}

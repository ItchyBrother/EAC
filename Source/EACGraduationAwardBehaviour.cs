// EAC - Enhanced Astronaut Complex
// Optional Contract Configurator bridge behaviour and requirement.
// Build this into EAC_CCBridge.dll. This project intentionally lives outside EAC.dll
// so Contract Configurator remains an optional dependency.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Contracts;
using Contracts.Parameters;
using ContractConfigurator;
using ContractConfigurator.Parameters;
using UnityEngine;
using RosterRotation;

namespace RosterRotation.ContractConfiguratorIntegration
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class EACCCBridgeBootstrap : MonoBehaviour
    {
        private static EACCCBridgeBootstrap instance;

        private void Awake()
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            // Contract Configurator auto-registers EACGraduationExamPending and
            // EAC*Factory classes during its factory scan.  Do not register the
            // requirement here; doing so can race/duplicate CC's own registry.
            EACGraduationAwardFactory.RegisterAliases();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(instance, this))
                instance = null;
        }

        public static void ReconcileGraduationAwardsNow()
        {
            try { RosterRotationKSCUI.TryReconcileGraduationExamAwards(); }
            catch (Exception ex) { RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] Graduation reconciliation callback failed: " + ex.Message); }
        }

        public static void ScheduleReconcileGraduationAwards(float delaySeconds = 1f)
        {
            ReconcileGraduationAwardsNow();
            if (instance != null)
                instance.StartCoroutine(instance.ReconcileGraduationAwardsAfterDelay(delaySeconds));
        }

        private System.Collections.IEnumerator ReconcileGraduationAwardsAfterDelay(float delaySeconds)
        {
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);
            ReconcileGraduationAwardsNow();
        }
    }

    public sealed class EACGraduationExamPending : ContractRequirement
    {
        private static bool registered;
        private string trait;
        private int targetLevel;
        private string examId;
        private bool excludeRecentlyUsed = true;

        public static void RegisterRequirement()
        {
            if (registered) return;
            try
            {
                ContractRequirement.Register(typeof(EACGraduationExamPending), "EACGraduationExamPending");
                registered = true;
                RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] Registered Contract Configurator requirement EACGraduationExamPending.");
            }
            catch (Exception ex)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] Could not register EACGraduationExamPending requirement: " + ex.Message);
            }
        }

        public EACGraduationExamPending()
        {
            trait = "";
            targetLevel = 0;
            examId = "";
            excludeRecentlyUsed = true;
        }

        public override bool LoadFromConfig(ConfigNode configNode)
        {
            bool valid = base.LoadFromConfig(configNode);
            valid &= ConfigNodeUtil.ParseValue(configNode, "trait", x => trait = x, this, "");
            valid &= ConfigNodeUtil.ParseValue(configNode, "targetLevel", x => targetLevel = x, this, 0);
            valid &= ConfigNodeUtil.ParseValue(configNode, "examId", x => examId = x, this, "");
            valid &= ConfigNodeUtil.ParseValue(configNode, "excludeRecentlyUsed", x => excludeRecentlyUsed = x, this, true);
            return valid;
        }

        public override void OnSave(ConfigNode configNode)
        {
            if (!string.IsNullOrEmpty(trait)) configNode.AddValue("trait", trait);
            if (targetLevel > 0) configNode.AddValue("targetLevel", targetLevel);
            if (!string.IsNullOrEmpty(examId)) configNode.AddValue("examId", examId);
            configNode.AddValue("excludeRecentlyUsed", excludeRecentlyUsed);
        }

        public override void OnLoad(ConfigNode configNode)
        {
            trait = ConfigNodeUtil.ParseValue(configNode, "trait", "");
            targetLevel = ConfigNodeUtil.ParseValue(configNode, "targetLevel", 0);
            examId = ConfigNodeUtil.ParseValue(configNode, "examId", "");
            excludeRecentlyUsed = ConfigNodeUtil.ParseValue(configNode, "excludeRecentlyUsed", true);
        }

        public override bool RequirementMet(ConfiguredContract contract)
        {
            try
            {
                return RosterRotationKSCUI.TryPrepareGraduationExamContractFromCC(contract as Contract, trait, targetLevel, examId, excludeRecentlyUsed);
            }
            catch (Exception ex)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACGraduationExamPending requirement failed: " + ex.Message);
                return false;
            }
        }

        protected override string RequirementText()
        {
            return "An EAC final exam is pending for this trait and level";
        }
    }


    public sealed class EACPartInstalledFactory : ParameterFactory
    {
        private List<string> partNames = new List<string>();
        private int minCount = 1;
        private int maxCount = int.MaxValue;

        public override bool Load(ConfigNode configNode)
        {
            bool valid = base.Load(configNode);
            string configuredPart = string.Empty;
            valid &= ConfigNodeUtil.ParseValue(configNode, "part", x => configuredPart = x, this, string.Empty);
            partNames = string.IsNullOrEmpty(configuredPart) ? new List<string>() : new List<string> { configuredPart };
            valid &= ConfigNodeUtil.ParseValue(configNode, "minCount", x => minCount = x, this, 1, x => Validation.GE(x, 0));
            valid &= ConfigNodeUtil.ParseValue(configNode, "maxCount", x => maxCount = x, this, int.MaxValue, x => Validation.GE(x, 0));
            if (partNames.Count == 0)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACPartInstalled parameter has no part configured.");
                valid = false;
            }
            if (minCount < 0 || maxCount < 0 || maxCount < minCount)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACPartInstalled parameter has an invalid minCount/maxCount.");
                valid = false;
            }
            return valid;
        }

        public override ContractParameter Generate(Contract contract)
        {
            return new EACPartInstalled(partNames, minCount, maxCount, title);
        }

    }

    /// <summary>
    /// Contract Configurator vessel parameter that counts only parts attached to the
    /// physical vessel tree.  Stock PartValidation counts KSP inventory/cargo parts
    /// in this test article, so the battery/solar install checks were already green
    /// at launch.  This parameter stays incomplete until the EVA construction part is
    /// actually attached to the rover.
    /// </summary>
    public sealed class EACPartInstalled : VesselParameter
    {
        private List<string> partNames = new List<string>();
        private int minCount = 1;
        private int maxCount = int.MaxValue;

        public EACPartInstalled() : this(null, 1, int.MaxValue, null) { }

        public EACPartInstalled(IEnumerable<string> parts, int min, int max, string title) : base(title)
        {
            partNames = parts == null ? new List<string>() : parts.Where(x => !string.IsNullOrEmpty(x)).ToList();
            minCount = min;
            maxCount = max;
            this.title = string.IsNullOrEmpty(title) ? "Install the required part on the target vessel" : title;
            disableOnStateChange = true;
        }

        protected override void OnRegister()
        {
            base.OnRegister();
            GameEvents.onVesselWasModified.Add(new EventData<Vessel>.OnEvent(OnVesselWasModified));
        }

        protected override void OnUnregister()
        {
            base.OnUnregister();
            GameEvents.onVesselWasModified.Remove(new EventData<Vessel>.OnEvent(OnVesselWasModified));
        }

        private void OnVesselWasModified(Vessel vessel)
        {
            if (ReadyToComplete() && VesselMeetsCondition(vessel))
                SetState(ParameterState.Complete);
        }

        protected override void OnParameterSave(ConfigNode node)
        {
            foreach (string part in partNames)
                node.AddValue("part", part);
            node.AddValue("minCount", minCount);
            node.AddValue("maxCount", maxCount);
        }

        protected override void OnParameterLoad(ConfigNode node)
        {
            partNames = (node.GetValues("part") ?? new string[0]).Where(x => !string.IsNullOrEmpty(x)).ToList();
            minCount = ParseInt(node, "minCount", 1);
            maxCount = ParseInt(node, "maxCount", int.MaxValue);
        }

        protected override bool VesselMeetsCondition(Vessel vessel)
        {
            if (vessel == null || partNames == null || partNames.Count == 0) return false;
            int count = CountAttachedParts(vessel);
            return count >= minCount && count <= maxCount;
        }

        private int CountAttachedParts(Vessel vessel)
        {
            int count = 0;
            HashSet<Part> visited = new HashSet<Part>();
            if (vessel.rootPart != null)
            {
                CountAttachedPartsRecursive(vessel.rootPart, visited, ref count);
            }
            else if (vessel.parts != null)
            {
                foreach (Part part in vessel.parts)
                {
                    if (part != null && part.parent != null)
                        CountAttachedPartsRecursive(part, visited, ref count);
                }
            }
            return count;
        }

        private void CountAttachedPartsRecursive(Part part, HashSet<Part> visited, ref int count)
        {
            if (part == null || visited.Contains(part)) return;
            visited.Add(part);
            if (PartMatches(part)) count++;
            if (part.children == null) return;
            foreach (Part child in part.children)
                CountAttachedPartsRecursive(child, visited, ref count);
        }

        private bool PartMatches(Part part)
        {
            string actual = EACBridgePartUtil.NormalizePartName(part != null && part.partInfo != null ? part.partInfo.name : part != null ? part.name : "");
            foreach (string expected in partNames)
            {
                if (EACBridgePartUtil.PartNameMatches(actual, expected)) return true;
            }
            return false;
        }

        private static int ParseInt(ConfigNode node, string key, int fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            int value;
            return int.TryParse(node.GetValue(key), out value) ? value : fallback;
        }
    }

    public sealed class EACRecoverVesselWithPartFactory : ParameterFactory
    {
        private List<string> partNames = new List<string>();
        private int minCount = 1;
        private bool ignoreEva = true;
        private string vesselNameContains = string.Empty;

        public override bool Load(ConfigNode configNode)
        {
            bool valid = base.Load(configNode);
            string configuredPart = string.Empty;
            valid &= ConfigNodeUtil.ParseValue(configNode, "part", x => configuredPart = x, this, string.Empty);
            partNames = string.IsNullOrEmpty(configuredPart) ? new List<string>() : new List<string> { configuredPart };
            valid &= ConfigNodeUtil.ParseValue(configNode, "minCount", x => minCount = x, this, 1, x => Validation.GE(x, 1));
            valid &= ConfigNodeUtil.ParseValue(configNode, "ignoreEva", x => ignoreEva = x, this, true);
            valid &= ConfigNodeUtil.ParseValue(configNode, "vesselNameContains", x => vesselNameContains = x, this, string.Empty);
            if (partNames.Count == 0)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACRecoverVesselWithPart parameter has no part configured.");
                valid = false;
            }
            if (minCount < 1)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACRecoverVesselWithPart parameter minCount must be at least 1.");
                valid = false;
            }
            return valid;
        }

        public override ContractParameter Generate(Contract contract)
        {
            return new EACRecoverVesselWithPart(partNames, minCount, ignoreEva, vesselNameContains, title);
        }

    }

    /// <summary>
    /// Completion parameter for rover inspections/service drills.  RecoverVessel can
    /// be satisfied by a recovered EVA Kerbal in some flows, so this parameter waits
    /// for a recovered vessel that actually contains the rover body/test article part.
    /// </summary>
    public sealed class EACRecoverVesselWithPart : ContractConfiguratorParameter
    {
        private List<string> partNames = new List<string>();
        private int minCount = 1;
        private bool ignoreEva = true;
        private string vesselNameContains = string.Empty;

        public EACRecoverVesselWithPart() : this(null, 1, true, string.Empty, null) { }

        public EACRecoverVesselWithPart(IEnumerable<string> parts, int min, bool ignoreEvaVessels, string nameContains, string title) : base(title)
        {
            partNames = parts == null ? new List<string>() : parts.Where(x => !string.IsNullOrEmpty(x)).ToList();
            minCount = Math.Max(1, min);
            ignoreEva = ignoreEvaVessels;
            vesselNameContains = nameContains ?? string.Empty;
            this.title = string.IsNullOrEmpty(title) ? "Recover the required EAC test vessel" : title;
            disableOnStateChange = true;
        }

        protected override void OnRegister()
        {
            base.OnRegister();
            GameEvents.onVesselRecovered.Add(new EventData<ProtoVessel, bool>.OnEvent(OnVesselRecovered));
        }

        protected override void OnUnregister()
        {
            base.OnUnregister();
            GameEvents.onVesselRecovered.Remove(new EventData<ProtoVessel, bool>.OnEvent(OnVesselRecovered));
        }

        protected override void OnParameterSave(ConfigNode node)
        {
            foreach (string part in partNames)
                node.AddValue("part", part);
            node.AddValue("minCount", minCount);
            node.AddValue("ignoreEva", ignoreEva);
            if (!string.IsNullOrEmpty(vesselNameContains)) node.AddValue("vesselNameContains", vesselNameContains);
        }

        protected override void OnParameterLoad(ConfigNode node)
        {
            partNames = (node.GetValues("part") ?? new string[0]).Where(x => !string.IsNullOrEmpty(x)).ToList();
            minCount = ParseInt(node, "minCount", 1);
            ignoreEva = ParseBool(node, "ignoreEva", true);
            vesselNameContains = node.HasValue("vesselNameContains") ? node.GetValue("vesselNameContains") ?? string.Empty : string.Empty;
        }

        private void OnVesselRecovered(ProtoVessel protoVessel, bool quick)
        {
            if (!ReadyToComplete() || protoVessel == null) return;
            if (ignoreEva && protoVessel.vesselType == VesselType.EVA) return;
            if (!string.IsNullOrEmpty(vesselNameContains) &&
                (string.IsNullOrEmpty(protoVessel.vesselName) || protoVessel.vesselName.IndexOf(vesselNameContains, StringComparison.OrdinalIgnoreCase) < 0))
                return;

            int count = CountRecoveredParts(protoVessel);
            if (count >= minCount)
                SetState(ParameterState.Complete);
        }

        private int CountRecoveredParts(ProtoVessel protoVessel)
        {
            int count = 0;
            if (protoVessel.protoPartSnapshots == null) return count;
            foreach (ProtoPartSnapshot part in protoVessel.protoPartSnapshots)
            {
                string actual = EACBridgePartUtil.NormalizePartName(part != null ? part.partName : "");
                foreach (string expected in partNames)
                {
                    if (EACBridgePartUtil.PartNameMatches(actual, expected))
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        private static int ParseInt(ConfigNode node, string key, int fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            int value;
            return int.TryParse(node.GetValue(key), out value) ? value : fallback;
        }

        private static bool ParseBool(ConfigNode node, string key, bool fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            bool value;
            return bool.TryParse(node.GetValue(key), out value) ? value : fallback;
        }
    }



    public sealed class EACFlightCheckpointFactory : ParameterFactory
    {
        private double latitude;
        private double longitude;
        private double radius = 5000.0;
        private string situation = string.Empty;
        private double minAltitude = double.MinValue;
        private double maxAltitude = double.MaxValue;
        private double minSpeed = double.MinValue;
        private double maxSpeed = double.MaxValue;

        public override bool Load(ConfigNode configNode)
        {
            bool valid = base.Load(configNode);
            valid &= ConfigNodeUtil.ParseValue(configNode, "latitude", x => latitude = x, this, 0.0);
            valid &= ConfigNodeUtil.ParseValue(configNode, "longitude", x => longitude = x, this, 0.0);
            valid &= ConfigNodeUtil.ParseValue(configNode, "radius", x => radius = x, this, 5000.0, x => Validation.GT(x, 0.0));
            valid &= ConfigNodeUtil.ParseValue(configNode, "situation", x => situation = x, this, string.Empty);
            valid &= ConfigNodeUtil.ParseValue(configNode, "minAltitude", x => minAltitude = x, this, double.MinValue);
            valid &= ConfigNodeUtil.ParseValue(configNode, "maxAltitude", x => maxAltitude = x, this, double.MaxValue);
            valid &= ConfigNodeUtil.ParseValue(configNode, "minSpeed", x => minSpeed = x, this, double.MinValue);
            valid &= ConfigNodeUtil.ParseValue(configNode, "maxSpeed", x => maxSpeed = x, this, double.MaxValue);
            return valid;
        }

        public override ContractParameter Generate(Contract contract)
        {
            return new EACFlightCheckpoint(latitude, longitude, radius, situation, minAltitude, maxAltitude, minSpeed, maxSpeed, title);
        }
    }

    /// <summary>
    /// Vessel checkpoint used by Level 2/3 pilot exams.  This avoids depending on
    /// waypoint-generation syntax while still letting Contract Configurator own the
    /// visible objective and completion state.
    /// </summary>
    public sealed class EACFlightCheckpoint : VesselParameter
    {
        private double latitude;
        private double longitude;
        private double radius = 5000.0;
        private string situation = string.Empty;
        private double minAltitude = double.MinValue;
        private double maxAltitude = double.MaxValue;
        private double minSpeed = double.MinValue;
        private double maxSpeed = double.MaxValue;

        public EACFlightCheckpoint() : this(0.0, 0.0, 5000.0, string.Empty, double.MinValue, double.MaxValue, double.MinValue, double.MaxValue, null) { }

        public EACFlightCheckpoint(double lat, double lon, double radiusMeters, string requiredSituation, double minAlt, double maxAlt, double minSrfSpeed, double maxSrfSpeed, string title) : base(title)
        {
            latitude = lat;
            longitude = lon;
            radius = Math.Max(1.0, radiusMeters);
            situation = requiredSituation ?? string.Empty;
            minAltitude = minAlt;
            maxAltitude = maxAlt;
            minSpeed = minSrfSpeed;
            maxSpeed = maxSrfSpeed;
            this.title = string.IsNullOrEmpty(title) ? "Reach the EAC flight checkpoint" : title;
            disableOnStateChange = true;
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (ReadyToComplete() && VesselMeetsCondition(FlightGlobals.ActiveVessel))
                SetState(ParameterState.Complete);
        }

        protected override void OnParameterSave(ConfigNode node)
        {
            node.AddValue("latitude", latitude);
            node.AddValue("longitude", longitude);
            node.AddValue("radius", radius);
            if (!string.IsNullOrEmpty(situation)) node.AddValue("situation", situation);
            if (minAltitude != double.MinValue) node.AddValue("minAltitude", minAltitude);
            if (maxAltitude != double.MaxValue) node.AddValue("maxAltitude", maxAltitude);
            if (minSpeed != double.MinValue) node.AddValue("minSpeed", minSpeed);
            if (maxSpeed != double.MaxValue) node.AddValue("maxSpeed", maxSpeed);
        }

        protected override void OnParameterLoad(ConfigNode node)
        {
            latitude = ParseDouble(node, "latitude", 0.0);
            longitude = ParseDouble(node, "longitude", 0.0);
            radius = Math.Max(1.0, ParseDouble(node, "radius", 5000.0));
            situation = node.HasValue("situation") ? node.GetValue("situation") ?? string.Empty : string.Empty;
            minAltitude = ParseDouble(node, "minAltitude", double.MinValue);
            maxAltitude = ParseDouble(node, "maxAltitude", double.MaxValue);
            minSpeed = ParseDouble(node, "minSpeed", double.MinValue);
            maxSpeed = ParseDouble(node, "maxSpeed", double.MaxValue);
        }

        protected override bool VesselMeetsCondition(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null || !vessel.mainBody.isHomeWorld) return false;
            if (!SituationMatches(vessel)) return false;
            if (vessel.altitude < minAltitude || vessel.altitude > maxAltitude) return false;
            if (vessel.srfSpeed < minSpeed || vessel.srfSpeed > maxSpeed) return false;
            return EACBridgeNavigationUtil.SurfaceDistanceMeters(vessel.mainBody, vessel.latitude, vessel.longitude, latitude, longitude) <= radius;
        }

        private bool SituationMatches(Vessel vessel)
        {
            if (string.IsNullOrEmpty(situation)) return true;
            Vessel.Situations parsed;
            if (!TryParseSituation(situation, out parsed)) return true;
            return vessel.situation == parsed;
        }

        private static bool TryParseSituation(string value, out Vessel.Situations situation)
        {
            situation = Vessel.Situations.PRELAUNCH;
            if (string.IsNullOrEmpty(value)) return false;
            try
            {
                situation = (Vessel.Situations)Enum.Parse(typeof(Vessel.Situations), value, true);
                return true;
            }
            catch { return false; }
        }

        private static double ParseDouble(ConfigNode node, string key, double fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            double value;
            string raw = node.GetValue(key);
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return value;
            if (double.TryParse(raw, out value)) return value;
            return fallback;
        }
    }

    public sealed class EACDriveDistanceFactory : ParameterFactory
    {
        private double distance = 250.0;
        private bool ignoreEva = true;

        public override bool Load(ConfigNode configNode)
        {
            bool valid = base.Load(configNode);
            valid &= ConfigNodeUtil.ParseValue(configNode, "distance", x => distance = x, this, 250.0, x => Validation.GT(x, 0.0));
            valid &= ConfigNodeUtil.ParseValue(configNode, "ignoreEva", x => ignoreEva = x, this, true);
            return valid;
        }

        public override ContractParameter Generate(Contract contract)
        {
            return new EACDriveDistance(distance, ignoreEva, title);
        }
    }

    /// <summary>
    /// Tracks surface distance travelled by the active rover.  It is intentionally
    /// simple and stock-friendly: no waypoints, no DLC, just drive away from the
    /// launch point and recover the provided rover test article.
    /// </summary>
    public sealed class EACDriveDistance : VesselParameter
    {
        private double distance = 250.0;
        private bool ignoreEva = true;
        private bool hasStart;
        private double startLat;
        private double startLon;

        public EACDriveDistance() : this(250.0, true, null) { }

        public EACDriveDistance(double distanceMeters, bool ignoreEvaVessels, string title) : base(title)
        {
            distance = Math.Max(1.0, distanceMeters);
            ignoreEva = ignoreEvaVessels;
            this.title = string.IsNullOrEmpty(title) ? "Drive the assigned EAC test vessel" : title;
            disableOnStateChange = true;
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (ReadyToComplete() && VesselMeetsCondition(FlightGlobals.ActiveVessel))
                SetState(ParameterState.Complete);
        }

        protected override void OnParameterSave(ConfigNode node)
        {
            node.AddValue("distance", distance);
            node.AddValue("ignoreEva", ignoreEva);
            node.AddValue("hasStart", hasStart);
            if (hasStart)
            {
                node.AddValue("startLat", startLat);
                node.AddValue("startLon", startLon);
            }
        }

        protected override void OnParameterLoad(ConfigNode node)
        {
            distance = Math.Max(1.0, ParseDouble(node, "distance", 250.0));
            ignoreEva = ParseBool(node, "ignoreEva", true);
            hasStart = ParseBool(node, "hasStart", false);
            startLat = ParseDouble(node, "startLat", 0.0);
            startLon = ParseDouble(node, "startLon", 0.0);
        }

        protected override bool VesselMeetsCondition(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null || !vessel.mainBody.isHomeWorld) return false;
            if (ignoreEva && vessel.vesselType == VesselType.EVA) return false;
            if (vessel.situation == Vessel.Situations.FLYING || vessel.situation == Vessel.Situations.ORBITING || vessel.situation == Vessel.Situations.SUB_ORBITAL || vessel.situation == Vessel.Situations.ESCAPING) return false;

            if (!hasStart)
            {
                hasStart = true;
                startLat = vessel.latitude;
                startLon = vessel.longitude;
                return false;
            }

            return EACBridgeNavigationUtil.SurfaceDistanceMeters(vessel.mainBody, vessel.latitude, vessel.longitude, startLat, startLon) >= distance;
        }

        private static double ParseDouble(ConfigNode node, string key, double fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            double value;
            string raw = node.GetValue(key);
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) return value;
            if (double.TryParse(raw, out value)) return value;
            return fallback;
        }

        private static bool ParseBool(ConfigNode node, string key, bool fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            bool value;
            return bool.TryParse(node.GetValue(key), out value) ? value : fallback;
        }
    }


    public sealed class EACAssignedKerbalPresentFactory : ParameterFactory
    {
        private string kerbalName = string.Empty;

        public override bool Load(ConfigNode configNode)
        {
            bool valid = base.Load(configNode);
            valid &= ConfigNodeUtil.ParseValue(configNode, "kerbal", x => kerbalName = x, this, string.Empty);
            return valid;
        }

        public override ContractParameter Generate(Contract contract)
        {
            string resolvedKerbal = string.IsNullOrEmpty(kerbalName)
                ? GetUniqueDataString(contract, "eacKerbal")
                : kerbalName;
            return new EACAssignedKerbalPresent(resolvedKerbal, title);
        }

        private static string GetUniqueDataString(Contract contract, string key)
        {
            if (contract == null || string.IsNullOrEmpty(key)) return string.Empty;
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                for (Type t = contract.GetType(); t != null; t = t.BaseType)
                {
                    PropertyInfo p = t.GetProperty("uniqueData", flags);
                    object value = p != null ? p.GetValue(contract, null) : null;
                    if (value == null)
                    {
                        FieldInfo f = t.GetField("uniqueData", flags);
                        value = f != null ? f.GetValue(contract) : null;
                    }

                    var dict = value as System.Collections.IDictionary;
                    if (dict == null || !dict.Contains(key)) continue;
                    object found = dict[key];
                    return found != null ? found.ToString() : string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }
    }

    /// <summary>
    /// Visible safety check for all EAC graduation exams.  The parameter only completes
    /// while the active vessel/EVA contains the Kerbal that EAC assigned to the contract.
    /// This prevents a wrong-Kerbal mission from satisfying the final exam checklist and
    /// also gives testers a clear "Correct Kerbal assigned" line in the contract UI.
    /// </summary>
    public sealed class EACAssignedKerbalPresent : VesselParameter
    {
        private string assignedKerbalName = string.Empty;

        public EACAssignedKerbalPresent() : this(string.Empty, null) { }

        public EACAssignedKerbalPresent(string kerbalName, string title) : base(title)
        {
            assignedKerbalName = kerbalName ?? string.Empty;
            this.title = string.IsNullOrEmpty(title) ? BuildTitle(assignedKerbalName) : title;
            disableOnStateChange = false;
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (!ReadyToComplete() || string.IsNullOrEmpty(assignedKerbalName)) return;

            Vessel vessel = FlightGlobals.ActiveVessel;
            bool present = VesselMeetsCondition(vessel);
            if (present && State != ParameterState.Complete)
            {
                SetState(ParameterState.Complete);
            }
            else if (!present && vessel != null && State == ParameterState.Complete)
            {
                // Keep this as a live assignment check.  If the player switches to a
                // wrong-Kerbal vessel before finishing the contract, the line reopens.
                SetState(ParameterState.Incomplete);
            }
        }

        protected override void OnParameterSave(ConfigNode node)
        {
            if (!string.IsNullOrEmpty(assignedKerbalName)) node.AddValue("kerbal", assignedKerbalName);
        }

        protected override void OnParameterLoad(ConfigNode node)
        {
            assignedKerbalName = node != null && node.HasValue("kerbal") ? node.GetValue("kerbal") ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(title)) title = BuildTitle(assignedKerbalName);
        }

        protected override bool VesselMeetsCondition(Vessel vessel)
        {
            if (vessel == null || string.IsNullOrEmpty(assignedKerbalName)) return false;
            try
            {
                foreach (ProtoCrewMember crew in vessel.GetVesselCrew())
                {
                    if (crew != null && string.Equals(crew.name, assignedKerbalName, StringComparison.Ordinal))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static string BuildTitle(string kerbalName)
        {
            return string.IsNullOrEmpty(kerbalName)
                ? "Correct Kerbal assigned"
                : "Correct Kerbal assigned: " + kerbalName;
        }
    }

    public sealed class EACPassengerPresentFactory : ParameterFactory
    {
        private List<string> traits = new List<string>();
        private int minCount = 1;
        private int minExperience = 0;
        private int maxExperience = 5;

        public override bool Load(ConfigNode configNode)
        {
            bool valid = base.Load(configNode);

            // Read via ConfigNodeUtil so Contract Configurator marks the attribute as
            // consumed.  Reading only configNode.GetValues("trait") works functionally,
            // but CC reports it as an unexpected/ignored attribute in the log.
            string parsedTrait = string.Empty;
            valid &= ConfigNodeUtil.ParseValue(configNode, "trait", x => parsedTrait = x, this, string.Empty);

            traits = (configNode.GetValues("trait") ?? new string[0])
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();
            if (traits.Count == 0 && !string.IsNullOrEmpty(parsedTrait))
                traits.Add(parsedTrait);

            valid &= ConfigNodeUtil.ParseValue(configNode, "minCount", x => minCount = x, this, 1, x => Validation.GE(x, 1));
            valid &= ConfigNodeUtil.ParseValue(configNode, "minExperience", x => minExperience = x, this, 0, x => Validation.GE(x, 0));
            valid &= ConfigNodeUtil.ParseValue(configNode, "maxExperience", x => maxExperience = x, this, 5, x => Validation.GE(x, 0));
            if (traits.Count == 0)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] EACPassengerPresent parameter has no passenger trait configured.");
                valid = false;
            }
            return valid;
        }

        public override ContractParameter Generate(Contract contract)
        {
            return new EACPassengerPresent(traits, minCount, minExperience, maxExperience, title);
        }
    }

    /// <summary>
    /// Passenger check for Level 3 pilot exams.  It deliberately avoids the stock
    /// HasCrew parameter because EAC personalizes all HasCrew parameters to the
    /// assigned trainee; this one must continue to look for a separate Scientist or
    /// Engineer passenger.
    /// </summary>
    public sealed class EACPassengerPresent : VesselParameter
    {
        private List<string> traits = new List<string>();
        private int minCount = 1;
        private int minExperience = 0;
        private int maxExperience = 5;

        public EACPassengerPresent() : this(null, 1, 0, 5, null) { }

        public EACPassengerPresent(IEnumerable<string> passengerTraits, int requiredCount, int minLevel, int maxLevel, string title) : base(title)
        {
            traits = passengerTraits == null ? new List<string>() : passengerTraits.Where(x => !string.IsNullOrEmpty(x)).Select(NormalizeTrait).ToList();
            minCount = Math.Max(1, requiredCount);
            minExperience = Math.Max(0, minLevel);
            maxExperience = Math.Max(minExperience, maxLevel);
            this.title = string.IsNullOrEmpty(title) ? "Carry the required EAC passenger" : title;
            disableOnStateChange = true;
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (ReadyToComplete() && VesselMeetsCondition(FlightGlobals.ActiveVessel))
                SetState(ParameterState.Complete);
        }

        protected override void OnParameterSave(ConfigNode node)
        {
            foreach (string trait in traits) node.AddValue("trait", trait);
            node.AddValue("minCount", minCount);
            node.AddValue("minExperience", minExperience);
            node.AddValue("maxExperience", maxExperience);
        }

        protected override void OnParameterLoad(ConfigNode node)
        {
            traits = (node.GetValues("trait") ?? new string[0]).Where(x => !string.IsNullOrEmpty(x)).Select(NormalizeTrait).ToList();
            minCount = ParseInt(node, "minCount", 1);
            minExperience = ParseInt(node, "minExperience", 0);
            maxExperience = Math.Max(minExperience, ParseInt(node, "maxExperience", 5));
        }

        protected override bool VesselMeetsCondition(Vessel vessel)
        {
            if (vessel == null || traits == null || traits.Count == 0) return false;
            int count = 0;
            foreach (ProtoCrewMember crew in vessel.GetVesselCrew())
            {
                if (crew == null) continue;
                string trait = NormalizeTrait(crew.trait);
                if (!traits.Contains(trait)) continue;
                int level = (int)crew.experienceLevel;
                if (level < minExperience || level > maxExperience) continue;
                count++;
            }
            return count >= minCount;
        }

        private static string NormalizeTrait(string trait)
        {
            if (string.IsNullOrEmpty(trait)) return string.Empty;
            if (string.Equals(trait, "Pilots", StringComparison.OrdinalIgnoreCase)) return "Pilot";
            if (string.Equals(trait, "Engineers", StringComparison.OrdinalIgnoreCase)) return "Engineer";
            if (string.Equals(trait, "Scientists", StringComparison.OrdinalIgnoreCase)) return "Scientist";
            if (trait.Length == 0) return string.Empty;
            return char.ToUpperInvariant(trait[0]) + (trait.Length > 1 ? trait.Substring(1) : string.Empty);
        }

        private static int ParseInt(ConfigNode node, string key, int fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            int value;
            return int.TryParse(node.GetValue(key), out value) ? value : fallback;
        }
    }

    internal static class EACBridgeNavigationUtil
    {
        public static double SurfaceDistanceMeters(CelestialBody body, double latA, double lonA, double latB, double lonB)
        {
            double radius = body != null ? body.Radius : 600000.0;
            double lat1 = latA * Math.PI / 180.0;
            double lat2 = latB * Math.PI / 180.0;
            double dLat = (latB - latA) * Math.PI / 180.0;
            double dLon = NormalizeLongitudeDelta(lonB - lonA) * Math.PI / 180.0;
            double sinLat = Math.Sin(dLat / 2.0);
            double sinLon = Math.Sin(dLon / 2.0);
            double a = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0.0, 1.0 - a)));
            return radius * c;
        }

        private static double NormalizeLongitudeDelta(double delta)
        {
            while (delta > 180.0) delta -= 360.0;
            while (delta < -180.0) delta += 360.0;
            return delta;
        }
    }

    internal static class EACBridgePartUtil
    {
        public static string NormalizePartName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string output = name.Replace("(Clone)", "").Trim();
            return output;
        }

        public static bool PartNameMatches(string actual, string expected)
        {
            actual = NormalizePartName(actual);
            expected = NormalizePartName(expected);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            return actual.StartsWith(expected + "_", StringComparison.OrdinalIgnoreCase);
        }
    }


    public sealed class EACGraduationAwardFactory : BehaviourFactory
    {
        private static bool registeredAliases;
        private string scenario = string.Empty;
        private string trait = string.Empty;
        private int targetLevel;
        private string examId = string.Empty;

        public static void RegisterAliases()
        {
            if (registeredAliases) return;
            try
            {
                // Contract Configurator automatically registers BehaviourFactory classes by
                // stripping the "Factory" suffix, so EACGraduationAwardFactory handles
                // type = EACGraduationAward.  Keep the fully qualified alias for older
                // beta cfg copies that used the implementation class name directly.
                BehaviourFactory.Register(typeof(EACGraduationAwardFactory), typeof(EACGraduationAward).FullName);
                registeredAliases = true;
                RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] Registered Contract Configurator behaviour EACGraduationAward alias " + typeof(EACGraduationAward).FullName + ".");
            }
            catch (Exception ex)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] Could not register EACGraduationAward behaviour alias: " + ex.Message);
            }
        }

        public override bool Load(ConfigNode configNode)
        {
            bool valid = base.Load(configNode);
            valid &= ConfigNodeUtil.ParseValue(configNode, "scenario", x => scenario = x, this, string.Empty);
            valid &= ConfigNodeUtil.ParseValue(configNode, "eacScenario", x => scenario = x, this, scenario);
            valid &= ConfigNodeUtil.ParseValue(configNode, "trait", x => trait = x, this, string.Empty);
            valid &= ConfigNodeUtil.ParseValue(configNode, "targetLevel", x => targetLevel = x, this, 0);
            valid &= ConfigNodeUtil.ParseValue(configNode, "examId", x => examId = x, this, string.Empty);
            return valid;
        }

        public override ContractBehaviour Generate(ConfiguredContract contract)
        {
            return new EACGraduationAward(scenario, trait, targetLevel, examId);
        }
    }

    public sealed class EACGraduationAward : ContractBehaviour
    {
        private string scenario = string.Empty;
        private string configuredTrait = string.Empty;
        private int configuredTargetLevel;
        private string configuredExamId = string.Empty;
        private string assignedKerbalName = string.Empty;
        private string assignedKerbalIdentity = string.Empty;
        private int assignedTargetLevel;
        private string assignedContractType = string.Empty;
        private string assignedExamId = string.Empty;
        private bool scenarioSatisfied;
        private bool phase1;
        private bool phase2;
        private bool phase3;
        private string trackedVesselId = string.Empty;
        private int baselinePartCount = -1;
        private int baselineBatteryCount = -1;
        private int baselineSolarPanelCount = -1;
        private double startLat;
        private double startLon;
        private bool hasStartPosition;

        public EACGraduationAward()
        {
        }

        public EACGraduationAward(string scenarioId) : this(scenarioId, string.Empty, 0, string.Empty) { }

        public EACGraduationAward(string scenarioId, string trait, int targetLevel, string examId)
        {
            scenario = scenarioId ?? string.Empty;
            configuredTrait = trait ?? string.Empty;
            configuredTargetLevel = targetLevel;
            configuredExamId = examId ?? string.Empty;
        }

        protected override void OnOffered()
        {
            CacheAssignmentMetadata();
            try
            {
                RosterRotationKSCUI.HandleGraduationContractOffered(contract as Contract);
            }
            catch (Exception ex)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] Contract Configurator graduation offer bridge failed: " + ex.Message);
            }
        }

        protected override void OnAccepted()
        {
            CacheAssignmentMetadata();
            try
            {
                RosterRotationKSCUI.HandleGraduationContractAccepted(contract as Contract);
            }
            catch (Exception ex)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] Contract Configurator graduation acceptance bridge failed: " + ex.Message);
            }
        }

        protected override void OnUpdate()
        {
            // Contract Configurator owns objective detection and contract completion.
            // EAC only observes CC lifecycle events and reconciles EAC state after CC
            // has completed the contract and awarded experience.
        }

        protected override void OnCompleted()
        {
            CacheAssignmentMetadata();
            try
            {
                RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] Contract Configurator graduation contract completed for " + DisplayKerbalName() +
                          " -> L" + assignedTargetLevel +
                          (string.IsNullOrEmpty(assignedExamId) ? "" : " examId=" + assignedExamId) +
                          "; EAC will reconcile after CC applies experience.");
                RosterRotationKSCUI.TryRebindGraduationAwardToKerbal(contract as Contract, assignedKerbalName);
                RosterRotationKSCUI.HandleGraduationContractCompletedForKerbal(assignedKerbalName, assignedTargetLevel, assignedContractType, GetContractIdentifier(), assignedExamId, assignedKerbalIdentity);
                EACCCBridgeBootstrap.ScheduleReconcileGraduationAwards(1.0f);
            }
            catch (Exception ex)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] Contract Configurator graduation reconciliation bridge failed: " + ex.Message);
            }
        }

        protected override void OnCancelled() { ResetExam(); }
        protected override void OnDeclined() { ResetExam(); }
        protected override void OnFailed() { ResetExam(); }
        protected override void OnDeadlineExpired() { ResetExam(); }
        protected override void OnOfferExpired() { ResetExam(); }
        protected override void OnWithdrawn() { }

        protected override void OnRegister()
        {
            CacheAssignmentMetadata();
        }

        protected override void OnUnregister()
        {
        }

        protected override void OnLoad(ConfigNode configNode)
        {
            if (configNode == null) return;
            if (configNode.HasValue("scenario")) scenario = configNode.GetValue("scenario") ?? string.Empty;
            if (configNode.HasValue("eacScenario")) scenario = configNode.GetValue("eacScenario") ?? scenario;
            if (configNode.HasValue("eacTrait")) configuredTrait = configNode.GetValue("eacTrait") ?? configuredTrait;
            if (configNode.HasValue("eacTargetLevel")) int.TryParse(configNode.GetValue("eacTargetLevel"), out configuredTargetLevel);
            if (configNode.HasValue("eacExamId")) configuredExamId = configNode.GetValue("eacExamId") ?? configuredExamId;
            if (configNode.HasValue("eacScenarioSatisfied")) bool.TryParse(configNode.GetValue("eacScenarioSatisfied"), out scenarioSatisfied);
            if (configNode.HasValue("eacPhase1")) bool.TryParse(configNode.GetValue("eacPhase1"), out phase1);
            if (configNode.HasValue("eacPhase2")) bool.TryParse(configNode.GetValue("eacPhase2"), out phase2);
            if (configNode.HasValue("eacPhase3")) bool.TryParse(configNode.GetValue("eacPhase3"), out phase3);
            if (configNode.HasValue("eacTrackedVesselId")) trackedVesselId = configNode.GetValue("eacTrackedVesselId") ?? string.Empty;
            if (configNode.HasValue("eacBaselinePartCount")) int.TryParse(configNode.GetValue("eacBaselinePartCount"), out baselinePartCount);
            if (configNode.HasValue("eacBaselineBatteryCount")) int.TryParse(configNode.GetValue("eacBaselineBatteryCount"), out baselineBatteryCount);
            if (configNode.HasValue("eacBaselineSolarPanelCount")) int.TryParse(configNode.GetValue("eacBaselineSolarPanelCount"), out baselineSolarPanelCount);
            if (configNode.HasValue("eacStartLat")) double.TryParse(configNode.GetValue("eacStartLat"), out startLat);
            if (configNode.HasValue("eacStartLon")) double.TryParse(configNode.GetValue("eacStartLon"), out startLon);
            if (configNode.HasValue("eacHasStartPosition")) bool.TryParse(configNode.GetValue("eacHasStartPosition"), out hasStartPosition);
            if (configNode.HasValue("eacAssignedKerbal")) assignedKerbalName = configNode.GetValue("eacAssignedKerbal") ?? string.Empty;
            if (configNode.HasValue("eacAssignedKerbalIdentity")) assignedKerbalIdentity = configNode.GetValue("eacAssignedKerbalIdentity") ?? string.Empty;
            if (configNode.HasValue("eacAssignedTargetLevel")) int.TryParse(configNode.GetValue("eacAssignedTargetLevel"), out assignedTargetLevel);
            if (configNode.HasValue("eacAssignedContractType")) assignedContractType = configNode.GetValue("eacAssignedContractType") ?? string.Empty;
            if (configNode.HasValue("eacAssignedExamId")) assignedExamId = configNode.GetValue("eacAssignedExamId") ?? string.Empty;
        }

        protected override void OnSave(ConfigNode configNode)
        {
            if (configNode == null) return;
            if (!string.IsNullOrEmpty(scenario)) configNode.AddValue("eacScenario", scenario);
            if (!string.IsNullOrEmpty(configuredTrait)) configNode.AddValue("eacTrait", configuredTrait);
            if (configuredTargetLevel > 0) configNode.AddValue("eacTargetLevel", configuredTargetLevel);
            if (!string.IsNullOrEmpty(configuredExamId)) configNode.AddValue("eacExamId", configuredExamId);
            configNode.AddValue("eacScenarioSatisfied", scenarioSatisfied);
            configNode.AddValue("eacPhase1", phase1);
            configNode.AddValue("eacPhase2", phase2);
            configNode.AddValue("eacPhase3", phase3);
            if (!string.IsNullOrEmpty(trackedVesselId)) configNode.AddValue("eacTrackedVesselId", trackedVesselId);
            if (baselinePartCount >= 0) configNode.AddValue("eacBaselinePartCount", baselinePartCount);
            if (baselineBatteryCount >= 0) configNode.AddValue("eacBaselineBatteryCount", baselineBatteryCount);
            if (baselineSolarPanelCount >= 0) configNode.AddValue("eacBaselineSolarPanelCount", baselineSolarPanelCount);
            if (hasStartPosition)
            {
                configNode.AddValue("eacStartLat", startLat);
                configNode.AddValue("eacStartLon", startLon);
                configNode.AddValue("eacHasStartPosition", hasStartPosition);
            }
            if (!string.IsNullOrEmpty(assignedKerbalName)) configNode.AddValue("eacAssignedKerbal", assignedKerbalName);
            if (!string.IsNullOrEmpty(assignedKerbalIdentity)) configNode.AddValue("eacAssignedKerbalIdentity", assignedKerbalIdentity);
            if (assignedTargetLevel > 0) configNode.AddValue("eacAssignedTargetLevel", assignedTargetLevel);
            if (!string.IsNullOrEmpty(assignedContractType)) configNode.AddValue("eacAssignedContractType", assignedContractType);
            if (!string.IsNullOrEmpty(assignedExamId)) configNode.AddValue("eacAssignedExamId", assignedExamId);
        }

        private void ResetExam()
        {
            try
            {
                RosterRotationKSCUI.HandleGraduationContractAbandonedFromBridge(contract as Contract);
            }
            catch (Exception ex)
            {
                RosterRotationKSCUI.LogContractConfiguratorWarning("[EAC] Contract Configurator graduation reset bridge failed: " + ex.Message);
            }
        }

        private void UpdatePilotTestCircuit(Vessel vessel)
        {
            if (!IsKerbin(vessel) || vessel.situation != Vessel.Situations.FLYING) return;
            if (!VesselHasAssignedKerbal(vessel)) return;
            if (vessel.altitude < 150.0 || vessel.altitude > 5000.0 || vessel.srfSpeed < 60.0) return;
            MarkScenarioSatisfied("Local circuit complete. Land and recover safely.");
        }

        private void UpdatePilotBailout(Vessel vessel)
        {
            if (!IsKerbin(vessel)) return;
            if (!phase1 && VesselHasAssignedKerbal(vessel) && vessel.situation == Vessel.Situations.FLYING && vessel.altitude >= 800.0 && vessel.srfSpeed >= 40.0)
            {
                phase1 = true;
                ScreenMessages.PostScreenMessage("EAC final exam: safe bail-out altitude reached. EVA the pilot and recover them safely.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (phase1 && !phase2 && IsAssignedKerbalEva(vessel))
            {
                phase2 = true;
                MarkScenarioSatisfied("Bail-out observed. Recover the pilot safely to complete the exam.");
            }
        }

        private void UpdatePilotHighAltitude(Vessel vessel)
        {
            if (!IsKerbin(vessel) || vessel.situation != Vessel.Situations.FLYING) return;
            if (!VesselHasAssignedKerbal(vessel)) return;
            if (vessel.altitude < 3000.0 || vessel.altitude > 10000.0 || vessel.srfSpeed < 60.0) return;
            MarkScenarioSatisfied("High-altitude handling checkpoint complete. Land and recover safely.");
        }

        private void UpdatePilotStallRecovery(Vessel vessel)
        {
            if (!IsKerbin(vessel) || vessel.situation != Vessel.Situations.FLYING) return;
            if (!VesselHasAssignedKerbal(vessel)) return;
            if (!phase1 && vessel.altitude >= 1000.0 && vessel.altitude <= 5000.0 && vessel.srfSpeed >= 70.0)
            {
                phase1 = true;
                ScreenMessages.PostScreenMessage("EAC final exam: setup altitude reached. Slow below stall threshold, then recover.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (phase1 && !phase2 && vessel.srfSpeed <= 45.0)
            {
                phase2 = true;
                ScreenMessages.PostScreenMessage("EAC final exam: stall/slow-flight condition observed. Recover to stable flight.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (phase2 && vessel.altitude >= 500.0 && vessel.srfSpeed >= 70.0)
                MarkScenarioSatisfied("Stall recovery complete. Land and recover safely.");
        }

        private void UpdatePilotAbortTakeoff(Vessel vessel)
        {
            if (!IsKerbin(vessel) || !VesselHasAssignedKerbal(vessel)) return;
            if (!phase1 && !IsEva(vessel) && vessel.srfSpeed >= 55.0 && vessel.situation != Vessel.Situations.FLYING)
            {
                phase1 = true;
                ScreenMessages.PostScreenMessage("EAC final exam: abort speed reached. Briefly rotate, then land/stop safely.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (phase1 && !phase2 && vessel.situation == Vessel.Situations.FLYING)
                phase2 = true;
            if (phase1 && phase2 && vessel.situation != Vessel.Situations.FLYING && vessel.srfSpeed <= 5.0)
                MarkScenarioSatisfied("Abort takeoff complete. Recover safely.");
        }

        private void UpdateEngineerFieldInspection(Vessel vessel)
        {
            if (!IsKerbin(vessel)) return;
            if (!phase1 && IsAssignedKerbalEva(vessel))
            {
                phase1 = true;
                ScreenMessages.PostScreenMessage("EAC final exam: EVA inspection started. Board or recover safely.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (phase1 && VesselHasAssignedKerbal(vessel) && !IsEva(vessel) && vessel.situation != Vessel.Situations.FLYING)
                MarkScenarioSatisfied("Field inspection complete. Recover safely.");
        }

        private void UpdateEngineerBatteryInstall(Vessel vessel)
        {
            Vessel tracked = TrackOrFindAssignedWorkVessel(vessel);
            if (tracked == null) return;
            int count = CountBatteryParts(tracked);
            if (baselineBatteryCount < 0) baselineBatteryCount = count;
            if (count > baselineBatteryCount)
                MarkScenarioSatisfied("Battery installation detected. Recover safely.");
        }

        private void UpdateEngineerSolarPanelInstall(Vessel vessel)
        {
            Vessel tracked = TrackOrFindAssignedWorkVessel(vessel);
            if (tracked == null) return;
            int count = CountSolarPanelParts(tracked);
            if (baselineSolarPanelCount < 0) baselineSolarPanelCount = count;
            if (count > baselineSolarPanelCount)
                MarkScenarioSatisfied("Solar panel installation detected. Recover safely.");
        }

        private void UpdateEngineerRemovePart(Vessel vessel)
        {
            Vessel tracked = TrackOrFindAssignedWorkVessel(vessel);
            if (tracked == null) return;
            int count = tracked.parts == null ? 0 : tracked.parts.Count;
            if (baselinePartCount < 0) baselinePartCount = count;
            if (baselinePartCount > 0 && count < baselinePartCount)
                MarkScenarioSatisfied("Part removal detected. Recover safely.");
        }

        private void UpdateEngineerParachuteInspection(Vessel vessel)
        {
            if (!IsKerbin(vessel)) return;
            if (!phase1 && VesselHasAssignedKerbal(vessel) && !IsEva(vessel) && vessel.situation != Vessel.Situations.FLYING && VesselHasParachute(vessel))
            {
                phase1 = true;
                ScreenMessages.PostScreenMessage("EAC final exam: parachute-equipped craft landed. EVA the engineer for inspection.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (phase1 && IsAssignedKerbalEva(vessel))
                MarkScenarioSatisfied("Parachute inspection complete. Recover safely.");
        }

        private void UpdateEngineerRoverService(Vessel vessel)
        {
            if (!IsKerbin(vessel) || !VesselHasAssignedKerbal(vessel) || IsEva(vessel)) return;
            if (!hasStartPosition)
            {
                hasStartPosition = true;
                startLat = vessel.latitude;
                startLon = vessel.longitude;
            }
            if (!phase1 && SurfaceDistanceMeters(vessel.latitude, vessel.longitude, startLat, startLon, 600000.0) >= 200.0)
            {
                phase1 = true;
                ScreenMessages.PostScreenMessage("EAC final exam: rover service distance complete. EVA the engineer for inspection.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (phase1 && IsAssignedKerbalEva(vessel))
                MarkScenarioSatisfied("Rover service inspection complete. Recover safely.");
        }

        private void UpdateScientistKSCSurvey(Vessel vessel)
        {
            if (!IsKerbin(vessel)) return;
            if (VesselHasAssignedKerbal(vessel) && HasAnyScienceData(vessel))
            {
                phase1 = true;
                MarkScenarioSatisfied("KSC science data recorded. Recover the scientist to complete the exam.");
            }
        }

        private void UpdateScientistScienceData(Vessel vessel, string token, string message)
        {
            if (!IsKerbin(vessel)) return;
            if (!VesselHasAssignedKerbal(vessel)) return;
            if (HasScienceDataMatchingAny(vessel, token, "mystery", "mysteryGoo"))
            {
                phase1 = true;
                MarkScenarioSatisfied(message);
            }
        }

        private void UpdateScientistInstrumentCalibration(Vessel vessel)
        {
            if (!IsKerbin(vessel)) return;
            if (!VesselHasAssignedKerbal(vessel)) return;
            if (HasInstrumentScienceData(vessel))
            {
                phase1 = true;
                MarkScenarioSatisfied("Instrument calibration data recorded. Recover the scientist to complete the exam.");
            }
        }

        private void UpdateScientistAtmosphericData(Vessel vessel)
        {
            if (!IsKerbin(vessel)) return;
            if (!VesselHasAssignedKerbal(vessel)) return;
            if ((vessel.situation == Vessel.Situations.FLYING || vessel.situation == Vessel.Situations.LANDED) &&
                (HasScienceDataMatchingAny(vessel, "temperature", "barometer", "pressure") || HasAnyScienceData(vessel)))
            {
                phase1 = true;
                MarkScenarioSatisfied("Atmospheric science data recorded. Recover the scientist to complete the exam.");
            }
        }

        private void UpdateScientistShorelineExpedition(Vessel vessel)
        {
            if (!IsKerbin(vessel)) return;
            if (!VesselHasAssignedKerbal(vessel)) return;
            bool shoreline = vessel.situation == Vessel.Situations.SPLASHED || (vessel.situation == Vessel.Situations.LANDED && vessel.altitude < 100.0);
            if (shoreline && HasAnyScienceData(vessel))
            {
                phase1 = true;
                MarkScenarioSatisfied("Shoreline science data recorded. Recover the scientist to complete the exam.");
            }
        }

        private void MarkScenarioSatisfied(string message)
        {
            if (scenarioSatisfied) return;
            scenarioSatisfied = true;
            RosterRotationKSCUI.LogContractConfiguratorVerbose("[EAC] Final exam scenario satisfied for " + DisplayKerbalName() + " (" + GetScenarioId() + ").");
            if (!string.IsNullOrEmpty(message))
                ScreenMessages.PostScreenMessage("EAC final exam: " + message, 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        private Vessel TrackOrFindAssignedWorkVessel(Vessel active)
        {
            if (string.IsNullOrEmpty(trackedVesselId) && active != null && VesselHasAssignedKerbal(active) && !IsEva(active))
                trackedVesselId = active.id.ToString();

            if (string.IsNullOrEmpty(trackedVesselId)) return null;

            try
            {
                foreach (Vessel v in FlightGlobals.Vessels)
                {
                    if (v == null) continue;
                    if (string.Equals(v.id.ToString(), trackedVesselId, StringComparison.Ordinal))
                        return v;
                }
            }
            catch { }
            return active != null && string.Equals(active.id.ToString(), trackedVesselId, StringComparison.Ordinal) ? active : null;
        }

        private bool IsKerbin(Vessel vessel)
        {
            return vessel != null && vessel.mainBody != null && string.Equals(vessel.mainBody.name, "Kerbin", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsEva(Vessel vessel)
        {
            return vessel != null && vessel.vesselType == VesselType.EVA;
        }

        private bool IsAssignedKerbalEva(Vessel vessel)
        {
            return IsEva(vessel) && VesselHasAssignedKerbal(vessel);
        }

        private bool VesselHasAssignedKerbal(Vessel vessel)
        {
            CacheAssignmentMetadata();
            if (vessel == null) return false;
            if (string.IsNullOrEmpty(assignedKerbalName)) return true;

            try
            {
                foreach (ProtoCrewMember crew in vessel.GetVesselCrew())
                {
                    if (crew != null && string.Equals(crew.name, assignedKerbalName, StringComparison.Ordinal))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private bool ProtoVesselHasAssignedKerbal(ProtoVessel protoVessel)
        {
            CacheAssignmentMetadata();
            if (protoVessel == null) return false;
            if (string.IsNullOrEmpty(assignedKerbalName)) return true;

            try
            {
                foreach (ProtoCrewMember crew in protoVessel.GetVesselCrew())
                {
                    if (crew != null && string.Equals(crew.name, assignedKerbalName, StringComparison.Ordinal))
                        return true;
                }
            }
            catch { }

            // External command seats and EVA transitions can store crew names in module snapshots
            // rather than the normal ProtoVessel crew list at recovery time.  Fall back to a
            // conservative proto-node text scan so science/rover exams are not blocked.
            try
            {
                ConfigNode node = new ConfigNode("VESSEL");
                protoVessel.Save(node);
                string text = node.ToString();
                if (!string.IsNullOrEmpty(text) && text.IndexOf(assignedKerbalName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }

            return false;
        }

        private static double SurfaceDistanceMeters(double latA, double lonA, double latB, double lonB, double radius)
        {
            double dLat = (latB - latA) * Math.PI / 180.0;
            double dLon = (lonB - lonA) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                       Math.Cos(latA * Math.PI / 180.0) * Math.Cos(latB * Math.PI / 180.0) *
                       Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            return radius * c;
        }

        private int CountBatteryParts(Vessel vessel)
        {
            int count = 0;
            if (vessel == null || vessel.parts == null) return 0;
            foreach (Part part in vessel.parts)
            {
                if (part == null) continue;
                string name = GetPartName(part).ToLowerInvariant();
                if (name.Contains("battery")) count++;
            }
            return count;
        }

        private int CountSolarPanelParts(Vessel vessel)
        {
            int count = 0;
            if (vessel == null || vessel.parts == null) return 0;
            foreach (Part part in vessel.parts)
            {
                if (part == null) continue;
                string name = GetPartName(part).ToLowerInvariant();
                if (name.Contains("solar")) { count++; continue; }
                try
                {
                    foreach (PartModule module in part.Modules)
                    {
                        if (module != null && module.moduleName != null && module.moduleName.IndexOf("Solar", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            count++;
                            break;
                        }
                    }
                }
                catch { }
            }
            return count;
        }

        private bool VesselHasParachute(Vessel vessel)
        {
            if (vessel == null || vessel.parts == null) return false;
            foreach (Part part in vessel.parts)
            {
                if (part == null) continue;
                string name = GetPartName(part).ToLowerInvariant();
                if (name.Contains("parachute") || name.Contains("parachutes")) return true;
                try
                {
                    foreach (PartModule module in part.Modules)
                    {
                        if (module != null && module.moduleName != null && module.moduleName.IndexOf("Parachute", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private string GetPartName(Part part)
        {
            if (part == null) return string.Empty;
            try
            {
                if (part.partInfo != null && !string.IsNullOrEmpty(part.partInfo.name)) return part.partInfo.name;
            }
            catch { }
            try { return part.name ?? string.Empty; } catch { return string.Empty; }
        }

        private bool HasInstrumentScienceData(Vessel vessel)
        {
            return HasScienceDataMatchingAny(vessel, "temperature", "barometer", "pressure", "gravity", "gravimeter", "accelerometer", "acceleration") ||
                   HasAnyScienceData(vessel);
        }

        private bool ProtoVesselHasInstrumentScienceData(ProtoVessel protoVessel)
        {
            return ProtoVesselHasScienceDataMatchingAny(protoVessel, "temperature", "barometer", "pressure", "gravity", "gravimeter", "accelerometer", "acceleration") ||
                   ProtoVesselHasAnyScienceData(protoVessel);
        }

        private bool HasAnyScienceData(Vessel vessel)
        {
            return HasScienceDataMatching(vessel, string.Empty);
        }

        private bool HasScienceDataMatchingAny(Vessel vessel, params string[] tokens)
        {
            if (tokens == null || tokens.Length == 0) return HasAnyScienceData(vessel);
            foreach (string token in tokens)
            {
                if (HasScienceDataMatching(vessel, token)) return true;
            }
            return false;
        }

        private bool HasScienceDataMatching(Vessel vessel, string token)
        {
            if (vessel == null || vessel.parts == null) return false;
            foreach (Part part in vessel.parts)
            {
                if (PartHasScienceDataMatching(part, token)) return true;
            }
            return false;
        }

        private bool PartHasScienceDataMatching(Part part, string token)
        {
            if (part == null) return false;
            try
            {
                foreach (IScienceDataContainer container in part.FindModulesImplementing<IScienceDataContainer>())
                {
                    ScienceData[] data = container.GetData();
                    if (data == null) continue;
                    foreach (ScienceData science in data)
                    {
                        if (science == null) continue;
                        if (string.IsNullOrEmpty(token)) return true;
                        string text = GetScienceDataText(science);
                        if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool ProtoVesselHasAnyScienceData(ProtoVessel protoVessel)
        {
            return ProtoVesselHasScienceDataMatching(protoVessel, string.Empty);
        }

        private bool ProtoVesselHasScienceDataMatchingAny(ProtoVessel protoVessel, params string[] tokens)
        {
            if (tokens == null || tokens.Length == 0) return ProtoVesselHasAnyScienceData(protoVessel);
            foreach (string token in tokens)
            {
                if (ProtoVesselHasScienceDataMatching(protoVessel, token)) return true;
            }
            return false;
        }

        private bool ProtoVesselHasScienceDataMatching(ProtoVessel protoVessel, string token)
        {
            // ProtoVessel science storage is spread across proto-part module snapshots.
            // Use a conservative text scan so this remains compatible across KSP versions.
            try
            {
                ConfigNode node = new ConfigNode("VESSEL");
                protoVessel.Save(node);
                string text = node.ToString();
                if (string.IsNullOrEmpty(token))
                    return text.IndexOf("ScienceData", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Science", StringComparison.OrdinalIgnoreCase) >= 0;
                return text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private string GetScienceDataText(ScienceData science)
        {
            if (science == null) return string.Empty;
            try
            {
                string title = string.Empty;
                string subject = string.Empty;
                try { title = science.title ?? string.Empty; } catch { }
                try { subject = science.subjectID ?? string.Empty; } catch { }
                return title + " " + subject + " " + science.ToString();
            }
            catch { return science.ToString(); }
        }

        private bool IsPilotLevelOneExam()
        {
            string typeName = GetContractTypeName();
            if (!string.IsNullOrEmpty(typeName) && typeName.StartsWith("EAC.Graduation.Pilot.Level1", StringComparison.Ordinal))
                return true;

            string title = contract == null ? string.Empty : contract.Title;
            return !string.IsNullOrEmpty(title) && title.IndexOf("Pilot Level 1 Final Exam", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetScenarioId()
        {
            if (!string.IsNullOrEmpty(scenario)) return scenario;
            string typeName = GetContractTypeName();
            if (typeName.EndsWith(".TestCircuit", StringComparison.Ordinal)) return "PilotTestCircuit";
            if (typeName.EndsWith(".Bailout", StringComparison.Ordinal)) return "PilotBailout";
            if (typeName.EndsWith(".HighAltitude", StringComparison.Ordinal)) return "PilotHighAltitude";
            if (typeName.EndsWith(".StallRecovery", StringComparison.Ordinal)) return "PilotStallRecovery";
            if (typeName.EndsWith(".AbortTakeoff", StringComparison.Ordinal)) return "PilotAbortTakeoff";
            if (typeName.EndsWith(".FieldInspection", StringComparison.Ordinal)) return "EngineerFieldInspection";
            if (typeName.EndsWith(".BatteryInstall", StringComparison.Ordinal)) return "EngineerBatteryInstall";
            if (typeName.EndsWith(".SolarPanelInstall", StringComparison.Ordinal)) return "EngineerSolarPanelInstall";
            if (typeName.EndsWith(".RemovePart", StringComparison.Ordinal)) return "EngineerRemovePart";
            if (typeName.EndsWith(".ParachuteInspection", StringComparison.Ordinal)) return "EngineerParachuteInspection";
            if (typeName.EndsWith(".KSCSurvey", StringComparison.Ordinal)) return "ScientistKSCSurvey";
            if (typeName.EndsWith(".MysteryGoo", StringComparison.Ordinal)) return "ScientistMysteryGoo";
            if (typeName.EndsWith(".InstrumentCalibration", StringComparison.Ordinal)) return "ScientistInstrumentCalibration";
            if (typeName.EndsWith(".AtmosphericData", StringComparison.Ordinal)) return "ScientistAtmosphericData";
            if (typeName.EndsWith(".ShorelineExpedition", StringComparison.Ordinal)) return "ScientistShorelineExpedition";
            if (IsPilotLevelOneExam()) return "PilotTestCircuit";
            return string.Empty;
        }

        private string GetContractIdentifier()
        {
            try
            {
                object stock = contract;
                if (stock == null) return string.Empty;
                string[] names = { "ContractGuid", "contractGuid", "Guid", "guid", "ID", "id", "MissionSeed", "missionSeed" };
                foreach (string name in names)
                {
                    FieldInfo field = stock.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    object value = field == null ? null : field.GetValue(stock);
                    if (value == null)
                    {
                        PropertyInfo property = stock.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        value = property == null ? null : property.GetValue(stock, null);
                    }
                    if (value == null) continue;
                    string id = value.ToString();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch { }
            return string.Empty;
        }

        private string GetContractTypeName()
        {
            try
            {
                object cc = contract;
                if (cc == null) return string.Empty;

                FieldInfo contractTypeField = cc.GetType().GetField("contractType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object contractType = contractTypeField == null ? null : contractTypeField.GetValue(cc);
                if (contractType == null)
                {
                    PropertyInfo contractTypeProperty = cc.GetType().GetProperty("contractType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    contractType = contractTypeProperty == null ? null : contractTypeProperty.GetValue(cc, null);
                }
                if (contractType == null) return string.Empty;

                FieldInfo nameField = contractType.GetType().GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = nameField == null ? null : nameField.GetValue(contractType);
                if (value != null) return value.ToString();

                PropertyInfo nameProperty = contractType.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                value = nameProperty == null ? null : nameProperty.GetValue(contractType, null);
                return value == null ? string.Empty : value.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private void CacheAssignmentMetadata()
        {
            if (string.IsNullOrEmpty(assignedContractType)) assignedContractType = GetContractTypeName();
            if (string.IsNullOrEmpty(assignedExamId)) assignedExamId = configuredExamId;
            if (string.IsNullOrEmpty(assignedExamId)) assignedExamId = GetUniqueDataString("eacExamId");
            if (string.IsNullOrEmpty(assignedExamId)) assignedExamId = assignedContractType;

            if (assignedTargetLevel <= 0)
            {
                int uniqueLevel;
                if (int.TryParse(GetUniqueDataString("eacTargetLevel"), out uniqueLevel) && uniqueLevel >= 1 && uniqueLevel <= 3)
                    assignedTargetLevel = uniqueLevel;
            }
            if (string.IsNullOrEmpty(assignedKerbalName)) assignedKerbalName = GetUniqueDataString("eacKerbal");
            if (string.IsNullOrEmpty(assignedKerbalIdentity)) assignedKerbalIdentity = GetUniqueDataString("eacKerbalIdentity");

            string title = contract == null ? string.Empty : contract.Title;
            if (string.IsNullOrEmpty(assignedKerbalName))
            {
                int suffix = title.IndexOf("'s ", StringComparison.OrdinalIgnoreCase);
                if (suffix > 0) assignedKerbalName = title.Substring(0, suffix);
            }

            if (assignedTargetLevel <= 0 && configuredTargetLevel >= 1 && configuredTargetLevel <= 3) assignedTargetLevel = configuredTargetLevel;
            if (assignedTargetLevel <= 0) assignedTargetLevel = ParseTargetLevel(assignedContractType);
            if (assignedTargetLevel <= 0) assignedTargetLevel = ParseTargetLevel(title);
            if (assignedTargetLevel <= 0 && IsPilotLevelOneExam()) assignedTargetLevel = 1;
        }

        private static int ParseTargetLevel(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int index = text.IndexOf("Level", StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                int pos = index + "Level".Length;
                while (pos < text.Length && (text[pos] == ' ' || text[pos] == '.' || text[pos] == '_' || text[pos] == '-')) pos++;
                int start = pos;
                while (pos < text.Length && char.IsDigit(text[pos])) pos++;
                if (pos > start)
                {
                    int level;
                    if (int.TryParse(text.Substring(start, pos - start), out level) && level >= 1 && level <= 3)
                        return level;
                }
                index = text.IndexOf("Level", index + 5, StringComparison.OrdinalIgnoreCase);
            }
            return 0;
        }

        private string GetUniqueDataString(string key)
        {
            try
            {
                object cc = contract;
                if (cc == null || string.IsNullOrEmpty(key)) return string.Empty;

                FieldInfo field = cc.GetType().GetField("uniqueData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object uniqueData = field == null ? null : field.GetValue(cc);
                if (uniqueData == null) return string.Empty;

                object value = null;
                MethodInfo containsKey = uniqueData.GetType().GetMethod("ContainsKey", new[] { typeof(string) });
                MethodInfo getItem = uniqueData.GetType().GetMethod("get_Item", new[] { typeof(string) });
                if (containsKey != null && getItem != null && (bool)containsKey.Invoke(uniqueData, new object[] { key }))
                    value = getItem.Invoke(uniqueData, new object[] { key });

                return value == null ? string.Empty : value.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private string DisplayKerbalName()
        {
            CacheAssignmentMetadata();
            return string.IsNullOrEmpty(assignedKerbalName) ? "assigned trainee" : assignedKerbalName;
        }
    }
}

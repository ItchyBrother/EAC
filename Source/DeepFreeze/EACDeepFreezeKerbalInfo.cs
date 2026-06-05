// EAC - DeepFreeze compatibility: local immutable snapshot types.
// This file intentionally has no dependency on DeepFreeze or DFWrapper.

using System;

namespace RosterRotation.DeepFreeze
{
    internal sealed class EACDeepFreezeKerbalInfo
    {
        public readonly string Name;
        public readonly double LastUpdateUT;
        public readonly ProtoCrewMember.RosterStatus? Status;
        public readonly ProtoCrewMember.KerbalType? Type;
        public readonly Guid VesselID;
        public readonly string VesselName;
        public readonly uint PartID;
        public readonly int SeatIndex;
        public readonly string SeatName;
        public readonly string ExperienceTraitName;

        public EACDeepFreezeKerbalInfo(
            string name,
            double lastUpdateUT,
            ProtoCrewMember.RosterStatus? status,
            ProtoCrewMember.KerbalType? type,
            Guid vesselID,
            string vesselName,
            uint partID,
            int seatIndex,
            string seatName,
            string experienceTraitName)
        {
            Name = name ?? string.Empty;
            LastUpdateUT = lastUpdateUT;
            Status = status;
            Type = type;
            VesselID = vesselID;
            VesselName = vesselName ?? string.Empty;
            PartID = partID;
            SeatIndex = seatIndex;
            SeatName = seatName ?? string.Empty;
            ExperienceTraitName = experienceTraitName ?? string.Empty;
        }
    }
}

using UnityEngine;

namespace Modern8bitTanks
{
    internal static class OnlineTeamColors
    {
        internal static readonly Color32 Friendly = new(103, 184, 255, 255);
        internal static readonly Color32 Hostile = new(255, 156, 82, 255);

        internal static Color32 ForRelation(bool friendly) => friendly ? Friendly : Hostile;
    }
}

using System;
using UnityEngine;

namespace Modern8bitTanks
{
    internal enum BattlefieldCoverKind
    {
        Landmark,
        Building,
        Wall,
        Barricade,
        Ridge
    }

    [Serializable]
    internal sealed class BattlefieldTheme
    {
        [SerializeField] private DestructibleProp landmark;
        [SerializeField] private DestructibleProp building;
        [SerializeField] private DestructibleProp wall;
        [SerializeField] private DestructibleProp barricade;
        [SerializeField] private DestructibleProp ridge;

        internal DestructibleProp GetCover(BattlefieldCoverKind kind) => kind switch
        {
            BattlefieldCoverKind.Landmark => landmark,
            BattlefieldCoverKind.Building => building,
            BattlefieldCoverKind.Wall => wall,
            BattlefieldCoverKind.Barricade => barricade,
            BattlefieldCoverKind.Ridge => ridge,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [Serializable]
    internal sealed class BattlefieldPlan
    {
        [SerializeField] private string name;
        [SerializeField] private BattlefieldDistrict[] districts;

        internal string Name => name;
        internal BattlefieldDistrict[] Districts => districts;
    }

    [Serializable]
    internal struct BattlefieldDistrict
    {
        [SerializeField] private Vector2 center;
        [SerializeField] private Vector2 groundSize;
        [SerializeField] private float angle;
        [SerializeField] private BattlefieldCover[] cover;

        internal Vector2 Center => center;
        internal Vector2 GroundSize => groundSize;
        internal float Angle => angle;
        internal BattlefieldCover[] Cover => cover;
    }

    [Serializable]
    internal struct BattlefieldCover
    {
        [SerializeField] private BattlefieldCoverKind kind;
        [SerializeField] private Vector2 offset;
        [SerializeField] private float angle;

        internal BattlefieldCoverKind Kind => kind;
        internal Vector2 Offset => offset;
        internal float Angle => angle;
    }
}

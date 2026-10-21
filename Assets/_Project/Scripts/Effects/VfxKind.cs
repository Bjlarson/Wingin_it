namespace WingingIt.Effects
{
    /// <summary>Every effect in the VFX pack (Design/03-Art/final-asset-list.md, Pack 8). One prefab each in <see cref="VfxLibrary"/>.</summary>
    public enum VfxKind
    {
        // One-shots
        /// <summary>A plane shot down in mid-air: flash, fireball, black smoke, debris, sparks, shock ring and a "BOOM!".</summary>
        ExplosionPlane,
        /// <summary>A rocket or missile bursting in the air (or on a plane). Scaled by the blast's size.</summary>
        ExplosionAir,
        /// <summary>A blast on the ground: fireball, dust ring, flying dirt and a smoke column. Bombs, rockets, crashes.</summary>
        ExplosionGround,
        /// <summary>An AA shell bursting: a cluster of black flak puffs.</summary>
        FlakBurst,
        /// <summary>A bullet hitting a plane: sparks, a hit flash, metal chips.</summary>
        BulletHitPlane,
        /// <summary>A bullet hitting the ground or a building: sparks, a dust puff, chips.</summary>
        BulletHitGround,
        /// <summary>A bullet or shell hitting the sea: a little spray.</summary>
        BulletHitWater,
        /// <summary>Something big hitting the sea (a ditched plane, a bomb): a crown and spray.</summary>
        WaterSplash,
        /// <summary>A part tearing off: sparks, a puff of smoke, chips.</summary>
        PartDetach,
        /// <summary>Tyre smoke where a wheel touches down.</summary>
        Touchdown,
        /// <summary>A shimmer of sparkles round a plane that just spawned.</summary>
        RespawnShimmer,
        /// <summary>A hit glancing off a shield: sparkles and a ripple.</summary>
        ShieldHit,

        // Looping emitters (attached, then stopped)
        /// <summary>Grey wisps from a Damaged plane.</summary>
        DamageSmokeLight,
        /// <summary>Thick black smoke from a Critical plane or a dead engine.</summary>
        DamageSmokeHeavy,
        /// <summary>Flames trailing a Critical plane or a burning wreck.</summary>
        DamageFire,
        /// <summary>A tall column of black smoke rising off a wreck on the ground.</summary>
        WreckSmoke,
        /// <summary>Smoke behind a tumbling broken-off part.</summary>
        DebrisSmoke,
        /// <summary>The puffy white trail behind a missile or rocket.</summary>
        MissileTrail,
        /// <summary>Wrenches and sparkles rising from a plane being repaired.</summary>
        RepairSparkles,
        /// <summary>
        /// Dust, dirt or sea mist kicked up under a low, fast plane: trails under each wing and a cluster under the nose,
        /// tinted and tuned live by <see cref="Flight.GroundKickup"/>. Droplets only over water.
        /// </summary>
        GroundKickup,
    }
}

using System;
using Blocks.Character;
using Blocks.Extras;

namespace Blocks.Attack
{
    /// <summary>
    /// Per-shot configuration for a projectile, owned by the firing ability and passed at launch
    /// so the same pooled prefab can be reused with different behaviors.
    /// <para>
    /// Built through <see cref="Contact"/> or <see cref="Fused"/> and nothing else: the constructor is
    /// private, and neither factory takes an optional argument. That is deliberate: the two name the
    /// branch <c>Projectile</c> takes on impact, which is also what decides whether a given field is
    /// live at all (a fused shot never reaches the contact damage path, so <see cref="HitVfx"/> and
    /// <see cref="OnHit"/> are unreachable on one). A new field therefore has to be answered for at both
    /// call sites instead of quietly defaulting at one of them.
    /// </para>
    /// </summary>
    public readonly struct ProjectileLaunchSettings
    {
        public readonly float Lifetime;
        public readonly float GravityScale;
        public readonly float FuseTime;
        public readonly bool StickOnImpact;
        public readonly float ExplosionRadius;

        // How the hit moves its victims; the horizontal sign follows the travel direction.
        public readonly HitReaction Reaction;

        // Uniform scale applied to the projectile's transform for this shot (e.g. a charged shot fired bigger).
        public readonly float SizeScale;

        // Optional impact effect spawned where the projectile damages a target. Contact shots only.
        public readonly OneShotVfx HitVfx;

        // Optional effect spawned where a fused projectile detonates.
        public readonly OneShotVfx ExplodeVfx;

        // Raised on the firing ability when the projectile damages a target, so it can play its own hit
        // feedback. Sounds belong on the ability rather than the projectile: the shot is recycled in the
        // same frame it hits, which would cut off anything playing on it. Contact shots only.
        public readonly Action OnHit;

        ProjectileLaunchSettings(float lifetime, float gravityScale, float fuseTime, bool stickOnImpact,
                                 float explosionRadius, HitReaction reaction, float sizeScale,
                                 OneShotVfx hitVfx, OneShotVfx explodeVfx, Action onHit)
        {
            Lifetime = lifetime;
            GravityScale = gravityScale;
            FuseTime = fuseTime;
            StickOnImpact = stickOnImpact;
            ExplosionRadius = explosionRadius;
            Reaction = reaction;
            SizeScale = sizeScale;
            HitVfx = hitVfx;
            ExplodeVfx = explodeVfx;
            OnHit = onHit;
        }

        /// <summary>
        /// A shot that damages the first thing it touches: flat when <paramref name="gravityScale"/> is 0,
        /// arcing above that. <paramref name="hitVfx"/> and <paramref name="onHit"/> fire on that impact.
        /// </summary>
        public static ProjectileLaunchSettings Contact(float lifetime, float gravityScale, HitReaction reaction,
                                                       float sizeScale, OneShotVfx hitVfx, Action onHit)
        {
            return new ProjectileLaunchSettings(lifetime, gravityScale, fuseTime: 0f, stickOnImpact: false,
                                                explosionRadius: 0f, reaction, sizeScale, hitVfx,
                                                explodeVfx: null, onHit);
        }

        /// <summary>
        /// A shot on a timer: it detonates when the fuse runs out rather than damaging what it touches, so
        /// it takes no hit VFX or hit callback, since the contact path never runs for one. It can stick to its
        /// first impact and wait there, and an <paramref name="explosionRadius"/> above 0 damages everything
        /// inside it on detonation instead of just the target it stuck to.
        /// </summary>
        public static ProjectileLaunchSettings Fused(float lifetime, float gravityScale, float fuseTime,
                                                     bool stickOnImpact, float explosionRadius,
                                                     HitReaction reaction, OneShotVfx explodeVfx,
                                                     float sizeScale)
        {
            return new ProjectileLaunchSettings(lifetime, gravityScale, fuseTime, stickOnImpact,
                                                explosionRadius, reaction, sizeScale, hitVfx: null,
                                                explodeVfx, onHit: null);
        }
    }
}

using UnityEngine;
using Blocks.Audio;
using Blocks.Character;
using Blocks.Extras;

namespace Blocks.Attack
{
    /// <summary>
    /// Auto projectile: fires at the current target, straight or arcing. The projectile spawns
    /// windupTime seconds after the attack animation starts; tune it to the release moment.
    /// </summary>
    public class ProjectileAttackAbility : AttackAbility
    {
        public enum MovementMode { Straight, Arc }

        [Header("Projectile")]
        [SerializeField] float damage = 1f;
        [SerializeField] float range = 8f;
        [Tooltip("Seconds between the attack animation starting and the projectile spawning — tune it to match " +
                 "the release moment in the animation. 0 = fires instantly.")]
        [SerializeField] float windupTime;
        [SerializeField] float cooldown = 0.6f;
        [SerializeField] Projectile projectilePrefab;
        [SerializeField] Transform firePoint;
        [Tooltip("Launch speed for Straight mode. Ignored in Arc mode — arc speed is derived from Arc Height " +
                 "and the target distance.")]
        [SerializeField] float projectileSpeed = 8f;
        [SerializeField] float projectileLifetime = 3f;
        [SerializeField] int poolSize = 8;
        [Tooltip("Seconds the character stands still, counted from the start of the attack. Set it to at least " +
                 "Windup Time so the character doesn't walk mid-windup. 0 = keeps moving.")]
        [SerializeField] float movementLockTime = 0f;
        [Tooltip("Spawned at the fire point when the projectile launches, mirrored to the character's facing.")]
        [SerializeField] OneShotVfx launchVfxPrefab;

        [Header("Hit Reaction")]
        [Tooltip("Shove speed on hit, divided by the victim's Rigidbody2D mass. 0 = damage only.")]
        [SerializeField] float pushSpeed = 15f;
        [Tooltip("Seconds the victim can't move or attack after being hit.")]
        [SerializeField] float hitStunTime = 0.4f;

        [Header("Movement")]
        [SerializeField] MovementMode movementMode = MovementMode.Straight;
        [Tooltip("Gravity multiplier applied while in flight. Used by Arc mode; Straight mode forces 0.")]
        [SerializeField] float gravityScale = 1f;
        [Tooltip("Arc mode only. Peak height of the arc above the fire point. Must be greater than the target's " +
                 "vertical offset for a valid solve; otherwise falls back to a straight shot.")]
        [SerializeField] float arcHeight = 3f;

        [Header("Detonation")]
        [Tooltip("Seconds after launch until the projectile detonates. 0 = damage applies on contact instead.")]
        [SerializeField] float fuseTime = 0f;
        [Tooltip("Only used when Fuse Time > 0. When true, the projectile attaches to whatever it first hits " +
                 "(damageable or wall) and detonates there on fuse expiry.")]
        [SerializeField] bool stickOnImpact = false;
        [Tooltip("Only used when Fuse Time > 0. When > 0, detonation damages every IDamageable inside this " +
                 "radius instead of just the stuck target. 0 = single-target / contact damage.")]
        [SerializeField] float explosionRadius = 0f;
        [Tooltip("Only used when Fuse Time > 0. Spawned where the projectile detonates.")]
        [SerializeField] OneShotVfx explodeVfxPrefab;

        [Header("Audio")]
        [Tooltip("Plays the launch sound. Leave empty to use the AudioSource on this GameObject. The " +
                 "projectile's own feedback — bounces, the fuse countdown and flicker, the explosion — " +
                 "lives on the projectile prefab instead, on its Projectile Feedback component.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Played the moment the projectile launches, alongside the Launch Vfx.")]
        [SerializeField] AudioClip launchClip;
        [Tooltip("1 is the clip's own level; above that boosts it. Effect recordings are usually mastered far " +
                 "quieter than music, so they need the boost to be heard at all.")]
        [SerializeField, Range(0f, 5f)] float launchVolume = 2.5f;
        [Tooltip("Each launch is pitched up or down by up to this much, so repeats don't sound identical. " +
                 "0 = no variation.")]
        [SerializeField, Range(0f, 0.5f)] float pitchVariation = 0.08f;

        [Header("Debug")]
        [SerializeField] bool drawTrajectoryGizmo = true;
        [SerializeField, Range(8, 64)] int trajectorySamples = 32;

        ProjectilePool m_Pool;
        AudioHelper m_Audio;

        protected override Transform FirePoint => firePoint;

        protected override void OnInitialize()
        {
            m_Pool = Character.CreateProjectilePool(projectilePrefab, poolSize);

            AudioHelper.WarmUp(launchClip);
            m_Audio = new AudioHelper(this, audioSource, launchClip != null, clipFieldHint: "'Launch Clip'",
                                      ownerLabel: Character.name, pitchVariation: pitchVariation);
        }

        protected override void OnUpdate()
        {
            switch (TickAttackTimer())
            {
                case AttackTimerState.Landing:
                    Fire();
                    return;
                case AttackTimerState.Busy:
                    return;
            }

            if (m_Pool == null) return;
            if (!Character.HasTargetInRange(range)) return;

            BeginAttack();
        }

        protected override void OnCleanup()
        {
            ResetAttackTimer();
            m_Pool = null;
        }

        void BeginAttack()
        {
            Character.FaceTarget();
            Character.NotifyAttackPerformed();
            if (movementLockTime > 0f) Character.LockAttackMovement(movementLockTime);

            // A zero windup releases on the same frame the attack starts.
            if (!BeginWindup(windupTime)) Fire();
        }

        void Fire()
        {
            // Cooldown runs from the shot spawning, so tuning the windup never eats into the
            // pause between shots.
            StartCooldown(cooldown);

            // The target can die or despawn during the windup: swallow the shot but keep the cooldown.
            Transform target = Character.CurrentTargetTransform;
            if (m_Pool == null || target == null) return;

            Vector2 origin = FirePosition;
            Vector2 velocity = ComputeLaunchVelocity(origin, target.position);

            SpawnVfx(launchVfxPrefab, origin);
            m_Audio.Play(launchClip, launchVolume);
            m_Pool.Fire(origin, velocity, damage, BuildSettings());
        }

        protected override void CancelAttack()
        {
            if (!IsWindupArmed) return;

            ClearWindup();
            StartCooldown(cooldown);
        }

        void OnValidate()
        {
            if (damage < 0f) damage = 0f;
            if (range < 0f) range = 0f;
            if (windupTime < 0f) windupTime = 0f;
            if (cooldown < 0f) cooldown = 0f;
            if (projectileSpeed < 0f) projectileSpeed = 0f;
            if (projectileLifetime < 0f) projectileLifetime = 0f;
            if (poolSize < 0) poolSize = 0;
            if (movementLockTime < 0f) movementLockTime = 0f;
            if (pushSpeed < 0f) pushSpeed = 0f;
            if (hitStunTime < 0f) hitStunTime = 0f;
            if (gravityScale < 0f) gravityScale = 0f;
            if (arcHeight < 0f) arcHeight = 0f;
            if (fuseTime < 0f) fuseTime = 0f;
            if (explosionRadius < 0f) explosionRadius = 0f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, range);

            Vector2 origin = FirePosition;
            Gizmos.DrawWireSphere(origin, 0.1f);

            if (explosionRadius > 0f && fuseTime > 0f)
            {
                Gizmos.color = new Color(1f, 0.7f, 0f, 1f);
                Gizmos.DrawWireSphere(origin, explosionRadius);
            }

            if (drawTrajectoryGizmo)
            {
                DrawTrajectoryGizmo(origin);
            }
        }

        void DrawTrajectoryGizmo(Vector2 origin)
        {
            Vector2 target = GetGizmoTargetPosition(origin);
            Vector2 velocity = ComputeLaunchVelocity(origin, target);

            Gizmos.color = Color.cyan;

            if (movementMode == MovementMode.Straight || gravityScale <= 0f)
            {
                Gizmos.DrawLine(origin, target);
                return;
            }

            float gravity = -Physics2D.gravity.y * gravityScale;
            float totalTime = velocity.x != 0f
                ? (target.x - origin.x) / velocity.x
                : projectileLifetime;
            if (totalTime <= 0f || float.IsNaN(totalTime) || float.IsInfinity(totalTime))
            {
                Gizmos.DrawLine(origin, target);
                return;
            }

            int samples = Mathf.Max(2, trajectorySamples);
            Vector2 previous = origin;
            for (int i = 1; i <= samples; i++)
            {
                float t = totalTime * i / samples;
                float x = origin.x + velocity.x * t;
                float y = origin.y + velocity.y * t - 0.5f * gravity * t * t;
                Vector2 point = new Vector2(x, y);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }

        Vector2 GetGizmoTargetPosition(Vector2 origin)
        {
            if (Character != null && Character.CurrentTargetTransform != null)
            {
                return Character.CurrentTargetTransform.position;
            }

            // Edit-mode preview: aim at the edge of range so designers can see the shape of the shot before
            // pressing Play. Direction follows the firePoint's local +X so the preview tracks how the ability
            // is oriented in the scene.
            Vector2 forward = firePoint != null
                ? (Vector2)firePoint.right
                : Vector2.right;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector2.right;
            return origin + forward.normalized * range;
        }

        ProjectileLaunchSettings BuildSettings()
        {
            float effectiveGravity = movementMode == MovementMode.Arc ? gravityScale : 0f;
            HitReaction reaction = HitReaction.PushBack(pushSpeed, hitStunTime);

            // Split on the same condition Projectile branches on when it hits something: above 0 the fuse
            // owns the damage and contact does nothing, at 0 it's the other way round.
            if (fuseTime > 0f)
            {
                return ProjectileLaunchSettings.Fused(projectileLifetime, effectiveGravity, fuseTime,
                                                      stickOnImpact, explosionRadius, reaction,
                                                      explodeVfxPrefab, sizeScale: 1f);
            }

            return ProjectileLaunchSettings.Contact(projectileLifetime, effectiveGravity, reaction,
                                                    sizeScale: 1f, hitVfx: null, onHit: null);
        }

        Vector2 ComputeLaunchVelocity(Vector2 origin, Vector2 targetPosition)
        {
            Vector2 displacement = targetPosition - origin;

            if (movementMode == MovementMode.Straight || gravityScale <= 0f)
            {
                return displacement.sqrMagnitude > 0f
                    ? displacement.normalized * projectileSpeed
                    : Vector2.right * projectileSpeed;
            }

            return ComputeArcVelocity(displacement, arcHeight, gravityScale, projectileSpeed);
        }

        static Vector2 ComputeArcVelocity(Vector2 displacement, float arcHeight, float gravityScale, float fallbackSpeed)
        {
            // Height-driven solve: arc peak sits `arcHeight` above the launch point. Pick the vertical velocity
            // that reaches that peak, then size horizontal velocity so the projectile lands on the target as it
            // descends. Falls back to a straight shot when the geometry has no solution (e.g. target above peak).
            float gravity = -Physics2D.gravity.y * gravityScale;
            float dx = displacement.x;
            float dy = displacement.y;

            if (gravity <= 0f || arcHeight <= 0f || Mathf.Abs(dx) < 0.01f || arcHeight <= dy)
            {
                return displacement.sqrMagnitude > 0f
                    ? displacement.normalized * fallbackSpeed
                    : Vector2.right * fallbackSpeed;
            }

            float vy = Mathf.Sqrt(2f * gravity * arcHeight);
            float timeToPeak = vy / gravity;
            float timeFromPeak = Mathf.Sqrt(2f * (arcHeight - dy) / gravity);
            float totalTime = timeToPeak + timeFromPeak;
            float vx = dx / totalTime;

            return new Vector2(vx, vy);
        }
    }
}

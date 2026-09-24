using System.Collections.Generic;
using Blocks.Character;
using Blocks.GameFeel;
using UnityEngine;

namespace Blocks.Attack
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    [DisallowMultipleComponent]
    public sealed class Projectile : MonoBehaviour
    {
        Rigidbody2D m_Rigidbody;
        Collider2D m_Collider;
        ProjectileVisuals m_Visuals;
        ProjectileFeedback m_Feedback;
        Vector3 m_BaseScale;
        ProjectilePool m_Pool;
        BuildingBlocksCharacter m_Source;
        Collider2D m_IgnoredSourceCollider;
        ProjectileLaunchSettings m_Settings;
        float m_Damage;
        float m_LifetimeLeft;
        float m_FuseLeft;
        bool m_FuseArmed;
        bool m_Stuck;
        Transform m_StuckTo;
        Vector2 m_StuckLocalOffset;
        IDamageable m_StuckDamageable;

        void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody2D>();
            m_Collider = GetComponent<Collider2D>();
            m_Visuals = GetComponent<ProjectileVisuals>();
            m_Feedback = GetComponent<ProjectileFeedback>();
            m_BaseScale = transform.localScale;
        }

        // Unity's == reports a destroyed object as null even though the reference itself isn't, so a shooter
        // that was defeated mid-flight would otherwise still be handed out. This normalizes it to a true null,
        // which keeps a dead shooter out of DamageInfo and lets the guards below read as ordinary null checks.
        BuildingBlocksCharacter LiveSource => m_Source != null ? m_Source : null;

        void Update()
        {
            if (m_Stuck)
            {
                if (m_StuckTo == null)
                {
                    // Whatever it was stuck to is gone. Hang in place rather than vanishing, since sticking only
                    // ever happens on a fused shot, so the fuse below still detonates it where it sits.
                    m_Stuck = false;
                    m_StuckDamageable = null;
                }
                else
                {
                    transform.position = m_StuckTo.TransformPoint(m_StuckLocalOffset);
                }
            }

            if (m_FuseArmed)
            {
                m_FuseLeft -= Time.deltaTime;
                if (m_FuseLeft <= 0f)
                {
                    Detonate();
                    return;
                }
            }

            m_LifetimeLeft -= Time.deltaTime;
            if (m_LifetimeLeft <= 0f)
            {
                Release();
            }
        }

        internal void Bind(ProjectilePool pool)
        {
            m_Pool = pool;
        }

        /// <summary>
        /// Cuts the projectile loose from its pool, for when the pool's owner is destroyed while this shot is
        /// still in the air. The shot keeps flying and destroys itself instead of pooling once it's spent.
        /// </summary>
        internal void Orphan()
        {
            m_Pool = null;
        }

        public void Fire(Vector2 position, Vector2 velocity, BuildingBlocksCharacter source, float damage,
                         in ProjectileLaunchSettings settings)
        {
            m_Source = source;
            m_Damage = damage;
            m_Settings = settings;
            m_LifetimeLeft = settings.Lifetime;
            m_FuseLeft = settings.FuseTime;
            m_FuseArmed = settings.FuseTime > 0f;
            m_Stuck = false;
            m_StuckTo = null;
            m_StuckDamageable = null;

            transform.SetParent(null, true);
            transform.position = position;
            gameObject.SetActive(true);

            m_Rigidbody.bodyType = RigidbodyType2D.Dynamic;
            m_Rigidbody.gravityScale = settings.GravityScale;
            // Solid collider when the projectile needs to bounce off the world (fuse + no stick).
            // Trigger otherwise so bullets and sticky shots detect via overlap without being deflected.
            m_Collider.isTrigger = !(settings.FuseTime > 0f && !settings.StickOnImpact);
            m_Rigidbody.linearVelocity = velocity;

            if (source != null && m_Collider != null)
            {
                m_IgnoredSourceCollider = source.Collider;
                if (m_IgnoredSourceCollider != null)
                {
                    Physics2D.IgnoreCollision(m_Collider, m_IgnoredSourceCollider, true);
                }
            }

            // Guard against a default-constructed settings struct, whose SizeScale would be 0.
            float sizeScale = settings.SizeScale > 0f ? settings.SizeScale : 1f;
            if (m_Visuals != null)
            {
                // The visuals drive the transform scale every frame, so the per-shot size goes through them.
                m_Visuals.NotifyFired(velocity, settings.Lifetime, sizeScale);
            }
            else
            {
                transform.localScale = m_BaseScale * sizeScale;
            }

            // Last, so the fuse cues are armed against a shot that is already live and moving.
            m_Feedback?.NotifyFired(settings.FuseTime);
        }

        public void Release()
        {
            if (!gameObject.activeSelf) return;

            if (m_Collider != null && m_IgnoredSourceCollider != null)
            {
                Physics2D.IgnoreCollision(m_Collider, m_IgnoredSourceCollider, false);
            }

            m_Rigidbody.linearVelocity = Vector2.zero;
            m_Rigidbody.bodyType = RigidbodyType2D.Dynamic;
            transform.localScale = m_BaseScale;
            gameObject.SetActive(false);

            m_Source = null;
            m_IgnoredSourceCollider = null;
            m_Damage = 0f;
            m_LifetimeLeft = 0f;
            m_FuseLeft = 0f;
            m_FuseArmed = false;
            m_Stuck = false;
            m_StuckTo = null;
            m_StuckDamageable = null;

            if (m_Pool != null)
            {
                m_Pool.Return(this);
                return;
            }

            // No pool left to return to: the shooter that owned it was destroyed while this shot was still
            // in the air, so the spent shot cleans itself up rather than lingering as an orphan.
            Destroy(gameObject);
        }

        // Only ever called for a projectile with a solid collider: a fused shot that doesn't stick, i.e.
        // one that physically bounces off the world. Presentation only; the damage paths are all triggers.
        void OnCollisionEnter2D(Collision2D collision)
        {
            if (m_Stuck) return;

            m_Feedback?.NotifyBounced(collision.relativeVelocity.magnitude);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other == m_IgnoredSourceCollider) return;
            if (m_Stuck) return;

            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            bool isDamageable = damageable != null;

            // A shot outlives the character that fired it, so the shooter may be gone by now. It still hits
            // and still damages; every use of it below is simply guarded.
            BuildingBlocksCharacter source = LiveSource;

            if (isDamageable && source != null && source.IsSelf(damageable)) return;
            if (other.isTrigger && !isDamageable) return;

            if (m_FuseArmed)
            {
                if (m_Settings.StickOnImpact)
                {
                    if (isDamageable && !damageable.IsDamageable) return;
                    Stick(other, isDamageable ? damageable : null);
                }

                return;
            }

            if (isDamageable)
            {
                if (!damageable.IsDamageable) return;

                // The travel direction signs the reaction's horizontal half.
                Vector2 direction = ((Vector2)m_Rigidbody.linearVelocity).normalized;
                DamageInfo damage = new DamageInfo(m_Damage, source, transform.position, direction,
                                                   m_Settings.Reaction);
                damageable.TakeDamage(damage);
                // After TakeDamage on purpose: a finishing blow should still trigger hit feedback.
                if (source != null) source.NotifyDamageDealt(in damage);
                SpawnHitVfx(direction);

                // Before Release, which recycles this projectile: the firing ability plays the hit sound
                // on its own AudioSource, which outlives the shot. Skipped once the shooter itself is gone,
                // since that AudioSource died with it.
                if (source != null) m_Settings.OnHit?.Invoke();
                Release();
                return;
            }

            Release();
        }

        void SpawnHitVfx(Vector2 travelDirection)
        {
            if (m_Settings.HitVfx == null) return;

            // Mirror by rotating around Y so the effect plays back along the travel direction.
            Quaternion rotation = travelDirection.x < 0f
                ? Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.identity;

            Instantiate(m_Settings.HitVfx, transform.position, rotation);
        }

        void Stick(Collider2D other, IDamageable damageable)
        {
            m_Stuck = true;
            m_StuckDamageable = damageable;
            m_StuckTo = other.transform;
            m_StuckLocalOffset = m_StuckTo.InverseTransformPoint(transform.position);

            m_Rigidbody.linearVelocity = Vector2.zero;
            m_Rigidbody.bodyType = RigidbodyType2D.Kinematic;
        }

        static readonly Collider2D[] s_ExplosionBuffer = new Collider2D[32];
        static readonly HashSet<IDamageable> s_ExplosionDedup = new HashSet<IDamageable>();

        void Detonate()
        {
            m_Feedback?.NotifyDetonated();

            if (m_Settings.ExplodeVfx != null)
            {
                Instantiate(m_Settings.ExplodeVfx, transform.position, Quaternion.identity);
            }

            if (m_Settings.ExplosionRadius > 0f)
            {
                ExplodeAoE();
            }
            else if (m_StuckDamageable != null && m_StuckDamageable.IsDamageable)
            {
                DamageInfo damage = new DamageInfo(m_Damage, LiveSource, transform.position, Vector2.zero,
                                                   m_Settings.Reaction);
                m_StuckDamageable.TakeDamage(damage);
            }

            Release();
        }

        void ExplodeAoE()
        {
            Vector2 center = transform.position;
            int hitCount = Physics2D.OverlapCircle(center, m_Settings.ExplosionRadius, ContactFilter2D.noFilter, s_ExplosionBuffer);

            // May be null: a fuse can outlast the character that threw the grenade.
            BuildingBlocksCharacter source = LiveSource;

            s_ExplosionDedup.Clear();
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = s_ExplosionBuffer[i];
                if (hit == null) continue;

                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable == null || !damageable.IsDamageable) continue;
                if (source != null && source.IsSelf(damageable)) continue;
                if (!s_ExplosionDedup.Add(damageable)) continue;

                Vector2 direction = ((Vector2)damageable.Transform.position - center).normalized;
                DamageInfo damage = new DamageInfo(m_Damage, source, center, direction, m_Settings.Reaction);
                damageable.TakeDamage(damage);
            }
            s_ExplosionDedup.Clear();
        }
    }
}

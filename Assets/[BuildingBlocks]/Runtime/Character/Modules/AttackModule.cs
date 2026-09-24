using UnityEngine;
using Blocks.Attack;
using System.Collections.Generic;

namespace Blocks.Character
{
    /// <summary>
    /// The helpers an <see cref="AttackAbility"/> reaches through its character: hitbox queries,
    /// projectile pools, and the attack notifications feedback systems listen to.
    /// </summary>
    sealed class AttackModule
    {
        readonly Collider2D[] m_HitBuffer = new Collider2D[32];
        readonly HashSet<IDamageable> m_HitTargets = new HashSet<IDamageable>();
        readonly Dictionary<Projectile, ProjectilePool> m_PoolByPrefab = new Dictionary<Projectile, ProjectilePool>();

        public bool HasTargetInRange(BuildingBlocksCharacter character, float range)
        {
            if (!character.HasTarget) return false;

            Vector2 diff = (Vector2)character.CurrentTargetTransform.position
                           - (Vector2)character.transform.position;
            return diff.sqrMagnitude <= range * range;
        }

        public void HitBox(BuildingBlocksCharacter character, Vector2 offset, Vector2 size, float damage,
                           in HitReaction reaction, List<Vector2> hitPoints = null)
        {
            m_HitTargets.Clear();
            HitBox(character, offset, size, damage, in reaction, hitPoints, m_HitTargets);
        }

        // Multi-frame variant: victims already in alreadyHit are skipped and new ones are added, so
        // an attack swept over several frames (e.g. a traveling shockwave) damages each target once.
        public void HitBox(BuildingBlocksCharacter character, Vector2 offset, Vector2 size, float damage,
                           in HitReaction reaction, List<Vector2> hitPoints, HashSet<IDamageable> alreadyHit)
        {
            Vector2 center = GetFacingPosition(character, offset);

            hitPoints?.Clear();
            int hitCount = Physics2D.OverlapBox(center, size, 0f, ContactFilter2D.noFilter, m_HitBuffer);

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = m_HitBuffer[i];
                IDamageable target = hit.GetComponentInParent<IDamageable>();

                if (target == null) continue;
                if (character.IsSelf(target)) continue;
                if (!target.IsDamageable) continue;
                if (!alreadyHit.Add(target)) continue;

                Vector2 hitPoint = hit.ClosestPoint(center);
                DamageInfo damageInfo = new DamageInfo(damage, character, hitPoint,
                                                       AwayFromAttacker(character, target), reaction);
                target.TakeDamage(damageInfo);
                // After TakeDamage on purpose: a finishing blow should still trigger hit feedback.
                character.NotifyDamageDealt(in damageInfo);
                hitPoints?.Add(hitPoint);
            }
        }

        // Measured from the attacker's own position, never the hitbox center, which sits in front of
        // the attacker and would shove close-range victims backwards through it.
        static Vector2 AwayFromAttacker(BuildingBlocksCharacter character, IDamageable target)
        {
            Vector2 away = (Vector2)target.Transform.position - (Vector2)character.transform.position;
            if (Mathf.Abs(away.x) > 0.0001f) return away.normalized;

            return new Vector2(character.FacingDirection, 0f);
        }

        public ProjectilePool CreateProjectilePool(BuildingBlocksCharacter character, Projectile prefab, int size)
        {
            if (prefab == null)
            {
                Debug.LogError(
                    $"[AttackModule] {character.name}: Cannot create projectile pool — prefab is null.",
                    character);
                return null;
            }

            if (m_PoolByPrefab.TryGetValue(prefab, out ProjectilePool existing)) return existing;

            ProjectilePool pool = new ProjectilePool(character, prefab, size);
            m_PoolByPrefab.Add(prefab, pool);
            return pool;
        }

        /// <summary>
        /// Tears down every pool this character owns. Call it when the character is destroyed, not when it is
        /// defeated: shots already in the air belong to the world and are left to finish their flight.
        /// </summary>
        public void DisposePools()
        {
            foreach (ProjectilePool pool in m_PoolByPrefab.Values)
            {
                pool.Dispose();
            }

            m_PoolByPrefab.Clear();
        }

        static Vector2 GetFacingPosition(BuildingBlocksCharacter character, Vector2 offset)
        {
            return (Vector2)character.transform.position
                   + new Vector2(offset.x * character.FacingDirection, offset.y);
        }
    }
}

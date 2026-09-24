using UnityEngine;
using Blocks.Character;
using System.Collections.Generic;

namespace Blocks.Attack
{
    public sealed class ProjectilePool
    {
        readonly BuildingBlocksCharacter m_Owner;
        readonly Projectile m_Prefab;
        readonly Queue<Projectile> m_Idle = new Queue<Projectile>();
        readonly HashSet<Projectile> m_Active = new HashSet<Projectile>();

        internal ProjectilePool(BuildingBlocksCharacter owner, Projectile prefab, int size)
        {
            m_Owner = owner;
            m_Prefab = prefab;

            for (int i = 0; i < size; i++)
            {
                m_Idle.Enqueue(CreateProjectile());
            }
        }

        public void Fire(Vector2 origin, Vector2 velocity, float damage, in ProjectileLaunchSettings settings)
        {
            Projectile projectile = m_Idle.Count > 0 ? m_Idle.Dequeue() : CreateProjectile();
            m_Active.Add(projectile);
            projectile.Fire(origin, velocity, m_Owner, damage, in settings);
        }

        internal void Return(Projectile projectile)
        {
            if (projectile == null) return;

            m_Active.Remove(projectile);
            if (!m_Idle.Contains(projectile))
            {
                m_Idle.Enqueue(projectile);
            }
        }

        /// <summary>
        /// Tears the pool down when its owner goes away. Shots already in the air are deliberately left
        /// flying: they outlive the shooter and destroy themselves once their lifetime or fuse ends, so a
        /// defeated character never yanks a live projectile out of the world.
        /// </summary>
        internal void Dispose()
        {
            // Orphan() only clears the projectile's pool reference, so it never mutates m_Active here.
            foreach (Projectile projectile in m_Active)
            {
                if (projectile != null) projectile.Orphan();
            }

            m_Active.Clear();

            while (m_Idle.Count > 0)
            {
                Projectile projectile = m_Idle.Dequeue();
                if (projectile != null) Object.Destroy(projectile.gameObject);
            }
        }

        Projectile CreateProjectile()
        {
            Projectile projectile = Object.Instantiate(m_Prefab, m_Owner.transform);
            projectile.Bind(this);
            projectile.gameObject.SetActive(false);
            return projectile;
        }
    }
}

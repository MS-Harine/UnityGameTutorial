using UnityEngine;

namespace Blocks.Character
{
    /// <summary>
    /// Anything an attack can damage. <see cref="BuildingBlocksCharacter"/> is the implementation the
    /// template ships; implement it on a prop to make a crate or a breakable wall a valid target too.
    /// </summary>
    public interface IDamageable
    {
        Transform Transform { get; }

        /// <summary>False while hits should be skipped, e.g. during post-hit invulnerability.</summary>
        bool IsDamageable { get; }

        void TakeDamage(DamageInfo damage);
    }
}

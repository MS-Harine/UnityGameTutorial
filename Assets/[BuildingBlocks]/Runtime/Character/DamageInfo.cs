using UnityEngine;

namespace Blocks.Character
{
    /// <summary>
    /// One damage event, built by the attack that landed the hit and passed to
    /// <see cref="IDamageable.TakeDamage"/>.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly BuildingBlocksCharacter Source;
        public readonly Vector2 HitPoint;

        // Normalized, pointing away from the attacker; its x sign picks the reaction's horizontal direction.
        public readonly Vector2 Direction;

        // How the hit moves the victim. Authored by the attack; see HitReaction.
        public readonly HitReaction Reaction;

        public DamageInfo(float amount, BuildingBlocksCharacter source = null, Vector2 hitPoint = default, Vector2 direction = default,
                          HitReaction reaction = default)
        {
            Amount = amount;
            Source = source;
            HitPoint = hitPoint;
            Direction = direction;
            Reaction = reaction;
        }
    }
}

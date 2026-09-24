using Blocks.Character;
using UnityEngine;

namespace Blocks.Attack
{
    /// <summary>
    /// Hurts and shoves the target on physical contact. Put it on an enemy so bumping into it
    /// (or landing on its head) deals damage. Like any attack, it authors the victim's reaction.
    /// </summary>
    /// <remarks>
    /// Offered but not demonstrated: the sample's enemies all damage through a hitbox or a projectile,
    /// so a text search finds no references to this. The Creator reaches it by reflection, listing every
    /// non-abstract <see cref="AttackAbility"/> in its attack dropdown. Not dead code.
    /// </remarks>
    public class ContactAttackAbility : AttackAbility
    {
        [Header("Contact")]
        [SerializeField] float damage = 20f;
        [Tooltip("Only characters with this tag get hurt — keeps enemies from damaging each other.")]
        [SerializeField] string targetTag = "Player";
        [Tooltip("Shove speed on contact, divided by the victim's Rigidbody2D mass.")]
        [SerializeField] float pushSpeed = 15f;
        [Tooltip("Seconds the victim can't move or attack after the bump.")]
        [SerializeField] float hitStunTime = 0.4f;

        void OnCollisionEnter2D(Collision2D collision)
        {
            // Damage arrives on a collision message rather than a tick, so the character's Attack switch,
            // pauses and stuns are checked here — nothing upstream gets the chance to gate this one.
            if (Character == null || !Character.IsAttackActive) return;

            BuildingBlocksCharacter victim = collision.collider.GetComponentInParent<BuildingBlocksCharacter>();
            if (victim == null || !victim.CompareTag(targetTag) || !victim.IsDamageable) return;
            if (Character.IsSelf(victim)) return;

            Vector2 hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : (Vector2)victim.transform.position;

            DamageInfo damageInfo = new DamageInfo(damage, Character, hitPoint, PushDirection(victim),
                                                   HitReaction.PushBack(pushSpeed, hitStunTime));
            victim.TakeDamage(damageInfo);
            Character.NotifyDamageDealt(in damageInfo);
        }

        // The push must always have a horizontal part, so a victim standing exactly on top slides off.
        Vector2 PushDirection(BuildingBlocksCharacter victim)
        {
            Vector2 away = (Vector2)victim.transform.position - (Vector2)transform.position;
            if (Mathf.Abs(away.x) > 0.0001f) return away.normalized;

            return new Vector2(-victim.FacingDirection, 0f);
        }

        void OnValidate()
        {
            if (damage < 0f) damage = 0f;
            if (pushSpeed < 0f) pushSpeed = 0f;
            if (hitStunTime < 0f) hitStunTime = 0f;

            Collider2D contactCollider = GetComponent<Collider2D>();
            if (contactCollider != null && contactCollider.isTrigger)
            {
                Debug.LogWarning(
                    $"[ContactAttackAbility] {name}: Collider2D is set as a trigger. " +
                    "Disable 'Is Trigger' so physical collisions are detected.",
                    this);
            }
        }
    }
}

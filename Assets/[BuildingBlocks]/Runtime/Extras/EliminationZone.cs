using Blocks.Character;
using UnityEngine;

namespace Blocks.Extras
{
    /// <summary>
    /// Instantly eliminates any character that enters this trigger. Stretch one under the level
    /// so falling off always counts as an elimination.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [DisallowMultipleComponent]
    public sealed class EliminationZone : MonoBehaviour
    {
        void OnTriggerEnter2D(Collider2D other)
        {
            BuildingBlocksCharacter character = other.GetComponentInParent<BuildingBlocksCharacter>();
            if (character == null || !character.IsDamageable) return;

            // Full damage: dealing MaxHealth is clamped by the stat, so the actor is always fully depleted.
            Vector2 hitPoint = other.ClosestPoint(transform.position);
            character.TakeDamage(new DamageInfo(character.MaxHealth, null, hitPoint, Vector2.zero));
        }

        void OnValidate()
        {
            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null && !collider.isTrigger)
            {
                Debug.LogWarning(
                    $"[EliminationZone] {name}: Collider2D is not set as a trigger. " +
                    "Enable 'Is Trigger' so characters pass into the zone instead of colliding with it.",
                    this);
            }
        }
    }
}

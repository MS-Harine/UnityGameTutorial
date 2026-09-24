using UnityEngine;
using Blocks.Character;

namespace Blocks.Movement
{
    public abstract class MovementAbility : CharacterAbility
    {
        /// <summary>
        /// Bottom-centre of the character's collider, where dust puffs and other footfall effects
        /// belong. Not <c>transform.position</c>: the pivot can sit mid-body, which would leave the
        /// effect floating in the air.
        /// </summary>
        protected Vector2 FeetPosition
        {
            get
            {
                Bounds bounds = Character.ColliderBounds;
                return new Vector2(bounds.center.x, bounds.min.y);
            }
        }

        protected override void OnDestroy()
        {
            Character?.RemoveAbility(this);
            base.OnDestroy();
        }
    }
}

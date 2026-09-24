using UnityEngine;
using Blocks.Character;
using Blocks.Extras;
using UnityEngine.InputSystem;

namespace Blocks.Movement.Examples
{
    /// <summary>
    /// Player jump via input. Coyote time and a short input buffer; can cost stamina.
    /// </summary>
    public class JumpAbility : MovementAbility
    {
        [Header("Input")]
        [SerializeField] InputActionReference jumpAction;

        [Header("Jump Settings")]
        [SerializeField] float jumpHeight = 2.0f;
        [SerializeField] float coyoteTime = 0.15f;
        [SerializeField] float jumpBufferTime = 0.1f;
        [SerializeField] float jumpCooldown = 0.1f;

        [Header("Stat Cost")]
        [SerializeField] StatType jumpStatType = StatType.Stamina;
        [SerializeField] float jumpCost;

        [Header("Effects")]
        [Tooltip("Spawned at the character's feet when the jump starts, mirrored to facing — e.g. a dust puff.")]
        [SerializeField] OneShotVfx jumpVfxPrefab;

        protected override void OnInitialize()
        {
            BindInput(jumpAction, Configure);
        }

        void Configure(InputHandle input)
        {
            input.Performed = PerformJump;
            input.CanExecute = CanJump;
            input.BufferTime = jumpBufferTime;
            input.CooldownTime = jumpCooldown;
        }

        bool CanJump()
        {
            return Character.IsGroundedOrInCoyote(coyoteTime) &&
                   Character.HasAtLeast(jumpStatType, jumpCost);
        }

        void PerformJump()
        {
            Character.Consume(jumpStatType, jumpCost);
            Character.Jump(jumpHeight);
            SpawnVfx(jumpVfxPrefab, FeetPosition);
        }
    }
}

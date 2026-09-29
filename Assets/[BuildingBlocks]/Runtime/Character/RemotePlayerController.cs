using System;
using UnityEngine;
using Blocks.Movement;
using TMPro;

namespace Blocks.Character
{
    /// <summary>
    /// Controls a remote player's character puppet. Receives network transform, animation, and action
    /// updates and drives the underlying BuildingBlocksCharacter and CharacterAnimator smoothly.
    /// Does not listen to local player inputs.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BuildingBlocksCharacter))]
    public class RemotePlayerController : MovementAbility
    {
        [Header("Identity")]
        [SerializeField] ulong playerId;
        [SerializeField] string playerName = "Remote Player";

        [Header("Interpolation Settings")]
        [Tooltip("How quickly position catches up to network target.")]
        [SerializeField] float interpolationSpeed = 15f;
        [Tooltip("Distance threshold beyond which the character snaps immediately instead of interpolating.")]
        [SerializeField] float snapDistance = 4f;

        [Header("Visuals")]
        [Tooltip("Optional text for displaying player name above head.")]
        [SerializeField] TMP_Text nameplateText;
        [Tooltip("SpriteRenderer for visual effects or tinting.")]
        [SerializeField] SpriteRenderer spriteRenderer;

        Vector3 m_TargetPosition;
        Vector2 m_TargetVelocity;
        bool m_TargetFacingRight = true;
        bool m_HasReceivedFirstUpdate;

        public ulong PlayerId => playerId;
        public string PlayerName => playerName;
        public Vector3 TargetPosition => m_TargetPosition;
        public Vector2 TargetVelocity => m_TargetVelocity;
        public bool TargetFacingRight => m_TargetFacingRight;
        public BuildingBlocksCharacter CharacterComponent => Character;

        protected override void OnInitialize()
        {
            m_TargetPosition = transform.position;
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
            if (nameplateText == null)
            {
                nameplateText = GetComponentInChildren<TMP_Text>();
            }
            UpdateNameplate();
        }

        public void Initialize(ulong id, string displayName)
        {
            playerId = id;
            if (!string.IsNullOrEmpty(displayName))
            {
                playerName = displayName;
            }
            else
            {
                playerName = $"Player {id}";
            }
            UpdateNameplate();
        }

        public void SetPlayerName(string newName)
        {
            playerName = newName;
            UpdateNameplate();
        }

        public void SetSpriteColor(Color color)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = color;
            }
        }

        void UpdateNameplate()
        {
            if (nameplateText != null)
            {
                nameplateText.text = playerName;
            }
        }

        /// <summary>
        /// Updates the target transform received from network packet.
        /// </summary>
        public void UpdateNetworkState(Vector3 position, Vector2 velocity, bool facingRight)
        {
            if (!m_HasReceivedFirstUpdate)
            {
                m_HasReceivedFirstUpdate = true;
                transform.position = position;
                m_TargetPosition = position;
                m_TargetVelocity = velocity;
                m_TargetFacingRight = facingRight;
                return;
            }

            m_TargetPosition = position;
            m_TargetVelocity = velocity;
            m_TargetFacingRight = facingRight;

            // Apply vertical velocity on packet arrival (e.g. Jump or Land)
            if (Character != null && (Mathf.Abs(velocity.y) > 0.01f || Mathf.Approximately(velocity.x, 0f)))
            {
                Character.SetVerticalVelocity(velocity.y);
            }
        }

        /// <summary>
        /// Convenience overload when velocity is not explicitly sent; infers velocity from delta.
        /// </summary>
        public void UpdateNetworkState(Vector3 position, bool facingRight)
        {
            Vector2 inferredVelocity = (position - m_TargetPosition) / Mathf.Max(Time.deltaTime, 0.001f);
            UpdateNetworkState(position, inferredVelocity, facingRight);
        }

        public void SetPositionImmediate(Vector3 position)
        {
            transform.position = position;
            m_TargetPosition = position;
            m_TargetVelocity = Vector2.zero;
            if (Character != null)
            {
                Character.SetVerticalVelocity(0f);
            }
        }

        protected override void OnUpdate()
        {
            if (Character == null || Character.IsEliminated) return;

            // 1. Dead reckoning: advance horizontal target position while moving
            if (Mathf.Abs(m_TargetVelocity.x) > 0.001f)
            {
                m_TargetPosition.x += m_TargetVelocity.x * Time.deltaTime;
            }

            // While airborne, let gravity and physics drive vertical movement without MoveTowards fighting it
            if (!Character.IsGrounded)
            {
                m_TargetPosition.y = transform.position.y;
            }

            // 2. Position interpolation / reconciliation
            float distance = Vector2.Distance(transform.position, m_TargetPosition);
            if (distance > snapDistance)
            {
                transform.position = m_TargetPosition;
            }
            else if (distance > 0.001f)
            {
                transform.position = Vector3.MoveTowards(transform.position, m_TargetPosition, interpolationSpeed * Time.deltaTime);
            }

            // 3. Feed horizontal velocity to BuildingBlocksCharacter's MovementModule
            // Character.AddMovement feeds m_AdditiveMovement.x for animation and physics
            Character.AddMovement(new Vector2(m_TargetVelocity.x, 0f));

            // 4. Facing direction
            Vector2 faceTarget = (Vector2)transform.position + (m_TargetFacingRight ? Vector2.right : Vector2.left);
            Character.FacePosition(faceTarget);
        }

        /// <summary>
        /// Triggers attack animation/action on this puppet.
        /// </summary>
        /// <param name="attackType">0 = Melee, 1 = Charged Melee, 2 = Ranged</param>
        public void TriggerAttack(int attackType = 0)
        {
            if (Character == null || Character.IsEliminated) return;

            switch (attackType)
            {
                case 1:
                    Character.NotifyChargedAttackPerformed();
                    break;
                case 0:
                case 2:
                default:
                    Character.NotifyAttackPerformed();
                    break;
            }
        }
    }
}

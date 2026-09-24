using UnityEngine;
using Blocks.Character;
using UnityEngine.InputSystem;

namespace Blocks.Movement.Examples
{
    /// <summary>
    /// Player walk + sprint via input. The default ground movement for players.
    /// </summary>
    public class WalkAbility : MovementAbility
    {
        [Header("Input")]
        [SerializeField] InputActionReference moveAction;
        [SerializeField] InputActionReference sprintAction;

        [Header("Speed")]
        [SerializeField] float moveSpeed = 4.0f;
        [SerializeField] float sprintSpeed = 6.0f;
        [SerializeField] float speedChangeRate = 10.0f;

        [Header("Air Control")]
        [SerializeField][Range(0f, 1f)] float airControl = 0.5f;

        [Header("Stat Cost")]
        [SerializeField] StatType sprintStatType = StatType.Stamina;
        [SerializeField] float sprintCostPerSecond;

        InputHandle m_MoveHandle;
        InputHandle m_SprintHandle;
        float m_CurrentSpeed;

        protected override void OnInitialize()
        {
            m_MoveHandle = BindInput(moveAction);
            m_SprintHandle = BindInput(sprintAction);
        }

        protected override void OnRespawn()
        {
            // Accumulated speed describes a walk already under way. Respawning with the stick still
            // held would start at full speed instead of accelerating from rest.
            m_CurrentSpeed = 0f;
        }

        protected override void OnUpdate()
        {
            Vector2 moveInput = m_MoveHandle.GetValue<Vector2>();
            bool isSprinting = m_SprintHandle.IsPressed;

            float inputX = moveInput.x;
            bool hasInput = Mathf.Abs(inputX) > 0.01f;

            bool wantsToSprint = isSprinting && hasInput;

            if (wantsToSprint && !Character.HasAtLeast(sprintStatType, sprintCostPerSecond * Time.deltaTime))
            {
                isSprinting = false;
            }
            else if (wantsToSprint)
            {
                Character.Consume(sprintStatType, sprintCostPerSecond * Time.deltaTime);
            }

            float targetSpeed = hasInput ?
                (isSprinting ? sprintSpeed : moveSpeed)
                : 0f;
            m_CurrentSpeed = Mathf.Lerp(m_CurrentSpeed, targetSpeed, GetAcceleration());
            if (Mathf.Abs(m_CurrentSpeed - targetSpeed) < 0.01f)
            {
                m_CurrentSpeed = targetSpeed;
            }

            float direction = hasInput ? Mathf.Sign(inputX) : 0f;
            Character.AddMovement(new Vector2(direction * m_CurrentSpeed, 0f));
        }

        float GetAcceleration()
        {
            float acceleration = speedChangeRate * Time.deltaTime;

            if (!Character.IsGrounded)
            {
                acceleration *= airControl;
            }

            return acceleration;
        }
    }
}

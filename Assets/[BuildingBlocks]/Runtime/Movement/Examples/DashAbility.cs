using UnityEngine;
using Blocks.Character;
using UnityEngine.InputSystem;

namespace Blocks.Movement.Examples
{
    /// <summary>
    /// Player dash via input. Has a cooldown; can cost stamina.
    /// </summary>
    public class DashAbility : MovementAbility
    {
        [Header("Input")]
        [SerializeField] InputActionReference dashAction;

        [Header("Dash Settings")]
        [SerializeField] float duration = 0.2f;
        [SerializeField] float speed = 18.0f;
        [SerializeField] float cooldown = 0.6f;

        [Header("Stat Cost")]
        [SerializeField] StatType dashStatType = StatType.Stamina;
        [SerializeField] float dashCost;

        float m_DashRemaining;
        float m_CooldownRemaining;
        float m_Direction;

        protected override void OnInitialize()
        {
            BindInput(dashAction, input =>
            {
                input.Performed = StartDash;
                input.CanExecute = CanDash;
            });
        }

        protected override void OnCleanup()
        {
            ResetDash();
        }

        protected override void OnRespawn()
        {
            ResetDash();
        }

        // A dash still counting down resumes on the first tick after a respawn, driving the character
        // sideways at dash speed, from the spawn point, with no input. m_Direction needs no clearing:
        // nothing reads it until the next StartDash sets it.
        void ResetDash()
        {
            m_DashRemaining = 0f;
            m_CooldownRemaining = 0f;
        }

        protected override void OnUpdate()
        {
            if (m_DashRemaining > 0f)
            {
                Character.AddMovement(new Vector2(m_Direction * speed, 0f));
                Character.SetVerticalVelocity(0f);

                m_DashRemaining -= Time.deltaTime;
                if (m_DashRemaining <= 0f)
                {
                    m_DashRemaining = 0f;
                    m_CooldownRemaining = cooldown;
                }
                return;
            }

            if (m_CooldownRemaining > 0f)
            {
                m_CooldownRemaining -= Time.deltaTime;
            }
        }

        bool CanDash()
        {
            return m_DashRemaining <= 0f &&
                   m_CooldownRemaining <= 0f &&
                   Character.HasAtLeast(dashStatType, dashCost);
        }

        void StartDash()
        {
            if (!CanDash()) return;
            if (!Character.Consume(dashStatType, dashCost)) return;

            m_Direction = Character.FacingDirection;
            m_DashRemaining = duration;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Blocks.Character
{
    /// <summary>
    /// Drives an Animator from the character's own state and events: velocity, grounded, attacks,
    /// charging, hurt, elimination and victory. Every parameter is written only if the controller
    /// declares it, so a controller needs just the ones it actually animates.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CharacterAnimator : MonoBehaviour
    {
        static readonly int k_AnimIDVelocityX = Animator.StringToHash("velocityX");
        static readonly int k_AnimIDVelocityY = Animator.StringToHash("velocityY");
        static readonly int k_AnimIDGrounded = Animator.StringToHash("grounded");
        static readonly int k_AnimIDAttack = Animator.StringToHash("attack");
        static readonly int k_AnimIDChargedAttack = Animator.StringToHash("chargedAttack");
        static readonly int k_AnimIDCharging = Animator.StringToHash("charging");
        static readonly int k_AnimIDHurt = Animator.StringToHash("hurt");
        static readonly int k_AnimIDDead = Animator.StringToHash("dead");
        static readonly int k_AnimIDVictory = Animator.StringToHash("victory");

        readonly HashSet<int> m_KnownParams = new HashSet<int>();

        Animator m_Animator;
        BuildingBlocksCharacter m_Character;
        SpriteRenderer m_SpriteRenderer;

        // A charge that starts and ends inside one frame would never be seen by the controller; see
        // HandleAttackChargingChanged. These hold the 'charging' bool up for one evaluation.
        bool m_IsChargingOffPending;
        int m_ChargingOnFrame = -1;

        void Awake()
        {
            m_Animator = GetComponent<Animator>();
            // Search upward so the visuals (SpriteRenderer + Animator + this) can live on a scaled child
            // while the character stays on the root at scale 1.
            m_Character = GetComponentInParent<BuildingBlocksCharacter>();
            m_SpriteRenderer = GetComponent<SpriteRenderer>();

            if (m_Character == null)
            {
                Debug.LogWarning(
                    $"[CharacterAnimator] {name}: No BuildingBlocksCharacter on this GameObject or its parents — animation will not react to character events.",
                    this);
            }

            CacheKnownParameters();
        }

        void OnEnable()
        {
            if (m_Character == null) return;
            m_Character.OnDamaged += HandleDamaged;
            m_Character.OnEliminated += HandleEliminated;
            m_Character.OnRespawned += HandleRespawned;
            m_Character.OnAttackPerformed += HandleAttackPerformed;
            m_Character.OnChargedAttackPerformed += HandleChargedAttackPerformed;
            m_Character.OnAttackChargingChanged += HandleAttackChargingChanged;
            m_Character.OnVictory += HandleVictory;
        }

        void OnDisable()
        {
            // No Update will run to complete a deferred write, and leaving 'charging' stuck true would
            // strand the controller in the charge state.
            if (m_IsChargingOffPending)
            {
                m_IsChargingOffPending = false;
                SafeSetBool(k_AnimIDCharging, false);
            }

            if (m_Character == null) return;
            m_Character.OnDamaged -= HandleDamaged;
            m_Character.OnEliminated -= HandleEliminated;
            m_Character.OnRespawned -= HandleRespawned;
            m_Character.OnAttackPerformed -= HandleAttackPerformed;
            m_Character.OnChargedAttackPerformed -= HandleChargedAttackPerformed;
            m_Character.OnAttackChargingChanged -= HandleAttackChargingChanged;
            m_Character.OnVictory -= HandleVictory;
        }

        void Update()
        {
            if (m_Animator == null || m_Character == null) return;

            FlushPendingChargingOff();

            SafeSetFloat(k_AnimIDVelocityX, m_Character.CurrentSpeed);
            SafeSetFloat(k_AnimIDVelocityY, m_Character.VerticalVelocity);
            SafeSetBool(k_AnimIDGrounded, m_Character.IsGrounded);

            UpdateFacingDirection();
        }

        void UpdateFacingDirection()
        {
            if (m_SpriteRenderer == null) return;
            float directionX = m_Character.FacingDirection;
            m_SpriteRenderer.flipX = directionX < 0f;
        }

        void HandleDamaged(DamageInfo _)
        {
            SafeSetTrigger(k_AnimIDHurt);
        }

        void HandleEliminated()
        {
            SafeSetBool(k_AnimIDDead, true);
        }

        void HandleRespawned()
        {
            SafeSetBool(k_AnimIDDead, false);
        }

        void HandleAttackPerformed()
        {
            SafeSetTrigger(k_AnimIDAttack);
        }

        void HandleChargedAttackPerformed()
        {
            // Falls back to the regular attack trigger so a controller without a dedicated
            // 'chargedAttack' parameter still plays something on the release.
            if (m_KnownParams.Contains(k_AnimIDChargedAttack)) m_Animator.SetTrigger(k_AnimIDChargedAttack);
            else SafeSetTrigger(k_AnimIDAttack);
        }

        // A tap can begin and end a charge within a single frame: a buffered press flushing after the
        // button was already released resolves both in one call. Writing 'charging' straight through
        // would set it true and false between two animator evaluations, so the controller would never
        // see the charge: it would skip the charge state, and with it every transition leading out of
        // that state. Holding the 'off' back by one frame guarantees the charge is always observed.
        void HandleAttackChargingChanged(bool isCharging)
        {
            if (isCharging)
            {
                // A charge starting again supersedes an 'off' still waiting to be written.
                m_IsChargingOffPending = false;
                m_ChargingOnFrame = Time.frameCount;
                SafeSetBool(k_AnimIDCharging, true);
                return;
            }

            if (Time.frameCount == m_ChargingOnFrame)
            {
                m_IsChargingOffPending = true;
                return;
            }

            SafeSetBool(k_AnimIDCharging, false);
        }

        // Runs from Update, which is always a later frame than the one that deferred the write: the
        // animator has evaluated once in between, so the charge has been seen.
        void FlushPendingChargingOff()
        {
            if (!m_IsChargingOffPending || Time.frameCount == m_ChargingOnFrame) return;

            m_IsChargingOffPending = false;
            SafeSetBool(k_AnimIDCharging, false);
        }

        void HandleVictory()
        {
            SafeSetTrigger(k_AnimIDVictory);
        }

        public void BeginStun()
        {
            if (m_Character == null) return;
            m_Character.SetStunned(true);
        }

        public void EndStun()
        {
            if (m_Character == null) return;
            m_Character.SetStunned(false);
        }

        void CacheKnownParameters()
        {
            m_KnownParams.Clear();
            if (m_Animator == null) return;

            foreach (var parameter in m_Animator.parameters)
            {
                m_KnownParams.Add(parameter.nameHash);
            }
        }

        void SafeSetFloat(int id, float value)
        {
            if (m_KnownParams.Contains(id)) m_Animator.SetFloat(id, value);
        }

        void SafeSetBool(int id, bool value)
        {
            if (m_KnownParams.Contains(id)) m_Animator.SetBool(id, value);
        }

        void SafeSetTrigger(int id)
        {
            if (m_KnownParams.Contains(id)) m_Animator.SetTrigger(id);
        }
    }
}

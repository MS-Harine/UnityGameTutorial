using UnityEngine;
using UnityEngine.InputSystem;
using Blocks.Audio;
using Blocks.Character;
using Blocks.Extras;

namespace Blocks.Movement
{
    /// <summary>
    /// All-in-one player locomotion tuned for game feel: walk/run with acceleration and turn grip,
    /// plus a variable-height jump with coyote time, input buffering and apex hang.
    /// Replaces the separate Walk/Jump example abilities; don't run those alongside this one.
    /// Fall gravity (heavier while descending) is tuned on the BuildingBlocksCharacter itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerMovementAbility : MovementAbility
    {
        [Header("Input")]
        [SerializeField] InputActionReference moveAction;
        [SerializeField] InputActionReference sprintAction;
        [SerializeField] InputActionReference jumpAction;

        [Header("Walk & Run")]
        [SerializeField] float walkSpeed = 4.0f;
        [Tooltip("Top speed while Sprint is held.")]
        [SerializeField] float runSpeed = 6.5f;
        [Tooltip("How quickly speed builds toward the target, in units/second². Higher = snappier starts.")]
        [SerializeField] float acceleration = 60f;
        [Tooltip("How quickly the character stops once input is released. Higher = less sliding.")]
        [SerializeField] float deceleration = 80f;
        [Tooltip("Acceleration is multiplied by this while reversing direction — extra grip so turns feel responsive instead of skatey.")]
        [SerializeField][Range(1f, 5f)] float turnGrip = 2f;
        [Tooltip("Fraction of ground acceleration available while airborne. 1 = full mid-air steering, 0 = jumps keep their launch momentum.")]
        [SerializeField][Range(0f, 1f)] float airControl = 0.7f;

        [Header("Jump")]
        [SerializeField] float jumpHeight = 2.5f;
        [Tooltip("Grace period after walking off a ledge where a jump still counts, in seconds.")]
        [SerializeField] float coyoteTime = 0.15f;
        [Tooltip("A jump pressed this early before landing is remembered and fires on touchdown.")]
        [SerializeField] float jumpBufferTime = 0.1f;
        [SerializeField] float jumpCooldown = 0.1f;
        [Tooltip("Releasing Jump while still rising multiplies upward speed by this. Lower = tap for short hops, hold for full height.")]
        [SerializeField][Range(0f, 1f)] float jumpCutMultiplier = 0.5f;

        [Header("Jump Apex")]
        [Tooltip("Gravity is scaled by this near the top of a jump — lower = floatier hang time at the peak.")]
        [SerializeField][Range(0f, 1f)] float apexGravityMultiplier = 0.6f;
        [Tooltip("Upward speed below which the jump counts as 'near the apex' and hang time kicks in.")]
        [SerializeField] float apexThreshold = 1.5f;

        [Header("Stat Cost")]
        [SerializeField] StatType staminaStat = StatType.Stamina;
        [SerializeField] float sprintCostPerSecond;
        [SerializeField] float jumpCost;

        [Header("Effects")]
        [Tooltip("Spawned at the feet when a jump starts — e.g. a dust puff.")]
        [SerializeField] OneShotVfx jumpVfxPrefab;
        [Tooltip("Spawned at the feet on landing.")]
        [SerializeField] OneShotVfx landVfxPrefab;
        [Tooltip("Spawned at the feet on an interval while running on the ground.")]
        [SerializeField] OneShotVfx runDustVfxPrefab;
        [SerializeField] float runDustInterval = 0.25f;

        [Header("Audio")]
        [Tooltip("Plays the movement sounds. Leave empty to use the AudioSource on this GameObject.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Footstep sounds — one is picked at random per step, so a handful keeps walking from sounding looped.")]
        [SerializeField] AudioClip[] footstepClips;
        [Tooltip("Ground covered between footsteps, in units. Measuring in distance rather than time keeps the steps in sync with the legs whether walking or sprinting.")]
        [SerializeField] float footstepStride = 1.75f;
        [Tooltip("1 is the clip's own level; above that boosts it. Footstep recordings are usually mastered far quieter than music, so they need the boost to be heard at all.")]
        [SerializeField][Range(0f, 5f)] float footstepVolume = 3f;
        [Tooltip("Played once when a jump starts.")]
        [SerializeField] AudioClip jumpClip;
        [SerializeField][Range(0f, 5f)] float jumpVolume = 2.5f;
        [Tooltip("Played once on touchdown.")]
        [SerializeField] AudioClip landClip;
        [SerializeField][Range(0f, 5f)] float landVolume = 2.5f;
        [Tooltip("Each sound is pitched up or down by up to this much, so repeats don't sound identical. 0 = no variation.")]
        [SerializeField][Range(0f, 0.5f)] float pitchVariation = 0.08f;

        InputHandle m_MoveHandle;
        InputHandle m_SprintHandle;
        float m_HorizontalSpeed;
        bool m_CanCutJump;
        float m_RunDustTimer;
        float m_DistanceSinceFootstep;
        AudioHelper m_Audio;

        protected override void OnInitialize()
        {
            m_MoveHandle = BindInput(moveAction);
            m_SprintHandle = BindInput(sprintAction);
            BindInput(jumpAction, ConfigureJump);

            Character.OnLanded += HandleLanded;

            SetUpAudio();
            WarnAboutCompetingAbilities();
        }

        protected override void OnCleanup()
        {
            if (Character != null)
            {
                Character.OnLanded -= HandleLanded;
            }
        }

        protected override void OnRespawn()
        {
            // Momentum survives elimination, and TickLocomotion decelerates from wherever it left off
            // rather than starting at rest, so without this a sprint into a pit respawns mid-slide.
            // m_DistanceSinceFootstep is left alone: TickFootsteps re-primes it on the first tick at
            // rest, and to half a stride rather than to zero.
            m_HorizontalSpeed = 0f;
            m_CanCutJump = false;
            m_RunDustTimer = 0f;
        }

        protected override void OnUpdate()
        {
            float deltaTime = Time.deltaTime;

            if (!Character.IsGrounded)
            {
                ApplyApexHang(deltaTime);
            }

            TickLocomotion(deltaTime);
        }

        void TickLocomotion(float deltaTime)
        {
            float inputX = m_MoveHandle.GetValue<Vector2>().x;
            bool hasInput = Mathf.Abs(inputX) > 0.01f;

            bool isSprinting = hasInput && m_SprintHandle.IsPressed && TryPaySprintCost(deltaTime);

            float maxSpeed = isSprinting ? runSpeed : walkSpeed;
            float targetSpeed = hasInput ? Mathf.Sign(inputX) * maxSpeed : 0f;

            float rate = hasInput ? acceleration : deceleration;
            bool isReversing = hasInput &&
                               Mathf.Abs(m_HorizontalSpeed) > 0.01f &&
                               !Mathf.Approximately(Mathf.Sign(inputX), Mathf.Sign(m_HorizontalSpeed));
            if (isReversing)
            {
                rate *= turnGrip;
            }

            if (!Character.IsGrounded)
            {
                rate *= airControl;
            }

            m_HorizontalSpeed = Mathf.MoveTowards(m_HorizontalSpeed, targetSpeed, rate * deltaTime);
            Character.AddMovement(new Vector2(m_HorizontalSpeed, 0f));

            TickRunDust(deltaTime);
            TickFootsteps(deltaTime);
        }

        bool TryPaySprintCost(float deltaTime)
        {
            if (sprintCostPerSecond <= 0f) return true;

            float cost = sprintCostPerSecond * deltaTime;
            if (!Character.HasAtLeast(staminaStat, cost)) return false;

            Character.Consume(staminaStat, cost);
            return true;
        }

        void ApplyApexHang(float deltaTime)
        {
            float verticalVelocity = Character.VerticalVelocity;
            if (verticalVelocity <= 0f || verticalVelocity > apexThreshold) return;

            // The character applies full gravity after abilities tick; adding back the difference
            // leaves an effective gravity of (gravity * apexGravityMultiplier) near the peak.
            float counterGravity = Character.Gravity * (apexGravityMultiplier - 1f) * deltaTime;
            Character.SetVerticalVelocity(verticalVelocity + counterGravity);
        }

        void ConfigureJump(InputHandle input)
        {
            input.Performed = PerformJump;
            input.Canceled = CutJump;
            input.CanExecute = CanJump;
            input.BufferTime = jumpBufferTime;
            input.CooldownTime = jumpCooldown;
        }

        bool CanJump()
        {
            // Character.Jump silently no-ops while stunned or attack-locked; gating here instead
            // lets the input buffer hold the press and fire it the moment the lock clears.
            if (Character.IsStunned || Character.IsAttackMovementLocked) return false;
            if (!Character.HasAtLeast(staminaStat, jumpCost)) return false;

            return Character.IsGroundedOrInCoyote(coyoteTime);
        }

        void PerformJump()
        {
            if (!Character.IsGroundedOrInCoyote(coyoteTime)) return;

            Character.Consume(staminaStat, jumpCost);

            Character.Jump(jumpHeight);
            SpawnVfx(jumpVfxPrefab, FeetPosition);
            m_Audio.Play(jumpClip, jumpVolume);

            m_CanCutJump = true;
        }

        void CutJump()
        {
            if (!m_CanCutJump) return;
            m_CanCutJump = false;

            float verticalVelocity = Character.VerticalVelocity;
            if (verticalVelocity <= 0f) return;

            Character.SetVerticalVelocity(verticalVelocity * jumpCutMultiplier);
        }

        void TickRunDust(float deltaTime)
        {
            if (m_RunDustTimer > 0f)
            {
                m_RunDustTimer -= deltaTime;
            }

            bool isRunning = Character.IsGrounded && Mathf.Abs(m_HorizontalSpeed) > walkSpeed + 0.01f;
            if (!isRunning || m_RunDustTimer > 0f) return;

            m_RunDustTimer = runDustInterval;

            SpawnVfx(runDustVfxPrefab, FeetPosition);
        }

        void HandleLanded(float impactVelocity)
        {
            SpawnVfx(landVfxPrefab, FeetPosition);
            m_Audio.Play(landClip, landVolume);
        }

        void TickFootsteps(float deltaTime)
        {
            float speed = Mathf.Abs(m_HorizontalSpeed);
            if (!Character.IsGrounded || speed < 0.01f)
            {
                // Start the next stride half-spent so the first step lands shortly after the legs
                // start moving, instead of a full stride of silence every time the player sets off.
                m_DistanceSinceFootstep = footstepStride * 0.5f;
                return;
            }

            m_DistanceSinceFootstep += speed * deltaTime;
            if (m_DistanceSinceFootstep < footstepStride) return;

            m_DistanceSinceFootstep -= footstepStride;
            PlayFootstep();
        }

        void PlayFootstep()
        {
            m_Audio.PlayRandom(footstepClips, footstepVolume);
        }

        void SetUpAudio()
        {
            AudioHelper.WarmUp(jumpClip);
            AudioHelper.WarmUp(landClip);
            AudioHelper.WarmUp(footstepClips);

            bool hasClips = jumpClip != null || landClip != null ||
                            (footstepClips != null && footstepClips.Length > 0);

            m_Audio = new AudioHelper(
                this, audioSource, hasClips,
                clipFieldHint: "'Footstep Clips', 'Jump Clip', 'Land Clip'",
                ownerLabel: Character.name, pitchVariation: pitchVariation);
        }

        void WarnAboutCompetingAbilities()
        {
            MovementAbility[] abilities = Character.GetComponentsInChildren<MovementAbility>();
            foreach (MovementAbility ability in abilities)
            {
                if (ability == this || !ability.enabled) continue;

                Debug.LogWarning(
                    $"[PlayerMovementAbility] {Character.name}: '{ability.GetType().Name}' is also enabled — " +
                    "this ability already handles walk, run and jump, so both will fight for control. Disable one of them.",
                    this);
            }
        }

        void OnValidate()
        {
            if (walkSpeed < 0f) walkSpeed = 0f;
            if (runSpeed < walkSpeed) runSpeed = walkSpeed;
            if (acceleration < 0f) acceleration = 0f;
            if (deceleration < 0f) deceleration = 0f;
            if (jumpHeight < 0f) jumpHeight = 0f;
            if (coyoteTime < 0f) coyoteTime = 0f;
            if (jumpBufferTime < 0f) jumpBufferTime = 0f;
            if (jumpCooldown < 0f) jumpCooldown = 0f;
            if (apexThreshold < 0f) apexThreshold = 0f;
            if (sprintCostPerSecond < 0f) sprintCostPerSecond = 0f;
            if (jumpCost < 0f) jumpCost = 0f;
            if (runDustInterval < 0.05f) runDustInterval = 0.05f;
            if (footstepStride < 0.1f) footstepStride = 0.1f;
        }
    }
}

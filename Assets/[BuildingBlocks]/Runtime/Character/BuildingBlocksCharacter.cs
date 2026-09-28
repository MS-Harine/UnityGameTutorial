using System;
using UnityEngine;
using Blocks.Attack;
using Blocks.Movement;
using System.Collections;
using System.Collections.Generic;

namespace Blocks.Character
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    [DisallowMultipleComponent]
    public class BuildingBlocksCharacter : MonoBehaviour, IDamageable
    {
        #region Inspector Configuration

        [SerializeField] List<Stat> stats = new();

        [Header("On Eliminated")]
        [SerializeField] EliminationBehavior onEliminated = EliminationBehavior.Respawn;
        [Tooltip("Seconds between being eliminated and respawning (or being destroyed). The HUD counts this down.")]
        [SerializeField] float delay;
        [Tooltip("Seconds after respawning before the character gets its controls back — use it to let a spawn " +
                 "animation finish. The character can't be damaged while it waits. 0 hands control straight back.")]
        [SerializeField] float controlDelayAfterRespawn;

        [Header("Modules")]
        [SerializeField] bool isMovementEnabled = true;
        [SerializeField] bool isTargetingEnabled = true;
        [SerializeField] bool isAttackEnabled = true;

        [Header("Movement")]
        [SerializeField] float gravity = -20.0f;
        [SerializeField] float fallGravityMultiplier = 1.5f;
        [SerializeField] float maxFallSpeed = 53.0f;
        [SerializeField][Range(1f, 89f)] float groundSlopeLimit = 45f;
        [SerializeField][Range(1f, 89f)] float wallSlopeLimit = 45f;

        [Header("Contact")]
        [Tooltip("Other characters can stand on this one. Off means anyone landing on it slides off.")]
        [SerializeField] bool canBeStoodOn;
        [Tooltip("Sideways speed that shoves a character off this one's head. Ignored when Can Be Stood On is on.")]
        [SerializeField] float pushOffSpeed = 9f;

        [Header("Targeting")]
        [SerializeField] TargetingMode targetingMode = TargetingMode.NearestDamageable;
        [SerializeField] string targetTag = "Player";
        [SerializeField] float targetRadius = 8f;
        [SerializeField] float targetMemoryDuration = 1.5f;

        [Header("Hit Reaction")]
        [SerializeField] float invulnerabilityDuration;

        #endregion

        #region Module Instances

        readonly StatsModule m_StatsModule = new StatsModule();
        readonly RespawnModule m_RespawnModule = new RespawnModule();
        readonly MovementModule m_MovementModule = new MovementModule();
        readonly TargetingModule m_TargetingModule = new TargetingModule();
        readonly AttackModule m_AttackModule = new AttackModule();
        readonly AbilitiesModule m_AbilitiesModule = new AbilitiesModule();
        readonly HitReactionModule m_HitReactionModule = new HitReactionModule();

        #endregion

        #region Runtime State

        Rigidbody2D m_Rigidbody;
        Coroutine m_RespawnRoutine;
        bool m_IsPaused;
        bool m_IsAttackLockHeld;
        float m_AttackLockLeft;

        #endregion

        #region Unity Lifecycle

        void Reset()
        {
            if (stats == null)
            {
                stats = new List<Stat>();
            }

            if (stats.Count == 0)
            {
                stats.Add(new Stat(StatType.Health));
            }
        }

        void OnValidate()
        {
            if (delay < 0f) delay = 0f;
            if (controlDelayAfterRespawn < 0f) controlDelayAfterRespawn = 0f;
            if (targetRadius < 0f) targetRadius = 0f;
            if (targetMemoryDuration < 0f) targetMemoryDuration = 0f;
            if (invulnerabilityDuration < 0f) invulnerabilityDuration = 0f;
            if (pushOffSpeed < 0f) pushOffSpeed = 0f;

            if (!canBeStoodOn && pushOffSpeed <= 0f)
            {
                Debug.LogWarning(
                    $"[BuildingBlocksCharacter] {name}: Push Off Speed is 0 while Can Be Stood On is off — " +
                    "a character landing on this one will hover on its head with no way off. " +
                    "Set a speed above 0, or tick Can Be Stood On to make it a platform.",
                    this);
            }

            if (targetingMode == TargetingMode.TaggedDamageable && string.IsNullOrWhiteSpace(targetTag))
            {
                Debug.LogError(
                    $"[BuildingBlocksCharacter] {name}: Target Tag is required when Targeting Mode is Tagged Damageable.",
                    this);
            }

            // A module switch flipped in the inspector during play lands on the serialized field without
            // going through its property, so this is the only chance to unbind that module's input.
            if (Application.isPlaying) ApplyAbilityInputState();

            if (stats == null || stats.Count == 0)
            {
                Debug.LogError(
                    $"[BuildingBlocksCharacter] {name}: No stats configured. Add a Health stat — actor cannot take damage.",
                    this);
            }
        }

        void Awake()
        {
            m_RespawnModule.Initialize(transform);

            m_Rigidbody = GetComponent<Rigidbody2D>();
            Collider2D ownedCollider = GetComponent<Collider2D>();
            m_MovementModule.Initialize(m_Rigidbody, ownedCollider, HandleLanded, HandleGroundedStateChanged, HandleCeilingHit);

            m_StatsModule.Initialize(stats, this, name, HandleStatChanged, HandleStatDepleted);

            m_AbilitiesModule.Discover(this);
            ApplyAbilityInputState();
        }

        void Update()
        {
            if (IsEliminated) return;

            float deltaTime = Time.deltaTime;

            m_StatsModule.Tick(stats, deltaTime);

            m_MovementModule.RefreshContacts();
            m_MovementModule.CheckGrounded(GroundDotThreshold);
            m_MovementModule.CheckCeiling(GroundDotThreshold);

            TickTargeting(deltaTime);
            TickHitReaction(deltaTime);

            if (m_AttackLockLeft > 0f) m_AttackLockLeft -= deltaTime;

            if (!isMovementEnabled)
            {
                // Switched off in the inspector: the module owns this character's physics, so nothing
                // moves it — gravity included. A pause is the other case, and it still falls.
                m_MovementModule.Freeze();
            }
            else if (m_IsPaused)
            {
                m_MovementModule.ProcessPaused(gravity, fallGravityMultiplier, maxFallSpeed);
            }
            else
            {
                m_MovementModule.BeginMovementAbilities();

                // While stunned, attack-locked, or being moved by a hit, the character's own
                // abilities don't get to fight the reaction.
                if (!IsStunned && !IsAttackMovementLocked && !m_HitReactionModule.IsReacting)
                {
                    m_AbilitiesModule.TickMovement();
                }

                float reactionVelocityX = m_HitReactionModule.PushVelocityX + m_HitReactionModule.LaunchVelocityX;
                if (reactionVelocityX != 0f)
                {
                    m_MovementModule.AddMovement(new Vector2(reactionVelocityX, 0f));
                }

                m_MovementModule.ApplyMovement(gravity, fallGravityMultiplier, maxFallSpeed, IsFacingLocked,
                                               m_HitReactionModule.IsReacting);
            }

            if (IsAttackActive)
            {
                m_AbilitiesModule.TickAttack();
            }
        }

        void FixedUpdate()
        {
            if (IsEliminated) return;

            // Ground motion is sampled on the physics clock, where platforms move (MovePosition).
            m_MovementModule.SampleGroundVelocity();

            if (isMovementEnabled && !m_IsPaused && !IsStunned && !IsAttackMovementLocked && !m_HitReactionModule.IsReacting)
            {
                m_AbilitiesModule.FixedTickMovement();
            }

            if (IsAttackActive)
            {
                m_AbilitiesModule.FixedTickAttack();
            }
        }

        void OnDestroy()
        {
            m_AbilitiesModule.Cleanup();
            m_AttackModule.DisposePools();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, targetRadius);

            if (CurrentTargetTransform != null)
            {
                Gizmos.DrawLine(transform.position, CurrentTargetTransform.position);
            }
        }

        #endregion

        #region Module Enablement

        public bool IsMovementEnabled
        {
            get => isMovementEnabled;
            set
            {
                if (isMovementEnabled == value) return;
                isMovementEnabled = value;
                ApplyAbilityInputState();
            }
        }

        public bool IsTargetingEnabled
        {
            get => isTargetingEnabled;
            set
            {
                if (isTargetingEnabled == value) return;
                isTargetingEnabled = value;
                ApplyAbilityInputState();
            }
        }

        public bool IsAttackEnabled
        {
            get => isAttackEnabled;
            set
            {
                if (isAttackEnabled == value) return;
                isAttackEnabled = value;
                ApplyAbilityInputState();
            }
        }

        /// <summary>
        /// True when this character's attacks may act. The one definition of that, because attacks are
        /// driven from two places: the tick loops here, and <c>ContactAttackAbility</c>, whose damage
        /// comes from a collision message that no tick gate can reach.
        /// </summary>
        public bool IsAttackActive => isAttackEnabled && !m_IsPaused && !IsStunned && !IsEliminated;

        /// <summary>
        /// Pushes the module switches down to the abilities. The single place that decides whether an
        /// ability's input is live, so the inspector switches and <see cref="Pause"/> can never disagree
        /// about it. Called from OnValidate too: the inspector writes the serialized fields directly, so
        /// a switch flipped during play never runs the property setters above, and without this its
        /// abilities would keep buffering input that the tick loops then refuse to act on.
        /// </summary>
        void ApplyAbilityInputState()
        {
            m_AbilitiesModule.SetMovementInputEnabled(isMovementEnabled && !m_IsPaused);
            m_AbilitiesModule.SetAttackInputEnabled(isAttackEnabled && !m_IsPaused);

            if (!isTargetingEnabled || m_IsPaused) ClearTarget();
        }

        /// <summary>
        /// Takes this character's controls away until <see cref="Resume"/>. Distinct from the module
        /// switches above, which are the designer's authoring choice: a pause leaves them untouched and
        /// gates on top of them, so resuming hands back exactly what was authored. Both halves ignore a
        /// call that would double up, since pauses overlap in practice — dying inside <c>WinDoor</c>'s
        /// victory pause, or during the respawn control delay.
        /// </summary>
        public void Pause()
        {
            if (m_IsPaused) return;
            m_IsPaused = true;
            ApplyAbilityInputState();
        }

        public void Resume()
        {
            if (!m_IsPaused) return;
            m_IsPaused = false;
            ApplyAbilityInputState();
        }

        #endregion

        #region Stats and Damage

        public event Action<float, float> OnHealthChanged;
        public event Action<StatType, float, float> OnAnyStatChanged;

        /// <summary>
        /// Raised when any stat empties, carrying which one. Nothing subscribes in the sample: Health is
        /// the only stat that matters here, and elimination already handles it. This is the hook for the
        /// others, like stamina gating a sprint or mana gating a spell.
        /// </summary>
        public event Action<StatType> OnAnyStatDepleted;
        public event Action<DamageInfo> OnDamaged;
        public event Action<bool> OnInvulnerabilityChanged;

        public bool IsDepleted => m_StatsModule.IsDepleted;
        public bool IsDamageable => !IsEliminated && !IsInvulnerable;
        public bool IsInvulnerable => m_HitReactionModule.IsInvulnerable;
        public float HealthRatio => m_StatsModule.HealthRatio;
        public float CurrentHealth => m_StatsModule.CurrentHealth;
        public float MaxHealth => m_StatsModule.MaxHealth;

        public bool Has(StatType type) => m_StatsModule.Has(type);

        public float GetValue(StatType type)
        {
            return m_StatsModule.GetValue(type);
        }

        public float GetMax(StatType type)
        {
            return m_StatsModule.GetMax(type);
        }

        public Stat GetStat(StatType type)
        {
            return m_StatsModule.GetStat(type);
        }

        public bool HasAtLeast(StatType type, float amount)
        {
            return m_StatsModule.HasAtLeast(type, amount);
        }

        public bool Consume(StatType type, float amount)
        {
            return m_StatsModule.Consume(type, amount);
        }

        public void Apply(StatType type, float delta)
        {
            m_StatsModule.Apply(type, delta);
        }

        public void TakeDamage(DamageInfo damage)
        {
            if (!IsDamageable) return;
            if (damage.Amount <= 0f) return;

            Apply(StatType.Health, -damage.Amount);
            OnDamaged?.Invoke(damage);

            // A lethal hit already ran Eliminate() inside Apply; no reaction for the dead.
            if (IsEliminated) return;

            m_HitReactionModule.OnHit(in damage, invulnerabilityDuration, Mass);

            // The vertical half of a fly-away is a one-time pop; the horizontal half is
            // sustained by the hit-reaction module until landing.
            if (m_HitReactionModule.ConsumeLaunchStarted(out float launchVelocityY))
            {
                SetVerticalVelocity(launchVelocityY);
            }

            if (IsInvulnerable) OnInvulnerabilityChanged?.Invoke(true);
            if (damage.Reaction.StunTime > 0f) SetStunned(true);
        }

        void TickHitReaction(float deltaTime)
        {
            m_HitReactionModule.Tick(deltaTime, IsGrounded);

            if (m_HitReactionModule.ConsumeStunEnded()) SetStunned(false);
            if (m_HitReactionModule.ConsumeInvulnerabilityEnded()) OnInvulnerabilityChanged?.Invoke(false);
        }

        // The heavier the rigidbody, the smaller every hit reaction (see HitReactionModule).
        float Mass => m_Rigidbody != null ? m_Rigidbody.mass : 1f;

        // Freeze facing during a hit reaction so the push doesn't flip the sprite away from the attacker.
        bool IsFacingLocked => IsStunned || m_HitReactionModule.IsReacting;

        public void ForEachStat(Action<Stat> action)
        {
            m_StatsModule.ForEachStat(stats, action);
        }

        void HandleStatChanged(StatType type, float oldValue, float newValue)
        {
            OnAnyStatChanged?.Invoke(type, oldValue, newValue);

            if (type != StatType.Health) return;

            OnHealthChanged?.Invoke(oldValue, newValue);
        }

        void HandleStatDepleted(StatType type)
        {
            OnAnyStatDepleted?.Invoke(type);

            if (type == StatType.Health) Eliminate();
        }

        #endregion

        #region Respawn and Elimination

        public event Action OnEliminated;
        public event Action<float> OnRespawning;
        public event Action OnRespawned;

        public bool IsEliminated { get; private set; }

        /// <summary>True when this character comes back to life after being eliminated.</summary>
        public bool RespawnsAfterElimination => onEliminated == EliminationBehavior.Respawn;

        void Eliminate()
        {
            if (IsEliminated) return;
            IsEliminated = true;

            IsStunned = false;
            if (IsInvulnerable) OnInvulnerabilityChanged?.Invoke(false);
            m_HitReactionModule.Clear();
            m_IsAttackLockHeld = false;
            m_AttackLockLeft = 0f;
            Pause();
            ClearTarget();
            // Projectiles already in the air are deliberately left alone: a grenade keeps arcing and still
            // explodes after the character that threw it goes down. The pool itself is torn down in OnDestroy.
            m_RespawnModule.SetActiveInScene(false);
            m_RespawnModule.StopRigidbodyMotion();

            OnEliminated?.Invoke();

            switch (onEliminated)
            {
                case EliminationBehavior.Respawn:
                    if (m_RespawnRoutine != null) StopCoroutine(m_RespawnRoutine);
                    m_RespawnRoutine = StartCoroutine(RespawnRoutine());
                    break;
                case EliminationBehavior.Destroy:
                    if (delay <= 0f) Destroy(gameObject);
                    else StartCoroutine(DestroyRoutine());
                    break;
                case EliminationBehavior.Disable:
                    break;
            }
        }

        IEnumerator RespawnRoutine()
        {
            float remaining = delay;
            while (remaining > 0f)
            {
                OnRespawning?.Invoke(remaining);
                yield return null;
                remaining -= Time.deltaTime;
            }

            Respawn();

            // Respawn() deliberately stops short of Resume(), so the spawn animation gets to play over a
            // character that is back in the scene but still has no controls. Keeping the wait in this
            // coroutine rather than a second one means Eliminate()'s StopCoroutine already covers it: dying
            // again inside the window cancels the wait instead of handing control back mid-death.
            if (controlDelayAfterRespawn > 0f) yield return new WaitForSeconds(controlDelayAfterRespawn);

            m_RespawnRoutine = null;
            Resume();
        }

        IEnumerator DestroyRoutine()
        {
            yield return new WaitForSeconds(delay);
            Destroy(gameObject);
        }

        public void Respawn()
        {
            RespawnAt(m_RespawnModule.StartPosition, m_RespawnModule.StartRotation);
        }

        public void Respawn(Vector3 position)
        {
            RespawnAt(position, transform.rotation);
        }

        void RespawnAt(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);

            // All three are needed, and none replaces another. StopRigidbodyMotion zeroes the bodies,
            // children included; the movement model composes velocity additively and writes it to the
            // rigidbody every tick, so its own state would undo that zeroing a frame later; and the
            // abilities feed that composition, so their accumulated speed would come back with them.
            // Ahead of IsEliminated and OnRespawned so no tick can interleave and listeners (the
            // animator reads VerticalVelocity) see a character at rest rather than one still falling.
            m_RespawnModule.StopRigidbodyMotion();
            m_MovementModule.ResetVelocityAndForces();
            m_AbilitiesModule.NotifyRespawn();

            m_StatsModule.SetAllToMax(stats);

            m_RespawnModule.SetActiveInScene(true);

            IsEliminated = false;

            // Controls come back in RespawnRoutine, not here. Until they do the character is visible and
            // collidable but can't move or fight back, so the same window gets i-frames — nothing should get
            // a free hit on a player still playing their spawn animation.
            if (controlDelayAfterRespawn > 0f)
            {
                m_HitReactionModule.BeginInvulnerability(controlDelayAfterRespawn);
                OnInvulnerabilityChanged?.Invoke(true);
            }

            OnRespawned?.Invoke();
        }

        #endregion

        #region Victory

        public event Action OnVictory;

        /// <summary>Called by game code (e.g. <c>WinDoor</c>) when this character wins; raises <see cref="OnVictory"/>.</summary>
        public void NotifyVictory()
        {
            OnVictory?.Invoke();
        }

        #endregion

        #region Movement

        public event Action OnJump;
        public event Action<float> OnLanded;
        public event Action<bool> OnGroundedStateChanged;

        /// <summary>
        /// Raised when this character's head hits a ceiling. <c>MovementModule</c> already cancels the
        /// upward velocity, so nothing subscribes in the sample. This is the hook for the presentation
        /// on top of that: a dust puff, a thud, a screen shake.
        /// </summary>
        public event Action OnCeilingHit;

        public bool IsGrounded => m_MovementModule.IsGrounded;
        public bool IsTouchingCeiling => m_MovementModule.IsTouchingCeiling;
        public bool IsTouchingLeftWall => m_MovementModule.IsTouchingLeftWall(WallDotThreshold);
        public bool IsTouchingRightWall => m_MovementModule.IsTouchingRightWall(WallDotThreshold);

        public float VerticalVelocity => m_MovementModule.VerticalVelocity;
        public float CurrentSpeed => m_MovementModule.CurrentSpeed;
        public float FacingDirection => m_MovementModule.FacingDirection;
        public float Gravity => gravity;

        public Vector2 Velocity => m_MovementModule.Velocity;

        public Bounds ColliderBounds => m_MovementModule.Collider != null
            ? m_MovementModule.ColliderBounds
            : new Bounds(transform.position, Vector3.zero);

        public Collider2D Collider => m_MovementModule.Collider;
        public Transform Transform => transform;

        /// <summary>
        /// False (the default) means this character is not a platform: another character landing on
        /// it is never grounded and gets shoved sideways off at <see cref="PushOffSpeed"/>, unharmed.
        /// </summary>
        public bool CanBeStoodOn => canBeStoodOn;

        /// <summary>Sideways speed that shoves a character off this one's head. See <see cref="CanBeStoodOn"/>.</summary>
        public float PushOffSpeed => pushOffSpeed;

        float GroundDotThreshold => Mathf.Cos(groundSlopeLimit * Mathf.Deg2Rad);
        float WallDotThreshold => Mathf.Cos(wallSlopeLimit * Mathf.Deg2Rad);

        public void FacePosition(Vector2 position)
        {
            m_MovementModule.FacePosition(transform.position, position);
        }

        public void SetVerticalVelocity(float newVerticalVelocity) => m_MovementModule.SetVerticalVelocity(newVerticalVelocity);

        public bool IsGroundedOrInCoyote(float coyoteTime)
        {
            return m_MovementModule.IsGroundedOrInCoyote(coyoteTime);
        }

        public void Jump(float height)
        {
            // Input callbacks fire outside the tick loop, so locks must be enforced here too.
            if (IsStunned || IsAttackMovementLocked || m_HitReactionModule.IsReacting) return;

            m_MovementModule.Jump(height, gravity);
            OnJump?.Invoke();
        }

        public void AddMovement(Vector2 movement) => m_MovementModule.AddMovement(movement);

        public void SetPosition(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        void HandleLanded(float verticalVelocity)
        {
            OnLanded?.Invoke(verticalVelocity);
        }

        void HandleGroundedStateChanged(bool isGrounded)
        {
            OnGroundedStateChanged?.Invoke(isGrounded);
        }

        void HandleCeilingHit()
        {
            OnCeilingHit?.Invoke();
        }

        #endregion

        #region Targeting

        public event Action<IDamageable> OnTargetAcquired;
        public event Action OnTargetLost;

        public IDamageable CurrentTarget => m_TargetingModule.CurrentTarget;
        public Transform CurrentTargetTransform => m_TargetingModule.CurrentTargetTransform;
        public bool HasTarget => m_TargetingModule.HasTarget;

        public bool IsSelf(IDamageable target)
        {
            if (target == null) return false;

            Transform targetTransform = target.Transform;
            return targetTransform == transform || targetTransform.IsChildOf(transform);
        }

        public void FaceTarget()
        {
            if (!HasTarget) return;
            FacePosition(CurrentTargetTransform.position);
        }

        void TickTargeting(float deltaTime)
        {
            m_TargetingModule.Tick(transform, Collider, isTargetingEnabled && !m_IsPaused, targetingMode, targetTag, targetRadius, targetMemoryDuration, deltaTime, IsSelf, HandleTargetAcquired, HandleTargetLost);
        }

        void ClearTarget()
        {
            m_TargetingModule.ClearTarget(HandleTargetLost);
        }

        void HandleTargetAcquired(IDamageable target)
        {
            OnTargetAcquired?.Invoke(target);
        }

        void HandleTargetLost()
        {
            OnTargetLost?.Invoke();
        }

        #endregion

        #region Attack

        public event Action OnAttackPerformed;
        // Raised instead of OnAttackPerformed when a charged attack releases, so animators can play
        // a dedicated release animation ('chargedAttack' trigger) without the regular one firing too.
        public event Action OnChargedAttackPerformed;
        public event Action<bool> OnAttackChargingChanged;

        /// <summary>
        /// Raised every time an attack owned by this character damages a target.
        /// Feedback systems listen here for hit-stop, camera shake, etc.
        /// </summary>
        public event Action<DamageInfo> OnDamageDealt;

        public bool HasTargetInRange(float range)
        {
            return m_AttackModule.HasTargetInRange(this, range);
        }

        /// <summary>
        /// Deals damage in a box in front of the character; <paramref name="reaction"/> says how victims
        /// are moved. Pass <paramref name="hitPoints"/> to receive each target's contact point (for VFX).
        /// </summary>
        public void HitBox(Vector2 offset, Vector2 size, float damage, in HitReaction reaction = default,
                           List<Vector2> hitPoints = null)
        {
            m_AttackModule.HitBox(this, offset, size, damage, in reaction, hitPoints);
        }

        /// <summary>
        /// HitBox for attacks swept over multiple frames: targets already in <paramref name="alreadyHit"/>
        /// are skipped and new victims are added to it, so one sweep never damages the same target twice.
        /// </summary>
        public void HitBox(Vector2 offset, Vector2 size, float damage, in HitReaction reaction,
                           List<Vector2> hitPoints, HashSet<IDamageable> alreadyHit)
        {
            m_AttackModule.HitBox(this, offset, size, damage, in reaction, hitPoints, alreadyHit);
        }

        public ProjectilePool CreateProjectilePool(Projectile prefab, int size)
        {
            return m_AttackModule.CreateProjectilePool(this, prefab, size);
        }

        public void NotifyAttackPerformed()
        {
            OnAttackPerformed?.Invoke();
        }

        public void NotifyChargedAttackPerformed()
        {
            OnChargedAttackPerformed?.Invoke();
        }

        public void NotifyAttackCharging(bool isCharging)
        {
            OnAttackChargingChanged?.Invoke(isCharging);
        }

        public void NotifyDamageDealt(in DamageInfo damage)
        {
            OnDamageDealt?.Invoke(damage);
        }

        // While attacking, the character stands its ground: held lock covers open-ended states
        // (charging); the timed lock covers fixed swings. Gravity and hit reactions still apply.
        public bool IsAttackMovementLocked => m_IsAttackLockHeld || m_AttackLockLeft > 0f;

        public void SetAttackMovementLock(bool isLocked)
        {
            m_IsAttackLockHeld = isLocked;
        }

        public void LockAttackMovement(float duration)
        {
            if (duration > m_AttackLockLeft) m_AttackLockLeft = duration;
        }

        public bool IsStunned { get; private set; }

        public void SetStunned(bool value)
        {
            IsStunned = value;
        }

        #endregion

        #region Abilities

        public bool RemoveAbility(MovementAbility ability)
        {
            return m_AbilitiesModule.Remove(ability);
        }

        public bool RemoveAbility(AttackAbility ability)
        {
            return m_AbilitiesModule.Remove(ability);
        }

        #endregion
    }
}

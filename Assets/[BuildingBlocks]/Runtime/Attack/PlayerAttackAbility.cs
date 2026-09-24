using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Blocks.Audio;
using Blocks.Character;
using Blocks.Extras;

namespace Blocks.Attack
{
    /// <summary>
    /// Player attack via input: hold-to-charge melee and projectile, each on its own action. Charge
    /// scales damage and push-back; an exactly full charge flies victims away (Smash-style) instead.
    /// </summary>
    public class PlayerAttackAbility : AttackAbility
    {
        /// <summary>
        /// Which of the player's two attacks something refers to. Public because the HUD reads it to
        /// know which of its two slots to fill.
        /// </summary>
        public enum AttackKind
        {
            None,
            Melee,
            Projectile
        }

        [Header("Melee")]
        [FormerlySerializedAs("attackAction")]
        [SerializeField] InputActionReference meleeAction;
        [FormerlySerializedAs("damage")]
        [SerializeField] float meleeDamage = 1f;
        [SerializeField] float meleeWindupTime;
        [FormerlySerializedAs("cooldown")]
        [SerializeField] float meleeCooldown = 0.6f;
        [SerializeField] float meleeBufferTime;
        [Tooltip("Shove speed on hit, divided by the victim's Rigidbody2D mass. Heavy characters barely move.")]
        [SerializeField] float pushSpeed = 20f;
        [Tooltip("Seconds the victim can't move or attack after being hit.")]
        [SerializeField] float hitStunTime = 0.8f;
        [SerializeField] Vector2 boxSize = new Vector2(1.5f, 1f);
        [SerializeField] Vector2 boxOffset = new Vector2(0.75f, 0f);
        [Tooltip("Seconds the player stays planted after the attack lands (melee or projectile).")]
        [SerializeField] float movementLockTime;
        [SerializeField] OneShotVfx hitVfxPrefab;
        [Tooltip("Spawned when the swing hits nothing — the sword striking the floor.")]
        [SerializeField] OneShotVfx missVfxPrefab;
        [Tooltip("Where the miss VFX appears, relative to the player. X is mirrored to the facing direction.")]
        [SerializeField] Vector2 missVfxOffset = new Vector2(0.75f, 0f);

        [Header("Charge")]
        [SerializeField] float chargeTime = 1f;
        [SerializeField] float fullChargeDamageMultiplier = 2f;
        [Tooltip("Multiplier on Push Speed as the charge grows (1 = no growth).")]
        [SerializeField] float fullChargeKnockbackMultiplier = 2f;
        [Tooltip("Size multiplier on the melee hit/miss VFX at full charge (1 = no growth).")]
        [SerializeField] float fullChargeVfxSizeMultiplier = 1.5f;
        [Tooltip("Damage multiplier on the projectile at full charge (1 = no bonus).")]
        [SerializeField] float fullChargeProjectileDamageMultiplier = 2f;
        [Tooltip("Size multiplier on the projectile at full charge (1 = no growth). Scales the collider too.")]
        [SerializeField] float fullChargeProjectileSizeMultiplier = 1.5f;
        [Tooltip("Fly-away speed on a fully charged hit — melee and projectile (0 = never launch).")]
        [SerializeField] float fullChargeLaunchSpeed = 12f;
        [Tooltip("Fly-away angle in degrees above horizontal — 50 sends victims up and away.")]
        [Range(0f, 90f)]
        [SerializeField] float fullChargeLaunchAngle = 50f;

        [Header("Projectile")]
        [SerializeField] InputActionReference projectileAction;
        [SerializeField] float projectileDamage = 1f;
        [SerializeField] float projectileWindupTime;
        [SerializeField] float projectileCooldown = 0.6f;
        [SerializeField] float projectileBufferTime;
        [SerializeField] Projectile projectilePrefab;
        [Tooltip("Spawned where the projectile damages a target. Projectile hits never knock the target back.")]
        [SerializeField] OneShotVfx projectileHitVfxPrefab;
        [SerializeField] Transform firePoint;
        [SerializeField] float projectileSpeed = 8f;
        [SerializeField] float projectileLifetime = 3f;
        [SerializeField] int poolSize = 8;

        [Header("Audio")]
        [Tooltip("Plays the release sounds. Leave empty to use the AudioSource on this GameObject.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Looping sound held while charging, melee or projectile. It fades in, so it can be matched to the wind-up animation.")]
        [SerializeField] AudioClip chargeLoopClip;
        [SerializeField] ChargeLoopSettings chargeLoop = ChargeLoopSettings.Default;
        [Tooltip("Played once the moment the melee attack is released.")]
        [SerializeField] AudioClip meleeReleaseClip;
        [SerializeField][Range(0f, 5f)] float meleeReleaseVolume = 2.5f;
        [Tooltip("Played once the moment the projectile is released.")]
        [SerializeField] AudioClip projectileReleaseClip;
        [SerializeField][Range(0f, 5f)] float projectileReleaseVolume = 2.5f;
        [Tooltip("Played when the projectile hits something it can damage — the counterpart to Projectile Hit Vfx Prefab.")]
        [SerializeField] AudioClip projectileHitClip;
        [SerializeField][Range(0f, 5f)] float projectileHitVolume = 2.5f;
        [Tooltip("Each release sound is pitched up or down by up to this much, so repeats don't sound identical. 0 = no variation.")]
        [SerializeField][Range(0f, 0.5f)] float pitchVariation = 0.08f;

        readonly List<Vector2> m_HitPoints = new List<Vector2>();

        InputHandle m_MeleeHandle;
        InputHandle m_ProjectileHandle;

        // Which attack the armed windup belongs to. The windup itself lives on AttackAbility, shared
        // between the two attacks since only ever one of them is in flight.
        AttackKind m_PendingKind;
        AttackKind m_ChargingKind;
        float m_ChargeElapsed;
        float m_ChargeRatio;

        // Two cooldowns, one per attack: a melee swing must not gate the projectile, they're separate
        // attacks on separate buttons. AttackAbility's single cooldown serves the attacks that need only
        // one, so this ability never starts it and it stays at zero.
        //
        // Absolute deadlines rather than the countdowns the enemy attacks use: a deadline keeps running
        // while attack ticks are frozen, so the player's recovery finishes DURING a stun instead of
        // resuming after it. That is what lets them swing back the instant they get up from the boss's
        // shockwave, rather than waiting out a cooldown stacked on top of the stun.
        float m_MeleeReadyTime;
        float m_ProjectileReadyTime;

        ProjectilePool m_Pool;
        AudioHelper m_Audio;

        // Cached so firing doesn't allocate a delegate per shot. Fully qualified because a bare `using
        // System` would make every `Random.Range` below ambiguous with System.Random.
        System.Action m_PlayProjectileHitSfx;

        protected override Transform FirePoint => firePoint;

        bool IsAttackInProgress => IsCharging || IsWindupArmed;

        /// <summary>True while a melee or projectile charge is being held. Feedback systems poll this.</summary>
        public bool IsCharging => m_ChargingKind != AttackKind.None;

        /// <summary>
        /// Which attack is charging, or <c>None</c> when idle. The two attacks share one charge, so only
        /// ever one of them is building at a time.
        /// </summary>
        public AttackKind ChargingAttack => m_ChargingKind;

        /// <summary>
        /// The action that swings the melee attack. Exposed so the HUD can label its slot from the action's
        /// own name and binding, rather than having them typed out again somewhere else.
        /// </summary>
        public InputActionReference MeleeAction => meleeAction;

        /// <summary>The action that throws the projectile, the ranged counterpart to <see cref="MeleeAction"/>.</summary>
        public InputActionReference RangedAction => projectileAction;

        /// <summary>Live charge progress (0–1) while a charge is held; 0 when idle.</summary>
        public float CurrentChargeRatio
        {
            get
            {
                if (!IsCharging) return 0f;
                return chargeTime > 0f ? Mathf.Clamp01(m_ChargeElapsed / chargeTime) : 1f;
            }
        }

        protected override void OnInitialize()
        {
            m_MeleeHandle = BindInput(meleeAction, ConfigureMelee);
            m_ProjectileHandle = BindInput(projectileAction, ConfigureProjectile);
            m_PlayProjectileHitSfx = PlayProjectileHitSfx;

            if (projectilePrefab != null)
            {
                m_Pool = Character.CreateProjectilePool(projectilePrefab, poolSize);
            }

            // Ahead of the helper: the charge loop plays through its own source, so its clip needs
            // loading even when there is no one-shot AudioSource at all.
            WarmUpClips();
            m_Audio = new AudioHelper(
                this, audioSource, HasOneShotClips,
                clipFieldHint: "'Melee Release Clip', 'Projectile Release Clip', 'Projectile Hit Clip'",
                ownerLabel: Character.name, pitchVariation: pitchVariation);
        }

        protected override void OnCleanup()
        {
            CancelAttack();

            // Take the generated child with it, so re-initializing this ability doesn't stack up copies.
            m_Audio?.DestroyLoop();

            m_MeleeHandle = null;
            m_ProjectileHandle = null;
            m_PlayProjectileHitSfx = null;
            m_Pool = null;
        }

        void ConfigureMelee(InputHandle input)
        {
            input.Performed = BeginCharge;
            input.Canceled = ReleaseCharge;
            input.BufferTime = meleeBufferTime;
            // Grounded-only: an air press is dropped outright instead of freezing the jump.
            // Cooldown counts from the hit landing (m_MeleeReadyTime), not from press, so a held
            // charge shouldn't quietly burn through its own cooldown.
            input.CanExecute = () => Character.IsGrounded && !IsAttackInProgress && Time.time >= m_MeleeReadyTime;
        }

        void ConfigureProjectile(InputHandle input)
        {
            input.Performed = BeginProjectileCharge;
            input.Canceled = ReleaseProjectileCharge;
            input.BufferTime = projectileBufferTime;
            // Same rules as melee: grounded-only, and the cooldown runs from the shot spawning
            // (m_ProjectileReadyTime), not from press, so a held charge doesn't burn through it.
            input.CanExecute = () => Character.IsGrounded && !IsAttackInProgress && m_Pool != null
                                     && Time.time >= m_ProjectileReadyTime;
        }

        protected override void OnUpdate()
        {
            m_Audio.TickLoop(CurrentChargeRatio);

            if (IsCharging)
            {
                m_ChargeElapsed += Time.deltaTime;
                return;
            }

            if (TickAttackTimer() != AttackTimerState.Landing) return;

            AttackKind kind = m_PendingKind;
            m_PendingKind = AttackKind.None;
            Land(kind);
        }

        void BeginCharge()
        {
            Character.FaceTarget();
            Character.SetAttackMovementLock(true);

            m_ChargingKind = AttackKind.Melee;
            m_ChargeElapsed = 0f;
            Character.NotifyAttackCharging(true);
            m_Audio.StartLoop(chargeLoopClip, in chargeLoop);

            // A buffered press can flush after the button was already released; resolve it as a quick tap.
            if (!m_MeleeHandle.IsPressed) ReleaseCharge();
        }

        void ReleaseCharge()
        {
            if (m_ChargingKind != AttackKind.Melee) return;

            m_ChargingKind = AttackKind.None;
            m_ChargeRatio = chargeTime > 0f ? Mathf.Clamp01(m_ChargeElapsed / chargeTime) : 1f;

            Character.NotifyAttackCharging(false);
            Character.NotifyAttackPerformed();

            m_Audio.StopLoop(immediate: false);
            m_Audio.Play(meleeReleaseClip, meleeReleaseVolume);

            // A zero windup lands on the same frame the button comes up.
            if (!BeginWindup(meleeWindupTime))
            {
                Land(AttackKind.Melee);
                return;
            }

            m_PendingKind = AttackKind.Melee;
        }

        void BeginProjectileCharge()
        {
            Character.FaceTarget();
            Character.SetAttackMovementLock(true);

            m_ChargingKind = AttackKind.Projectile;
            m_ChargeElapsed = 0f;
            Character.NotifyAttackCharging(true);
            m_Audio.StartLoop(chargeLoopClip, in chargeLoop);

            // A buffered press can flush after the button was already released; resolve it as a quick tap.
            if (!m_ProjectileHandle.IsPressed) ReleaseProjectileCharge();
        }

        void ReleaseProjectileCharge()
        {
            if (m_ChargingKind != AttackKind.Projectile) return;

            m_ChargingKind = AttackKind.None;
            m_ChargeRatio = chargeTime > 0f ? Mathf.Clamp01(m_ChargeElapsed / chargeTime) : 1f;

            Character.NotifyAttackCharging(false);
            Character.NotifyAttackPerformed();

            m_Audio.StopLoop(immediate: false);
            m_Audio.Play(projectileReleaseClip, projectileReleaseVolume);

            // A zero windup releases on the same frame the button comes up.
            if (!BeginWindup(projectileWindupTime))
            {
                Land(AttackKind.Projectile);
                return;
            }

            m_PendingKind = AttackKind.Projectile;
        }

        void Land(AttackKind kind)
        {
            if (kind == AttackKind.Melee) LandMelee();
            else if (kind == AttackKind.Projectile) LandProjectile();

            Character.SetAttackMovementLock(false);
            if (movementLockTime > 0f) Character.LockAttackMovement(movementLockTime);
        }

        void LandMelee()
        {
            float damage = meleeDamage * Mathf.Lerp(1f, fullChargeDamageMultiplier, m_ChargeRatio);
            float vfxScale = Mathf.Lerp(1f, fullChargeVfxSizeMultiplier, m_ChargeRatio);

            m_MeleeReadyTime = Time.time + meleeCooldown;

            Character.HitBox(boxOffset, boxSize, damage, BuildMeleeReaction(), m_HitPoints);

            if (m_HitPoints.Count > 0)
            {
                SpawnHitVfx(hitVfxPrefab, m_HitPoints, vfxScale);
                return;
            }

            // Whiffed: the sword strikes the floor in front of the player instead.
            SpawnVfx(missVfxPrefab, MirroredPoint(missVfxOffset), vfxScale);
        }

        void LandProjectile()
        {
            m_ProjectileReadyTime = Time.time + projectileCooldown;

            if (m_Pool == null) return;

            float damage = projectileDamage * Mathf.Lerp(1f, fullChargeProjectileDamageMultiplier, m_ChargeRatio);
            float sizeScale = Mathf.Lerp(1f, fullChargeProjectileSizeMultiplier, m_ChargeRatio);

            Vector2 velocity = new Vector2(Character.FacingDirection * projectileSpeed, 0f);
            ProjectileLaunchSettings settings = ProjectileLaunchSettings.Contact(
                projectileLifetime, gravityScale: 0f, reaction: BuildProjectileReaction(),
                sizeScale: sizeScale, hitVfx: projectileHitVfxPrefab, onHit: m_PlayProjectileHitSfx);

            m_Pool.Fire(FirePosition, velocity, damage, in settings);
        }

        // An exactly full charge earns the fly-away, the reward for holding all the way.
        // Anything less shoves, harder the longer the hold.
        HitReaction BuildMeleeReaction()
        {
            if (IsFullChargeLaunch)
            {
                return HitReaction.FlyAway(fullChargeLaunchSpeed, fullChargeLaunchAngle, hitStunTime);
            }

            float speed = pushSpeed * Mathf.Lerp(1f, fullChargeKnockbackMultiplier, m_ChargeRatio);
            return HitReaction.PushBack(speed, hitStunTime);
        }

        // Projectiles never shove (they spawn the hit VFX instead), but a full charge still flies.
        HitReaction BuildProjectileReaction()
        {
            return IsFullChargeLaunch
                ? HitReaction.FlyAway(fullChargeLaunchSpeed, fullChargeLaunchAngle, hitStunTime)
                : HitReaction.None;
        }

        bool IsFullChargeLaunch => m_ChargeRatio >= 1f && fullChargeLaunchSpeed > 0f;

        protected override void CancelAttack()
        {
            // An interrupted charge or windup never reaches its Land step, so start the cooldowns here.
            if (m_ChargingKind == AttackKind.Melee || m_PendingKind == AttackKind.Melee)
            {
                m_MeleeReadyTime = Time.time + meleeCooldown;
            }

            if (m_ChargingKind == AttackKind.Projectile || m_PendingKind == AttackKind.Projectile)
            {
                m_ProjectileReadyTime = Time.time + projectileCooldown;
            }

            if (IsCharging)
            {
                m_ChargingKind = AttackKind.None;
                Character?.NotifyAttackCharging(false);

                // Cut dead, not faded: a hit freezes attack ticks, and a fade nothing ticks down sticks.
                m_Audio.StopLoop(immediate: true);
            }

            m_PendingKind = AttackKind.None;
            ClearWindup();
            Character?.SetAttackMovementLock(false);
        }

        // Runs when a shot lands, which can be long after it was fired and anywhere on screen. It plays on
        // the player's own 2D AudioSource: the projectile is pooled and switches off in the same frame it
        // hits, so a source on the shot itself would be cut off mid-clip.
        void PlayProjectileHitSfx()
        {
            m_Audio.Play(projectileHitClip, projectileHitVolume);
        }

        bool HasOneShotClips => meleeReleaseClip != null || projectileReleaseClip != null
                                || projectileHitClip != null;

        void WarmUpClips()
        {
            AudioHelper.WarmUp(chargeLoopClip);
            AudioHelper.WarmUp(meleeReleaseClip);
            AudioHelper.WarmUp(projectileReleaseClip);
            AudioHelper.WarmUp(projectileHitClip);
        }

        void OnValidate()
        {
            if (meleeDamage < 0f) meleeDamage = 0f;
            if (meleeWindupTime < 0f) meleeWindupTime = 0f;
            if (meleeCooldown < 0f) meleeCooldown = 0f;
            if (meleeBufferTime < 0f) meleeBufferTime = 0f;
            if (pushSpeed < 0f) pushSpeed = 0f;
            if (hitStunTime < 0f) hitStunTime = 0f;
            if (movementLockTime < 0f) movementLockTime = 0f;
            if (boxSize.x < 0f) boxSize.x = 0f;
            if (boxSize.y < 0f) boxSize.y = 0f;
            if (chargeTime < 0f) chargeTime = 0f;
            if (fullChargeDamageMultiplier < 1f) fullChargeDamageMultiplier = 1f;
            if (fullChargeKnockbackMultiplier < 1f) fullChargeKnockbackMultiplier = 1f;
            if (fullChargeVfxSizeMultiplier < 1f) fullChargeVfxSizeMultiplier = 1f;
            if (fullChargeProjectileDamageMultiplier < 1f) fullChargeProjectileDamageMultiplier = 1f;
            if (fullChargeProjectileSizeMultiplier < 1f) fullChargeProjectileSizeMultiplier = 1f;
            if (fullChargeLaunchSpeed < 0f) fullChargeLaunchSpeed = 0f;
            if (projectileDamage < 0f) projectileDamage = 0f;
            if (projectileWindupTime < 0f) projectileWindupTime = 0f;
            if (projectileCooldown < 0f) projectileCooldown = 0f;
            if (projectileBufferTime < 0f) projectileBufferTime = 0f;
            if (projectileSpeed < 0f) projectileSpeed = 0f;
            if (projectileLifetime < 0f) projectileLifetime = 0f;
            if (poolSize < 1) poolSize = 1;
            chargeLoop.Sanitize();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            DrawMirroredBox(boxOffset, boxSize);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(MirroredPoint(missVfxOffset), 0.15f);

            if (firePoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(firePoint.position, 0.1f);
            }
        }
    }
}

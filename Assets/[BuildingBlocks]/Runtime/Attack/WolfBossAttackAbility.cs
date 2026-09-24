using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Blocks.Audio;
using Blocks.Character;
using Blocks.Extras;

namespace Blocks.Attack
{
    /// <summary>
    /// Boss melee that strictly alternates whenever a target is in range: a regular strike, then a
    /// charged version of the same strike: telegraphed for chargeTime, landing the identical hit,
    /// then sending a shockwave hitbox traveling from a start point to an end point. Pair the
    /// shockwave with an earth-shatter VFX tuned to the same path and speed.
    /// </summary>
    public class WolfBossAttackAbility : AttackAbility
    {
        // The windup between a swing starting and its hit landing isn't a phase here: AttackAbility's
        // timer owns it, and IsWindupArmed is the one place that asks. Ready therefore covers both
        // "idle" and "mid-swing"; the two are told apart by that flag.
        enum Phase
        {
            Ready,
            Charging,
            Shockwave
        }

        // How long the swing sound takes to fade when an attack is interrupted. Brief enough that the
        // swing still reads as stopping dead, long enough not to click if the cut lands mid-transient.
        // Not exposed: this is click avoidance, not something to tune.
        const float k_SwingFadeOutTime = 0.05f;

        [Header("Targeting")]
        [Tooltip("An attack starts whenever the current target is within this distance.")]
        [SerializeField] float range = 2f;
        [Tooltip("Seconds the boss stays planted after an attack lands (recovery).")]
        [SerializeField] float movementLockTime = 0.5f;

        [Header("Strike (used by both attacks)")]
        [SerializeField] float damage = 1f;
        [Tooltip("When on, taking damage during the regular strike's windup cancels it.")]
        [SerializeField] bool strikeCanBeInterrupted = true;
        [Tooltip("Seconds between the attack animation starting and the hit landing — tune to the impact frame.")]
        [SerializeField] float windupTime = 0.3f;
        [SerializeField] Vector2 boxSize = new Vector2(1.5f, 1f);
        [SerializeField] Vector2 boxOffset = new Vector2(0.75f, 0f);
        [Tooltip("Shove speed on hit, divided by the victim's Rigidbody2D mass.")]
        [SerializeField] float pushSpeed = 15f;
        [Tooltip("Seconds the victim can't move or attack after being hit.")]
        [SerializeField] float hitStunTime = 0.4f;
        [SerializeField] OneShotVfx hitVfxPrefab;
        [SerializeField] float regularCooldown = 1f;

        [Header("Charged Attack")]
        [Tooltip("When on, taking damage during the charge telegraph or its windup cancels the attack. Leave off so the boss armors through hits.")]
        [SerializeField] bool chargedCanBeInterrupted;
        [Tooltip("Seconds the boss telegraphs before the strike — the animator 'charging' bool and feedback flicker run for exactly this long.")]
        [SerializeField] float chargeTime = 1.2f;
        [Tooltip("Seconds between the release animation starting and the strike landing — tune to the impact frame of the finish clip.")]
        [SerializeField] float chargedWindupTime = 0.25f;
        [SerializeField] float chargedCooldown = 2f;
        [Tooltip("Spawned once when the charged strike lands, whether or not it connects — the impact effect.")]
        [SerializeField] OneShotVfx slamVfxPrefab;
        [Tooltip("Where the slam VFX appears, relative to the boss. X is mirrored to the facing direction.")]
        [SerializeField] Vector2 slamVfxOffset = new Vector2(0.75f, 0f);

        [Header("Shockwave (follows the charged strike)")]
        [Tooltip("Seconds after the charged strike lands before the shockwave starts traveling.")]
        [SerializeField] float shockwaveDelay = 0.25f;
        [Tooltip("Where the shockwave box starts, relative to the boss. X is mirrored to the facing direction.")]
        [SerializeField] Vector2 shockwaveStartOffset = new Vector2(1f, 0f);
        [Tooltip("Where the shockwave box stops, relative to the boss. X is mirrored to the facing direction.")]
        [SerializeField] Vector2 shockwaveEndOffset = new Vector2(7f, 0f);
        [Tooltip("Travel speed in units per second — match your earth-shatter VFX. 0 disables the shockwave.")]
        [SerializeField] float shockwaveSpeed = 6f;
        [Tooltip("Size of the traveling hitbox.")]
        [SerializeField] Vector2 shockwaveBoxSize = new Vector2(1f, 1f);
        [SerializeField] float shockwaveDamage = 1f;
        [Tooltip("Fly-away speed on victims the wave touches (0 = shove with Shockwave Push Speed instead).")]
        [SerializeField] float shockwaveLaunchSpeed = 12f;
        [Tooltip("Fly-away angle in degrees above horizontal — high values launch the victim up.")]
        [Range(0f, 90f)]
        [SerializeField] float shockwaveLaunchAngle = 80f;
        [Tooltip("Shove speed on wave victims when Shockwave Launch Speed is 0.")]
        [SerializeField] float shockwavePushSpeed = 15f;
        [SerializeField] float shockwaveStunTime = 0.4f;
        [Tooltip("Spawned once at the start position when the wave launches — your earth-shatter VFX.")]
        [SerializeField] OneShotVfx shockwaveVfxPrefab;
        [Tooltip("Spawned at every target the wave connects with.")]
        [SerializeField] OneShotVfx shockwaveHitVfxPrefab;

        [Header("Audio")]
        [Tooltip("Plays the strike and shockwave sounds. Leave empty to use the AudioSource on this GameObject.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Played as the axe starts swinging — the moment the attack animation begins, not when the hit " +
                 "lands. Used by both attacks unless Charged Strike Clip is set.")]
        [SerializeField] AudioClip strikeClip;
        [Tooltip("1 is the clip's own level; above that boosts it. Effect recordings are usually mastered far " +
                 "quieter than music, so they need the boost to be heard at all.")]
        [SerializeField][Range(0f, 5f)] float strikeVolume = 2.5f;
        [Tooltip("Played instead of Strike Clip when the charged strike releases. Leave empty to reuse Strike Clip.")]
        [SerializeField] AudioClip chargedStrikeClip;
        [Tooltip("Volume for the charged release — raise it above Strike Volume so the big swing hits harder.")]
        [SerializeField][Range(0f, 5f)] float chargedStrikeVolume = 3f;
        [Tooltip("Played when the shockwave launches, alongside the Shockwave Vfx.")]
        [SerializeField] AudioClip shockwaveClip;
        [SerializeField][Range(0f, 5f)] float shockwaveVolume = 2.5f;
        [Tooltip("Looping sound held while the boss telegraphs its charged strike. It fades in, so it can be " +
                 "matched to the start of the charge animation.")]
        [SerializeField] AudioClip chargeLoopClip;
        [SerializeField] ChargeLoopSettings chargeLoop = ChargeLoopSettings.Default;
        [Tooltip("Each one-shot is pitched up or down by up to this much, so repeats don't sound identical. 0 = no variation.")]
        [SerializeField][Range(0f, 0.5f)] float pitchVariation = 0.08f;

        readonly List<Vector2> m_HitPoints = new List<Vector2>();
        readonly HashSet<IDamageable> m_WaveHitTargets = new HashSet<IDamageable>();

        Phase m_Phase;
        bool m_NextIsCharged;
        bool m_IsChargedAttack;
        float m_ChargeElapsed;
        float m_WaveDelayLeft;
        bool m_HasWaveLaunched;
        float m_WaveTraveled;
        float m_WaveLength;
        Vector2 m_WaveStart;
        Vector2 m_WaveDirection;
        AudioHelper m_Audio;
        AudioSource m_SwingSource;
        Coroutine m_SwingFadeRoutine;

        /// <summary>True while the charged strike is being telegraphed. Feedback systems poll this.</summary>
        public bool IsCharging => m_Phase == Phase.Charging;

        /// <summary>Telegraph progress (0–1) while charging; 0 otherwise.</summary>
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
            // Ahead of the helper, since the charge loop has its own source: its clip needs loading even
            // when there is no one-shot AudioSource at all.
            WarmUpClips();
            m_Audio = new AudioHelper(
                this, audioSource, HasOneShotClips,
                clipFieldHint: "'Strike Clip', 'Charged Strike Clip', 'Shockwave Clip'",
                ownerLabel: Character.name, pitchVariation: pitchVariation);
            CreateSwingSource();
        }

        protected override void OnCleanup()
        {
            // Ahead of CancelAttack: with the swing already silenced, cancelling can't kick off a fade
            // coroutine on an object that is about to be destroyed.
            StopSwing(immediate: true);
            CancelAttack();

            // Take the generated children with it, so re-initializing this ability doesn't stack up copies.
            m_Audio?.DestroyLoop();

            if (m_SwingSource != null)
            {
                Destroy(m_SwingSource.gameObject);
                m_SwingSource = null;
            }

            ResetAttackTimer();
            m_NextIsCharged = false;
        }

        protected override void OnUpdate()
        {
            // Ahead of the phase switch: every branch below returns, and the loop's fade-out has to keep
            // ticking after the charge phase has already handed over to the windup.
            m_Audio.TickLoop(CurrentChargeRatio);

            switch (m_Phase)
            {
                case Phase.Charging:
                    TickCharge();
                    return;
                case Phase.Shockwave:
                    TickShockwave();
                    return;
            }

            switch (TickAttackTimer())
            {
                case AttackTimerState.Landing:
                    LandHit();
                    return;
                case AttackTimerState.Busy:
                    return;
            }

            if (!Character.HasTargetInRange(range)) return;

            if (m_NextIsCharged) BeginChargedAttack();
            else BeginRegularAttack();
        }

        void BeginRegularAttack()
        {
            m_IsChargedAttack = false;
            Character.FaceTarget();
            Character.NotifyAttackPerformed();
            if (movementLockTime > 0f) Character.LockAttackMovement(movementLockTime);

            // Plays as the swing starts, not when it connects, since windupTime is the gap to the impact frame,
            // so waiting for the hit would put the whoosh after the axe had already landed. Which means
            // the sound is always in flight while the strike can still be interrupted: PlaySwing puts it
            // on a source CancelAttack can take back.
            PlaySwing(strikeClip, strikeVolume);

            // A zero windup strikes on the same frame the swing starts.
            if (!BeginWindup(windupTime)) LandHit();
        }

        void BeginChargedAttack()
        {
            m_IsChargedAttack = true;
            Character.FaceTarget();
            // Held (not timed) so the boss stays planted through charge, windup, and strike, then released
            // once the shockwave is on its way. Facing is locked in here: crossing behind the boss
            // during the telegraph makes it whiff, which is the intended dodge.
            Character.SetAttackMovementLock(true);
            Character.NotifyAttackCharging(true);
            m_Audio.StartLoop(chargeLoopClip, in chargeLoop);

            m_Phase = Phase.Charging;
            m_ChargeElapsed = 0f;
        }

        void TickCharge()
        {
            m_ChargeElapsed += Time.deltaTime;
            if (m_ChargeElapsed < chargeTime) return;

            Character.NotifyAttackCharging(false);
            // Charged release fires its own event so the animator's regular 'attack' trigger stays
            // untouched; otherwise an Any State attack transition would steal the release animation.
            Character.NotifyChargedAttackPerformed();

            m_Audio.StopLoop(immediate: false);
            PlaySwing(chargedStrikeClip != null ? chargedStrikeClip : strikeClip, chargedStrikeVolume);

            // The charge phase is over either way, and a zero windup lands the release immediately.
            m_Phase = Phase.Ready;
            if (!BeginWindup(chargedWindupTime)) LandHit();
        }

        void LandHit()
        {
            if (m_IsChargedAttack) LandSlam();
            else LandRegular();
        }

        void LandRegular()
        {
            DealStrike();
            FinishAttack(regularCooldown, nextIsCharged: true);
        }

        // The charged version lands the exact same strike as the regular attack: that move split into
        // charge + finish. It then hands over to the shockwave.
        void LandSlam()
        {
            SpawnVfx(slamVfxPrefab, MirroredPoint(slamVfxOffset));

            DealStrike();

            Vector2 waveTravel = shockwaveEndOffset - shockwaveStartOffset;
            if (shockwaveSpeed <= 0f || waveTravel.sqrMagnitude < 0.0001f)
            {
                ReleaseMovementHold();
                FinishAttack(chargedCooldown, nextIsCharged: false);
                return;
            }

            m_Phase = Phase.Shockwave;
            m_WaveDelayLeft = shockwaveDelay;
            m_HasWaveLaunched = false;
        }

        void DealStrike()
        {
            Character.HitBox(boxOffset, boxSize, damage,
                             HitReaction.PushBack(pushSpeed, hitStunTime), m_HitPoints);
            SpawnHitVfx(hitVfxPrefab, m_HitPoints);
        }

        void TickShockwave()
        {
            if (!m_HasWaveLaunched)
            {
                m_WaveDelayLeft -= Time.deltaTime;
                if (m_WaveDelayLeft > 0f) return;

                LaunchWave();
                return;
            }

            float previousDistance = m_WaveTraveled;
            m_WaveTraveled = Mathf.Min(m_WaveTraveled + shockwaveSpeed * Time.deltaTime, m_WaveLength);

            // Sweep the box over the ground covered this frame so a fast wave can't skip a target.
            Vector2 previousCenter = m_WaveStart + m_WaveDirection * previousDistance;
            Vector2 currentCenter = m_WaveStart + m_WaveDirection * m_WaveTraveled;
            Vector2 worldCenter = (previousCenter + currentCenter) * 0.5f;
            Vector2 step = currentCenter - previousCenter;
            Vector2 sweptSize = shockwaveBoxSize + new Vector2(Mathf.Abs(step.x), Mathf.Abs(step.y));

            // HitBox takes a character-relative offset mirrored by the CURRENT facing; converting back
            // from the captured world position keeps the wave on course even if the boss moves or turns.
            Vector2 offset = new Vector2((worldCenter.x - transform.position.x) * Character.FacingDirection,
                                         worldCenter.y - transform.position.y);
            Character.HitBox(offset, sweptSize, shockwaveDamage, BuildShockwaveReaction(),
                             m_HitPoints, m_WaveHitTargets);
            SpawnHitVfx(shockwaveHitVfxPrefab, m_HitPoints);

            if (m_WaveTraveled >= m_WaveLength)
            {
                FinishAttack(chargedCooldown, nextIsCharged: false);
            }
        }

        void LaunchWave()
        {
            Vector2 waveEnd = MirroredPoint(shockwaveEndOffset);

            m_HasWaveLaunched = true;
            m_WaveTraveled = 0f;
            m_WaveStart = MirroredPoint(shockwaveStartOffset);
            m_WaveLength = (waveEnd - m_WaveStart).magnitude;
            m_WaveDirection = (waveEnd - m_WaveStart) / m_WaveLength;
            m_WaveHitTargets.Clear();

            SpawnVfx(shockwaveVfxPrefab, m_WaveStart);
            m_Audio.Play(shockwaveClip, shockwaveVolume);

            // The boss is free to act again once the wave is on its way; the wave keeps its own course.
            ReleaseMovementHold();
        }

        HitReaction BuildShockwaveReaction()
        {
            return shockwaveLaunchSpeed > 0f
                ? HitReaction.FlyAway(shockwaveLaunchSpeed, shockwaveLaunchAngle, shockwaveStunTime)
                : HitReaction.PushBack(shockwavePushSpeed, shockwaveStunTime);
        }

        void ReleaseMovementHold()
        {
            Character.SetAttackMovementLock(false);
            if (movementLockTime > 0f) Character.LockAttackMovement(movementLockTime);
        }

        void FinishAttack(float cooldown, bool nextIsCharged)
        {
            m_Phase = Phase.Ready;
            StartCooldown(cooldown);
            m_NextIsCharged = nextIsCharged;
        }

        // The boss armors through hits its flags say it should, unlike every other attack, which is
        // always interruptible.
        protected override bool CanBeInterrupted
        {
            get
            {
                // Once the strike has landed the shockwave is already visible, so let it finish.
                if (m_Phase != Phase.Charging && !IsWindupArmed) return false;

                // Charging is always the charged attack; mid-windup, m_IsChargedAttack says which move it is.
                bool isCharged = m_Phase == Phase.Charging || m_IsChargedAttack;
                return isCharged ? chargedCanBeInterrupted : strikeCanBeInterrupted;
            }
        }

        protected override void CancelAttack()
        {
            // Ready with nothing armed is genuinely idle; Ready mid-windup still has a swing to drop.
            if (m_Phase == Phase.Ready && !IsWindupArmed) return;

            if (m_Phase == Phase.Charging)
            {
                Character?.NotifyAttackCharging(false);

                // Cut dead, not faded: nothing would tick the fade down once the stun freezes attack ticks.
                m_Audio.StopLoop(immediate: true);
            }

            // The swing sound leads the impact frame by the whole windup, so an interrupt always catches
            // it mid-flight; without this the axe is heard connecting after the boss was staggered.
            if (IsWindupArmed) StopSwing(immediate: false);

            // An interrupted attack never reaches its landing step, so start the cooldown here.
            // m_NextIsCharged is left as-is: the boss retries the same move, keeping the alternation.
            ClearWindup();
            StartCooldown(m_IsChargedAttack ? chargedCooldown : regularCooldown);
            m_Phase = Phase.Ready;
            m_ChargeElapsed = 0f;
            m_HasWaveLaunched = false;
            Character?.SetAttackMovementLock(false);
        }

        void PlaySwing(AudioClip clip, float volume)
        {
            if (m_SwingSource == null || clip == null) return;

            // Takes the previous swing with it, along with any abandoned fade, which would otherwise
            // revive when the volume is reset below.
            StopSwingFade();
            m_SwingSource.Stop();

            // The boost rides PlayOneShot's unclamped multiplier, leaving the source's own volume free
            // for the fade. Back to full here in case the last swing was cut short.
            m_SwingSource.volume = 1f;
            m_Audio.PlayOn(m_SwingSource, clip, volume);
        }

        void StopSwing(bool immediate)
        {
            if (m_SwingSource == null) return;

            StopSwingFade();
            if (!m_SwingSource.isPlaying) return;

            // A fade needs something to tick it; with the object inactive or the component disabled,
            // no coroutine will run, so cut it instead of leaving the swing ringing forever.
            if (immediate || !isActiveAndEnabled)
            {
                m_SwingSource.Stop();
                return;
            }

            m_SwingFadeRoutine = StartCoroutine(FadeOutSwing());
        }

        void StopSwingFade()
        {
            if (m_SwingFadeRoutine == null) return;

            StopCoroutine(m_SwingFadeRoutine);
            m_SwingFadeRoutine = null;
        }

        // A coroutine rather than the ability's own tick: attack ticks freeze while the boss is stunned,
        // which is exactly when an interrupt lands. Unscaled, since audio ignores timeScale.
        IEnumerator FadeOutSwing()
        {
            while (m_SwingSource != null && m_SwingSource.volume > 0f)
            {
                m_SwingSource.volume = Mathf.MoveTowards(m_SwingSource.volume, 0f,
                                                         Time.unscaledDeltaTime / k_SwingFadeOutTime);
                yield return null;
            }

            if (m_SwingSource != null) m_SwingSource.Stop();
            m_SwingFadeRoutine = null;
        }

        bool HasOneShotClips => strikeClip != null || chargedStrikeClip != null || shockwaveClip != null;

        // Its own source, like the charge loop, because it has to be stoppable: stopping it on the shared
        // source would also cut HitFeedback's hit sounds, and PlayOneShot voices can't be stopped one at
        // a time.
        void CreateSwingSource()
        {
            if (strikeClip == null && chargedStrikeClip == null) return;

            // Nothing to route through: the helper has already warned that the clips stay silent, and
            // playing them unrouted would dodge the SFX mixer group along with its volume.
            if (!m_Audio.HasSource) return;

            var child = new GameObject("Swing Audio");
            child.transform.SetParent(transform, false);

            m_SwingSource = child.AddComponent<AudioSource>();
            m_SwingSource.playOnAwake = false;
            m_SwingSource.spatialBlend = 0f;
            m_SwingSource.outputAudioMixerGroup = m_Audio.OutputGroup;
        }

        void WarmUpClips()
        {
            AudioHelper.WarmUp(strikeClip);
            AudioHelper.WarmUp(chargedStrikeClip);
            AudioHelper.WarmUp(shockwaveClip);
            AudioHelper.WarmUp(chargeLoopClip);
        }

        void OnValidate()
        {
            if (range < 0f) range = 0f;
            if (movementLockTime < 0f) movementLockTime = 0f;
            if (damage < 0f) damage = 0f;
            if (windupTime < 0f) windupTime = 0f;
            if (regularCooldown < 0f) regularCooldown = 0f;
            if (boxSize.x < 0f) boxSize.x = 0f;
            if (boxSize.y < 0f) boxSize.y = 0f;
            if (pushSpeed < 0f) pushSpeed = 0f;
            if (hitStunTime < 0f) hitStunTime = 0f;
            if (chargeTime < 0f) chargeTime = 0f;
            if (chargedWindupTime < 0f) chargedWindupTime = 0f;
            if (chargedCooldown < 0f) chargedCooldown = 0f;
            if (shockwaveDelay < 0f) shockwaveDelay = 0f;
            if (shockwaveSpeed < 0f) shockwaveSpeed = 0f;
            if (shockwaveBoxSize.x < 0f) shockwaveBoxSize.x = 0f;
            if (shockwaveBoxSize.y < 0f) shockwaveBoxSize.y = 0f;
            if (shockwaveDamage < 0f) shockwaveDamage = 0f;
            if (shockwaveLaunchSpeed < 0f) shockwaveLaunchSpeed = 0f;
            if (shockwavePushSpeed < 0f) shockwavePushSpeed = 0f;
            if (shockwaveStunTime < 0f) shockwaveStunTime = 0f;
            chargeLoop.Sanitize();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            DrawMirroredBox(boxOffset, boxSize);
            Gizmos.DrawWireSphere(transform.position, range);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(MirroredPoint(slamVfxOffset), 0.15f);

            // Shockwave: its box at the start and end points, with the travel path between.
            Gizmos.color = Color.cyan;
            Vector2 waveStart = MirroredPoint(shockwaveStartOffset);
            Vector2 waveEnd = MirroredPoint(shockwaveEndOffset);
            Gizmos.DrawWireCube(waveStart, shockwaveBoxSize);
            Gizmos.DrawWireCube(waveEnd, shockwaveBoxSize);
            Gizmos.DrawLine(waveStart, waveEnd);
        }
    }
}

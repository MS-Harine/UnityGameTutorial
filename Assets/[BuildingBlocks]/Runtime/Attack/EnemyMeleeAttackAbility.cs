using System.Collections.Generic;
using UnityEngine;
using Blocks.Audio;
using Blocks.Character;
using Blocks.Extras;

namespace Blocks.Attack
{
    /// <summary>
    /// Auto melee strike: attacks whenever a target is in range. The hit lands windupTime seconds
    /// after the animation starts, so tune windupTime to the moment of impact in the animation.
    /// </summary>
    public class EnemyMeleeAttackAbility : AttackAbility
    {
        [Header("Melee")]
        [SerializeField] float damage = 1f;
        [SerializeField] float range = 1.5f;
        [SerializeField] float windupTime;
        [SerializeField] float cooldown = 0.6f;
        [SerializeField] Vector2 boxSize = new Vector2(1.5f, 1f);
        [SerializeField] Vector2 boxOffset = new Vector2(0.75f, 0f);
        [SerializeField] float movementLockTime = 0.5f;
        [Tooltip("Shove speed on hit, divided by the victim's Rigidbody2D mass.")]
        [SerializeField] float pushSpeed = 15f;
        [Tooltip("Seconds the victim can't move or attack after being hit.")]
        [SerializeField] float hitStunTime = 0.4f;
        [SerializeField] OneShotVfx hitVfxPrefab;

        [Header("Audio")]
        [Tooltip("Plays the growl. Leave empty to use the AudioSource on this GameObject.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Played the moment the swing starts, so it telegraphs the strike rather than landing with it.")]
        [SerializeField] AudioClip growlClip;
        [Tooltip("1 is the clip's own level; above that boosts it. Effect recordings are usually mastered far " +
                 "quieter than music, so they need the boost to be heard at all.")]
        [SerializeField, Range(0f, 5f)] float growlVolume = 2.5f;
        [Tooltip("Played when the strike connects — the counterpart to Hit Vfx Prefab. A swing that misses " +
                 "stays silent.")]
        [SerializeField] AudioClip hitClip;
        [SerializeField, Range(0f, 5f)] float hitVolume = 2.5f;
        [Tooltip("Each sound is pitched up or down by up to this much, so repeats don't sound identical. " +
                 "0 = no variation.")]
        [SerializeField, Range(0f, 0.5f)] float pitchVariation = 0.08f;

        readonly List<Vector2> m_HitPoints = new List<Vector2>();

        AudioHelper m_Audio;

        protected override void OnInitialize()
        {
            AudioHelper.WarmUp(growlClip);
            AudioHelper.WarmUp(hitClip);

            m_Audio = new AudioHelper(
                this, audioSource, growlClip != null || hitClip != null,
                clipFieldHint: "'Growl Clip', 'Hit Clip'",
                ownerLabel: Character.name, pitchVariation: pitchVariation);
        }

        protected override void OnCleanup()
        {
            ResetAttackTimer();
        }

        protected override void OnUpdate()
        {
            switch (TickAttackTimer())
            {
                case AttackTimerState.Landing:
                    LandHit();
                    return;
                case AttackTimerState.Busy:
                    return;
            }

            if (!Character.HasTargetInRange(range)) return;

            BeginAttack();
        }

        void BeginAttack()
        {
            Character.FaceTarget();
            Character.NotifyAttackPerformed();
            m_Audio.Play(growlClip, growlVolume);
            if (movementLockTime > 0f) Character.LockAttackMovement(movementLockTime);

            // A zero windup strikes on the same frame the swing starts.
            if (!BeginWindup(windupTime)) LandHit();
        }

        void LandHit()
        {
            // Cooldown runs from the landed hit, so tuning the windup never eats into the
            // pause between strikes.
            StartCooldown(cooldown);

            Character.HitBox(boxOffset, boxSize, damage, HitReaction.PushBack(pushSpeed, hitStunTime), m_HitPoints);

            if (m_HitPoints.Count == 0) return;

            SpawnHitVfx(hitVfxPrefab, m_HitPoints);
            m_Audio.Play(hitClip, hitVolume);
        }

        protected override void CancelAttack()
        {
            if (!IsWindupArmed) return;

            ClearWindup();
            StartCooldown(cooldown);
        }

        void OnValidate()
        {
            if (damage < 0f) damage = 0f;
            if (range < 0f) range = 0f;
            if (windupTime < 0f) windupTime = 0f;
            if (cooldown < 0f) cooldown = 0f;
            if (boxSize.x < 0f) boxSize.x = 0f;
            if (boxSize.y < 0f) boxSize.y = 0f;
            if (movementLockTime < 0f) movementLockTime = 0f;
            if (pushSpeed < 0f) pushSpeed = 0f;
            if (hitStunTime < 0f) hitStunTime = 0f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            DrawMirroredBox(boxOffset, boxSize);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}

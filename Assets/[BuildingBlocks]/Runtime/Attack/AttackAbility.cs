using System.Collections.Generic;
using UnityEngine;
using Blocks.Character;
using Blocks.Extras;

namespace Blocks.Attack
{
    public abstract class AttackAbility : CharacterAbility
    {
        /// <summary>What an attack's timer wants the ability to do this frame.</summary>
        protected enum AttackTimerState
        {
            /// <summary>Nothing is running; the ability is free to start an attack.</summary>
            Ready,

            /// <summary>A windup or a cooldown is still running. Do nothing this frame.</summary>
            Busy,

            /// <summary>
            /// The windup just ran out: land the hit now. Reported on one frame only, so the landing
            /// code runs exactly once.
            /// </summary>
            Landing
        }

        /// <summary>
        /// The facing to mirror authored offsets by: the character's while the game runs, falling back to
        /// the transform's own flip so gizmos still draw the right way round before there is a character.
        /// </summary>
        protected float FacingDirection => Character != null
            ? Character.FacingDirection
            : transform.localScale.x >= 0f ? 1f : -1f;

        /// <summary>
        /// The Transform a projectile leaves from — a muzzle, a hand. Null for the attacks that don't fire
        /// anything, which is why this is an override rather than a serialized field here: a field would put
        /// a dead Inspector slot on every melee attack.
        /// </summary>
        protected virtual Transform FirePoint => null;

        /// <summary>
        /// Where a shot spawns: the <see cref="FirePoint"/> if there is one, else the character's own
        /// position, else this ability's. The last hop keeps gizmos drawable on a prefab that has no
        /// character yet.
        /// </summary>
        protected Vector2 FirePosition
        {
            get
            {
                Transform firePoint = FirePoint;
                if (firePoint != null) return firePoint.position;
                if (Character != null) return Character.transform.position;

                return transform.position;
            }
        }

        /// <summary>
        /// False while the attack should armor through damage instead of being interrupted by it. Gates
        /// damage only; being eliminated always cancels.
        /// </summary>
        protected virtual bool CanBeInterrupted => true;

        /// <summary>
        /// Drops whatever the attack has in flight — a held charge, an armed windup — and starts its
        /// cooldown. Called when the character is damaged or eliminated. An attack that lands the moment
        /// it starts has nothing in flight to drop, so the default does nothing.
        /// </summary>
        protected virtual void CancelAttack() { }

        // ── The windup → land → cooldown timer ───────────────────────────────────────────────────────
        //
        // Every attack runs the same three beats: a windup that holds the hit back until the animation's
        // impact frame, the hit landing, then a cooldown before the next attack may start. Both timers
        // count down in the ability's own tick, so both stop whenever the attack does: while the
        // character is stunned, paused, or the ability is disabled.
        //
        // That freeze is a feel decision, not an implementation detail: attack recovery does NOT
        // continue through a stun. The three enemy attacks want that; PlayerAttackAbility answers the
        // opposite way, and its cooldown fields say why.
        //
        // Not to be confused with the cooldown on CharacterAbility.InputHandle (BindInput's
        // CooldownTime), which starts when the button is pressed. This one starts when the hit LANDS, so
        // a long windup, or a charge held for two seconds, can never quietly burn through it.
        // Input-driven attacks still use InputHandle for its buffering, gating on a timer like this one
        // through CanExecute.

        float m_WindupLeft;
        float m_CooldownLeft;
        bool m_IsWindupArmed;

        /// <summary>True between the attack starting and its hit landing.</summary>
        protected bool IsWindupArmed => m_IsWindupArmed;

        /// <summary>
        /// Arms the windup, returning true once it is running; wait for <see cref="TickAttackTimer"/> to
        /// report <see cref="AttackTimerState.Landing"/>. Returns false when there is no windup to wait
        /// through (<paramref name="windupTime"/> of 0 or less), in which case land the hit immediately.
        /// </summary>
        protected bool BeginWindup(float windupTime)
        {
            if (windupTime <= 0f) return false;

            m_IsWindupArmed = true;
            m_WindupLeft = windupTime;
            return true;
        }

        /// <summary>
        /// Advances whichever of the two timers is running and says what to do about it. Call it once per
        /// <c>OnUpdate</c>, before deciding whether to start an attack.
        /// </summary>
        protected AttackTimerState TickAttackTimer()
        {
            if (m_IsWindupArmed)
            {
                m_WindupLeft -= Time.deltaTime;
                if (m_WindupLeft > 0f) return AttackTimerState.Busy;

                ClearWindup();
                return AttackTimerState.Landing;
            }

            if (m_CooldownLeft > 0f)
            {
                m_CooldownLeft -= Time.deltaTime;
                return AttackTimerState.Busy;
            }

            return AttackTimerState.Ready;
        }

        /// <summary>
        /// Blocks the next attack for this many seconds. Call it as the hit lands rather than as the
        /// attack starts, so tuning the windup never eats into the pause between attacks.
        /// <para>
        /// This is the single cooldown an ability with one attack needs. An ability with several (the
        /// player's melee and projectile each recover on their own clock) keeps its own instead and
        /// leaves this one sitting at zero.
        /// </para>
        /// </summary>
        protected void StartCooldown(float seconds)
        {
            m_CooldownLeft = seconds;
        }

        /// <summary>
        /// Disarms a windup that will never land, leaving the cooldown alone. Cancelling an attack
        /// usually pairs this with <see cref="StartCooldown"/>, since the attack still has to recover, even
        /// though its hit never happened.
        /// </summary>
        protected void ClearWindup()
        {
            m_IsWindupArmed = false;
            m_WindupLeft = 0f;
        }

        /// <summary>
        /// Clears both timers, so an ability that gets re-initialized doesn't inherit a windup or a
        /// cooldown from its last life. For <c>OnCleanup</c>.
        /// </summary>
        protected void ResetAttackTimer()
        {
            ClearWindup();
            m_CooldownLeft = 0f;
        }

        /// <summary>
        /// Turns an offset authored in the Inspector into a world point, mirroring X to
        /// <see cref="FacingDirection"/> so one set of values serves both facings.
        /// </summary>
        protected Vector2 MirroredPoint(Vector2 localOffset)
        {
            return (Vector2)transform.position
                   + new Vector2(localOffset.x * FacingDirection, localOffset.y);
        }

        /// <summary>Draws a hitbox at a mirrored offset, in the current <see cref="Gizmos.color"/>.</summary>
        protected void DrawMirroredBox(Vector2 localOffset, Vector2 size)
        {
            Gizmos.DrawWireCube(MirroredPoint(localOffset), size);
        }

        /// <summary>
        /// Spawns the effect prefab at every point where the attack connected, mirrored to facing.
        /// Does nothing when no prefab is set or nothing was hit; <paramref name="scale"/> sizes it.
        /// </summary>
        protected void SpawnHitVfx(OneShotVfx effectPrefab, List<Vector2> hitPoints, float scale = 1f)
        {
            if (effectPrefab == null || hitPoints == null || hitPoints.Count == 0) return;

            foreach (Vector2 hitPoint in hitPoints)
            {
                SpawnVfx(effectPrefab, hitPoint, scale);
            }
        }

        // Taking a hit or dying interrupts a charge or windup: attack ticks freeze while stunned, so a
        // still-armed attack would otherwise land the moment the stun ends. Wired here rather than in each
        // attack so none of them can forget it.
        protected override void OnBeforeInitialize()
        {
            Character.OnDamaged += HandleDamaged;
            Character.OnEliminated += CancelAttack;
        }

        protected override void OnBeforeCleanup()
        {
            if (Character == null) return;

            Character.OnDamaged -= HandleDamaged;
            Character.OnEliminated -= CancelAttack;
        }

        void HandleDamaged(DamageInfo damage)
        {
            if (!CanBeInterrupted) return;

            CancelAttack();
        }

        protected override void OnDestroy()
        {
            Character?.RemoveAbility(this);
            base.OnDestroy();
        }
    }
}

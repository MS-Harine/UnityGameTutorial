using UnityEngine;

namespace Blocks.Character
{
    /// <summary>
    /// Applies the attacker-authored <see cref="HitReaction"/> plus the victim's own i-frames.
    /// The victim's Rigidbody2D mass divides the reaction speed, so heavy characters barely move.
    /// </summary>
    sealed class HitReactionModule
    {
        // A fly-away should never outlive a victim stuck on a ledge or against a wall.
        const float k_MaxFlightDuration = 2.5f;

        // Mass-scaled reactions slower than this don't move the victim at all, so a heavy
        // boss flinches in place instead of micro-sliding.
        const float k_MinReactionSpeed = 3f;

        float m_InvulnerabilityLeft;
        bool m_InvulnerabilityEnded;

        float m_StunLeft;
        bool m_HasTimedStun;
        bool m_StunEnded;

        float m_PushLeft;
        float m_PushDuration;
        float m_PushSpeedX;

        Vector2 m_FlightVelocity;
        float m_FlightTimeLeft;
        bool m_HasLeftGround;
        bool m_LaunchStarted;

        public bool IsInvulnerable => m_InvulnerabilityLeft > 0f;
        public bool IsPushedBack => m_PushLeft > 0f;
        public bool IsLaunched => m_FlightTimeLeft > 0f;

        /// <summary>True while any reaction moves this character; its own abilities stay muted.</summary>
        public bool IsReacting => IsPushedBack || IsLaunched;

        public float LaunchVelocityX => IsLaunched ? m_FlightVelocity.x : 0f;

        // Ease-out: the shove is strongest at impact and bleeds off quadratically.
        public float PushVelocityX
        {
            get
            {
                if (!IsPushedBack || m_PushDuration <= 0f) return 0f;

                float remaining = m_PushLeft / m_PushDuration;
                return m_PushSpeedX * remaining * remaining;
            }
        }

        public void OnHit(in DamageInfo damage, float invulnerabilityDuration, float mass)
        {
            if (invulnerabilityDuration > 0f)
            {
                m_InvulnerabilityLeft = invulnerabilityDuration;
            }

            HitReaction reaction = damage.Reaction;
            if (reaction.StunTime > 0f)
            {
                m_StunLeft = reaction.StunTime;
                m_HasTimedStun = true;
            }

            if (reaction.Kind == HitReactionKind.None) return;

            float directionX = damage.Direction.x;
            if (Mathf.Abs(directionX) < 0.0001f) return;

            // The heavier the victim, the smaller the reaction; below the threshold it stands its ground.
            float speed = mass > 0f ? reaction.Speed / mass : reaction.Speed;
            if (speed < k_MinReactionSpeed) return;

            if (reaction.Kind == HitReactionKind.FlyAway)
            {
                float angle = reaction.Angle * Mathf.Deg2Rad;
                m_FlightVelocity = new Vector2(Mathf.Cos(angle) * Mathf.Sign(directionX), Mathf.Sin(angle)) * speed;
                m_FlightTimeLeft = k_MaxFlightDuration;
                m_HasLeftGround = false;
                m_LaunchStarted = true;

                // A launch replaces any shove still running; the two would double up horizontally.
                m_PushLeft = 0f;
                return;
            }

            // A victim already in flight ignores shoves until it lands.
            if (IsLaunched) return;

            if (reaction.Duration <= 0f) return;
            m_PushSpeedX = Mathf.Sign(directionX) * speed;
            m_PushDuration = reaction.Duration;
            m_PushLeft = reaction.Duration;
        }

        /// <summary>
        /// Opens an i-frame window with no hit behind it — the respawn control delay uses this. Never
        /// shortens a window already running.
        /// </summary>
        public void BeginInvulnerability(float duration)
        {
            if (duration <= m_InvulnerabilityLeft) return;

            m_InvulnerabilityLeft = duration;
            m_InvulnerabilityEnded = false;
        }

        public void Tick(float deltaTime, bool isGrounded)
        {
            if (m_InvulnerabilityLeft > 0f)
            {
                m_InvulnerabilityLeft -= deltaTime;
                if (m_InvulnerabilityLeft <= 0f) m_InvulnerabilityEnded = true;
            }

            if (m_StunLeft > 0f)
            {
                m_StunLeft -= deltaTime;
                if (m_StunLeft <= 0f && m_HasTimedStun)
                {
                    m_HasTimedStun = false;
                    m_StunEnded = true;
                }
            }

            if (m_PushLeft > 0f)
            {
                m_PushLeft -= deltaTime;
            }

            if (m_FlightTimeLeft > 0f)
            {
                m_FlightTimeLeft -= deltaTime;

                // The flight ends on touching ground again, but only after actually leaving it,
                // so the launch isn't cancelled on the very frame the hit connects.
                if (!isGrounded) m_HasLeftGround = true;
                else if (m_HasLeftGround) m_FlightTimeLeft = 0f;
            }
        }

        /// <summary>True exactly once, on the hit that started a launch; hands out its vertical velocity.</summary>
        public bool ConsumeLaunchStarted(out float verticalVelocity)
        {
            verticalVelocity = m_FlightVelocity.y;
            if (!m_LaunchStarted) return false;

            m_LaunchStarted = false;
            return true;
        }

        /// <summary>True exactly once, on the frame a timed stun ran out.</summary>
        public bool ConsumeStunEnded()
        {
            if (!m_StunEnded) return false;
            m_StunEnded = false;
            return true;
        }

        /// <summary>True exactly once, on the frame the invulnerability window ran out.</summary>
        public bool ConsumeInvulnerabilityEnded()
        {
            if (!m_InvulnerabilityEnded) return false;
            m_InvulnerabilityEnded = false;
            return true;
        }

        public void Clear()
        {
            m_InvulnerabilityLeft = 0f;
            m_InvulnerabilityEnded = false;
            m_StunLeft = 0f;
            m_HasTimedStun = false;
            m_StunEnded = false;
            m_PushLeft = 0f;
            m_PushDuration = 0f;
            m_PushSpeedX = 0f;
            m_FlightVelocity = Vector2.zero;
            m_FlightTimeLeft = 0f;
            m_HasLeftGround = false;
            m_LaunchStarted = false;
        }
    }
}

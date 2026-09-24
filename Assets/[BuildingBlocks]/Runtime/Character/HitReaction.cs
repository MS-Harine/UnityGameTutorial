using UnityEngine;

namespace Blocks.Character
{
    public enum HitReactionKind
    {
        None,
        PushBack,
        FlyAway
    }

    /// <summary>
    /// How a hit moves its victim: authored by the attack, divided by the victim's Rigidbody2D mass.
    /// Build one with <see cref="PushBack"/> or <see cref="FlyAway"/>; <see cref="None"/> deals damage only.
    /// </summary>
    public readonly struct HitReaction
    {
        public readonly HitReactionKind Kind;

        // World units/second, before the victim's mass divides it.
        public readonly float Speed;

        // FlyAway only: launch angle in degrees above horizontal.
        public readonly float Angle;

        // PushBack only: seconds the shove lasts. A FlyAway lasts until the victim lands.
        public readonly float Duration;

        // Seconds the victim can't move or attack. 0 = no stun.
        public readonly float StunTime;

        /// <summary>Damage only; the victim is not moved or stunned.</summary>
        public static HitReaction None => default;

        /// <summary>A grounded shove away from the attacker, strongest at impact and easing out.</summary>
        public static HitReaction PushBack(float speed, float stunTime = 0.15f, float duration = 0.2f)
        {
            return new HitReaction(HitReactionKind.PushBack, speed, 0f, duration, stunTime);
        }

        /// <summary>A Smash-style diagonal launch; the victim flies until it lands.</summary>
        public static HitReaction FlyAway(float speed, float angle = 50f, float stunTime = 0.3f)
        {
            return new HitReaction(HitReactionKind.FlyAway, speed, Mathf.Clamp(angle, 0f, 90f), 0f, stunTime);
        }

        HitReaction(HitReactionKind kind, float speed, float angle, float duration, float stunTime)
        {
            Kind = kind;
            Speed = Mathf.Max(0f, speed);
            Angle = angle;
            Duration = duration;
            StunTime = Mathf.Max(0f, stunTime);
        }
    }
}

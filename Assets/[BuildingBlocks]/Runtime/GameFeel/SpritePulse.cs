using UnityEngine;

namespace Blocks.GameFeel
{
    /// <summary>Shape of a pulse: a smooth swell, or a light switching on and off.</summary>
    public enum PulseWave
    {
        /// <summary>Eases in and out, reading as a glow.</summary>
        Sine,
        /// <summary>Half of each cycle on the flash colour, half back to normal, reading as a blink.</summary>
        Square
    }

    /// <summary>Which clock a pulse beats on.</summary>
    public enum PulseClock
    {
        /// <summary>Game time. Freezes with the game under hit-stop or a pause.</summary>
        Scaled,
        /// <summary>Wall-clock time. Keeps beating through hit-stop and pauses.</summary>
        Unscaled
    }

    /// <summary>How a pulse turns its rate into a phase.</summary>
    public enum PulseRate
    {
        /// <summary>Constant rate, so the phase is a plain multiple of elapsed time.</summary>
        Fixed,

        /// <summary>
        /// The rate changes over the pulse's life, so the phase accumulates instead. Scaling elapsed time
        /// by a rate that just changed would jump the whole wave on every speed change.
        /// </summary>
        Ramped
    }

    /// <summary>
    /// Pulses a sprite toward a colour: the charge tell every attack that winds up needs, and the fuse
    /// flicker on a grenade. It captures the sprite's own colour, advances the phase, and puts the sprite
    /// back where it found it on <see cref="Stop"/>.
    /// Not a MonoBehaviour: whoever owns the tell owns one of these, built in Awake once the serialized
    /// sprite is known, and calls <see cref="Tick"/> every frame the pulse should be beating.
    /// The waveform, clock and rate are all set once at construction, because they describe the tell rather
    /// than the moment: a charge that speeds up needs <see cref="PulseRate.Ramped"/> for its whole life.
    /// </summary>
    public class SpritePulse
    {
        readonly SpriteRenderer m_Renderer;
        readonly PulseWave m_Wave;
        readonly PulseClock m_Clock;
        readonly PulseRate m_Rate;
        readonly Color m_BaseColor = Color.white;

        float m_Phase;

        /// <param name="renderer">Sprite to write. Null computes the pulse without touching any sprite, for
        /// an owner that hands the strength somewhere else; see <c>ProjectileFeedback</c>, where
        /// <c>ProjectileVisuals</c> is the single writer of the colour.</param>
        /// <param name="wave">Smooth swell or hard blink.</param>
        /// <param name="clock">Game time, or wall-clock time that hit-stop can't freeze.</param>
        /// <param name="rate">Whether the rate handed to <see cref="Tick"/> is the same every frame.</param>
        public SpritePulse(SpriteRenderer renderer, PulseWave wave, PulseClock clock, PulseRate rate)
        {
            m_Renderer = renderer;
            m_Wave = wave;
            m_Clock = clock;
            m_Rate = rate;

            if (renderer != null) m_BaseColor = renderer.color;
        }

        /// <summary>
        /// Advances the pulse and blends <paramref name="color"/> over the sprite, returning the strength it
        /// landed on (0–1) for an owner driving something else off the same beat, such as a light.
        /// </summary>
        /// <param name="color">Colour to pulse toward. Its alpha is ignored; see <see cref="Apply"/>.</param>
        /// <param name="rate">Pulses per second. Varies over the pulse's life only for
        /// <see cref="PulseRate.Ramped"/>.</param>
        /// <param name="strengthScale">Ceiling on the blend, for a tell that deepens as well as quickens.
        /// 1 pulses all the way to <paramref name="color"/>.</param>
        public float Tick(Color color, float rate, float strengthScale = 1f)
        {
            float phase = Advance(rate);

            float wave = m_Wave == PulseWave.Square
                ? (Mathf.Repeat(phase, 1f) < 0.5f ? 0f : 1f)
                : 0.5f + 0.5f * Mathf.Sin(phase * 2f * Mathf.PI);

            float strength = Mathf.Clamp01(wave * strengthScale);
            Apply(color, strength);
            return strength;
        }

        /// <summary>
        /// Ends the pulse and puts the sprite back to its own colour. Also rewinds the phase, so the next
        /// pulse opens on the same beat the last one did, a no-op for <see cref="PulseRate.Fixed"/>, whose
        /// phase is the clock rather than something it keeps.
        /// </summary>
        public void Stop()
        {
            m_Phase = 0f;
            if (m_Renderer != null) m_Renderer.color = m_BaseColor;
        }

        /// <summary>
        /// The serialized sprite, or the first one on <paramref name="owner"/> or its children, so leaving
        /// the field empty still finds the obvious sprite. Separate from the constructor because an owner may
        /// need the reference for its own checks even when it hands the pulse no renderer to write.
        /// </summary>
        public static SpriteRenderer FindRenderer(Component owner, SpriteRenderer serialized)
        {
            if (serialized != null) return serialized;

            return owner != null ? owner.GetComponentInChildren<SpriteRenderer>() : null;
        }

        float Advance(float rate)
        {
            if (m_Rate == PulseRate.Fixed)
            {
                return (m_Clock == PulseClock.Unscaled ? Time.unscaledTime : Time.time) * rate;
            }

            m_Phase += (m_Clock == PulseClock.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime) * rate;
            return m_Phase;
        }

        void Apply(Color color, float strength)
        {
            if (m_Renderer == null) return;

            Color tinted = Color.Lerp(m_BaseColor, color, strength);

            // A flash never changes opacity. The sprite's alpha may belong to something else entirely (a
            // projectile's lifetime fade), and a flash colour dimming it would read as a glitch, not a tell.
            tinted.a = m_BaseColor.a;
            m_Renderer.color = tinted;
        }
    }

    /// <summary>
    /// The looping effect object a charging attack shows (a child with particles, say), grown up to its
    /// authored scale as the charge builds, so the progress reads at a glance. Hidden while nothing charges.
    /// The counterpart to <see cref="SpritePulse"/>: the two together are a charge tell.
    /// </summary>
    public class ChargeEffect
    {
        // Charges open at half size rather than nothing: an effect scaled to zero has nothing visible to
        // grow from, and the start of a charge is when the tell matters most.
        const float k_StartScale = 0.5f;

        readonly GameObject m_Object;
        readonly Vector3 m_BaseScale = Vector3.one;

        /// <param name="effect">Optional effect object. Null makes every call below a no-op.</param>
        public ChargeEffect(GameObject effect)
        {
            m_Object = effect;
            if (effect == null) return;

            m_BaseScale = effect.transform.localScale;
            effect.SetActive(false);
        }

        /// <summary>Shows, grows or hides the effect for this frame's charge state.</summary>
        public void Tick(bool isCharging, float chargeRatio)
        {
            if (m_Object == null) return;

            if (m_Object.activeSelf != isCharging) m_Object.SetActive(isCharging);
            if (!isCharging) return;

            m_Object.transform.localScale = m_BaseScale * Mathf.Lerp(k_StartScale, 1f, chargeRatio);
        }

        /// <summary>Hides the effect, so nothing is left hanging mid-charge.</summary>
        public void Stop()
        {
            if (m_Object != null) m_Object.SetActive(false);
        }
    }
}

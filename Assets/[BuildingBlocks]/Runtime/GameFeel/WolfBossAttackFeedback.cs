using UnityEngine;
using Blocks.Attack;

namespace Blocks.GameFeel
{
    /// <summary>
    /// Charge tell for the wolf boss: while WolfBossAttackAbility telegraphs its slam, the sprite
    /// flickers toward a configurable color, faster and stronger as the charge nears release.
    /// </summary>
    [DisallowMultipleComponent]
    public class WolfBossAttackFeedback : MonoBehaviour
    {
        [Header("References (auto-found when left empty)")]
        [SerializeField] SpriteRenderer spriteRenderer;

        [Header("Charge Flicker")]
        [Tooltip("Color the sprite pulses toward while the boss charges its slam. Its alpha is ignored: a " +
                 "flash never changes the sprite's opacity.")]
        [SerializeField] Color chargeFlashColor = new Color(1f, 0.3f, 0.2f);
        [Tooltip("Flicker speed when the charge starts.")]
        [SerializeField] float minPulsesPerSecond = 4f;
        [Tooltip("Flicker speed just before the slam releases.")]
        [SerializeField] float maxPulsesPerSecond = 10f;
        [Tooltip("Optional looping effect object (e.g. a child with particles) shown while charging; it grows with the charge.")]
        [SerializeField] GameObject chargeEffect;

        WolfBossAttackAbility m_Attack;
        SpritePulse m_Pulse;
        ChargeEffect m_ChargeEffect;
        bool m_WasCharging;

        void Awake()
        {
            m_Attack = GetComponentInParent<WolfBossAttackAbility>();

            // The flicker's rate climbs with the charge, so its phase has to accumulate: scaling elapsed
            // time by a rate that just changed would make the wave jump on every speed change.
            m_Pulse = new SpritePulse(SpritePulse.FindRenderer(this, spriteRenderer),
                                      PulseWave.Sine, PulseClock.Scaled, PulseRate.Ramped);
            m_ChargeEffect = new ChargeEffect(chargeEffect);

            if (m_Attack == null)
            {
                Debug.LogWarning(
                    $"[WolfBossAttackFeedback] {name}: No WolfBossAttackAbility on this GameObject or its parents — charge feedback will not play.",
                    this);
            }
        }

        void OnDisable()
        {
            StopFlicker();
        }

        void Update()
        {
            bool isCharging = m_Attack != null && m_Attack.IsCharging;

            if (!isCharging)
            {
                if (m_WasCharging) StopFlicker();
                return;
            }

            m_WasCharging = true;
            float chargeRatio = m_Attack.CurrentChargeRatio;

            // The tell winds up on both axes as the slam nears: the flicker quickens, and the flash also
            // deepens: subtle at first, loud on the last beats.
            m_Pulse.Tick(chargeFlashColor,
                         Mathf.Lerp(minPulsesPerSecond, maxPulsesPerSecond, chargeRatio),
                         Mathf.Lerp(0.4f, 1f, chargeRatio));

            m_ChargeEffect.Tick(true, chargeRatio);
        }

        void StopFlicker()
        {
            m_WasCharging = false;
            m_Pulse.Stop();
            m_ChargeEffect.Stop();
        }

        void OnValidate()
        {
            if (minPulsesPerSecond < 0f) minPulsesPerSecond = 0f;
            if (maxPulsesPerSecond < minPulsesPerSecond) maxPulsesPerSecond = minPulsesPerSecond;
        }
    }
}

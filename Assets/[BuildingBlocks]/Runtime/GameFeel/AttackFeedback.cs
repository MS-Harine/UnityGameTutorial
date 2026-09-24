using UnityEngine;
using Blocks.Attack;
using Blocks.Character;

namespace Blocks.GameFeel
{
    /// <summary>
    /// Attack "game feel" for the player: camera zoom + sprite flash while charging, then hit-stop,
    /// shake, and zoom punch on every hit, bigger for fully charged (fly-away) blows.
    /// </summary>
    [DisallowMultipleComponent]
    public class AttackFeedback : MonoBehaviour
    {
        [Header("References (auto-found when left empty)")]
        [SerializeField] CameraFeedback cameraFeedback;
        [SerializeField] SpriteRenderer spriteRenderer;

        [Header("Charging")]
        [Tooltip("How far the camera zooms in at full charge (0.1 = 10% closer). 0 disables the zoom.")]
        [Range(0f, 0.5f)]
        [SerializeField] float chargeZoomAmount = 0.08f;
        [Tooltip("Optional looping effect object (e.g. a child with particles) shown while charging; it grows with the charge.")]
        [SerializeField] GameObject chargeEffect;
        [Tooltip("Color the sprite pulses to once the charge is full — the 'ready' flash. Its alpha is " +
                 "ignored: a flash never changes the sprite's opacity.")]
        [SerializeField] Color fullChargeFlashColor = new Color(1f, 0.85f, 0.3f);
        [SerializeField] float fullChargePulsesPerSecond = 6f;
        [Tooltip("Small camera kick the moment the charge becomes full.")]
        [SerializeField] float fullChargeReadyShake = 0.15f;

        [Header("Regular Hit (quick taps and partial charges)")]
        [Tooltip("How long the game freezes on a regular hit. Keep tiny — it should be felt, not seen.")]
        [SerializeField] float regularHitStop = 0.03f;
        [SerializeField] float regularHitShake = 0.15f;
        [Tooltip("Instant zoom-in on hit that eases back out (0.02 = 2% closer).")]
        [SerializeField] float regularHitZoomPunch = 0.02f;

        [Header("Full Charge Hit (launching blows)")]
        [SerializeField] float fullChargeHitStop = 0.12f;
        [SerializeField] float fullChargeHitShake = 1f;
        [SerializeField] float fullChargeHitZoomPunch = 0.08f;

        BuildingBlocksCharacter m_Character;
        PlayerAttackAbility m_Attack;
        SpritePulse m_Pulse;
        ChargeEffect m_ChargeEffect;
        bool m_WasFullCharge;

        void Awake()
        {
            m_Character = GetComponentInParent<BuildingBlocksCharacter>();
            m_Attack = GetComponentInParent<PlayerAttackAbility>();

            // Unscaled clock so the ready flash keeps beating through a hit-stop freeze, since it is telling the
            // player the swing is armed, and a frozen tell reads as a bug. The rate is a fixed serialized
            // field, so the phase can come straight off that clock with no wave to jump.
            m_Pulse = new SpritePulse(SpritePulse.FindRenderer(this, spriteRenderer),
                                      PulseWave.Sine, PulseClock.Unscaled, PulseRate.Fixed);
            m_ChargeEffect = new ChargeEffect(chargeEffect);

            if (m_Character == null || m_Attack == null)
            {
                Debug.LogWarning(
                    $"[AttackFeedback] {name}: No BuildingBlocksCharacter/PlayerAttackAbility on this GameObject or its parents — attack feedback will not play.",
                    this);
            }
        }

        void Start()
        {
            // Deferred to Start so a camera spawned by another Awake is already in the scene.
            if (cameraFeedback == null) cameraFeedback = FindAnyObjectByType<CameraFeedback>();

            if (cameraFeedback == null)
            {
                Debug.LogWarning(
                    $"[AttackFeedback] {name}: No CameraFeedback found — camera zoom and shake feedback will not play.",
                    this);
            }
        }

        void OnEnable()
        {
            if (m_Character != null) m_Character.OnDamageDealt += HandleDamageDealt;
        }

        void OnDisable()
        {
            if (m_Character != null) m_Character.OnDamageDealt -= HandleDamageDealt;

            // Leave nothing stuck mid-charge: normal color, no effect, camera at rest.
            m_WasFullCharge = false;
            m_Pulse.Stop();
            m_ChargeEffect.Stop();
            if (cameraFeedback != null) cameraFeedback.SetZoomMultiplier(1f);
        }

        void Update()
        {
            bool isCharging = m_Attack != null && m_Attack.IsCharging;
            float chargeRatio = isCharging ? m_Attack.CurrentChargeRatio : 0f;

            if (cameraFeedback != null)
            {
                cameraFeedback.SetZoomMultiplier(1f - chargeZoomAmount * chargeRatio);
            }

            m_ChargeEffect.Tick(isCharging, chargeRatio);
            UpdateFullChargeFlash(isCharging && chargeRatio >= 1f);
        }

        void UpdateFullChargeFlash(bool isFullCharge)
        {
            if (isFullCharge && !m_WasFullCharge && cameraFeedback != null)
            {
                cameraFeedback.Shake(fullChargeReadyShake);
            }
            else if (!isFullCharge && m_WasFullCharge)
            {
                m_Pulse.Stop();
            }

            m_WasFullCharge = isFullCharge;

            if (!isFullCharge) return;

            m_Pulse.Tick(fullChargeFlashColor, fullChargePulsesPerSecond);
        }

        void HandleDamageDealt(DamageInfo damage)
        {
            // Only fully charged hits fly their victims away, the heavy tell.
            bool isFullChargeHit = damage.Reaction.Kind == HitReactionKind.FlyAway;

            HitStop.Play(isFullChargeHit ? fullChargeHitStop : regularHitStop);

            if (cameraFeedback == null) return;
            cameraFeedback.Shake(isFullChargeHit ? fullChargeHitShake : regularHitShake);
            cameraFeedback.PunchZoom(isFullChargeHit ? fullChargeHitZoomPunch : regularHitZoomPunch);
        }

        void OnValidate()
        {
            if (fullChargePulsesPerSecond < 0f) fullChargePulsesPerSecond = 0f;
            if (fullChargeReadyShake < 0f) fullChargeReadyShake = 0f;
            if (regularHitStop < 0f) regularHitStop = 0f;
            if (regularHitShake < 0f) regularHitShake = 0f;
            if (regularHitZoomPunch < 0f) regularHitZoomPunch = 0f;
            if (fullChargeHitStop < 0f) fullChargeHitStop = 0f;
            if (fullChargeHitShake < 0f) fullChargeHitShake = 0f;
            if (fullChargeHitZoomPunch < 0f) fullChargeHitZoomPunch = 0f;
        }
    }
}

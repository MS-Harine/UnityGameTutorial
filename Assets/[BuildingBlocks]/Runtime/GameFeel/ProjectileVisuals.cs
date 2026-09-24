using UnityEngine;
using Blocks.Attack;

namespace Blocks.GameFeel
{
    /// <summary>
    /// Optional, purely cosmetic launch animation for a projectile sprite (flip, pop, stretch, fade).
    /// Put it next to <see cref="Projectile"/> on the prefab; the sprite is assumed to face right.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [DisallowMultipleComponent]
    public sealed class ProjectileVisuals : MonoBehaviour
    {
        [Header("Spawn Pop")]
        [Tooltip("Seconds to grow from Pop Start Scale up to Pop Overshoot.")]
        [SerializeField] float popDuration = 0.1f;
        [Tooltip("Scale multiplier the sprite spawns at, relative to the prefab's scale.")]
        [SerializeField] float popStartScale = 0.4f;
        [Tooltip("Scale multiplier briefly reached at the end of the pop before settling back to 1.")]
        [SerializeField] float popOvershoot = 1.15f;

        [Header("Squash & Stretch")]
        [Tooltip("Scale multiplier along the travel direction at launch; eases back to 1.")]
        [SerializeField] float launchStretch = 1.35f;
        [Tooltip("Scale multiplier across the travel direction at launch; eases back to 1.")]
        [SerializeField] float launchSquash = 0.75f;
        [Tooltip("Seconds after the pop for the overshoot and stretch to settle back to normal.")]
        [SerializeField] float settleDuration = 0.2f;

        [Header("Fade")]
        [Tooltip("Portion of the lifetime played at full opacity before fading begins (0.5 = fade over the second half).")]
        [Range(0f, 1f)]
        [SerializeField] float fadeStartRatio = 0.5f;

        SpriteRenderer m_Renderer;
        Vector3 m_BaseScale;
        Color m_BaseColor;
        Color m_FlashColor = Color.white;
        float m_FlashStrength;
        float m_Lifetime;
        float m_Elapsed;
        float m_SizeScale = 1f;
        bool m_IsPlaying;

        void Awake()
        {
            m_Renderer = GetComponent<SpriteRenderer>();
            m_BaseScale = transform.localScale;
            m_BaseColor = m_Renderer.color;
        }

        /// <summary>
        /// Restarts the launch animation; called by Projectile every time it is fired.
        /// <paramref name="sizeScale"/> multiplies the whole animation for this shot.
        /// </summary>
        public void NotifyFired(Vector2 velocity, float lifetime, float sizeScale = 1f)
        {
            m_Lifetime = lifetime;
            m_Elapsed = 0f;
            m_SizeScale = sizeScale > 0f ? sizeScale : 1f;
            m_IsPlaying = true;
            m_Renderer.flipX = velocity.x < 0f;
            Apply();
        }

        /// <summary>
        /// Blends <paramref name="color"/> over the sprite at <paramref name="strength"/> (0–1), on top of
        /// the fade; <see cref="ProjectileFeedback"/> drives it for the fuse flicker. The colour is written
        /// here every frame, so anything tinting the sprite has to come through this rather than setting
        /// the SpriteRenderer itself. The flash colour's own alpha is ignored; the fade owns alpha.
        /// </summary>
        public void SetFlash(Color color, float strength)
        {
            m_FlashColor = color;
            m_FlashStrength = Mathf.Clamp01(strength);
        }

        void OnDisable()
        {
            // Pooled projectiles are reused, so leave the sprite exactly as it was authored.
            m_IsPlaying = false;
            m_SizeScale = 1f;
            m_FlashColor = Color.white;
            m_FlashStrength = 0f;
            transform.localScale = m_BaseScale;
            m_Renderer.color = m_BaseColor;
        }

        void Update()
        {
            if (!m_IsPlaying) return;
            m_Elapsed += Time.deltaTime;
            Apply();
        }

        void Apply()
        {
            ApplyScale();
            ApplyColor();
        }

        void ApplyScale()
        {
            // Uniform pop: grow fast to the overshoot, then relax to 1.
            float pop;
            if (m_Elapsed < popDuration)
            {
                float t = m_Elapsed / popDuration;
                float easedT = 1f - (1f - t) * (1f - t);
                pop = Mathf.Lerp(popStartScale, popOvershoot, easedT);
            }
            else
            {
                float t = settleDuration > 0f ? Mathf.Clamp01((m_Elapsed - popDuration) / settleDuration) : 1f;
                pop = Mathf.Lerp(popOvershoot, 1f, t);
            }

            // Directional squash & stretch relaxing back to the authored proportions.
            float totalSettleTime = popDuration + settleDuration;
            float settle = totalSettleTime > 0f ? Mathf.Clamp01(m_Elapsed / totalSettleTime) : 1f;
            float stretchX = Mathf.Lerp(launchStretch, 1f, settle);
            float squashY = Mathf.Lerp(launchSquash, 1f, settle);

            transform.localScale = new Vector3(
                m_BaseScale.x * m_SizeScale * pop * stretchX,
                m_BaseScale.y * m_SizeScale * pop * squashY,
                m_BaseScale.z);
        }

        // The single writer of the sprite's colour: the lifetime fade on alpha, and whatever flash another
        // component has asked for on top. Runs even with no lifetime set, so a flash still reaches the
        // renderer on a projectile that never fades.
        void ApplyColor()
        {
            float alpha = 1f;
            if (m_Lifetime > 0f)
            {
                float lifeRatio = Mathf.Clamp01(m_Elapsed / m_Lifetime);
                if (lifeRatio > fadeStartRatio)
                {
                    float fadeWindow = 1f - fadeStartRatio;
                    alpha = fadeWindow > 0f ? 1f - (lifeRatio - fadeStartRatio) / fadeWindow : 0f;
                }
            }

            Color color = m_FlashStrength > 0f
                ? Color.Lerp(m_BaseColor, m_FlashColor, m_FlashStrength)
                : m_BaseColor;
            color.a = m_BaseColor.a * alpha;
            m_Renderer.color = color;
        }

        void OnValidate()
        {
            if (popDuration < 0f) popDuration = 0f;
            if (popStartScale < 0f) popStartScale = 0f;
            if (popOvershoot < 1f) popOvershoot = 1f;
            if (launchStretch < 0f) launchStretch = 0f;
            if (launchSquash < 0f) launchSquash = 0f;
            if (settleDuration < 0f) settleDuration = 0f;
        }
    }
}

using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering.Universal;
using Blocks.Attack;
using Blocks.Audio;

namespace Blocks.GameFeel
{
    /// <summary>
    /// Sound and sprite feedback for a projectile in flight: a knock every time it bounces, and, for a
    /// fused shot, a countdown sound and a quickening flicker of the sprite and its light as the fuse runs
    /// out, then the explosion.
    /// Put it next to <see cref="Projectile"/> on the prefab; it builds its own AudioSources, so the only
    /// setup is picking clips and the mixer group. Both fuse cues are timed backwards from the explosion,
    /// so giving them the same lead time lands the flicker and the countdown together.
    /// The counterpart to <see cref="ProjectileVisuals"/>, which covers the launch rather than the fuse;
    /// the launch sound belongs to the firing ability (see <see cref="ProjectileAttackAbility"/>).
    /// </summary>
    [RequireComponent(typeof(Projectile))]
    [DisallowMultipleComponent]
    public sealed class ProjectileFeedback : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("The sprite the flicker flashes. Left empty it uses the SpriteRenderer on this object or " +
                 "a child.")]
        [SerializeField] SpriteRenderer spriteRenderer;
        [Tooltip("Optional 2D light flashed along with the sprite, so the glow doesn't stay flat while the " +
                 "sprite blinks. Left empty it uses the Light 2D on this object or a child, if there is one.")]
        [SerializeField] Light2D flickerLight;
        [Tooltip("Mixer group the sounds play through — use the SFX group on Assets/[BuildingBlocks]/Audio/Audio Mixer.mixer. " +
                 "Left empty they go straight to the master, where the SFX volume no longer reaches them.")]
        [SerializeField] AudioMixerGroup outputGroup;

        [Header("Bounce")]
        [Tooltip("Played every time the projectile hits a surface. One is picked at random per bounce, so a " +
                 "grenade that skips a few times doesn't sound looped. A single clip plays every time. Only " +
                 "projectiles that physically bounce make this sound — a fused shot that doesn't stick on impact.")]
        [SerializeField] AudioClip[] bounceClips;
        [Tooltip("1 is the clip's own level; above that boosts it. Effect recordings are usually mastered far " +
                 "quieter than music, so they need the boost to be heard at all.")]
        [SerializeField, Range(0f, 5f)] float bounceVolume = 2.5f;
        [Tooltip("Impacts slower than this stay silent. A projectile settling keeps touching the ground, and " +
                 "every touch is a bounce — without this it rattles as it comes to rest.")]
        [SerializeField, Min(0f)] float minBounceSpeed = 1.5f;
        [Tooltip("Shortest gap between two bounce sounds, which catches the rapid re-contacts of a projectile " +
                 "rolling down a slope.")]
        [SerializeField, Min(0f)] float minBounceInterval = 0.06f;

        [Header("Fuse Countdown")]
        [Tooltip("Played in the run-up to the explosion. Only used when the firing ability sets a Fuse Time.")]
        [SerializeField] AudioClip countdownClip;
        [SerializeField, Range(0f, 5f)] float countdownVolume = 2.5f;
        [Tooltip("Seconds before the explosion that the countdown starts. 0 = the clip's own length, so it " +
                 "finishes exactly as the projectile explodes.")]
        [SerializeField, Min(0f)] float countdownLeadTime;

        [Header("Fuse Flicker")]
        [Tooltip("Seconds before the explosion that the sprite starts flashing. Set it to the same lead time " +
                 "as the countdown — with Countdown Lead Time at 0 that's the countdown clip's own length — " +
                 "and the flash starts on the sound. 0 = no flicker.")]
        [SerializeField, Min(0f)] float flickerLeadTime = 1f;
        [Tooltip("Colour the sprite flashes to. Pick something closer to the sprite's own colour for a " +
                 "subtler blink. Its alpha is ignored — the projectile's fade owns that.")]
        [SerializeField] Color flickerColor = new Color(1f, 0.35f, 0.25f);
        [Tooltip("Flashes per second when the flicker starts.")]
        [SerializeField, Min(0f)] float flickerStartRate = 4f;
        [Tooltip("Flashes per second by the moment it explodes. Above the start rate the flashing quickens as " +
                 "the fuse runs out; the same value keeps it steady.")]
        [SerializeField, Min(0f)] float flickerEndRate = 14f;
        [Tooltip("How much brighter the light burns on each flash, as a multiple of its own intensity. " +
                 "1 = colour only, no brightness change.")]
        [SerializeField, Min(0f)] float flickerLightBoost = 1.6f;

        [Header("Explosion")]
        [Tooltip("Played where the projectile detonates.")]
        [SerializeField] AudioClip explodeClip;
        [SerializeField, Range(0f, 5f)] float explodeVolume = 2.5f;

        [Header("Variation")]
        [Tooltip("Each bounce is pitched up or down by up to this much, so repeats don't sound identical. " +
                 "0 = no variation.")]
        [SerializeField, Range(0f, 0.5f)] float pitchVariation = 0.08f;

        // Two sources: pitch is a property of the source, so varying a bounce's pitch would retune a
        // countdown still ringing on the same one, audibly, because the countdown is the long clip here.
        AudioSource m_BounceSource;
        AudioSource m_CountdownSource;
        AudioHelper m_Audio;
        SpritePulse m_Pulse;
        ProjectileVisuals m_Visuals;
        Color m_BaseLightColor = Color.white;
        float m_BaseLightIntensity = 1f;

        // A mirror of the projectile's own fuse. Both cues are thresholds on it, counted down from the
        // explosion rather than up from the launch, so they stay pinned to the moment that matters.
        float m_FuseLeft;
        bool m_IsFuseRunning;
        float m_CountdownAt;
        bool m_HasCountdownPlayed;
        float m_LastBounceTime = float.NegativeInfinity;

        void Awake()
        {
            spriteRenderer = SpritePulse.FindRenderer(this, spriteRenderer);

            if (flickerLight == null) flickerLight = GetComponentInChildren<Light2D>();
            if (flickerLight != null)
            {
                m_BaseLightColor = flickerLight.color;
                m_BaseLightIntensity = flickerLight.intensity;
            }

            // Only used to hand the flash over when the launch animation is also driving the colour.
            m_Visuals = GetComponent<ProjectileVisuals>();

            // A hard blink rather than a glow, on the same scaled clock as the fuse it mirrors, accumulating
            // its phase because the rate ramps up as that fuse runs out. It is handed no renderer when
            // ProjectileVisuals is there, since that component is the single writer of the sprite's colour:
            // the flash goes through it instead, and the pulse only counts out the beat.
            m_Pulse = new SpritePulse(m_Visuals != null ? null : spriteRenderer,
                                      PulseWave.Square, PulseClock.Scaled, PulseRate.Ramped);

            if (spriteRenderer == null && flickerLight == null && flickerLeadTime > 0f)
            {
                Debug.LogWarning(
                    $"[ProjectileFeedback] {name}: 'Flicker Lead Time' is set but there is no SpriteRenderer " +
                    "or Light 2D on this object or its children, so nothing will flicker. Drag one into the " +
                    "'Sprite Renderer' or 'Flicker Light' field, or set Flicker Lead Time to 0.",
                    this);
            }

            AudioHelper.WarmUp(bounceClips);
            AudioHelper.WarmUp(countdownClip);
            AudioHelper.WarmUp(explodeClip);

            m_BounceSource = CreateSource("Bounce Audio");
            m_CountdownSource = CreateSource("Countdown Audio");

            // The sources are built here rather than serialized, so there is no missing-source or 3D-blend
            // mistake for the helper to warn about.
            m_Audio = new AudioHelper(this, m_BounceSource, expectsClips: false,
                                      pitchVariation: pitchVariation);
        }

        void OnDisable()
        {
            // Pooled projectiles come back for reuse: cut anything mid-play, drop the fuse cues, and put the
            // sprite and light back to their own colours, or a shot that explodes mid-flash would return tinted.
            m_IsFuseRunning = false;
            m_FuseLeft = 0f;
            m_HasCountdownPlayed = false;
            m_LastBounceTime = float.NegativeInfinity;
            StopFlicker();

            m_BounceSource?.Stop();
            m_CountdownSource?.Stop();
        }

        void Update()
        {
            if (!m_IsFuseRunning) return;

            // Scaled time, matching the fuse the Projectile ticks; an unscaled clock would drift out of
            // sync with the explosion through a hit-stop or a pause.
            m_FuseLeft -= Time.deltaTime;

            if (!m_HasCountdownPlayed && m_FuseLeft <= m_CountdownAt)
            {
                m_HasCountdownPlayed = true;
                m_CountdownSource.PlayOneShot(countdownClip, countdownVolume);
            }

            UpdateFlicker();
        }

        /// <summary>
        /// Resets the per-shot state and starts the fuse cues; called by <see cref="Projectile"/> every time
        /// it is fired. A <paramref name="fuseTime"/> of 0 means the shot never detonates, so no cues.
        /// </summary>
        public void NotifyFired(float fuseTime)
        {
            // The "don't repeat the last clip" memory deliberately isn't reset with the rest of the per-shot
            // state: a reused projectile shouldn't open with the knock the previous one closed on.
            m_LastBounceTime = float.NegativeInfinity;
            StopFlicker();

            m_FuseLeft = fuseTime;
            m_IsFuseRunning = fuseTime > 0f;

            // Already true on the first tick when the fuse is shorter than a lead time, which is what a
            // fuse that short should do: start the cue straight away and let the explosion cut it off.
            m_HasCountdownPlayed = countdownClip == null;
            m_CountdownAt = countdownLeadTime > 0f || countdownClip == null
                ? countdownLeadTime
                : countdownClip.length;
        }

        /// <summary>
        /// Plays a bounce sound for an impact at <paramref name="impactSpeed"/>; called by
        /// <see cref="Projectile"/> on every collision. Slow or rapid repeat impacts are dropped.
        /// </summary>
        public void NotifyBounced(float impactSpeed)
        {
            if (impactSpeed < minBounceSpeed) return;

            // Unscaled: the gate is about how the ear hears the repeat, and a hit-stop freeze would
            // otherwise hold the guard open across a whole burst of contacts.
            if (Time.unscaledTime - m_LastBounceTime < minBounceInterval) return;

            AudioClip clip = m_Audio.PickRandom(bounceClips);
            if (clip == null) return;

            m_LastBounceTime = Time.unscaledTime;
            m_Audio.Play(clip, bounceVolume);
        }

        /// <summary>
        /// Plays the explosion; called by <see cref="Projectile"/> as it detonates.
        /// </summary>
        public void NotifyDetonated()
        {
            // Detonating recycles the projectile in the same call, deactivating this object and everything
            // playing on it, so the explosion plays on a throwaway object that outlives the shot. Pitch 1:
            // unlike the bounces, the blast is the same sound every time.
            m_Audio.PlayDetached(explodeClip, explodeVolume, 1f, outputGroup, transform.position);
        }

        void UpdateFlicker()
        {
            if (flickerLeadTime <= 0f) return;
            if (m_FuseLeft > flickerLeadTime) return;

            // Quickens as the fuse runs out: the rate ramps from start to end across the lead time.
            float progress = 1f - Mathf.Clamp01(m_FuseLeft / flickerLeadTime);
            float rate = Mathf.Lerp(flickerStartRate, flickerEndRate, progress);

            ApplyFlash(m_Pulse.Tick(flickerColor, rate));
        }

        void ApplyFlash(float strength)
        {
            // ProjectileVisuals writes the sprite's colour every frame for its fade, so when it is present
            // the flash goes through it, since two components setting the same colour would fight over it. When it
            // isn't, the pulse holds the sprite and has already written this strength itself.
            if (m_Visuals != null) m_Visuals.SetFlash(flickerColor, strength);

            ApplyFlashToLight(strength);
        }

        void ApplyFlashToLight(float strength)
        {
            if (flickerLight == null) return;

            // Nothing else drives the light, so it is written directly. Intensity is scaled from whatever
            // the light was authored at rather than set outright, so retuning the glow doesn't break this.
            flickerLight.color = strength > 0f
                ? Color.Lerp(m_BaseLightColor, flickerColor, strength)
                : m_BaseLightColor;
            flickerLight.intensity = m_BaseLightIntensity * Mathf.Lerp(1f, flickerLightBoost, strength);
        }

        void StopFlicker()
        {
            m_Pulse.Stop();
            ApplyFlash(0f);
        }

        // 2D rather than 3D: a 3D source is mixed by its distance to the AudioListener, which sits on a
        // camera pulled well back from the action, and the sounds would come out near-silent.
        AudioSource CreateSource(string sourceName)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);

            AudioSource source = child.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = outputGroup;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }
    }
}

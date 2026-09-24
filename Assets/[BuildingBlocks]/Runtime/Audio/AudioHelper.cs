using UnityEngine;
using UnityEngine.Audio;

namespace Blocks.Audio
{
    /// <summary>
    /// The sound plumbing every ability and feedback component needs: find the AudioSource, warn when it
    /// can't work, pitch each shot a little differently so repeats don't sound identical, pick from a set
    /// of clips without repeating the last one, and force clips into memory so the first play isn't silent.
    /// It also owns the charge loop: the held, swelling sound a charged attack rings while it builds
    /// (see <see cref="StartLoop"/>).
    /// Not a MonoBehaviour: whoever owns the sounds owns one of these, built in Awake or OnInitialize
    /// once the serialized clips are known. The owner drives the loop's fades by calling
    /// <see cref="TickLoop"/> every frame, and hands the generated source back with
    /// <see cref="DestroyLoop"/> when it tears down.
    /// </summary>
    public class AudioHelper
    {
        // Above this an AudioSource is mixed by its distance to the AudioListener, which sits on a camera
        // pulled well back from the action, so the sounds still play, they just come out near-silent.
        const float k_FlatBlendLimit = 0.01f;

        // Clip sets tracked for the "don't repeat the last clip" pick. Serialized arrays are stable
        // references, and no owner has more than two, so a handful of slots covers every real caller.
        const int k_MaxTrackedClipSets = 4;

        readonly Component m_Owner;
        readonly string m_OwnerLabel;
        readonly AudioSource m_Source;
        readonly float m_PitchVariation;
        readonly float m_BasePitch = 1f;

        readonly AudioClip[][] m_TrackedClips = new AudioClip[k_MaxTrackedClipSets][];
        readonly int[] m_LastIndices = new int[k_MaxTrackedClipSets];
        int m_TrackedCount;
        int m_NextSlot;

        // The charge loop's own source, built on the first StartLoop and reused after. Separate from the
        // one-shot source: a one-shot's pitch shift would retune a ringing loop, and the loop's
        // ramping volume would drag the one-shots down with it.
        AudioSource m_LoopSource;
        ChargeLoopSettings m_LoopSettings;
        float m_LoopTargetVolume;

        /// <param name="owner">Component the sounds belong to, used for the AudioSource fallback and to
        /// address warnings at the right object in the Inspector.</param>
        /// <param name="source">Serialized AudioSource. Null falls back to one on the owner's GameObject.</param>
        /// <param name="expectsClips">True when the owner has clips assigned, which is what makes a missing
        /// or 3D AudioSource worth warning about; with no clips there is nothing to play either way.</param>
        /// <param name="clipFieldHint">Inspector names of the clip fields, e.g. <c>"'Growl Clip', 'Hit Clip'"</c>.
        /// Named in the warnings so they point at a slot to fill rather than just a component.</param>
        /// <param name="ownerLabel">Name to address warnings to. An ability sitting on a child of its
        /// character passes the character's name, which is the one a designer recognises in the hierarchy;
        /// anything else leaves this empty and gets its own GameObject's name.</param>
        /// <param name="pitchVariation">Each shot is pitched up or down by up to this much. 0 = no variation.</param>
        public AudioHelper(Component owner, AudioSource source, bool expectsClips, string clipFieldHint = null,
                           string ownerLabel = null, float pitchVariation = 0f)
        {
            m_Owner = owner;
            m_OwnerLabel = ownerLabel;
            m_PitchVariation = pitchVariation;
            m_Source = source != null ? source : owner != null ? owner.GetComponent<AudioSource>() : null;

            if (m_Source == null)
            {
                if (expectsClips) WarnNoSource(clipFieldHint);
                return;
            }

            m_BasePitch = m_Source.pitch;

            if (expectsClips && m_Source.spatialBlend > k_FlatBlendLimit) WarnSpatialBlend(clipFieldHint);
        }

        /// <summary>False when there is nothing to play through, so sources built to sit alongside it
        /// (a charge loop, a swing) have nothing to route to either.</summary>
        public bool HasSource => m_Source != null;

        /// <summary>The mixer group the owner's sounds go through, for sources built beside this one so
        /// they land at the same level in the mix.</summary>
        public AudioMixerGroup OutputGroup => m_Source != null ? m_Source.outputAudioMixerGroup : null;

        /// <summary>The next shot's pitch. Only needed when the caller has to hand it somewhere this
        /// class doesn't reach, like the lifetime of a detached carrier.</summary>
        public float NextPitch()
        {
            return m_PitchVariation > 0f
                ? m_BasePitch + Random.Range(-m_PitchVariation, m_PitchVariation)
                : m_BasePitch;
        }

        /// <summary>Plays a pitch-randomised one-shot on the owner's source.</summary>
        public void Play(AudioClip clip, float volume)
        {
            PlayOn(m_Source, clip, volume);
        }

        /// <summary>
        /// The same pitch-randomised one-shot on a source the owner built for itself, one it can stop
        /// or fade without touching everything else playing on the shared source.
        /// </summary>
        public void PlayOn(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null) return;

            // Pitch lives on the source, so this also retunes anything still ringing. The shift is a
            // fraction of a semitone and these clips are all short, so the drift never reads.
            source.pitch = NextPitch();

            // Volume deliberately runs past 1: PlayOneShot's scale is a plain multiplier, and these
            // clips peak ~25 dB under the music, so 1 would leave them inaudible underneath it.
            source.PlayOneShot(clip, volume);
        }

        /// <summary>Plays one of <paramref name="clips"/>, never the one that played last.</summary>
        public void PlayRandom(AudioClip[] clips, float volume)
        {
            Play(PickRandom(clips), volume);
        }

        /// <summary>
        /// Picks from <paramref name="clips"/> without repeating the previous pick. Split out from
        /// <see cref="PlayRandom"/> for callers that have to look at the clip before playing it.
        /// </summary>
        public AudioClip PickRandom(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];

            int slot = GetCursorSlot(clips);
            int count = clips.Length;
            int lastIndex = m_LastIndices[slot];

            // Draw from every clip except the one that just played: with only a few clips, the same
            // sound twice in a row is the thing that gives the repetition away.
            bool hasPrevious = lastIndex >= 0 && lastIndex < count;
            int index = Random.Range(0, hasPrevious ? count - 1 : count);
            if (hasPrevious && index >= lastIndex) index++;

            m_LastIndices[slot] = index;
            return clips[index];
        }

        /// <summary>
        /// Plays a one-shot on a throwaway object, for sounds that have to outlive whatever raised them:
        /// an eliminated character is hidden in the same frame, a detonating projectile is recycled in the
        /// same call, and either takes its AudioSource, and anything playing on it, along.
        /// </summary>
        public void PlayDetached(AudioClip clip, float volume, float pitch, AudioMixerGroup group,
                                 Vector3 position)
        {
            if (clip == null) return;

            var carrier = new GameObject(m_Owner != null ? $"{m_Owner.name} One-Shot Audio" : "One-Shot Audio");
            carrier.transform.position = position;

            AudioSource source = carrier.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.pitch = pitch;

            // Routed like the owner's own source so it sits at the same level in the mix.
            source.outputAudioMixerGroup = group;

            // PlayOneShot rather than Play: source.volume is clamped to 1, and these clips need the same
            // past-1 boost the owner's other one-shots get.
            source.PlayOneShot(clip, volume);

            // Pitching the clip down stretches it, so the lifetime has to account for that.
            UnityEngine.Object.Destroy(carrier, clip.length / Mathf.Max(pitch, 0.01f) + 0.1f);
        }

        /// <summary>
        /// Starts the held charge loop, building its source the first time it's needed. The loop fades in
        /// and then swells as the charge builds, so the owner has to call <see cref="TickLoop"/> every frame
        /// for either to happen. Safe to call again on the next charge; it restarts from silence.
        /// </summary>
        public void StartLoop(AudioClip clip, in ChargeLoopSettings settings)
        {
            m_LoopSettings = settings;

            if (m_LoopSource == null) CreateLoopSource(clip);
            if (m_LoopSource == null) return;

            m_LoopTargetVolume = settings.volume;

            // Restart from silence every time, so the fade-in always begins where the animation does
            // even if the previous charge's tail is still ringing.
            m_LoopSource.volume = 0f;
            m_LoopSource.pitch = 1f;
            m_LoopSource.Play();
        }

        /// <summary>
        /// Advances the loop's fade, swell and pitch. Call it every frame the owner ticks, including after
        /// <see cref="StopLoop"/>, which leaves a fade-out still to run down.
        /// </summary>
        /// <param name="chargeRatio">Charge progress, 0–1. 0 when nothing is charging.</param>
        // Fades run on unscaled time: audio plays on its own clock that timeScale never touches, so a
        // hit-stop landing on the release would otherwise leave the loop hanging at half volume.
        public void TickLoop(float chargeRatio)
        {
            if (m_LoopSource == null) return;

            bool isFadingIn = m_LoopTargetVolume > 0f;
            if (!isFadingIn && !m_LoopSource.isPlaying) return;

            // Stepping across the clip's full volume range keeps the fade times honest at any volume.
            float fadeTime = isFadingIn ? m_LoopSettings.fadeInTime : m_LoopSettings.fadeOutTime;
            float step = fadeTime > 0f
                ? m_LoopSettings.volume / fadeTime * Time.unscaledDeltaTime
                : m_LoopSettings.volume;

            // The loop swells as the charge builds: its target climbs from the start fraction to full
            // volume and the fade chases that moving target, so the fade-in owns the first moments and
            // the swell carries on after it. Capped at the configured volume, not past it, since an
            // AudioSource's volume is clamped to 1, where the growth would flatten out.
            float targetVolume = isFadingIn
                ? m_LoopTargetVolume * Mathf.Lerp(m_LoopSettings.volumeAtStart, 1f, chargeRatio)
                : m_LoopTargetVolume;

            m_LoopSource.volume = Mathf.MoveTowards(m_LoopSource.volume, targetVolume, step);

            if (!isFadingIn)
            {
                // Faded all the way out, so free the voice instead of looping silence forever.
                if (m_LoopSource.volume <= 0f) m_LoopSource.Stop();
                return;
            }

            // Only while the charge is building: the fade-out tail keeps the pitch it ended on.
            if (!Mathf.Approximately(m_LoopSettings.pitchAtFullCharge, 1f))
            {
                m_LoopSource.pitch = Mathf.Lerp(1f, m_LoopSettings.pitchAtFullCharge, chargeRatio);
            }
        }

        /// <summary>
        /// Ends the charge loop, fading it out over the settings' fade-out time. Pass
        /// <paramref name="immediate"/> to cut it dead instead, which is what any caller that's about to
        /// stop ticking has to do, since a fade nothing ticks down leaves the loop stuck at the volume it
        /// had reached.
        /// </summary>
        public void StopLoop(bool immediate)
        {
            if (m_LoopSource == null) return;

            m_LoopTargetVolume = 0f;
            if (!immediate && m_LoopSettings.fadeOutTime > 0f) return;

            m_LoopSource.Stop();
            m_LoopSource.volume = 0f;
        }

        /// <summary>
        /// Destroys the generated loop source. Call it from the owner's teardown, so re-initializing doesn't
        /// stack up copies of the child object.
        /// </summary>
        public void DestroyLoop()
        {
            if (m_LoopSource == null) return;

            UnityEngine.Object.Destroy(m_LoopSource.gameObject);
            m_LoopSource = null;
            m_LoopTargetVolume = 0f;
        }

        /// <summary>
        /// Forces a clip's audio data into memory. Unity drops a play request for a clip whose data isn't
        /// loaded yet, and every clip in the template imports with "Preload Audio Data" off, so without
        /// this the first play of each sound is silent. Call it before building the helper.
        /// </summary>
        public static void WarmUp(AudioClip clip)
        {
            if (clip == null || clip.loadState == AudioDataLoadState.Loaded) return;

            clip.LoadAudioData();
        }

        /// <inheritdoc cref="WarmUp(AudioClip)"/>
        public static void WarmUp(AudioClip[] clips)
        {
            if (clips == null) return;

            foreach (AudioClip clip in clips)
            {
                WarmUp(clip);
            }
        }

        // The loop lives on a child of the owner: it needs loop and a volume that ramps, neither of which
        // the shared one-shot source can offer without dragging the one-shots along.
        void CreateLoopSource(AudioClip clip)
        {
            if (clip == null || m_Owner == null) return;

            var child = new GameObject("Charge Loop Audio");
            child.transform.SetParent(m_Owner.transform, false);

            m_LoopSource = child.AddComponent<AudioSource>();
            m_LoopSource.clip = clip;
            m_LoopSource.loop = true;
            m_LoopSource.playOnAwake = false;
            m_LoopSource.spatialBlend = 0f;
            m_LoopSource.volume = 0f;

            // Routed exactly like the one-shots so both sit at the same level in the mix.
            m_LoopSource.outputAudioMixerGroup = OutputGroup;
        }

        int GetCursorSlot(AudioClip[] clips)
        {
            for (int i = 0; i < m_TrackedCount; i++)
            {
                if (ReferenceEquals(m_TrackedClips[i], clips)) return i;
            }

            // Capped, oldest slot first: a caller handing this a freshly built array every time would
            // otherwise grow the table without bound. Past the cap the worst case is a repeated clip.
            int slot = m_NextSlot;
            m_TrackedClips[slot] = clips;
            m_LastIndices[slot] = -1;
            m_NextSlot = (m_NextSlot + 1) % k_MaxTrackedClipSets;
            if (m_TrackedCount < k_MaxTrackedClipSets) m_TrackedCount++;

            return slot;
        }

        void WarnNoSource(string clipFieldHint)
        {
            Debug.LogWarning(
                $"[{OwnerTypeName}] {OwnerName}: Audio clips are assigned but there is no AudioSource to " +
                $"play them, so they'll stay silent ({DescribeClipFields(clipFieldHint)}). Add an " +
                "AudioSource to this GameObject, or drag one into the 'Audio Source' field.",
                m_Owner);
        }

        void WarnSpatialBlend(string clipFieldHint)
        {
            Debug.LogWarning(
                $"[{OwnerTypeName}] {OwnerName}: The AudioSource's Spatial Blend is set towards 3D, so its " +
                $"sounds fade with the camera's distance and will be very quiet " +
                $"({DescribeClipFields(clipFieldHint)}). Set Spatial Blend to 2D.",
                m_Owner);
        }

        static string DescribeClipFields(string clipFieldHint)
        {
            return string.IsNullOrEmpty(clipFieldHint) ? "the assigned clips" : clipFieldHint;
        }

        string OwnerTypeName => m_Owner != null ? m_Owner.GetType().Name : nameof(AudioHelper);

        string OwnerName
        {
            get
            {
                if (!string.IsNullOrEmpty(m_OwnerLabel)) return m_OwnerLabel;

                return m_Owner != null ? m_Owner.name : "(destroyed)";
            }
        }
    }

    /// <summary>
    /// How a held charge loop sounds: how loud it is, how it swells as the charge builds, and how it fades
    /// in and out at either end. Serialized as one field so it reads as a single group in the Inspector
    /// instead of five loose sliders.
    /// </summary>
    // Not [Serializable] shorthand: this file calls Random.Range, and a `using System` would make every
    // one of those ambiguous with System.Random.
    [System.Serializable]
    public struct ChargeLoopSettings
    {
        [Tooltip("1 is the clip's own level; above that boosts it. Effect recordings are usually mastered far quieter than music, so they need the boost to be heard at all.")]
        [Range(0f, 5f)] public float volume;
        [Tooltip("Fraction of Volume the loop starts at, swelling to the full volume by full charge — below 1 the sound grows louder as the charge builds. 1 = no growth.")]
        [Range(0f, 1f)] public float volumeAtStart;
        [Tooltip("Seconds the loop takes to reach full volume. Match this to the start-of-charge animation.")]
        public float fadeInTime;
        [Tooltip("Seconds the loop takes to fade back out once the attack is released. 0 cuts it dead, which can click.")]
        public float fadeOutTime;
        [Tooltip("Pitch the loop reaches at full charge — above 1 gives a rising whine as the charge builds. 1 = no change.")]
        [Range(0.5f, 2f)] public float pitchAtFullCharge;

        /// <summary>
        /// What a newly added component starts with. A struct's own defaults are all zero, which would leave
        /// the loop silent and drag its pitch down to nothing, so every serialized field initializes to
        /// this instead.
        /// </summary>
        public static ChargeLoopSettings Default => new ChargeLoopSettings
        {
            volume = 1f,
            volumeAtStart = 0.4f,
            fadeInTime = 0.35f,
            fadeOutTime = 0.1f,
            pitchAtFullCharge = 1f
        };

        /// <summary>Clamps what the Inspector can type out of range. Call from the owner's OnValidate.</summary>
        public void Sanitize()
        {
            if (fadeInTime < 0f) fadeInTime = 0f;
            if (fadeOutTime < 0f) fadeOutTime = 0f;
        }
    }
}

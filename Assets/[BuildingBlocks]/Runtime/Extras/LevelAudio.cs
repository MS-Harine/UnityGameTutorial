using System.Collections;
using Blocks.Audio;
using Blocks.Character;
using UnityEngine;
using UnityEngine.Audio;

namespace Blocks.Extras
{
    /// <summary>
    /// The level's music and ambience in one place. It builds its own AudioSources, so the only setup
    /// is picking clips: the level music and ambience start themselves, and game code swaps tracks
    /// through <see cref="PlayMusic"/> / <see cref="ReturnToLevelMusic"/> or plays a one-shot over the
    /// top with <see cref="PlayStinger"/> (<c>WinDoor</c> uses both for its boss music and fanfare).
    /// Point it at the player and it also drops the music when they are eliminated and brings it back
    /// when they respawn, playing a cue at each end; see the Player Elimination section.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelAudio : MonoBehaviour
    {
        [Header("Mixer")]
        [SerializeField] AudioMixerGroup musicGroup;
        [SerializeField] AudioMixerGroup ambienceGroup;

        [Header("Music")]
        [SerializeField] AudioClip levelMusic;
        [SerializeField, Range(0f, 1f)] float levelMusicVolume = 1f;

        [Header("Ambience")]
        [SerializeField] AudioClip ambience;
        [SerializeField, Range(0f, 1f)] float ambienceVolume = 1f;

        [Header("Fades")]
        [Tooltip("Seconds to crossfade from one music track to another.")]
        [SerializeField, Min(0f)] float fadeDuration = 1.5f;
        [Tooltip("Seconds to fade the music down before a stinger plays over it.")]
        [SerializeField, Min(0f)] float stingerFadeOut = 0.3f;

        [Header("Player Elimination")]
        [Tooltip("The player. Drag them in and the music drops when they are eliminated, then comes back when they respawn. Leave empty to keep the music playing through it.")]
        [SerializeField] BuildingBlocksCharacter player;
        [Tooltip("Played once when the player is eliminated — the 'you died' sting. This is the musical cue; the character's own grunt belongs on its Hit Feedback component instead.")]
        [SerializeField] AudioClip eliminationCue;
        [Tooltip("Trim for the elimination cue. This can only turn a clip down, never up — so use a mastered sting here. A raw effect recording peaks far below the music and will be buried even at 1.")]
        [SerializeField, Range(0f, 1f)] float eliminationCueVolume = 0.5f;
        [Tooltip("Seconds to fade the music out once the player is eliminated. Keep it short so the cue reads over the silence.")]
        [SerializeField, Min(0f)] float eliminationFadeOut = 0.35f;
        [Tooltip("Played once when the player respawns, as the music comes back.")]
        [SerializeField] AudioClip respawnCue;
        [Tooltip("Trim for the respawn cue. Same as above — it only turns the clip down, so a quiet effect recording won't be lifted over the music.")]
        [SerializeField, Range(0f, 1f)] float respawnCueVolume = 0.5f;
        [Tooltip("Seconds to fade the music back in on respawn.")]
        [SerializeField, Min(0f)] float respawnFadeIn = 0.8f;

        // Two music sources so one track can fade in while the other fades out. m_MusicSource is
        // always the audible one; they swap roles on every crossfade.
        AudioSource m_MusicSource;
        AudioSource m_FadingSource;
        AudioSource m_AmbienceSource;
        AudioSource m_StingerSource;

        Coroutine m_CrossfadeRoutine;
        Coroutine m_StingerRoutine;
        Coroutine m_SuspendRoutine;

        float m_MusicTargetVolume;

        // The track that *should* be playing. Every music request records itself here, so a request
        // that arrives while the music is suspended can be honoured later instead of being lost.
        AudioClip m_RequestedClip;
        float m_RequestedVolume;

        bool m_IsMusicSuspended;

        void Awake()
        {
            m_MusicSource = CreateSource("Music A", musicGroup, true);
            m_FadingSource = CreateSource("Music B", musicGroup, true);
            m_AmbienceSource = CreateSource("Ambience", ambienceGroup, true);
            m_StingerSource = CreateSource("Stinger", musicGroup, false);
        }

        void Start()
        {
            if (ambience != null) Play(m_AmbienceSource, ambience, ambienceVolume);

            if (levelMusic == null) return;

            m_RequestedClip = levelMusic;
            m_RequestedVolume = levelMusicVolume;
            m_MusicTargetVolume = levelMusicVolume;
            Play(m_MusicSource, levelMusic, levelMusicVolume);
        }

        void OnEnable()
        {
            if (player == null) return;

            player.OnEliminated += HandlePlayerEliminated;
            player.OnRespawned += HandlePlayerRespawned;
        }

        void OnDisable()
        {
            if (player != null)
            {
                player.OnEliminated -= HandlePlayerEliminated;
                player.OnRespawned -= HandlePlayerRespawned;
            }

            StopRoutine(ref m_CrossfadeRoutine);
            StopRoutine(ref m_StingerRoutine);
            StopRoutine(ref m_SuspendRoutine);
        }

        /// <summary>
        /// Crossfades to <paramref name="clip"/>. Asking for the track that is already playing only
        /// retargets its volume, so a trigger the player walks in and out of won't restart it.
        /// Pass a negative <paramref name="fade"/> to use the inspector's fade duration.
        /// While the player is eliminated the request is remembered and takes effect on respawn,
        /// so nothing can start the music back up over a downed player.
        /// </summary>
        public void PlayMusic(AudioClip clip, float volume, float fade = -1f)
        {
            if (clip == null) return;

            m_RequestedClip = clip;
            m_RequestedVolume = volume;

            if (m_IsMusicSuspended) return;

            if (m_MusicSource.clip == clip && m_MusicSource.isPlaying)
            {
                m_MusicTargetVolume = volume;
                if (m_CrossfadeRoutine == null) m_MusicSource.volume = volume;
                return;
            }

            StartCrossfade(clip, volume, fade < 0f ? fadeDuration : fade);
        }

        /// <summary>Crossfades back to the level's own music.</summary>
        public void ReturnToLevelMusic(float fade = -1f)
        {
            PlayMusic(levelMusic, levelMusicVolume, fade);
        }

        /// <summary>
        /// Fades the music out, plays <paramref name="clip"/> once over the ambience, then brings the
        /// level music back. A null clip just returns to the level music.
        /// </summary>
        public void PlayStinger(AudioClip clip, float volume)
        {
            if (clip == null)
            {
                ReturnToLevelMusic();
                return;
            }

            StopRoutine(ref m_StingerRoutine);
            m_StingerRoutine = StartCoroutine(StingerRoutine(clip, volume));
        }

        void HandlePlayerEliminated()
        {
            // The cue goes out first so it lands on the beat of the hit, with the music ducking under it.
            PlayCue(eliminationCue, eliminationCueVolume);

            // A player who never comes back never raises OnRespawned, and suspending here would leave
            // the level silent for good, so only the respawning case takes the music away.
            if (player != null && player.RespawnsAfterElimination) SuspendMusic(eliminationFadeOut);
        }

        void HandlePlayerRespawned()
        {
            PlayCue(respawnCue, respawnCueVolume);
            ResumeMusic(respawnFadeIn);
        }

        // Fades the music out and keeps it out: music requests still arrive while the player is down
        // (WinDoor switches back to the level track a few seconds after they leave its trigger, which
        // lands right on top of a respawn), and they are recorded rather than played.
        void SuspendMusic(float fade)
        {
            m_IsMusicSuspended = true;

            StopRoutine(ref m_SuspendRoutine);
            m_SuspendRoutine = StartCoroutine(SuspendRoutine(fade));
        }

        void ResumeMusic(float fade)
        {
            if (!m_IsMusicSuspended) return;

            m_IsMusicSuspended = false;
            StopRoutine(ref m_SuspendRoutine);

            // Whatever was last asked for, which is the level music unless something switched tracks
            // while the player was down.
            PlayMusic(m_RequestedClip, m_RequestedVolume, fade);
        }

        IEnumerator SuspendRoutine(float fade)
        {
            yield return FadeMusicOut(fade);

            m_SuspendRoutine = null;
        }

        // A one-shot over the top of whatever is playing, music untouched: the difference from
        // PlayStinger, which ducks the music for its clip and then brings it back.
        void PlayCue(AudioClip clip, float volume)
        {
            if (clip == null) return;

            Play(m_StingerSource, clip, volume);
        }

        AudioSource CreateSource(string sourceName, AudioMixerGroup group, bool isLooping)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);

            AudioSource source = child.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = group;
            source.loop = isLooping;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 0;
            return source;
        }

        void StartCrossfade(AudioClip clip, float volume, float duration)
        {
            if (m_CrossfadeRoutine != null)
            {
                StopRoutine(ref m_CrossfadeRoutine);

                // The previous track is still mid-fade and its source is the one about to carry the new
                // clip, so it has to be cut. Only reachable when tracks change faster than a fade.
                Silence(m_FadingSource);
            }

            AudioSource outgoing = m_MusicSource;
            AudioSource incoming = m_FadingSource;

            m_MusicSource = incoming;
            m_FadingSource = outgoing;

            Play(incoming, clip, 0f);

            m_MusicTargetVolume = volume;
            m_CrossfadeRoutine = StartCoroutine(CrossfadeRoutine(incoming, outgoing, duration));
        }

        // Unscaled: audio runs on its own clock, so a pause or hit-stop mid-crossfade would leave a
        // track stuck at half volume.
        IEnumerator CrossfadeRoutine(AudioSource incoming, AudioSource outgoing, float duration)
        {
            float outgoingStartVolume = outgoing.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

                incoming.volume = Mathf.Lerp(0f, m_MusicTargetVolume, progress);
                outgoing.volume = Mathf.Lerp(outgoingStartVolume, 0f, progress);
                yield return null;
            }

            incoming.volume = m_MusicTargetVolume;
            Silence(outgoing);

            m_CrossfadeRoutine = null;
        }

        IEnumerator StingerRoutine(AudioClip clip, float volume)
        {
            yield return FadeMusicOut(stingerFadeOut);

            PlayCue(clip, volume);

            // Real time for the same reason as the fades: the clip finishes on the audio clock, so a
            // scaled wait would bring the music back over the top of it.
            yield return new WaitForSecondsRealtime(clip.length);

            m_StingerRoutine = null;
            ReturnToLevelMusic();
        }

        IEnumerator FadeMusicOut(float duration)
        {
            if (m_CrossfadeRoutine != null)
            {
                StopRoutine(ref m_CrossfadeRoutine);
                Silence(m_FadingSource);
            }

            float startVolume = m_MusicSource.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

                m_MusicSource.volume = Mathf.Lerp(startVolume, 0f, progress);
                yield return null;
            }

            Silence(m_MusicSource);
        }

        void StopRoutine(ref Coroutine routine)
        {
            if (routine == null) return;

            StopCoroutine(routine);
            routine = null;
        }

        // AudioSource.Play() is dropped on the floor when the clip's audio data isn't in memory yet, and
        // "Preload Audio Data" is off by default for the long clips music and ambience use, so the load
        // has to be forced first or the track silently never starts.
        static void Play(AudioSource source, AudioClip clip, float volume)
        {
            AudioHelper.WarmUp(clip);

            source.clip = clip;
            source.volume = volume;
            source.Play();
        }

        static void Silence(AudioSource source)
        {
            source.volume = 0f;
            source.Stop();
            source.clip = null;
        }

        void OnValidate()
        {
            if (player == null && (eliminationCue != null || respawnCue != null))
            {
                Debug.LogWarning(
                    $"[LevelAudio] {name}: Elimination cues are assigned but 'Player' is empty, so none of them will play and the music will keep going when the player goes down. Drag the player in, or clear the clips.",
                    this);
            }

            // The music deliberately keeps playing for a character that never comes back (see
            // HandlePlayerEliminated), so only the clip that can never play is worth warning about.
            if (player != null && !player.RespawnsAfterElimination && respawnCue != null)
            {
                Debug.LogWarning(
                    $"[LevelAudio] {name}: '{player.name}' doesn't respawn, so 'Respawn Cue' will never play and the music keeps running through its elimination. Set its elimination behavior to Respawn, or clear the clip.",
                    this);
            }
        }
    }
}

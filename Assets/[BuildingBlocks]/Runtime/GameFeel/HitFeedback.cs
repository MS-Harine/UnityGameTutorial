using UnityEngine;
using Blocks.Audio;
using Blocks.Character;

namespace Blocks.GameFeel
{
    /// <summary>
    /// Sounds for taking a hit: a clip when the character is damaged and another when it is
    /// eliminated. Give either one several clips and it picks between them, so repeated hits on the
    /// same enemy don't sound identical. Drop it on any character; the counterpart to
    /// <see cref="AttackFeedback"/>, which covers dealing the hit rather than receiving it.
    /// </summary>
    [DisallowMultipleComponent]
    public class HitFeedback : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("Plays the hit sounds. Leave empty to use the AudioSource on this GameObject.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Played when the character takes damage. One is picked at random per hit, so a handful keeps a drawn-out fight from sounding looped. A single clip plays every time.")]
        [SerializeField] AudioClip[] hitClips;
        [Tooltip("1 is the clip's own level; above that boosts it. Effect recordings are usually mastered far quieter than music, so they need the boost to be heard at all.")]
        [SerializeField][Range(0f, 5f)] float hitVolume = 2.5f;
        [Tooltip("Played when the character is eliminated, picked at random the same way. This plays on top of the hit sound, since the final blow raises both.")]
        [SerializeField] AudioClip[] eliminationClips;
        [SerializeField][Range(0f, 5f)] float eliminationVolume = 2.5f;
        [Tooltip("Each sound is pitched up or down by up to this much, so repeats don't sound identical. 0 = no variation.")]
        [SerializeField][Range(0f, 0.5f)] float pitchVariation = 0.08f;

        BuildingBlocksCharacter m_Character;
        AudioHelper m_Audio;

        void Awake()
        {
            // Search upward so this can sit on the root next to the AudioSource, or on a visuals child.
            m_Character = GetComponentInParent<BuildingBlocksCharacter>();

            if (m_Character == null)
            {
                Debug.LogWarning(
                    $"[HitFeedback] {name}: No BuildingBlocksCharacter on this GameObject or its parents — hit sounds will not play.",
                    this);
            }

            AudioHelper.WarmUp(hitClips);
            AudioHelper.WarmUp(eliminationClips);

            // Elimination sounds play on their own object, so they survive a missing source, but they lose
            // the mixer routing, and the damage sounds have nothing to play through at all.
            m_Audio = new AudioHelper(this, audioSource, HasAny(hitClips) || HasAny(eliminationClips),
                                      clipFieldHint: "'Hit Clips', 'Elimination Clips'",
                                      pitchVariation: pitchVariation);
        }

        void OnEnable()
        {
            if (m_Character == null) return;
            m_Character.OnDamaged += HandleDamaged;
            m_Character.OnEliminated += HandleEliminated;
        }

        void OnDisable()
        {
            if (m_Character == null) return;
            m_Character.OnDamaged -= HandleDamaged;
            m_Character.OnEliminated -= HandleEliminated;
        }

        void HandleDamaged(DamageInfo _)
        {
            PlayRandom(hitClips, hitVolume);
        }

        void HandleEliminated()
        {
            PlayRandom(eliminationClips, eliminationVolume);
        }

        void PlayRandom(AudioClip[] clips, float volume)
        {
            AudioClip clip = m_Audio.PickRandom(clips);
            if (clip == null) return;

            // An eliminated character is destroyed or hidden in the same frame the events fire, taking
            // its AudioSource, and anything playing on it, with it. The final hit and the elimination
            // sound both have to outlive the body, so they play on a throwaway object instead.
            if (m_Character != null && m_Character.IsEliminated)
            {
                m_Audio.PlayDetached(clip, volume, m_Audio.NextPitch(), m_Audio.OutputGroup,
                                     transform.position);
                return;
            }

            m_Audio.Play(clip, volume);
        }

        static bool HasAny(AudioClip[] clips)
        {
            if (clips == null) return false;

            foreach (AudioClip clip in clips)
            {
                if (clip != null) return true;
            }

            return false;
        }
    }
}

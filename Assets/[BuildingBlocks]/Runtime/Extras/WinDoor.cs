using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Blocks.Character;

namespace Blocks.Extras
{
    /// <summary>
    /// Win condition trigger, fires once: opens the door's Animator, raises the player's victory
    /// (animation + sound via <see cref="BuildingBlocksCharacter.NotifyVictory"/>), and briefly holds
    /// the player still before handing control back.
    /// Optionally stays locked until every character in <see cref="requiredEliminations"/> is defeated.
    /// While it is locked, entering the trigger can also swap the level music for a fight track and
    /// play a fanfare on the win; see the Audio section.
    /// </summary>
    /// <remarks>
    /// Two names are fixed in code rather than typed into the inspector, where a typo would fail
    /// silently: only characters tagged <c>Player</c> can open the door, and the Animator on this
    /// object is opened by a Trigger parameter named <c>win</c>.
    /// </remarks>
    [RequireComponent(typeof(Collider2D), typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class WinDoor : MonoBehaviour
    {
        // Unity's built-in Player tag, so CompareTag can never throw the way an undefined custom tag
        // would. Tagging the player is how the whole template tells them apart from enemies.
        const string k_PlayerTag = "Player";

        static readonly int k_AnimIDOpen = Animator.StringToHash("win");

        [Header("Win Condition")]
        [Tooltip("The door stays locked until every character in this list is defeated. Leave empty to keep the door always unlocked.")]
        [SerializeField] List<BuildingBlocksCharacter> requiredEliminations = new();
        [Tooltip("How long the player holds the victory pose before regaining control. Set to 0 to keep control the whole time.")]
        [SerializeField, Min(0f)] float victoryPauseDuration = 1f;

        [Header("Audio")]
        [Tooltip("The scene's Level Audio object. Leave empty to keep the door silent.")]
        [SerializeField] LevelAudio levelAudio;
        [Tooltip("Plays while the door is locked and the player is inside — the fight music.")]
        [SerializeField] AudioClip areaMusic;
        [Tooltip("Trim for the fight music. Tracks are rarely mastered at the same level as the level music.")]
        [SerializeField, Range(0f, 1f)] float areaMusicVolume = 0.27f;
        [Tooltip("Played once when the door opens, over the top of the music.")]
        [SerializeField] AudioClip victoryStinger;
        [SerializeField, Range(0f, 1f)] float victoryStingerVolume = 0.4f;
        [Tooltip("How long the player must stay out of the trigger before the fight music switches back.")]
        [SerializeField, Min(0f)] float musicRetreatDelay = 3f;

        Animator m_Animator;
        bool m_HasOpenParameter;
        bool m_HasBeenOpened;
        Coroutine m_VictoryPauseRoutine;

        bool m_IsAreaMusicPlaying;
        Coroutine m_MusicRetreatRoutine;

        // Every character this door hooked OnEliminated on, so teardown can unhook exactly those.
        // m_RemainingCharacters shrinks as they fall, so it can't answer that question by itself.
        readonly List<BuildingBlocksCharacter> m_SubscribedCharacters = new();

        // Characters from requiredEliminations that are still alive. Defeats are permanent for the
        // door: a character that respawns after dying is not re-added, so the door never re-locks.
        readonly HashSet<BuildingBlocksCharacter> m_RemainingCharacters = new();

        // The player currently standing in the trigger, so the door can open the moment the last
        // required character falls without the player having to step out and back in.
        BuildingBlocksCharacter m_PlayerInTrigger;

        void Awake()
        {
            m_Animator = GetComponent<Animator>();
            m_HasOpenParameter = HasOpenParameter();

            if (!m_HasOpenParameter)
            {
                Debug.LogWarning(
                    $"[WinDoor] {name}: The Animator has no Trigger parameter named 'win', so the door will never animate open. Add one to its controller — the template's WinDoor.controller already has it.",
                    this);
            }

            foreach (BuildingBlocksCharacter character in requiredEliminations)
            {
                if (character == null || character.IsEliminated) continue;
                if (!m_RemainingCharacters.Add(character)) continue;

                character.OnEliminated += HandleRequiredCharacterEliminated;
                m_SubscribedCharacters.Add(character);

                if (character.RespawnsAfterElimination)
                {
                    Debug.LogWarning(
                        $"[WinDoor] {name}: '{character.name}' respawns after being defeated. The door counts its first defeat and stays unlocked when it comes back. Set its elimination behavior to Destroy or Disable if that's not what you want.",
                        this);
                }
            }
        }

        void OnDestroy()
        {
            UnsubscribeFromRequiredCharacters();

            if (m_VictoryPauseRoutine != null) StopCoroutine(m_VictoryPauseRoutine);

            CancelMusicRetreat();
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (m_HasBeenOpened) return;

            BuildingBlocksCharacter character = other.GetComponentInParent<BuildingBlocksCharacter>();
            if (character == null || character.IsEliminated || !character.CompareTag(k_PlayerTag)) return;

            m_PlayerInTrigger = character;
            CancelMusicRetreat();

            PruneDefeatedCharacters();
            if (m_RemainingCharacters.Count == 0)
            {
                // Nothing left to fight, so skip the fight music and go straight to the fanfare.
                Open(character);
                return;
            }

            PlayAreaMusic();
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (m_PlayerInTrigger == null) return;

            BuildingBlocksCharacter character = other.GetComponentInParent<BuildingBlocksCharacter>();
            if (character != m_PlayerInTrigger) return;

            m_PlayerInTrigger = null;

            if (m_IsAreaMusicPlaying && m_MusicRetreatRoutine == null)
            {
                m_MusicRetreatRoutine = StartCoroutine(MusicRetreatRoutine());
            }
        }

        void HandleRequiredCharacterEliminated()
        {
            if (m_HasBeenOpened) return;

            PruneDefeatedCharacters();
            if (m_RemainingCharacters.Count != 0) return;

            // Going down in the trigger on the same blow that finishes the last enemy must not win the
            // level: a victory pose handed to a character the respawn machinery is already driving
            // would fight it. They walk back in once they are up, and the door opens then.
            if (m_PlayerInTrigger == null || m_PlayerInTrigger.IsEliminated) return;

            Open(m_PlayerInTrigger);
        }

        // Characters destroyed without dying (e.g. deleted by another script) never raise
        // OnEliminated, so re-check the whole set instead of trusting events alone.
        void PruneDefeatedCharacters()
        {
            m_RemainingCharacters.RemoveWhere(character => character == null || character.IsEliminated);
        }

        void Open(BuildingBlocksCharacter character)
        {
            m_HasBeenOpened = true;
            if (m_HasOpenParameter) m_Animator.SetTrigger(k_AnimIDOpen);

            // The fanfare owns the music from here, so drop any pending switch back to the level track.
            CancelMusicRetreat();
            m_IsAreaMusicPlaying = false;
            if (levelAudio != null) levelAudio.PlayStinger(victoryStinger, victoryStingerVolume);

            character.NotifyVictory();
            if (victoryPauseDuration > 0f) m_VictoryPauseRoutine = StartCoroutine(VictoryPauseRoutine(character));

            UnsubscribeFromRequiredCharacters();
        }

        // Holds the player still long enough to read the victory pose, then gives control back so the
        // level stays playable. Gravity keeps settling the player while paused.
        IEnumerator VictoryPauseRoutine(BuildingBlocksCharacter character)
        {
            character.Pause();

            yield return new WaitForSeconds(victoryPauseDuration);

            m_VictoryPauseRoutine = null;

            // Don't hand movement back to a player who died or was destroyed during the pause.
            if (character == null || character.IsEliminated) yield break;

            character.Resume();
        }

        void PlayAreaMusic()
        {
            if (levelAudio == null || areaMusic == null) return;

            levelAudio.PlayMusic(areaMusic, areaMusicVolume);
            m_IsAreaMusicPlaying = true;
        }

        // Leaving the fight switches the music back, but only after a delay: brushing the edge of the
        // trigger shouldn't flap between tracks, and stepping back in cancels the switch entirely.
        IEnumerator MusicRetreatRoutine()
        {
            yield return new WaitForSeconds(musicRetreatDelay);

            m_MusicRetreatRoutine = null;
            m_IsAreaMusicPlaying = false;

            if (levelAudio != null) levelAudio.ReturnToLevelMusic();
        }

        void CancelMusicRetreat()
        {
            if (m_MusicRetreatRoutine == null) return;

            StopCoroutine(m_MusicRetreatRoutine);
            m_MusicRetreatRoutine = null;
        }

        void UnsubscribeFromRequiredCharacters()
        {
            foreach (BuildingBlocksCharacter character in m_SubscribedCharacters)
            {
                if (character != null) character.OnEliminated -= HandleRequiredCharacterEliminated;
            }

            m_SubscribedCharacters.Clear();
        }

        bool HasOpenParameter()
        {
            if (m_Animator == null) return false;

            foreach (AnimatorControllerParameter parameter in m_Animator.parameters)
            {
                if (parameter.nameHash == k_AnimIDOpen) return true;
            }

            return false;
        }

        void OnValidate()
        {
            Collider2D ownedCollider = GetComponent<Collider2D>();
            if (ownedCollider != null && !ownedCollider.isTrigger)
            {
                Debug.LogWarning(
                    $"[WinDoor] {name}: The Collider2D is not set as a trigger — the player will bump into the door instead of winning. Enable 'Is Trigger'.",
                    this);
            }

            if (requiredEliminations.Contains(null))
            {
                Debug.LogWarning(
                    $"[WinDoor] {name}: 'Required Eliminations' has an empty entry — assign a character or remove it. Empty entries are ignored.",
                    this);
            }

            if (levelAudio == null && (areaMusic != null || victoryStinger != null))
            {
                Debug.LogWarning(
                    $"[WinDoor] {name}: Music clips are assigned but 'Level Audio' is empty, so none of them will play. Drag the scene's Level Audio object in, or clear the clips.",
                    this);
            }

            if (areaMusic != null && requiredEliminations.Count == 0)
            {
                Debug.LogWarning(
                    $"[WinDoor] {name}: 'Area Music' is set but 'Required Eliminations' is empty, so the door opens on entry and the fight music never plays. Add the characters to defeat, or clear the clip.",
                    this);
            }
        }
    }
}

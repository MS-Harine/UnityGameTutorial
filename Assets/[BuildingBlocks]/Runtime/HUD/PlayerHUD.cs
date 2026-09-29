using System;
using System.Collections.Generic;
using Blocks.Attack;
using Blocks.Character;
using Blocks.GameFeel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Blocks.Network;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.HUD
{
    /// <summary>
    /// The player's whole screen: the stat bars and respawn text, the corner hint telling the player how
    /// to pause, and the pause menu itself, all in one UIDocument, so nothing has to be wired between them.
    ///
    /// Pausing is <c>Time.timeScale = 0</c>, which stops every character, platform, projectile, animation
    /// and particle in the scene at once. The fades run on unscaled time so they still animate while the
    /// game is frozen.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public class PlayerHUD : MonoBehaviour
    {
        const string k_HudLayerName = "hud-layer";
        const string k_StackName = "stat-bar-stack";
        const string k_RespawnOverlayName = "respawn-overlay";
        const string k_RespawnCountdownName = "respawn-countdown";
        const string k_PauseHintKeyName = "pause-hint-key";
        const string k_AttackSlotsName = "attack-slots";
        const string k_RemoteHudContainerName = "remote-hud-container";

        static readonly Color[] s_RemoteAvatarTints = new[]
        {
            new Color(0.55f, 0.85f, 1f),      // Light Blue
            new Color(0.6f, 0.95f, 0.7f),     // Emerald Green
            new Color(1f, 0.85f, 0.45f),      // Golden Amber
            new Color(0.9f, 0.65f, 1f),       // Orchid Purple
            new Color(1f, 0.65f, 0.65f),      // Coral Pink
            new Color(0.5f, 1f, 0.95f),       // Cyan Aqua
        };

        [Header("References")]
        [SerializeField] BuildingBlocksCharacter character;

        [Header("Remote Players")]
        [Tooltip("Initial remote characters to observe in the HUD (useful for testing in Editor).")]
        [SerializeField] List<BuildingBlocksCharacter> remoteCharacters = new();
        [SerializeField, HideInInspector] BuildingBlocksCharacter remoteCharacter;

        [Header("Pause")]
        [Tooltip("The action that opens and closes the menu. Drag the 'PauseMenu' action out of " +
                 "InputSystem_Actions. The map it belongs to is the one silenced while paused, and the " +
                 "key it is bound to is the one the corner hint shows.")]
        [SerializeField] InputActionReference pauseAction;

        [Tooltip("The columns of the menu's controls list. One section per column, one row per line — " +
                 "the menu builds itself from these.")]
        [SerializeField] ControlSection[] controlSections;

        [Header("Fade")]
        [Tooltip("Seconds to cross the HUD out and the menu in. Runs on unscaled time, so it still " +
                 "animates while the game is frozen.")]
        [SerializeField, Min(0f)] float fadeDuration = 0.15f;

        public static PlayerHUD Instance { get; private set; }
        public BuildingBlocksCharacter LocalCharacter => character;

        UIDocument m_UIDocument;
        VisualElement m_HudLayer;
        StatBarStack m_Stack;
        PauseMenuView m_Menu;
        AttackSlotsView m_AttackSlots;
        VisualElement m_RespawnOverlay;
        Label m_RespawnCountdown;
        InputActionMap m_GameplayMap;
        PlayerAttackAbility m_Attack;

        VisualElement m_RemoteHudContainer;
        readonly List<RemotePlayerHudEntry> m_RemoteEntries = new();

        bool m_IsPaused;
        float m_HudOpacity = 1f;
        float m_MenuOpacity;

        /// <summary>
        /// True while the game is frozen and the menu is on screen. Nothing here reads it, but pause is
        /// owned by this component, so this pair is the route for game code that needs to know: a
        /// spawner that must not tick while paused, say.
        /// </summary>
        public bool IsPaused => m_IsPaused;

        /// <summary>Raised when pause is entered (true) or left (false). No subscriber in the sample.</summary>
        public event Action<bool> OnPauseChanged;

        void Awake()
        {
            Instance = this;
            m_UIDocument = GetComponent<UIDocument>();

            if (character != null && CharacterManager.Instance != null && CharacterManager.Instance.LocalCharacter == null)
            {
                CharacterManager.Instance.RegisterLocalCharacter(CharacterManager.InvalidPlayerId, character);
            }

            if (character == null)
            {
                Debug.LogWarning(
                    $"[PlayerHUD] {name}: BuildingBlocksCharacter not assigned — the stat bars and " +
                    "respawn text stay off. Drag the character into the Character field.",
                    this);
            }

            // Found rather than wired: the slots build themselves from whichever attack ability the
            // character has, and a character without one simply gets no slots.
            if (character != null) m_Attack = character.GetComponentInChildren<PlayerAttackAbility>();

            if (pauseAction == null || pauseAction.action == null)
            {
                Debug.LogWarning(
                    $"[PlayerHUD] {name}: No Pause Action assigned — the game can never be paused. " +
                    "Drag the 'PauseMenu' action from InputSystem_Actions into the Pause Action field.",
                    this);
                return;
            }

            // The pause action lives in the gameplay map, so it also tells us which map to silence.
            m_GameplayMap = pauseAction.action.actionMap;
        }

        void OnDestroy()
        {
            ClearRemoteCharacters();
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            if (pauseAction != null && pauseAction.action != null)
            {
                pauseAction.action.performed += HandlePausePressed;
                pauseAction.action.Enable();
            }

            // Nothing is built yet on the first enable; Start does that. On every later one the views
            // already exist and have to be hooked up again, because OnDisable unhooked them. Both Bind
            // calls ignore a second call, so this is safe either way.
            m_Stack?.Bind();
            for (int i = 0; i < m_RemoteEntries.Count; i++)
            {
                m_RemoteEntries[i].Bind();
            }
            if (m_Menu != null) m_Menu.Bind();

            if (character != null)
            {
                character.OnRespawning += HandleRespawning;
                character.OnRespawned += HandleRespawned;
            }
        }

        void Start()
        {
            BuildUi();
        }

        void OnDisable()
        {
            // Leaving play mode or unloading the scene mid-pause must not strand the game at scale 0.
            if (m_IsPaused) SetPaused(false);

            if (pauseAction != null && pauseAction.action != null)
            {
                pauseAction.action.performed -= HandlePausePressed;

                // Not calling Disable(): the reference is shared, so switching it off here
                // would steal input from anything else still bound to it.
            }

            m_Menu?.Unbind();

            if (character != null)
            {
                character.OnRespawning -= HandleRespawning;
                character.OnRespawned -= HandleRespawned;
            }

            m_Stack?.Unbind();
            for (int i = 0; i < m_RemoteEntries.Count; i++)
            {
                m_RemoteEntries[i].Unbind();
            }
        }

        void Update()
        {
            // Unscaled: pausing sets timeScale to 0, so the normal clock is stopped for both fades.
            float step = fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f;
            UpdateHudFade(step);
            UpdateMenuFade(step);
            UpdateAttackSlots();
        }

        /// <summary>
        /// Freezes or resumes the game, and crosses the HUD and the menu over. Safe to call from anywhere.
        /// </summary>
        public void SetPaused(bool isPaused)
        {
            if (m_IsPaused == isPaused) return;
            m_IsPaused = isPaused;

            // Restores to 1 rather than a remembered value: hit-stop is the only other writer of
            // timeScale in the template, and pausing during one would otherwise remember its 0.
            Time.timeScale = isPaused ? 0f : 1f;

            // Hit-stop drives timeScale too, so tell it to keep its hands off while we own the clock.
            HitStop.IsSuspended = isPaused;

            SetGameplayInputEnabled(!isPaused);

            if (isPaused)
            {
                // Laid out before the fade starts; taken back out once it has faded fully clear.
                m_Menu?.SetVisible(true);
                m_Menu?.ClearFocus();
            }

            OnPauseChanged?.Invoke(isPaused);
        }

        /// <summary>Closes the game. Wired to the menu's Quit button.</summary>
        public void Quit()
        {
            // Unpaused first: timeScale and the gameplay map are global, and leaving them frozen would
            // greet the next play session with a dead game.
            SetPaused(false);

#if UNITY_EDITOR
            // Application.Quit() does nothing in play mode, which would make the button look broken.
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void ConnectToServer(string ip, string port, string playerName = null)
        {
            if (PacketManager.Instance != null)
            {
                PacketManager.Instance.LocalUsername = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
            }
            _ = NetworkManager.Instance.Connect(ip, int.Parse(port));
        }

        public void ConnectToServer(string ip, string port) => ConnectToServer(ip, port, null);

        void BuildUi()
        {
            if (m_UIDocument == null) return;

            VisualElement root = m_UIDocument.rootVisualElement;
            if (root == null) return;

            m_HudLayer = root.Q<VisualElement>(k_HudLayerName);

            m_Menu = new PauseMenuView(root);
            if (m_Menu.IsValid)
            {
                m_Menu.Build(controlSections);
                m_Menu.Bind();
                m_Menu.ResumeRequested += HandleResumeRequested;
                m_Menu.QuitRequested += Quit;
                m_Menu.ConnectRequested += ConnectToServer;
                m_Menu.SetOpacity(m_MenuOpacity);
            }
            else
            {
                Debug.LogWarning(
                    $"[PlayerHUD] {name}: No 'pause-menu' element in the UIDocument, so there is no menu " +
                    "to show. Check that the Source Asset is PlayerHUD.uxml.",
                    this);
            }

            ApplyHudOpacity();
            FillPauseHint(root);
            BuildAttackSlots(root);

            m_RespawnOverlay = root.Q<VisualElement>(k_RespawnOverlayName);
            m_RespawnCountdown = root.Q<Label>(k_RespawnCountdownName);
            HideRespawnOverlay();

            m_RemoteHudContainer = root.Q<VisualElement>(k_RemoteHudContainerName);
            ClearRemoteCharacters();

            for (int i = 0; i < remoteCharacters.Count; i++)
            {
                if (remoteCharacters[i] != null)
                {
                    AddRemoteCharacter(remoteCharacters[i]);
                }
            }

            if (character != null)
            {
                VisualElement container = root.Q<VisualElement>(k_StackName);
                if (container == null)
                {
                    Debug.LogWarning($"[PlayerHUD] '{k_StackName}' not found in UIDocument.", this);
                }
                else
                {
                    m_Stack = new StatBarStack(character, container);
                    m_Stack.Build();
                    m_Stack.Bind();
                }
            }
        }

        void UpdateHudFade(float step)
        {
            if (m_HudLayer == null) return;

            float target = m_IsPaused ? 0f : 1f;
            if (Mathf.Approximately(m_HudOpacity, target)) return;

            m_HudOpacity = Mathf.MoveTowards(m_HudOpacity, target, step);
            ApplyHudOpacity();
        }

        void UpdateMenuFade(float step)
        {
            if (m_Menu == null || !m_Menu.IsValid) return;

            float target = m_IsPaused ? 1f : 0f;
            if (Mathf.Approximately(m_MenuOpacity, target)) return;

            m_MenuOpacity = Mathf.MoveTowards(m_MenuOpacity, target, step);
            m_Menu.SetOpacity(m_MenuOpacity);

            // Only now is it safe to drop out of the layout; while fading it still has to be drawn.
            if (m_MenuOpacity <= 0f) m_Menu.SetVisible(false);
        }

        void ApplyHudOpacity()
        {
            if (m_HudLayer == null) return;
            m_HudLayer.style.opacity = m_HudOpacity;
        }

        /// <summary>
        /// Writes the pause key onto the corner hint. Left as authored in the .uxml when no action is
        /// assigned, so the hint still reads correctly with nothing wired up.
        /// </summary>
        void FillPauseHint(VisualElement root)
        {
            if (pauseAction == null || pauseAction.action == null) return;

            Label keyLabel = root.Q<Label>(k_PauseHintKeyName);
            if (keyLabel == null) return;

            string display = InputBindingText.FirstBinding(pauseAction.action);
            if (!string.IsNullOrEmpty(display)) keyLabel.text = display;
        }

        void BuildAttackSlots(VisualElement root)
        {
            VisualElement container = root.Q<VisualElement>(k_AttackSlotsName);
            if (container == null) return;

            m_AttackSlots = new AttackSlotsView(container);
            m_AttackSlots.Build(m_Attack);
        }

        void UpdateAttackSlots()
        {
            if (m_AttackSlots == null || m_Attack == null || character == null) return;

            // Grounded is passed as availability because the attacks refuse to start in the air, and
            // nothing else on screen says so: the rings dim instead of the press vanishing silently.
            m_AttackSlots.Refresh(m_Attack.ChargingAttack, m_Attack.CurrentChargeRatio, character.IsGrounded);
        }

        /// <summary>
        /// Silences Move/Jump/Attack while paused, so a press behind the menu doesn't fire the instant the
        /// game resumes. The pause action is switched back on by itself so it can still close the menu.
        /// </summary>
        void SetGameplayInputEnabled(bool isEnabled)
        {
            if (m_GameplayMap == null) return;

            if (isEnabled)
            {
                m_GameplayMap.Enable();
                return;
            }

            m_GameplayMap.Disable();
            pauseAction.action.Enable();
        }

        void HandlePausePressed(InputAction.CallbackContext context) => SetPaused(!m_IsPaused);

        void HandleResumeRequested() => SetPaused(false);

        void HandleRespawning(float secondsRemaining)
        {
            if (m_RespawnCountdown != null)
            {
                m_RespawnCountdown.text = Mathf.CeilToInt(secondsRemaining).ToString();
            }

            if (m_RespawnOverlay != null) m_RespawnOverlay.style.display = DisplayStyle.Flex;
        }

        void HandleRespawned()
        {
            HideRespawnOverlay();
            m_Stack?.RefreshAll();
        }

        void HideRespawnOverlay()
        {
            if (m_RespawnOverlay == null) return;
            m_RespawnOverlay.style.display = DisplayStyle.None;
        }

        #region Remote Players Management

        /// <summary>
        /// Read-only access to all current remote player entries.
        /// </summary>
        public IReadOnlyList<RemotePlayerHudEntry> RemotePlayers => m_RemoteEntries;

        /// <summary>
        /// Adds a remote character to the HUD list.
        /// </summary>
        /// <param name="remoteChar">The character to track.</param>
        /// <param name="displayName">Display name (defaults to character name or "Player {N}").</param>
        /// <param name="id">Optional unique ID (e.g. client ID or player index). Defaults to character.</param>
        /// <returns>The created RemotePlayerHudEntry.</returns>
        public RemotePlayerHudEntry AddRemoteCharacter(
            BuildingBlocksCharacter remoteChar,
            string displayName = null,
            object id = null)
        {
            if (remoteChar == null || m_RemoteHudContainer == null) return null;

            id ??= remoteChar;

            // If an entry with this ID already exists, remove it first
            RemoveRemotePlayer(id);

            int playerIndex = m_RemoteEntries.Count;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = !string.IsNullOrEmpty(remoteChar.name) ? remoteChar.name : $"Player {playerIndex + 2}";
            }

            Color tint = s_RemoteAvatarTints[playerIndex % s_RemoteAvatarTints.Length];
            var entry = new RemotePlayerHudEntry(id, remoteChar, displayName, m_RemoteHudContainer, tint);
            m_RemoteEntries.Add(entry);

            m_RemoteHudContainer.style.display = DisplayStyle.Flex;
            return entry;
        }

        /// <summary>
        /// Removes a remote character by character reference.
        /// </summary>
        public bool RemoveRemoteCharacter(BuildingBlocksCharacter remoteChar)
        {
            if (remoteChar == null) return false;
            for (int i = m_RemoteEntries.Count - 1; i >= 0; i--)
            {
                if (m_RemoteEntries[i].Character == remoteChar)
                {
                    m_RemoteEntries[i].Destroy();
                    m_RemoteEntries.RemoveAt(i);
                    if (m_RemoteEntries.Count == 0 && m_RemoteHudContainer != null)
                    {
                        m_RemoteHudContainer.style.display = DisplayStyle.None;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes a remote player by ID.
        /// </summary>
        public bool RemoveRemotePlayer(object id)
        {
            if (id == null) return false;
            for (int i = m_RemoteEntries.Count - 1; i >= 0; i--)
            {
                if (Equals(m_RemoteEntries[i].Id, id))
                {
                    m_RemoteEntries[i].Destroy();
                    m_RemoteEntries.RemoveAt(i);
                    if (m_RemoteEntries.Count == 0 && m_RemoteHudContainer != null)
                    {
                        m_RemoteHudContainer.style.display = DisplayStyle.None;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Clears all remote player entries from the HUD.
        /// </summary>
        public void ClearRemoteCharacters()
        {
            for (int i = 0; i < m_RemoteEntries.Count; i++)
            {
                m_RemoteEntries[i].Destroy();
            }
            m_RemoteEntries.Clear();
            if (m_RemoteHudContainer != null)
            {
                m_RemoteHudContainer.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// Sets a single remote character, clearing any previous ones.
        /// Provided for simple 1v1 scenarios and backwards compatibility.
        /// </summary>
        public void SetRemoteCharacter(BuildingBlocksCharacter remoteChar, string displayName = "Player 2")
        {
            ClearRemoteCharacters();
            if (remoteChar != null)
            {
                AddRemoteCharacter(remoteChar, displayName);
            }
        }

        /// <summary>
        /// Toggles visibility of the entire remote players list container.
        /// </summary>
        public void SetRemoteHudVisible(bool visible)
        {
            if (m_RemoteHudContainer == null) return;
            m_RemoteHudContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        #endregion
    }
}

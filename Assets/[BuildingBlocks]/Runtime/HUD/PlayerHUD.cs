using System;
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

        [Header("References")]
        [SerializeField] BuildingBlocksCharacter character;

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

        UIDocument m_UIDocument;
        VisualElement m_HudLayer;
        StatBarStack m_Stack;
        PauseMenuView m_Menu;
        AttackSlotsView m_AttackSlots;
        VisualElement m_RespawnOverlay;
        Label m_RespawnCountdown;
        InputActionMap m_GameplayMap;
        PlayerAttackAbility m_Attack;

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
            m_UIDocument = GetComponent<UIDocument>();

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
            if (m_Menu != null) m_Menu.Bind();

            if (character == null) return;
            character.OnRespawning += HandleRespawning;
            character.OnRespawned += HandleRespawned;
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

            if (character == null) return;
            character.OnRespawning -= HandleRespawning;
            character.OnRespawned -= HandleRespawned;
            m_Stack?.Unbind();
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

        public void ConnectToServer(string ip, string port)
        {
            NetworkManager.Instance.Connect(ip, int.Parse(port));
        }

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

            if (character == null) return;

            VisualElement container = root.Q<VisualElement>(k_StackName);
            if (container == null)
            {
                Debug.LogWarning($"[PlayerHUD] '{k_StackName}' not found in UIDocument.", this);
                return;
            }

            m_Stack = new StatBarStack(character, container);
            m_Stack.Build();
            m_Stack.Bind();
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
            if (m_AttackSlots == null || m_Attack == null) return;

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
    }
}

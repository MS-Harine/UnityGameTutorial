using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Blocks
{
    /// <summary>
    /// The Building Blocks Creator editor window. Hosts the home screen with its Standard and AI Assisted
    /// card tabs, and drives the active wizard through its steps. Owns the static registry of home cards and
    /// raises ProposalRendered when the AI flow opens a review wizard.
    /// </summary>
    [InitializeOnLoad]
    sealed class CreatorWindow : EditorWindow, ISerializationCallbackReceiver
    {
        #region Registry

        public const string StandardTab = "Standard";

        /// <summary>One home-screen tab: its label, its sort position, and the cards shown under it.</summary>
        readonly struct HomeTab
        {
            public readonly string Name;
            public readonly int Order;
            public readonly HomeCard[] Cards;

            public HomeTab(string name, int order, HomeCard[] cards)
            {
                Name = name;
                Order = order;
                Cards = cards;
            }
        }

        static readonly List<HomeTab> k_Tabs = new List<HomeTab>();
        static CreatorWindow s_Current;

        public static event Action<Wizard> ProposalRendered;

        static CreatorWindow()
        {
            // The readme banner is not named here: ReadmeCard appends itself via AppendCard from its own
            // [InitializeOnLoad] hook, so "Remove Readme Assets" can delete the readme scripts without
            // leaving a dangling reference in this class.
            RegisterTab(StandardTab, 0,
                CharacterWizard.CreateCard(),
                AbilityWizard.CreateMovementCard(),
                AbilityWizard.CreateAttackCard());
        }

        /// <summary>
        /// Adds a tab to the home screen. The static constructor registers the built-in Standard tab; an
        /// optional sample registers its own from an <see cref="InitializeOnLoadAttribute"/> hook, which is
        /// how the AI Assisted tab appears only once that sample is imported. Cards live for the life of the
        /// domain, so a card can hold draft state across window rebuilds. Re-registering a name replaces the
        /// existing tab rather than doubling it.
        /// </summary>
        /// <param name="name">The tab label, also the key used to remember which tab was open.</param>
        /// <param name="order">Sort position; the built-in Standard tab is 0.</param>
        /// <param name="cards">The cards shown under the tab. Ignored when empty.</param>
        public static void RegisterTab(string name, int order, params HomeCard[] cards)
        {
            if (string.IsNullOrEmpty(name) || cards == null || cards.Length == 0) return;

            k_Tabs.RemoveAll(tab => tab.Name == name);
            k_Tabs.Add(new HomeTab(name, order, cards));
            k_Tabs.Sort((a, b) => a.Order.CompareTo(b.Order));

            // Deferred to the next editor tick, because every caller registers from a static initializer —
            // this class's own static constructor and each sample's [InitializeOnLoad] hook. Unity refuses
            // FindObjectsOfTypeAll while a ScriptableObject is being constructed, and CreatorWindow is one;
            // calling it straight from here throws inside the static constructor, which poisons the type
            // initializer for the rest of the domain and makes every later use of the window fail.
            EditorApplication.delayCall += RefreshHomeIfShowing;
        }

        /// <summary>
        /// Appends a card to an already-registered tab. This is how optional cards join a tab without the
        /// tab's owner naming them: the card registers itself from its own
        /// <see cref="InitializeOnLoadAttribute"/> hook, so deleting the card's script (as
        /// "Remove Readme Assets" does) also removes it from the home screen. Appending to a tab that is
        /// not registered does nothing.
        /// </summary>
        public static void AppendCard(string tabName, HomeCard card)
        {
            if (string.IsNullOrEmpty(tabName) || card == null) return;

            int index = k_Tabs.FindIndex(tab => tab.Name == tabName);
            if (index < 0) return;

            var cards = new HomeCard[k_Tabs[index].Cards.Length + 1];
            k_Tabs[index].Cards.CopyTo(cards, 0);
            cards[cards.Length - 1] = card;
            k_Tabs[index] = new HomeTab(tabName, k_Tabs[index].Order, cards);

            // Same deferral as RegisterTab, for the same reason: callers are static initializers.
            EditorApplication.delayCall += RefreshHomeIfShowing;
        }

        #endregion

        Wizard m_Active;

        // Which restorable wizard was open, so it can be rebuilt after a domain reload. AI review wizards
        // are mostly excluded: they're built live by the assistant and can't be reconstructed, so after a
        // recompile they fall back to the home screen (on the same tab, since m_ActiveTab survives). The
        // one exception is an AI ability review with a pending script write — the reload that lands there
        // IS the Assistant's file arriving, so its spec restores onto the standard wizard's Review step,
        // whose created-script panel answers "what do I do next" (see OnBeforeSerialize).
        enum RestorableWizard { None, Character, AbilityMovement, AbilityAttack }

        // Stored by tab name rather than index so a tab that goes away — its sample removed — falls back to
        // the first tab instead of pointing past the end of the list.
        [SerializeField] string m_ActiveTab;
        [SerializeField] RestorableWizard m_ActiveWizard;
        [SerializeField] int m_ActiveStep;
        [SerializeField] CharacterSpecData m_CharacterDraft;
        [SerializeField] AbilityScriptSpec m_AbilityDraft;

        VisualElement m_Top;
        ScrollView m_Content;
        VisualElement m_Footer;

        // What the content ScrollView last showed, so navigation can reset its scroll position. The
        // ScrollView survives every rebuild and keeps its offset — right when a rebuild is an in-place
        // edit (a toggle re-rendering the same step), wrong when it is navigation: without this,
        // scrolling a tall step and pressing Next opened the following step with its title scrolled
        // out of view.
        Wizard m_ShownWizard;
        int m_ShownStep = -1;
        string m_ShownTab;

        #region Opening

        [MenuItem("Building Blocks/Creator")]
        public static void Open() => GetOrCreate().Show();

        static CreatorWindow GetOrCreate()
        {
            var window = GetWindow<CreatorWindow>();
            window.titleContent = new GUIContent("Building Blocks Creator");
            window.minSize = new Vector2(560f, 540f);
            return window;
        }

        #endregion

        #region Navigation

        public static void ShowWizard(Wizard wizard)
        {
            if (wizard == null) return;

            var window = GetOrCreate();
            window.Show();
            window.Focus();
            window.m_Active = wizard;
            window.RebuildView();

            ProposalRendered?.Invoke(wizard);
        }

        /// <summary>Opens the window on the home screen with the named tab selected.</summary>
        public static void ShowHomeOnTab(string tabName)
        {
            var window = GetOrCreate();
            window.Show();
            window.Focus();
            window.m_ActiveTab = tabName;
            window.m_Active = null;
            window.RebuildView();
        }

        public static void GoHome()
        {
            if (s_Current == null) return;
            s_Current.m_Active = null;
            s_Current.RebuildView();
        }

        public static void Refresh() => s_Current?.RebuildView();

        public static void RefreshHomeIfShowing()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<CreatorWindow>())
                if (window.m_Active == null)
                    window.RebuildView();
        }

        #endregion

        #region Lifecycle

        void CreateGUI()
        {
            s_Current = this;

            BBStyles.ApplyCreatorWindow(rootVisualElement);
            rootVisualElement.AddToClassList("blocks-wizard");

            m_Top = new VisualElement();
            m_Top.AddToClassList("blocks-wizard__top");
            rootVisualElement.Add(m_Top);

            m_Content = new ScrollView(ScrollViewMode.Vertical);
            m_Content.AddToClassList("blocks-wizard__content");
            rootVisualElement.Add(m_Content);

            m_Footer = new VisualElement();
            rootVisualElement.Add(m_Footer);

            m_Active = RestoreActiveWizard();

            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            RebuildView();
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            if (s_Current == this) s_Current = null;
        }

        // --- Domain-reload persistence ---
        // CreateGUI runs again after every recompile, so without this the window would always rebuild on
        // the home screen. OnBeforeSerialize snapshots the live wizard into the serialized fields Unity
        // carries across the reload; RestoreActiveWizard (called from CreateGUI) rebuilds it afterward.

        public void OnBeforeSerialize()
        {
            m_ActiveStep = m_Active?.CurrentIndex ?? 0;

            switch (m_Active)
            {
                case CharacterWizard character:
                    m_ActiveWizard = RestorableWizard.Character;
                    m_CharacterDraft = CharacterSpecData.From(character.Spec);
                    break;
                case AbilityWizard ability:
                    m_ActiveWizard = ability.Spec.Kind == AbilityKind.Movement
                        ? RestorableWizard.AbilityMovement
                        : RestorableWizard.AbilityAttack;
                    m_AbilityDraft = ability.Spec;
                    break;
                case IAiAbilityWizard ai when HasScriptToShow(ai.Spec):
                    m_ActiveWizard = ai.Spec.Kind == AbilityKind.Movement
                        ? RestorableWizard.AbilityMovement
                        : RestorableWizard.AbilityAttack;
                    m_AbilityDraft = ai.Spec;
                    m_ActiveStep = 2; // the standard wizard's Review step (Template, Tweak, Review)
                    break;
                default:
                    m_ActiveWizard = RestorableWizard.None;
                    break;
            }
        }

        public void OnAfterDeserialize() { }

        /// <summary>
        /// Whether an AI review has a script worth carrying across the reload: one the Assistant is
        /// writing, or one its "create stub" button already wrote. Either way the reader lands back on the
        /// standard Review step, whose created-script panel says where the file is and how to attach it —
        /// both of that screen's buttons finish the same way. Any other reload (an unrelated recompile)
        /// leaves the review to fall through to Home rather than silently swapping it for the standard
        /// wizard and dropping the Assistant's reasoning.
        /// </summary>
        static bool HasScriptToShow(AbilityScriptSpec spec) =>
            spec != null && (spec.AiWritePending || !string.IsNullOrEmpty(spec.CreatedScriptPath));

        Wizard RestoreActiveWizard()
        {
            switch (m_ActiveWizard)
            {
                case RestorableWizard.Character:
                    var character = m_CharacterDraft != null
                        ? CharacterWizard.Restore(m_CharacterDraft.ToSpec())
                        : new CharacterWizard();
                    character.CurrentIndex = m_ActiveStep;
                    return character;

                case RestorableWizard.AbilityMovement:
                case RestorableWizard.AbilityAttack:
                    var kind = m_ActiveWizard == RestorableWizard.AbilityMovement
                        ? AbilityKind.Movement
                        : AbilityKind.Attack;
                    var ability = m_AbilityDraft != null
                        ? AbilityWizard.Restore(m_AbilityDraft)
                        : new AbilityWizard(kind);
                    ability.CurrentIndex = m_ActiveStep;
                    return ability;

                default:
                    return null;
            }
        }

        // Rebuild while a wizard's final (spawn/review) step shows, so a panel that tracks a scene object
        // follows it — deleting or undoing the spawn takes the panel with it. Single-step AI reviews count:
        // IsLast is always true for them, and their spawned panel needs the same tracking.
        void OnHierarchyChanged()
        {
            if (m_Active != null && m_Active.IsLast)
                RebuildView();
        }

        #endregion

        #region View

        void RebuildView()
        {
            if (m_Top == null) return;

            m_Top.Clear();
            m_Content.Clear();
            m_Footer.Clear();

            bool navigated = m_Active != m_ShownWizard
                || (m_Active != null && m_Active.CurrentIndex != m_ShownStep)
                || (m_Active == null && m_ActiveTab != m_ShownTab);
            m_ShownWizard = m_Active;
            m_ShownStep = m_Active?.CurrentIndex ?? -1;
            m_ShownTab = m_ActiveTab;
            if (navigated) m_Content.scrollOffset = Vector2.zero;

            if (m_Active == null)
                m_Content.Add(WizardView.Stage(BuildHome()));
            else
                WizardView.Render(m_Active, m_Top, m_Content, m_Footer);
        }

        VisualElement BuildHome()
        {
            var root = new VisualElement();

            var header = new VisualElement();
            header.AddToClassList("blocks-pick-header");

            var headerTitle = Heading.Title("What are you building?");
            headerTitle.AddToClassList("blocks-pick-header__title");
            header.Add(headerTitle);

            int active = ActiveTabIndex();

            // A single tab needs no tab bar: with no sample imported the Creator is a one-mode tool, and an
            // "always on" segmented control with one segment in it reads like something is missing.
            if (k_Tabs.Count > 1)
            {
                var labels = new string[k_Tabs.Count];
                for (int i = 0; i < labels.Length; i++) labels[i] = k_Tabs[i].Name;

                header.Add(new TabBar(labels, active, onSelect: i =>
                {
                    m_ActiveTab = k_Tabs[i].Name;
                    RebuildView();
                }));
            }

            root.Add(header);

            if (k_Tabs.Count == 0) return root;

            var grid = new VisualElement();
            grid.AddToClassList("blocks-pick-grid");
            foreach (HomeCard card in k_Tabs[active].Cards)
            {
                // A card may decline to render by returning null — the readme banner does once its
                // asset has been removed.
                var element = card.Build();
                if (element != null) grid.Add(element);
            }

            const float narrowBreakpoint = 440f;
            grid.RegisterCallback<GeometryChangedEvent>(evt =>
                grid.EnableInClassList("blocks-pick-grid--narrow", evt.newRect.width < narrowBreakpoint));

            root.Add(grid);

            return root;
        }

        int ActiveTabIndex()
        {
            for (int i = 0; i < k_Tabs.Count; i++)
                if (k_Tabs[i].Name == m_ActiveTab) return i;

            return 0;
        }

        #endregion
    }
}

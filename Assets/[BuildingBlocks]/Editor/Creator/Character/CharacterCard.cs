using System;
using UnityEditor;
using UnityEngine;
using Blocks.Attack;
using Blocks.Movement;
using Blocks.Character;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Blocks
{
    /// <summary>
    /// The manual character creation wizard: pick a template, set identity, stats, and abilities, then
    /// review and spawn. Seeds the spec from the Player template and runs five steps. Also provides the
    /// home-screen card.
    /// </summary>
    sealed class CharacterWizard : Wizard
    {
        public readonly CharacterSpec Spec;
        readonly WizardStep[] m_Steps;

        #region Wizard setup

        public CharacterWizard() : this(MakeDefaultSpec()) { }

        /// <summary>Rebuilds a wizard around an existing spec, e.g. one restored after a domain reload.</summary>
        public static CharacterWizard Restore(CharacterSpec spec) => new CharacterWizard(spec);

        CharacterWizard(CharacterSpec spec)
        {
            Spec = spec;

            m_Steps = new WizardStep[]
            {
                new CharacterTemplateStep(Spec),
                new IdentityStep(Spec),
                new StatsStep(Spec),
                new AbilitiesStep(Spec),
                new CharacterReviewStep(Spec),
            };
        }

        static CharacterSpec MakeDefaultSpec()
        {
            var spec = new CharacterSpec();
            CharacterTemplates.ApplyTo(spec, CharacterTemplate.Player);
            return spec;
        }

        public override string Title => "Character";
        public override IReadOnlyList<WizardStep> Steps => m_Steps;

        #endregion

        #region Home card

        public static readonly HomeCardInfo CardInfo = new HomeCardInfo(
            "A GameObject in your scene that moves, fights, takes damage, and can be eliminated. It can be your player or an enemy.",
            new[] { "Player", "Enemy", "Boss", "NPC" },
            new LearnSection(
                "How this works",
                "Every character is the same three parts: one BuildingBlocksCharacter component that runs the actor (sprite, physics, stats, targeting, respawning), plus the movement and attack abilities that plug into it. The five steps here assemble those parts and wire each ability to the input action and projectile it needs, so the character responds the first time you press Play.",
                new[]
                {
                    "There is no player type or enemy type, only different abilities: abilities that read input make a player, abilities that watch a target make an enemy. Enemies pick their own targets, so there is no separate AI brain.",
                    "Health keeps the character alive. At zero it is eliminated and respawns, disables, or is destroyed, your pick on the Stats step.",
                    "Abilities stay ordinary components: add or remove them later from the Movement and Attack cards on the character's Inspector, no wizard needed.",
                    "A boss is an enemy with more health and a heavier attack; an NPC is a character with no attack at all. Both start from the same two templates.",
                }));

        public static HomeCard CreateCard()
        {
            return new WizardLaunchCard("Character", CardInfo, wide: true, () => new CharacterWizard());
        }

        #endregion
    }

    /// <summary>First wizard step: pick a starting template (Player or Enemy).</summary>
    sealed class CharacterTemplateStep : WizardStep
    {
        readonly CharacterSpec m_Spec;
        public CharacterTemplateStep(CharacterSpec spec) => m_Spec = spec;
        public override string Label => "Template";

        public override VisualElement BuildBody()
        {
            var root = new VisualElement();
            root.Add(Heading.Title("Pick a starting template"));
            root.Add(Heading.Subtitle("Both are complete characters; every step after this can change what you start with."));

            var grid = new VisualElement();
            grid.AddToClassList("blocks-pick-grid");
            root.Add(grid);

            grid.Add(MakeCard(CharacterTemplate.Player,
                "Player", "HP 100 · Stamina 100 · respawns",
                new[] { "Walks & runs", "Jumps", "Melee & ranged", "Player tag" }));

            grid.Add(MakeCard(CharacterTemplate.MeleeEnemy,
                "Enemy", "HP 30 · destroys when eliminated",
                new[] { "Patrols", "Melee attack", "Targets player" }));

            return root;
        }

        OptionCard MakeCard(CharacterTemplate template, string name, string desc, string[] tags)
        {
            return new OptionCard(
                name, desc, tags,
                selected: m_Spec.Template == template,
                onClick: () =>
                {
                    CharacterTemplates.ApplyTo(m_Spec, template);
                    CreatorWindow.Refresh();
                });
        }
    }

    /// <summary>
    /// Second wizard step: name the character and choose its sprite. The sprite field leads with the four
    /// shipped characters as one-click picks, because the full project picker lists every sprite in the
    /// project and gave testers no idea what a safe choice looked like.
    /// </summary>
    sealed class IdentityStep : WizardStep
    {
        const string k_PrefabsFolder = "Assets/[BuildingBlocks]/Prefabs";

        readonly CharacterSpec m_Spec;
        public IdentityStep(CharacterSpec spec) => m_Spec = spec;
        public override string Label => "Identity";

        public override VisualElement BuildBody()
        {
            var root = new VisualElement();
            root.Add(Heading.Title("Identity"));
            root.Add(Heading.Subtitle("Give it a name and pick a sprite. The name becomes the GameObject name in the scene."));

            var twoCol = new VisualElement();
            twoCol.AddToClassList("blocks-two-col");

            var left = new VisualElement();
            left.AddToClassList("blocks-two-col__left");
            twoCol.Add(left);

            var right = new VisualElement();
            right.AddToClassList("blocks-two-col__right");
            twoCol.Add(right);

            var preview = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                sprite = m_Spec.Sprite,
            };
            preview.AddToClassList("blocks-sprite-preview");
            left.Add(preview);

            var spriteField = new ObjectField("Sprite")
            {
                objectType = typeof(Sprite),
                allowSceneObjects = false,
                value = m_Spec.Sprite,
            };
            spriteField.RegisterValueChangedCallback(evt =>
            {
                m_Spec.Sprite = evt.newValue as Sprite;
                preview.sprite = m_Spec.Sprite;
            });
            left.Add(spriteField);

            var quickPicks = BuildQuickPicks(preview, spriteField);
            if (quickPicks != null) left.Add(quickPicks);

            var spriteHint = new Label("Any sprite works as a placeholder — abilities drive behavior, and animation comes from an Animator, not this field.");
            spriteHint.AddToClassList("blocks-muted");
            spriteHint.style.whiteSpace = WhiteSpace.Normal;
            spriteHint.style.marginTop = 6;
            left.Add(spriteHint);

            var nameField = new TextField("Display name") { value = m_Spec.Name };
            nameField.RegisterValueChangedCallback(evt => { m_Spec.Name = evt.newValue; });
            right.Add(nameField);

            string tipText = m_Spec.Mode == CharacterMode.Enemy
                ? "Enemies are tagged so the player can find them. Set the player's targeting on the Abilities step."
                : "The spawned object is tagged Player.";
            var tip = new Label(tipText);
            tip.AddToClassList("blocks-muted");
            tip.style.marginTop = 8;
            right.Add(tip);

            root.Add(twoCol);
            return root;
        }

        #region Quick picks

        VisualElement BuildQuickPicks(Image preview, ObjectField spriteField)
        {
            List<(string Name, Sprite Sprite)> art = LoadShippedArt();
            if (art.Count == 0) return null;

            var section = new VisualElement();

            var label = new Label("Shipped character art");
            label.AddToClassList("blocks-muted");
            label.style.marginTop = 8;
            section.Add(label);

            var row = new VisualElement();
            row.AddToClassList("blocks-sprite-picks");
            section.Add(row);

            foreach ((string name, Sprite sprite) in art)
            {
                var tile = new VisualElement { tooltip = name };
                tile.AddToClassList("blocks-sprite-picks__tile");
                if (m_Spec.Sprite == sprite) tile.AddToClassList("blocks-sprite-picks__tile--selected");

                var image = new Image { scaleMode = ScaleMode.ScaleToFit, sprite = sprite };
                image.style.flexGrow = 1;
                tile.Add(image);

                Sprite captured = sprite;
                tile.RegisterCallback<ClickEvent>(_ =>
                {
                    m_Spec.Sprite = captured;
                    preview.sprite = captured;
                    spriteField.SetValueWithoutNotify(captured);

                    foreach (VisualElement sibling in row.Children())
                        sibling.RemoveFromClassList("blocks-sprite-picks__tile--selected");
                    tile.AddToClassList("blocks-sprite-picks__tile--selected");
                });

                row.Add(tile);
            }

            return section;
        }

        /// <summary>
        /// The sprite each shipped character prefab renders with, Player first. These are the safe picks:
        /// art the runner has already seen moving in the sample level.
        /// </summary>
        static List<(string Name, Sprite Sprite)> LoadShippedArt()
        {
            var art = new List<(string Name, Sprite Sprite)>();
            if (!AssetDatabase.IsValidFolder(k_PrefabsFolder)) return art;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { k_PrefabsFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab.GetComponent<BuildingBlocksCharacter>() == null) continue;

                // The shipped characters render on a "Visuals" child, so search the whole prefab.
                var renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer == null || renderer.sprite == null) continue;

                art.Add((prefab.name, renderer.sprite));
            }

            art.Sort((a, b) =>
            {
                bool aPlayer = a.Name == "Player";
                bool bPlayer = b.Name == "Player";
                if (aPlayer != bPlayer) return aPlayer ? -1 : 1;
                return string.CompareOrdinal(a.Name, b.Name);
            });

            return art;
        }

        #endregion
    }

    /// <summary>Third wizard step: edit the character's stats and choose what happens when it is eliminated.</summary>
    sealed class StatsStep : WizardStep
    {
        readonly CharacterSpec m_Spec;
        public StatsStep(CharacterSpec spec) => m_Spec = spec;
        public override string Label => "Stats";

        #region Cards

        public override VisualElement BuildBody()
        {
            var root = new VisualElement();
            root.Add(Heading.Title("Stats & lifecycle"));
            root.Add(Heading.Subtitle("Health drains as the character takes damage. When it hits zero, the lifecycle setting decides what happens."));

            root.Add(BuildStatsCard());
            root.Add(BuildLifecycleCard());

            return root;
        }

        VisualElement BuildStatsCard()
        {
            var card = new Card("Stats");

            var createTypeBtn = new Button(NewStatTypePopup.ShowPopup) { text = "Create new stat", tooltip = "Add a new entry to the StatType enum" };
            createTypeBtn.style.marginRight = 6;
            card.AddAction(createTypeBtn);

            card.AddAction(new Button(() =>
            {
                m_Spec.Stats.Add(new StatEntry(StatType.Health, 100f));
                CreatorWindow.Refresh();
            }) { text = "+ Add stat" });

            if (m_Spec.Stats.Count == 0)
            {
                var empty = new Label("No stats yet. Most characters need at least Health.");
                empty.AddToClassList("blocks-muted");
                card.Add(empty);
                return card;
            }

            for (int i = 0; i < m_Spec.Stats.Count; i++)
            {
                int index = i;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                var bar = new SpecStatBarField();
                bar.Bind(m_Spec.Stats[index], () => { });
                bar.style.flexGrow = 1;
                row.Add(bar);

                var del = new Button(() =>
                {
                    m_Spec.Stats.RemoveAt(index);
                    CreatorWindow.Refresh();
                }) { text = "×" };
                del.style.width = 26;
                del.style.marginLeft = 4;
                row.Add(del);

                card.Add(row);
            }

            var hint = new Label("Stats with Regen/s > 0 refill over time. They appear as bars in the on-screen HUD.");
            hint.AddToClassList("blocks-muted");
            hint.style.marginTop = 8;
            card.Add(hint);

            return card;
        }

        VisualElement BuildLifecycleCard()
        {
            var card = new Card("When eliminated");

            foreach (EliminationBehavior behavior in Enum.GetValues(typeof(EliminationBehavior)))
            {
                EliminationBehavior captured = behavior;
                bool selected = m_Spec.OnEliminated == behavior;

                var toggle = new Toggle { value = selected };
                toggle.SetEnabled(false);
                toggle.style.marginRight = 8;

                card.Add(new OptionRow(behavior.ToString(), Hint(behavior), selected)
                    .WithLeading(toggle)
                    .Clickable(() =>
                    {
                        m_Spec.OnEliminated = captured;
                        CreatorWindow.Refresh();
                    }));
            }

            bool needsDelay = m_Spec.OnEliminated == EliminationBehavior.Respawn || m_Spec.OnEliminated == EliminationBehavior.Destroy;
            if (needsDelay)
            {
                var delayField = new FloatField("Delay (s)") { value = m_Spec.EliminationDelay };
                delayField.style.maxWidth = 200;
                delayField.style.marginTop = 6;
                delayField.RegisterValueChangedCallback(evt =>
                {
                    m_Spec.EliminationDelay = Mathf.Max(0f, evt.newValue);
                });
                card.Add(delayField);
            }

            return card;
        }

        #endregion

        #region Helpers

        static string Hint(EliminationBehavior b)
        {
            switch (b)
            {
                case EliminationBehavior.Respawn: return "reset to original spawn position after the delay.";
                case EliminationBehavior.Disable: return "turn off the GameObject; it stays in scene.";
                case EliminationBehavior.Destroy: return "remove from scene after the delay.";
                default: return string.Empty;
            }
        }

        #endregion
    }

    /// <summary>
    /// Fourth wizard step: choose movement abilities (multi-select), one attack ability (or none), and the
    /// targeting setup when an attack is set. Lists the ability classes found in the project.
    /// </summary>
    sealed class AbilitiesStep : WizardStep
    {
        readonly CharacterSpec m_Spec;
        public AbilitiesStep(CharacterSpec spec) => m_Spec = spec;
        public override string Label => "Abilities";

        #region Cards

        public override VisualElement BuildBody()
        {
            var root = new VisualElement();
            root.Add(Heading.Title("Abilities"));
            root.Add(Heading.Subtitle("What can this character do? Movement is multi-select; attack is one (or none)."));

            root.Add(BuildMovementCard());
            root.Add(BuildAttackCard());
            if (m_Spec.IsTargetingEnabled) root.Add(BuildTargetingCard());

            return root;
        }

        VisualElement BuildMovementCard()
        {
            var card = new Card("Movement");

            var available = new List<Type>();
            int totalConcrete = 0;
            foreach (Type t in TypeCache.GetTypesDerivedFrom<MovementAbility>())
            {
                if (t.IsAbstract) continue;
                totalConcrete++;
                if (!m_Spec.MovementAbilityTypes.Contains(t)) available.Add(t);
            }

            var addBtn = new Button(() => ShowAddMenu(available, t =>
            {
                if (!m_Spec.MovementAbilityTypes.Contains(t)) m_Spec.MovementAbilityTypes.Add(t);
                CreatorWindow.Refresh();
            })) { text = "+ Add ability" };
            addBtn.SetEnabled(available.Count > 0);
            card.AddAction(addBtn);

            if (totalConcrete == 0)
            {
                var none = new Label("No MovementAbility subclasses found in the project.");
                none.AddToClassList("blocks-muted");
                card.Add(none);
                return card;
            }

            if (m_Spec.MovementAbilityTypes.Count == 0)
            {
                var empty = new Label("No movement — the character won't move on its own. Use + Add ability to attach one.");
                empty.AddToClassList("blocks-muted");
                card.Add(empty);
            }
            else
            {
                for (int i = 0; i < m_Spec.MovementAbilityTypes.Count; i++)
                {
                    int index = i;
                    Type abilityType = m_Spec.MovementAbilityTypes[index];
                    card.Add(BuildAbilityRow(abilityType, () =>
                    {
                        m_Spec.MovementAbilityTypes.RemoveAt(index);
                        CreatorWindow.Refresh();
                    }));
                }
            }

            var hint = new Label("Pick from MovementAbility scripts in your project. To make a new one, finish this character then build a Movement Ability.");
            hint.AddToClassList("blocks-muted");
            hint.style.marginTop = 8;
            card.Add(hint);

            return card;
        }

        VisualElement BuildAttackCard()
        {
            var card = new Card("Attack");

            var types = new List<Type>();
            var labels = new List<string> { "(none)" };
            foreach (Type t in TypeCache.GetTypesDerivedFrom<AttackAbility>())
            {
                if (t.IsAbstract) continue;
                types.Add(t);
                labels.Add(t.Name);
            }

            int currentIndex = 0;
            if (m_Spec.AttackAbilityType != null)
            {
                int idx = types.IndexOf(m_Spec.AttackAbilityType);
                if (idx >= 0) currentIndex = idx + 1;
            }

            var popup = new PopupField<string>(labels, currentIndex);
            popup.RegisterValueChangedCallback(_ =>
            {
                int sel = popup.index;
                m_Spec.AttackAbilityType = sel <= 0 ? null : types[sel - 1];
                CreatorWindow.Refresh();
            });
            card.Add(popup);

            if (m_Spec.AttackAbilityType != null)
            {
                string desc = DescribeType(m_Spec.AttackAbilityType);
                if (!string.IsNullOrEmpty(desc))
                {
                    var body = new Label(desc);
                    body.AddToClassList("blocks-muted");
                    body.style.marginTop = 6;
                    card.Add(body);
                }
            }
            else
            {
                var empty = new Label("No attack — the character can't deal damage.");
                empty.AddToClassList("blocks-muted");
                empty.style.marginTop = 6;
                card.Add(empty);
            }

            return card;
        }

        static VisualElement BuildAbilityRow(Type abilityType, Action onRemove)
        {
            string desc = DescribeType(abilityType);

            var del = new Button(onRemove) { text = "×" };
            del.style.width = 26;
            del.style.marginLeft = 4;
            del.style.alignSelf = Align.FlexStart;

            return new OptionRow(abilityType.Name, string.IsNullOrEmpty(desc) ? null : desc, selected: true)
                .WithTrailing(del);
        }

        static void ShowAddMenu(List<Type> available, Action<Type> onPick)
        {
            var menu = new GenericMenu();
            if (available.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("All abilities already added"));
            }
            else
            {
                foreach (Type t in available)
                {
                    Type captured = t;
                    menu.AddItem(new GUIContent(t.Name), false, () => onPick(captured));
                }
            }
            menu.ShowAsContext();
        }

        VisualElement BuildTargetingCard()
        {
            var card = new Card("Targeting");

            var modeField = new EnumField("Mode", m_Spec.TargetingMode);
            modeField.RegisterValueChangedCallback(evt =>
            {
                m_Spec.TargetingMode = (TargetingMode)evt.newValue;
                CreatorWindow.Refresh();
            });
            card.Add(modeField);

            if (m_Spec.TargetingMode == TargetingMode.TaggedDamageable)
            {
                var tagField = new TextField("Target tag") { value = m_Spec.TargetTag };
                tagField.RegisterValueChangedCallback(evt => { m_Spec.TargetTag = evt.newValue; });
                card.Add(tagField);
            }
            else
            {
                var hint = new Label("Nearest picks the closest IDamageable in range, regardless of tag.");
                hint.AddToClassList("blocks-muted");
                hint.style.marginTop = 4;
                hint.style.marginBottom = 8;
                card.Add(hint);
            }

            var radius = new FloatField("Search radius (m)") { value = m_Spec.TargetRadius };
            radius.style.maxWidth = 220;
            radius.RegisterValueChangedCallback(evt =>
            {
                m_Spec.TargetRadius = Mathf.Max(0f, evt.newValue);
            });
            card.Add(radius);

            return card;
        }

        #endregion

        #region Helpers

        static string DescribeType(Type t)
        {
            string summary = XmlSummaryReader.For(t);
            if (!string.IsNullOrEmpty(summary)) return summary;

            var attrs = t.GetCustomAttributes(typeof(TooltipAttribute), false);
            if (attrs.Length > 0) return ((TooltipAttribute)attrs[0]).tooltip;
            return string.Empty;
        }

        #endregion
    }

    /// <summary>
    /// Final wizard step: show the resolved spec and a button that spawns the character into the scene.
    /// After spawning, the step says what was made and where to configure it, instead of ending in silence.
    /// The panel tracks the live object — the window rebuilds on hierarchy changes while this step shows,
    /// so undoing or deleting the spawn takes the panel with it.
    /// </summary>
    sealed class CharacterReviewStep : WizardStep
    {
        readonly CharacterSpec m_Spec;
        GameObject m_Spawned;

        public CharacterReviewStep(CharacterSpec spec) => m_Spec = spec;
        public override string Label => "Review";

        public override VisualElement BuildBody()
        {
            bool isPlayer = m_Spec.Mode == CharacterMode.Player;
            string title = string.IsNullOrWhiteSpace(m_Spec.Name) ? (isPlayer ? "Player" : "Enemy") : m_Spec.Name;

            var root = new VisualElement();
            root.Add(Heading.Title("Review & spawn"));
            root.Add(Heading.Subtitle("Tweak anything below, then spawn."));

            root.Add(new CharacterSpecView(m_Spec));

            var spawn = Buttons.Primary($"Spawn \"{title}\" into scene", () =>
            {
                m_Spawned = CharacterSpawner.Spawn(m_Spec, m_Spec.Mode);
                CreatorWindow.Refresh();
            });
            spawn.style.height = 36;
            spawn.style.marginTop = 12;
            spawn.style.alignSelf = Align.Stretch;
            root.Add(spawn);

            if (m_Spawned != null) root.Add(BuildSpawnedPanel(m_Spawned));

            return root;
        }

        /// <summary>
        /// The "in your scene" success panel shown after a spawn. Static and public so the AI sample's
        /// review step can end on the same panel instead of in silence.
        /// </summary>
        public static VisualElement BuildSpawnedPanel(GameObject spawned)
        {
            var panel = new Callout(
                $"\"{spawned.name}\" is in your scene",
                "Selected and framed in the Scene view. Tune it on its BuildingBlocksCharacter component — " +
                "movement abilities live on the Movement card, its attack on the Attack card.",
                StripTone.Success);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.marginTop = 8;

            actions.Add(Buttons.Secondary("Select it again", () =>
            {
                Selection.activeGameObject = spawned;
                EditorGUIUtility.PingObject(spawned);
                SceneView.lastActiveSceneView?.FrameSelected();
            }));

            panel.Add(actions);
            return panel;
        }
    }
}

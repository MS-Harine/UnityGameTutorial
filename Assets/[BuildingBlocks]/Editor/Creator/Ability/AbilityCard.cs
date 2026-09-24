using System;
using UnityEditor;
using UnityEngine;
using Blocks.Attack;
using Blocks.Character;
using System.Globalization;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Blocks
{
    /// <summary>
    /// The manual ability creation wizard: pick a template, tweak the numbers, then create the script.
    /// Seeds the spec with movement or attack defaults and runs three steps. Also provides the home-screen
    /// card copy and the launch cards for both kinds.
    /// </summary>
    sealed class AbilityWizard : Wizard
    {
        public readonly AbilityScriptSpec Spec;

        readonly WizardStep[] m_Steps;

        #region Wizard setup

        public AbilityWizard(AbilityKind kind) : this(MakeDefaultSpec(kind)) { }

        /// <summary>Rebuilds a wizard around an existing spec, e.g. one restored after a domain reload.</summary>
        public static AbilityWizard Restore(AbilityScriptSpec spec) => new AbilityWizard(spec);

        AbilityWizard(AbilityScriptSpec spec)
        {
            Spec = spec;
            Title = spec.Kind == AbilityKind.Movement ? "Movement Ability" : "Attack Ability";

            m_Steps = new WizardStep[]
            {
                new AbilityTemplateStep(Spec),
                new TweakStep(Spec),
                new AbilityReviewStep(Spec),
            };
        }

        static AbilityScriptSpec MakeDefaultSpec(AbilityKind kind)
        {
            var spec = new AbilityScriptSpec { Kind = kind };
            if (kind == AbilityKind.Movement)
            {
                spec.Namespace = "Blocks.Movement.Examples";
                spec.ClassName = "NewMovementAbility";
                spec.OutputFolderPath = "Assets/[BuildingBlocks]/Runtime/Movement/Examples";
            }
            else
            {
                spec.Namespace = "Blocks.Attack";
                spec.ClassName = "NewAttackAbility";
                spec.OutputFolderPath = "Assets/[BuildingBlocks]/Runtime/Attack";
            }
            AbilityTemplates.ApplyTo(spec, AbilityTemplate.InstantAction);
            return spec;
        }

        public override string Title { get; }
        public override IReadOnlyList<WizardStep> Steps => m_Steps;

        #endregion

        #region Home cards

        public static HomeCardInfo CardInfo(AbilityKind kind) =>
            kind == AbilityKind.Movement ? k_MovementInfo : k_AttackInfo;

        static readonly HomeCardInfo k_MovementInfo = new HomeCardInfo(
            "A reusable C# script that gives a character a new way to move.",
            new[] { "Crouch", "Climb", "Glide", "Wall-jump" },
            new LearnSection(
                "How this works",
                "For a move nothing ships yet. The Creator writes a short C# file from the template you pick: it compiles right away, appears in the same ability pickers as the shipped moves, and marks a YOUR LOGIC section where the move itself goes.",
                new[]
                {
                    "Runs on a key press or on its own; self-running movement is how enemies patrol.",
                    "Stack as many as you like on one character. Walk, Jump, and Dash already run together on the Player.",
                    "The script is yours to edit, rename, or delete; nothing regenerates it. Read DashAbility for a finished example.",
                }));

        static readonly HomeCardInfo k_AttackInfo = new HomeCardInfo(
            "A reusable C# script that gives a character a new way to deal damage.",
            new[] { "Throw", "Charge shot", "Ground slam", "Melee combo" },
            new LearnSection(
                "How this works",
                "For an attack nothing ships yet. The Creator writes a short C# file from the template you pick: it compiles right away, appears on the Attack card's dropdown next to the shipped attacks, and marks a YOUR LOGIC section where the hit itself goes.",
                new[]
                {
                    "Acts on the character's current target, so there are no attack layers or masks to wire up.",
                    "Melee or ranged: you choose a hit box or a projectile pool when you create it.",
                    "The script is yours to edit, rename, or delete; nothing regenerates it.",
                }));

        public static HomeCard CreateMovementCard()
        {
            return new WizardLaunchCard("Movement Ability", CardInfo(AbilityKind.Movement), wide: false,
                () => new AbilityWizard(AbilityKind.Movement));
        }

        public static HomeCard CreateAttackCard()
        {
            return new WizardLaunchCard("Attack Ability", CardInfo(AbilityKind.Attack), wide: false,
                () => new AbilityWizard(AbilityKind.Attack));
        }

        #endregion
    }

    /// <summary>
    /// First wizard step: choose the trigger (input or auto) and the lifecycle template, and name the class.
    /// </summary>
    sealed class AbilityTemplateStep : WizardStep
    {
        #region Template data

        readonly struct Info
        {
            public readonly AbilityTemplate Template;
            public readonly string Name;
            public readonly string Desc;
            public readonly string MovementExamples;
            public readonly string AttackExamples;

            public Info(AbilityTemplate template, string name, string desc, string movementExamples, string attackExamples)
            {
                Template = template;
                Name = name;
                Desc = desc;
                MovementExamples = movementExamples;
                AttackExamples = attackExamples;
            }
        }

        static readonly Info[] k_Infos =
        {
            new Info(AbilityTemplate.InstantAction,  "Instant",
                "Fires once, then cools down.",
                "Jump · Double jump",
                "Sword swing · Single shot"),
            new Info(AbilityTemplate.TimedBurst,     "Burst",
                "Runs for a set time, then auto-stops.",
                "Dash · Roll",
                "Spin attack · Burst fire"),
            new Info(AbilityTemplate.ContinuousHold, "Sustained",
                "Stays active until something stops it.",
                "Glide · Hover",
                "Beam · Laser"),
        };

        #endregion

        readonly AbilityScriptSpec m_Spec;
        public AbilityTemplateStep(AbilityScriptSpec spec) => m_Spec = spec;
        public override string Label => "Template";

        #region Layout

        public override VisualElement BuildBody()
        {
            string friendly = m_Spec.Kind == AbilityKind.Movement ? "movement" : "attack";

            var root = new VisualElement();
            root.Add(Heading.Title($"What kind of {friendly} ability is it?"));

            root.Add(BuildTriggerPicker());

            var grid = new VisualElement();
            grid.AddToClassList("blocks-tpl-grid");
            root.Add(grid);

            for (int i = 0; i < k_Infos.Length; i++)
            {
                var info = k_Infos[i];
                string examples = m_Spec.Kind == AbilityKind.Movement ? info.MovementExamples : info.AttackExamples;
                AbilityTemplate captured = info.Template;

                var card = new OptionCard(
                    info.Name, info.Desc, new[] { examples },
                    selected: m_Spec.Template == info.Template,
                    onClick: () => { AbilityTemplates.ApplyTo(m_Spec, captured); CreatorWindow.Refresh(); },
                    layout: OptionLayout.Tile);
                if (i == k_Infos.Length - 1) card.style.marginRight = 0;
                grid.Add(card);
            }

            var nameCard = new Card("Name your ability");
            nameCard.SetSummary(AbilityScriptGenerator.SanitizeClassName(m_Spec.ClassName, m_Spec.Kind) + ".cs");

            var classField = new TextField("Class name") { value = m_Spec.ClassName };
            classField.RegisterValueChangedCallback(evt =>
            {
                m_Spec.ClassName = evt.newValue;
                nameCard.SetSummary(AbilityScriptGenerator.SanitizeClassName(m_Spec.ClassName, m_Spec.Kind) + ".cs");
            });
            nameCard.Add(classField);

            root.Add(nameCard);

            return root;
        }

        VisualElement BuildTriggerPicker()
        {
            var section = new VisualElement();

            var grid = new VisualElement();
            grid.AddToClassList("blocks-tpl-grid");
            section.Add(grid);

            grid.Add(BuildTriggerCard(
                AbilityTrigger.OnInput,
                "On player input",
                "Bound to a key — press or hold."));

            var autoCard = BuildTriggerCard(
                AbilityTrigger.Auto,
                "Auto",
                "Runs from game logic — no input. Typically used by enemies.");
            autoCard.style.marginRight = 0;
            grid.Add(autoCard);

            if (m_Spec.Trigger == AbilityTrigger.OnInput)
            {
                var inputHint = Heading.Hint(
                    "The key itself comes from the project's Input Actions asset — after attaching the " +
                    "script, you assign an action to its field in the Inspector. \"Adding a key\" in the " +
                    "Readme shows how to add a new one.");
                inputHint.style.whiteSpace = WhiteSpace.Normal;
                inputHint.style.marginBottom = 12;
                section.Add(inputHint);
            }

            return section;
        }

        VisualElement BuildTriggerCard(AbilityTrigger trigger, string name, string desc)
        {
            AbilityTrigger captured = trigger;
            return new OptionCard(
                name, desc, tags: null,
                selected: m_Spec.Trigger == trigger,
                onClick: () =>
                {
                    if (m_Spec.Trigger == captured) return;
                    m_Spec.Trigger = captured;
                    if (captured == AbilityTrigger.Auto) m_Spec.UseInputBuffer = false;
                    CreatorWindow.Refresh();
                },
                layout: OptionLayout.Tile);
        }

        #endregion
    }

    /// <summary>
    /// Second wizard step: tune the numbers for the chosen template (attack power, timing, and stat cost).
    /// Cards and fields appear only when they apply to the current template and trigger.
    /// </summary>
    sealed class TweakStep : WizardStep
    {
        readonly AbilityScriptSpec m_Spec;
        public TweakStep(AbilityScriptSpec spec) => m_Spec = spec;
        public override string Label => "Tweak";

        #region Cards

        public override VisualElement BuildBody()
        {
            bool isAttack = m_Spec.Kind == AbilityKind.Attack;
            bool isOnInput = m_Spec.Trigger == AbilityTrigger.OnInput;
            bool isPress = m_Spec.Activation == AbilityActivation.Press;

            var root = new VisualElement();
            root.Add(Heading.Title("Tweak the numbers"));
            root.Add(Heading.Subtitle($"Sensible defaults from the {TemplateLabel(m_Spec.Template)} template — change anything you want, leave the rest alone."));

            if (isAttack) root.Add(BuildPowerCard());
            root.Add(BuildTimingCard(isOnInput, isPress));
            root.Add(BuildStatCostCard());

            return root;
        }

        VisualElement BuildPowerCard()
        {
            var card = new Card("Power");
            card.SetSummary($"{m_Spec.Damage} dmg · {m_Spec.Range}m");

            card.Add(NumberField.Labeled("Damage", m_Spec.Damage, v => m_Spec.Damage = Mathf.Max(0f, v), 200));
            card.Add(NumberField.Labeled("Range (m)", m_Spec.Range, v => m_Spec.Range = Mathf.Max(0f, v), 200));

            return card;
        }

        VisualElement BuildTimingCard(bool isOnInput, bool isPress)
        {
            var card = new Card("Timing");
            card.SetSummary(TimingSummary(m_Spec));

            int fields = 0;
            if (m_Spec.UseTimer)
            {
                card.Add(NumberField.Labeled("Duration (s)", m_Spec.Duration, v => m_Spec.Duration = Mathf.Max(0f, v), 200));
                fields++;
            }
            if (m_Spec.UseCooldown)
            {
                card.Add(NumberField.Labeled("Cooldown (s)", m_Spec.Cooldown, v => m_Spec.Cooldown = Mathf.Max(0f, v), 200));
                fields++;
            }
            if (isOnInput && isPress && m_Spec.UseInputBuffer)
            {
                card.Add(NumberField.Labeled("Input buffer (s)", m_Spec.InputBuffer, v => m_Spec.InputBuffer = Mathf.Max(0f, v), 200));
                fields++;
            }
            if (fields == 0)
                card.Add(Heading.Hint("This template doesn't need any timing tweaks — it just runs while active."));

            return card;
        }

        VisualElement BuildStatCostCard()
        {
            var card = new Card("Stat cost");
            card.SetSummary(m_Spec.UseStat ? $"{m_Spec.StatCost} {m_Spec.StatType}{(m_Spec.StatPerSecond ? "/s" : "")}" : "free");

            var useStat = new Toggle("Costs a stat to use") { value = m_Spec.UseStat };
            useStat.RegisterValueChangedCallback(evt =>
            {
                m_Spec.UseStat = evt.newValue;
                CreatorWindow.Refresh();
            });
            card.Add(useStat);

            if (!m_Spec.UseStat) return card;

            var statTypeField = new EnumField("Stat", m_Spec.StatType);
            statTypeField.style.maxWidth = 220;
            statTypeField.RegisterValueChangedCallback(evt => { m_Spec.StatType = (StatType)evt.newValue; });
            card.Add(statTypeField);

            card.Add(NumberField.Labeled("Cost", m_Spec.StatCost, v => m_Spec.StatCost = Mathf.Max(0f, v), 200));

            var perSec = new Toggle("Per second (drain)") { value = m_Spec.StatPerSecond };
            perSec.RegisterValueChangedCallback(evt => { m_Spec.StatPerSecond = evt.newValue; });
            card.Add(perSec);

            return card;
        }

        #endregion

        #region Summaries

        static string TimingSummary(AbilityScriptSpec s)
        {
            bool isOnInput = s.Trigger == AbilityTrigger.OnInput;
            string parts = isOnInput ? s.Activation.ToString() : "Auto";
            if (s.UseTimer) parts += $" · {s.Duration.ToString("0.##", CultureInfo.InvariantCulture)}s{(s.AutoStop ? " auto-stop" : "")}";
            if (s.UseCooldown) parts += $" · cd {s.Cooldown.ToString("0.##", CultureInfo.InvariantCulture)}s";
            if (isOnInput && s.UseInputBuffer && s.Activation == AbilityActivation.Press)
                parts += $" · buf {s.InputBuffer.ToString("0.##", CultureInfo.InvariantCulture)}s";
            return parts;
        }

        static string TemplateLabel(AbilityTemplate t)
        {
            switch (t)
            {
                case AbilityTemplate.InstantAction: return "Instant";
                case AbilityTemplate.TimedBurst: return "Burst";
                case AbilityTemplate.ContinuousHold: return "Sustained";
                default: return t.ToString();
            }
        }

        #endregion
    }

    /// <summary>
    /// Final wizard step: show the resolved spec and a button that writes the script file. Creating does
    /// not end the flow in silence — the step then shows what was made and the three follow-ups (open the
    /// script, find it, attach it), because creation used to leave people with an orphaned script and no
    /// next step.
    /// </summary>
    sealed class AbilityReviewStep : WizardStep
    {
        readonly AbilityScriptSpec m_Spec;
        public AbilityReviewStep(AbilityScriptSpec spec) => m_Spec = spec;
        public override string Label => "Review";

        public override VisualElement BuildBody()
        {
            string className = AbilityScriptGenerator.SanitizeClassName(m_Spec.ClassName, m_Spec.Kind);

            var root = new VisualElement();
            root.Add(Heading.Title("Review & create script"));
            root.Add(Heading.Subtitle("Tweak anything below, then create the script file."));

            root.Add(new AbilitySpecView(m_Spec));

            var create = Buttons.Primary($"Create {className}.cs", () =>
            {
                AbilityScriptWriter.Create(m_Spec);
                CreatorWindow.Refresh();
            });
            create.style.height = 36;
            create.style.marginTop = 12;
            create.style.alignSelf = Align.Stretch;
            root.Add(create);

            string createdPath = m_Spec.CreatedScriptPath;
            if (!string.IsNullOrEmpty(createdPath) && System.IO.File.Exists(createdPath))
            {
                // The AI write landed (or Create ran); the wait is over either way.
                m_Spec.AiWritePending = false;
                root.Add(BuildCreatedPanel(createdPath));
            }
            else if (m_Spec.AiWritePending)
            {
                // Restored here mid-write: a reload fired before the Assistant's file landed. Explain the
                // wait — the panel above appears on the reload its file triggers.
                var waiting = Heading.Hint(
                    $"The Assistant is writing {className}.cs — watch the chat; it appears here when it lands.");
                waiting.style.whiteSpace = WhiteSpace.Normal;
                waiting.style.marginTop = 8;
                root.Add(waiting);
            }
            else
            {
                var closing = Heading.Hint($"Next, {AttachInstruction(className)}.");
                closing.style.whiteSpace = WhiteSpace.Normal;
                closing.style.marginTop = 8;
                root.Add(closing);
            }

            return root;
        }

        #region Created panel

        string AttachInstruction(string className)
        {
            return m_Spec.Kind == AbilityKind.Movement
                ? $"add {className} to a character from the Movement card in its Inspector"
                : $"pick {className} on a character's Attack card in its Inspector";
        }

        VisualElement BuildCreatedPanel(string assetPath)
        {
            string className = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            var panel = new Callout(
                $"{className}.cs created — this script is yours",
                $"Edit it freely; nothing regenerates it. To use it, {AttachInstruction(className)}.",
                StripTone.Success);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.marginTop = 8;

            actions.Add(Buttons.Secondary("Open script", () =>
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                if (script != null) AssetDatabase.OpenAsset(script);
            }));

            actions.Add(Buttons.Secondary("Show in Project", () =>
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                if (script == null) return;
                Selection.activeObject = script;
                EditorGUIUtility.PingObject(script);
            }));

            Button attach = null;
            attach = Buttons.Secondary("Add to selected character", () => AttachToSelection(assetPath, attach));
            actions.Add(attach);

            if (m_Spec.Trigger == AbilityTrigger.OnInput)
                actions.Add(Buttons.Secondary("Show input actions", PingInputActions));

            panel.Add(actions);

            if (m_Spec.Trigger == AbilityTrigger.OnInput)
            {
                var inputNote = Callout.Body(
                    "It listens to an input action: adding it from here or from a character's card wires a " +
                    "matching action automatically; otherwise assign the field in the Inspector.");
                inputNote.style.marginTop = 6;
                panel.Add(inputNote);
            }

            return panel;
        }

        void AttachToSelection(string assetPath, Button button)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            Type type = script != null ? script.GetClass() : null;
            if (type == null)
            {
                EditorUtility.DisplayDialog("Still compiling",
                    "The script has not finished compiling yet. Try again in a moment.", "OK");
                return;
            }

            GameObject target = Selection.activeGameObject;
            var character = target != null ? target.GetComponent<BuildingBlocksCharacter>() : null;
            if (character == null)
            {
                EditorUtility.DisplayDialog("Select a character first",
                    "Select a GameObject with a BuildingBlocksCharacter component in the Hierarchy, then try again.",
                    "OK");
                return;
            }

            if (target.GetComponent(type) != null)
            {
                EditorUtility.DisplayDialog("Already attached",
                    $"{type.Name} is already on '{target.name}'.", "OK");
                return;
            }

            // A character runs one attack ability; mirror the Attack card and swap rather than stack.
            if (m_Spec.Kind == AbilityKind.Attack && target.GetComponent<AttackAbility>() is AttackAbility existing)
            {
                bool replace = EditorUtility.DisplayDialog("Replace attack ability?",
                    $"'{target.name}' already has {existing.GetType().Name}. A character uses one attack " +
                    $"ability — replace it with {type.Name}?",
                    "Replace", "Cancel");
                if (!replace) return;
                Undo.DestroyObjectImmediate(existing);
            }

            AbilityDefaults.Apply(Undo.AddComponent(target, type));
            EditorGUIUtility.PingObject(target);

            button.text = $"Added to {target.name} ✓";
            button.SetEnabled(false);
        }

        static void PingInputActions()
        {
            UnityEngine.Object best = null;
            foreach (string guid in AssetDatabase.FindAssets("t:InputActionAsset"))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                if (asset.name == "InputSystem_Actions") { best = asset; break; }
                best ??= asset;
            }
            if (best != null)
            {
                Selection.activeObject = best;
                EditorGUIUtility.PingObject(best);
            }
        }

        #endregion
    }
}

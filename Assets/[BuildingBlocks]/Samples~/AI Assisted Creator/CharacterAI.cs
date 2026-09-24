using System;
using UnityEditor;
using UnityEngine;
using System.Text;
using Blocks.Attack;
using Blocks.Movement;
using Blocks.Character;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.FunctionCalling;

namespace Blocks
{
    /// <summary>One stat in a character proposal from the assistant: type name, max value, and regen rate.</summary>
    public sealed class CharacterStatProposal
    {
        public string Type { get; set; } = "Health";
        public float MaxValue { get; set; } = 100f;
        public float RegenRate { get; set; }
    }

    /// <summary>
    /// The character spec the assistant proposes, carried from the RenderCharacterProposal tool into the
    /// Creator's review screen. Plain data: kind, name, stats, elimination, abilities, and targeting, plus
    /// suggested follow-up abilities and a sprite prompt.
    /// </summary>
    public sealed class CharacterProposal
    {
        public string Kind { get; set; } = "player";
        public string Name { get; set; } = "NewCharacter";

        public string Summary { get; set; } = string.Empty;

        public List<CharacterStatProposal> Stats { get; set; } = new();

        public string OnEliminated { get; set; } = "Respawn";
        public float EliminationDelay { get; set; }

        public List<string> MovementAbilityNames { get; set; } = new();
        public string AttackAbilityName { get; set; } = string.Empty;

        public string TargetingMode { get; set; } = "NearestDamageable";
        public string TargetTag { get; set; } = "Player";
        public float TargetRadius { get; set; } = 8f;

        public SuggestedAbilityProposal SuggestedMovementAbility { get; set; }
        public SuggestedAbilityProposal SuggestedAttackAbility { get; set; }
        public SuggestedAbilityProposal SuggestedSprite { get; set; }

        public List<string> Tips { get; set; } = new();
    }

    /// <summary>
    /// Agent tools for the character flow. ListAbilities tells the assistant which ability classes exist;
    /// RenderCharacterProposal turns a proposal into a spec and opens the review wizard.
    /// </summary>
    public class CharacterProposalAgentTools
    {
        #region List abilities

        [AgentTool(
            CharacterProposalPrompts.ToolListAbilities,
            "BuildingBlocks.ListAbilities")]
        public static string ListAbilities()
        {
            var sb = new StringBuilder();

            sb.AppendLine("Movement abilities (MovementAbility subclasses):");
            AppendAbilityList<MovementAbility>(sb);
            sb.AppendLine();
            sb.AppendLine("Attack abilities (AttackAbility subclasses):");
            AppendAbilityList<AttackAbility>(sb);
            sb.AppendLine();
            sb.AppendLine(CharacterProposalPrompts.ListAbilitiesFooter);

            return sb.ToString();
        }

        static void AppendAbilityList<T>(StringBuilder sb) where T : CharacterAbility
        {
            bool any = false;
            foreach (Type t in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (t.IsAbstract) continue;
                any = true;
                string summary = XmlSummaryReader.For(t);
                if (string.IsNullOrWhiteSpace(summary)) sb.Append("- ").Append(t.Name).AppendLine();
                else sb.Append("- ").Append(t.Name).Append(": ").AppendLine(summary);
            }
            if (!any) sb.AppendLine("(none — none exist in the project yet)");
        }

        #endregion

        #region Render proposal

        [AgentTool(
            CharacterProposalPrompts.ToolRenderCharacterProposal,
            "BuildingBlocks.RenderCharacterProposal")]
        public static string RenderCharacterProposal(
            [ToolParameter(CharacterProposalPrompts.ParamUserPrompt)]
            string userPrompt,
            [ToolParameter(CharacterProposalPrompts.ParamRationale)]
            string rationale,
            [ToolParameter(CharacterProposalPrompts.ParamProposal)]
            CharacterProposal proposal)
        {
            if (proposal == null)
            {
                Debug.LogWarning("[BuildingBlocks] RenderCharacterProposal received a null proposal.");
                return "Proposal was null — nothing to render.";
            }

            var unresolved = new List<string>();
            var spec = ToSpec(proposal, unresolved);

            var suggestedMovement = AgentToolHelpers.Sanitize(proposal.SuggestedMovementAbility);
            var suggestedAttack = AgentToolHelpers.Sanitize(proposal.SuggestedAttackAbility);
            var suggestedSprite = AgentToolHelpers.Sanitize(proposal.SuggestedSprite);

            string summary = proposal.Summary?.Trim() ?? string.Empty;

            string[] tips = proposal.Tips != null && proposal.Tips.Count > 0 ? proposal.Tips.ToArray() : null;

            var review = new CharacterReviewAi(spec, userPrompt, summary, rationale, suggestedMovement, suggestedAttack, suggestedSprite, tips);
            CreatorWindow.ShowWizard(review);

            bool isPlayer = spec.Mode == CharacterMode.Player;
            string label = string.IsNullOrWhiteSpace(spec.Name) ? (isPlayer ? "Player" : "Enemy") : spec.Name;
            if (unresolved.Count > 0)
                return $"Rendered character proposal for {label}. {unresolved.Count} ability name(s) didn't resolve in this project; the user can pick replacements or build them from suggestions in the review screen.";
            return $"Rendered character proposal for {label}. Waiting for user approval.";
        }

        static CharacterSpec ToSpec(CharacterProposal p, List<string> unresolved)
        {
            var spec = new CharacterSpec();

            spec.Mode = string.Equals(p.Kind, "enemy", StringComparison.OrdinalIgnoreCase)
                ? CharacterMode.Enemy
                : CharacterMode.Player;

            if (!string.IsNullOrWhiteSpace(p.Name)) spec.Name = p.Name.Trim();

            spec.Stats.Clear();
            if (p.Stats != null)
            {
                foreach (var s in p.Stats)
                {
                    if (s == null) continue;
                    var type = AgentToolHelpers.ParseEnum(s.Type, StatType.Health);
                    float max = Mathf.Max(0f, s.MaxValue);
                    float regen = Mathf.Max(0f, s.RegenRate);
                    spec.Stats.Add(new StatEntry(type, max, regen));
                }
            }
            if (spec.Stats.Count == 0)
                spec.Stats.Add(new StatEntry(StatType.Health, 100f));

            spec.OnEliminated = AgentToolHelpers.ParseEnum(p.OnEliminated, EliminationBehavior.Respawn);
            spec.EliminationDelay = Mathf.Max(0f, p.EliminationDelay);

            spec.MovementAbilityTypes.Clear();
            if (p.MovementAbilityNames != null)
            {
                foreach (string name in p.MovementAbilityNames)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    Type t = AgentToolHelpers.FindAbility<MovementAbility>(name.Trim());
                    if (t == null) unresolved.Add(name.Trim());
                    else if (!spec.MovementAbilityTypes.Contains(t)) spec.MovementAbilityTypes.Add(t);
                }
            }

            spec.AttackAbilityType = null;
            if (!string.IsNullOrWhiteSpace(p.AttackAbilityName))
            {
                Type t = AgentToolHelpers.FindAbility<AttackAbility>(p.AttackAbilityName.Trim());
                if (t == null) unresolved.Add(p.AttackAbilityName.Trim());
                else spec.AttackAbilityType = t;
            }

            spec.TargetingMode = AgentToolHelpers.ParseEnum(p.TargetingMode, TargetingMode.NearestDamageable);
            spec.TargetTag = string.IsNullOrEmpty(p.TargetTag) ? "Player" : p.TargetTag;
            spec.TargetRadius = Mathf.Max(0f, p.TargetRadius);

            return spec;
        }

        #endregion
    }

    /// <summary>
    /// Wizard model for reviewing a proposed character. Holds the resolved spec, the original prompt, the
    /// summary and rationale, the suggested follow-up abilities and sprite, and any tips, and exposes a
    /// single review step.
    /// </summary>
    sealed class CharacterReviewAi : Wizard
    {
        public readonly CharacterSpec Spec;
        public readonly string Prompt;
        public readonly string Summary;
        public readonly string Rationale;
        public readonly SuggestedAbility SuggestedMovement;
        public readonly SuggestedAbility SuggestedAttack;
        public readonly SuggestedAbility SuggestedSprite;
        public readonly string[] Tips;

        readonly WizardStep[] m_Steps;

        public CharacterReviewAi(CharacterSpec spec, string prompt, string summary, string rationale,
            SuggestedAbility suggestedMovement, SuggestedAbility suggestedAttack, SuggestedAbility suggestedSprite, string[] tips)
        {
            Spec = spec;
            Prompt = prompt;
            Summary = summary;
            Rationale = rationale;
            SuggestedMovement = suggestedMovement;
            SuggestedAttack = suggestedAttack;
            SuggestedSprite = suggestedSprite;
            Tips = tips;
            m_Steps = new WizardStep[] { new CharacterReviewAiStep(this) };
        }

        public override string Title => "Character";
        public override IReadOnlyList<WizardStep> Steps => m_Steps;
    }

    /// <summary>
    /// The review step's UI: shows the intent, what is in the character, heads-up tips, and optional
    /// suggested next steps, then the editable spec and a Spawn button.
    /// </summary>
    sealed class CharacterReviewAiStep : WizardStep
    {
        readonly CharacterReviewAi m_R;
        GameObject m_Spawned;

        public CharacterReviewAiStep(CharacterReviewAi review) => m_R = review;
        public override string Label => "Review";

        #region Body

        public override VisualElement BuildBody()
        {
            CharacterSpec spec = m_R.Spec;
            bool isPlayer = spec.Mode == CharacterMode.Player;
            string title = string.IsNullOrWhiteSpace(spec.Name) ? (isPlayer ? "Player" : "Enemy") : spec.Name;

            var root = new VisualElement();
            root.Add(Heading.Title("Review the proposal"));
            root.Add(Heading.Subtitle("The Assistant inferred this. Tweak anything below, then spawn."));

            AddIfNotNull(root, Callout.Intent(m_R.Prompt));
            AddIfNotNull(root, WhatsInThisCharacterCard(spec, m_R.Summary, m_R.Rationale));
            AddIfNotNull(root, AiTipsStrip(m_R.Tips));
            AddIfNotNull(root, SuggestedAbilitiesCard(
                m_R.SuggestedMovement, () => AiCard.StartAbility(AbilityKind.Movement, m_R.SuggestedMovement?.Prompt),
                m_R.SuggestedAttack, () => AiCard.StartAbility(AbilityKind.Attack, m_R.SuggestedAttack?.Prompt),
                m_R.SuggestedSprite, () => GenerateSprite(m_R.SuggestedSprite?.Prompt)));

            root.Add(new CharacterSpecView(spec));

            var spawn = Buttons.Primary($"Spawn \"{title}\" into scene", () =>
            {
                m_Spawned = CharacterSpawner.Spawn(spec, spec.Mode);
                CreatorWindow.Refresh();
            });
            spawn.style.height = 36;
            spawn.style.marginTop = 12;
            spawn.style.alignSelf = Align.Stretch;
            root.Add(spawn);

            // Same "in your scene" panel the standard wizard ends on. The window rebuilds on hierarchy
            // changes while this step shows, so deleting or undoing the spawn takes the panel with it.
            if (m_Spawned != null) root.Add(CharacterReviewStep.BuildSpawnedPanel(m_Spawned));

            return root;
        }

        #endregion

        #region Card builders

        static void GenerateSprite(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return;
            AssistantChat.Prompt(CharacterProposalPrompts.BuildSpritePrompt(description.Trim()));
        }

        static void AddIfNotNull(VisualElement parent, VisualElement child)
        {
            if (child != null) parent.Add(child);
        }

        static VisualElement WhatsInThisCharacterCard(CharacterSpec spec, string summary, string defaultsDelta)
        {
            if (spec == null) return null;
            bool hasSummary = !string.IsNullOrWhiteSpace(summary);
            bool hasDefaults = !string.IsNullOrWhiteSpace(defaultsDelta);
            if (!hasSummary && !hasDefaults) return null;

            string name = !string.IsNullOrWhiteSpace(spec.Name)
                ? spec.Name.Trim().ToUpperInvariant()
                : "THIS CHARACTER";

            var strip = new Callout($"WHAT'S IN {name}", hasSummary ? summary.Trim() : null, StripTone.Success);
            if (hasDefaults)
                strip.WithSection("DIFFERENCES FROM DEFAULTS", new[] { defaultsDelta }, topMargin: hasSummary ? 8f : 0f);

            return strip;
        }

        static VisualElement AiTipsStrip(string[] tips)
        {
            if (tips == null || !HasAnyNonEmpty(tips)) return null;
            return new Callout("HEADS-UP").WithBullets(tips);
        }

        static VisualElement SuggestedAbilitiesCard(
            SuggestedAbility movement, Action onBuildMovement,
            SuggestedAbility attack, Action onBuildAttack,
            SuggestedAbility sprite, Action onBuildSprite)
        {
            bool hasMovement = movement != null && !string.IsNullOrWhiteSpace(movement.Prompt);
            bool hasAttack = attack != null && !string.IsNullOrWhiteSpace(attack.Prompt);
            bool hasSprite = sprite != null && !string.IsNullOrWhiteSpace(sprite.Prompt);
            if (!hasMovement && !hasAttack && !hasSprite) return null;

            var card = new VisualElement();
            card.AddToClassList("blocks-card");

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.cursor = new StyleCursor(StyleKeyword.Initial);
            card.Add(header);

            var chevron = new Label("▸");
            chevron.style.marginRight = 6;
            chevron.style.fontSize = 12;
            chevron.style.color = new Color(0.78f, 0.78f, 0.78f);
            header.Add(chevron);

            var heading = new Label("Suggested next steps (Optional)");
            heading.AddToClassList("blocks-card__title");
            heading.style.marginBottom = 0;
            heading.style.flexGrow = 1;
            header.Add(heading);

            var body = new VisualElement();
            body.style.display = DisplayStyle.None;
            body.style.marginTop = 8;
            card.Add(body);

            var sub = new Label("We picked some next steps that fit this character. Open one to refine and build it.");
            sub.AddToClassList("blocks-muted");
            sub.style.marginBottom = 6;
            body.Add(sub);

            if (hasMovement) body.Add(SuggestionRow("MOVEMENT ABILITY", movement, onBuildMovement));
            if (hasAttack) body.Add(SuggestionRow("ATTACK ABILITY", attack, onBuildAttack));
            if (hasSprite) body.Add(SuggestionRow("SPRITE", sprite, onBuildSprite));

            header.RegisterCallback<ClickEvent>(_ =>
            {
                bool open = body.style.display == DisplayStyle.None;
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                chevron.text = open ? "▾" : "▸";
            });

            return card;
        }

        static VisualElement SuggestionRow(string kindLabel, SuggestedAbility suggestion, Action onBuild)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-option-row");

            var col = new VisualElement();
            col.style.flexGrow = 1;

            var kind = new Label(kindLabel);
            kind.AddToClassList("blocks-prompt-card__label");
            col.Add(kind);

            if (!string.IsNullOrWhiteSpace(suggestion.Title))
            {
                var title = new Label(suggestion.Title);
                title.AddToClassList("blocks-option-row__title");
                col.Add(title);
            }

            var body = new Label($"“{suggestion.Prompt.Trim()}”");
            body.AddToClassList("blocks-option-row__body");
            col.Add(body);

            row.Add(col);

            var btn = Buttons.Primary("Build with AI →", onBuild);
            btn.style.alignSelf = Align.Center;
            btn.style.marginLeft = 8;
            row.Add(btn);

            return row;
        }

        static bool HasAnyNonEmpty(IEnumerable<string> items)
        {
            if (items == null) return false;
            foreach (string item in items)
                if (!string.IsNullOrWhiteSpace(item)) return true;
            return false;
        }

        #endregion
    }

    /// <summary>
    /// The Creator card that starts the AI character flow. Supplies the card's copy and placeholder, builds
    /// the design prompt, and wires the agent with the character and reference tools.
    /// </summary>
    sealed class CharacterAiCard : AiCard
    {
        protected override string Title => "Character";
        protected override string Summary => CharacterWizard.CardInfo.Summary;
        protected override string Placeholder => "e.g. \"a small fast goblin that runs at the player\"";
        protected override bool Wide => true;
        protected override string BusyLabel => "Creating your character…";

        protected override string BuildPrompt(string intent) => CharacterProposalPrompts.BuildDesignCharacter(intent);

        protected override IAgent BuildAgent() =>
            BuildingBlocksAgent.New()
                .WithToolsFrom<CharacterProposalAgentTools>()
                .WithToolsFrom<ReferenceScriptsAgentTools>();

        protected override bool Owns(Wizard review) => review is CharacterReviewAi;
    }
}

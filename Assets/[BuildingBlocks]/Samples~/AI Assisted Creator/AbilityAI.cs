using System;
using UnityEngine;
using System.Text;
using Blocks.Character;
using System.Globalization;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.FunctionCalling;

namespace Blocks
{
    /// <summary>
    /// The ability spec the assistant proposes, carried from the RenderAbilityProposal tool into the
    /// Creator's review screen. Plain data: template, naming, trigger, stat cost, attack values, and any
    /// existing-class match or from-scratch snippets.
    /// </summary>
    public sealed class AbilityProposal
    {
        public string Kind { get; set; }
        public string Template { get; set; }
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public string OutputFolderPath { get; set; }
        public string InputFieldName { get; set; }
        public string Trigger { get; set; } = "OnInput";

        public bool UseStat { get; set; }
        public string StatType { get; set; } = "Stamina";
        public float StatCost { get; set; } = 10f;
        public bool StatPerSecond { get; set; }

        public float Damage { get; set; } = 1f;
        public float Range { get; set; } = 1.5f;

        public ExistingAbilityMatch ExistingMatch { get; set; }
        public List<string> FromScratchSnippets { get; set; } = new();
    }

    /// <summary>
    /// Agent tools for the ability flow. ListAbilityTemplates describes the lifecycle templates to the
    /// assistant; RenderAbilityProposal turns a proposal into a spec and opens the review wizard.
    /// </summary>
    public class AbilityProposalAgentTools
    {
        #region List templates

        [AgentTool(
            AbilityProposalPrompts.ToolListAbilityTemplates,
            "BuildingBlocks.ListAbilityTemplates")]
        public static string ListAbilityTemplates()
        {
            var sb = new StringBuilder();
            sb.AppendLine(AbilityProposalPrompts.ListAbilityTemplatesHeader);
            sb.AppendLine();
            AppendTemplate(sb, AbilityTemplate.InstantAction, AbilityProposalPrompts.ShapeInstantAction);
            AppendTemplate(sb, AbilityTemplate.TimedBurst, AbilityProposalPrompts.ShapeTimedBurst);
            AppendTemplate(sb, AbilityTemplate.ContinuousHold, AbilityProposalPrompts.ShapeContinuousHold);
            sb.AppendLine(AbilityProposalPrompts.ListAbilityTemplatesTriggerLine);
            sb.AppendLine();
            sb.AppendLine(AbilityProposalPrompts.ListAbilityTemplatesFooter);
            return sb.ToString();
        }

        static void AppendTemplate(StringBuilder sb, AbilityTemplate template, string shape)
        {
            var spec = new AbilityScriptSpec();
            AbilityTemplates.ApplyTo(spec, template);

            sb.Append("- ").Append(template).Append(" — ").AppendLine(shape);
            sb.Append("    Activation default: ").Append(spec.Activation);
            sb.Append(" · Timer: ").Append(spec.UseTimer ? $"{spec.Duration:0.##}s" + (spec.AutoStop ? " auto-stop" : "") : "off");
            sb.Append(" · Cooldown: ").Append(spec.UseCooldown ? $"{spec.Cooldown:0.##}s" : "off");
            sb.Append(" · Input buffer: ").AppendLine(spec.UseInputBuffer ? $"{spec.InputBuffer:0.##}s" : "off");
        }

        #endregion

        #region Render proposal

        [AgentTool(
            AbilityProposalPrompts.ToolRenderAbilityProposal,
            "BuildingBlocks.RenderAbilityProposal")]
        public static string RenderAbilityProposal(
            [ToolParameter(AbilityProposalPrompts.ParamUserPrompt)]
            string userPrompt,
            [ToolParameter(AbilityProposalPrompts.ParamRationale)]
            string rationale,
            [ToolParameter(AbilityProposalPrompts.ParamProposal)]
            AbilityProposal proposal)
        {
            if (proposal == null)
            {
                Debug.LogWarning("[BuildingBlocks] RenderAbilityProposal received a null proposal.");
                return "Proposal was null — nothing to render.";
            }

            var spec = ToSpec(proposal);
            string[] snippets = ToStringArray(proposal.FromScratchSnippets);

            var review = new AbilityReviewAi(spec, userPrompt, rationale, proposal.ExistingMatch, snippets);
            CreatorWindow.ShowWizard(review);

            string className = AbilityScriptGenerator.SanitizeClassName(spec.ClassName, spec.Kind);
            return $"Rendered ability proposal for {className} ({spec.Kind} · template {spec.Template}). Waiting for user approval.";
        }

        static AbilityScriptSpec ToSpec(AbilityProposal p)
        {
            bool isAttack = string.Equals(p.Kind, "attack", StringComparison.OrdinalIgnoreCase);
            var kind = isAttack ? AbilityKind.Attack : AbilityKind.Movement;
            var template = AgentToolHelpers.ParseEnum(p.Template, AbilityTemplate.InstantAction);

            var spec = new AbilityScriptSpec { Kind = kind };

            AbilityTemplates.ApplyTo(spec, template);

            spec.Trigger = AgentToolHelpers.ParseEnum(p.Trigger, AbilityTrigger.OnInput);
            spec.ClassName = string.IsNullOrWhiteSpace(p.ClassName)
                ? (isAttack ? "NewAttackAbility" : "NewMovementAbility")
                : p.ClassName;
            spec.Namespace = string.IsNullOrWhiteSpace(p.Namespace)
                ? (isAttack ? "Blocks.Attack" : "Blocks.Movement.Examples")
                : p.Namespace;
            spec.OutputFolderPath = string.IsNullOrWhiteSpace(p.OutputFolderPath)
                ? (isAttack ? "Assets/[BuildingBlocks]/Runtime/Attack" : "Assets/[BuildingBlocks]/Runtime/Movement/Examples")
                : p.OutputFolderPath;
            spec.InputFieldName = string.IsNullOrWhiteSpace(p.InputFieldName) ? "abilityAction" : p.InputFieldName;
            spec.UseStat = p.UseStat;
            spec.StatType = AgentToolHelpers.ParseEnum(p.StatType, StatType.Stamina);
            spec.StatCost = Mathf.Max(0f, p.StatCost);
            spec.StatPerSecond = p.StatPerSecond;
            spec.Damage = Mathf.Max(0f, p.Damage);
            spec.Range = Mathf.Max(0f, p.Range);

            return spec;
        }

        static string[] ToStringArray(List<string> list)
        {
            if (list == null || list.Count == 0) return null;
            return list.ToArray();
        }

        #endregion
    }

    /// <summary>
    /// Wizard model for reviewing a proposed ability. Holds the resolved spec, the original prompt and
    /// rationale, an optional existing-class match, and body snippets, and exposes a single review step.
    /// Implements <see cref="IAiAbilityWizard"/> so a pending script write survives the domain reload the
    /// Assistant's file triggers: the Creator window restores the spec onto the standard wizard's Review
    /// step, whose created-script panel shows what to do next.
    /// </summary>
    sealed class AbilityReviewAi : Wizard, IAiAbilityWizard
    {
        public AbilityScriptSpec Spec { get; }
        public readonly string Prompt;
        public readonly string Rationale;
        public readonly ExistingAbilityMatch ExistingMatch;
        public readonly string[] Snippets;

        public bool ScriptWriteRequested;

        readonly WizardStep[] m_Steps;

        public AbilityReviewAi(AbilityScriptSpec spec, string prompt, string rationale, ExistingAbilityMatch existingMatch, string[] snippets)
        {
            Spec = spec;
            Prompt = prompt;
            Rationale = rationale;
            ExistingMatch = existingMatch;
            Snippets = snippets;
            Title = spec.Kind == AbilityKind.Attack ? "Attack Ability" : "Movement Ability";
            m_Steps = new WizardStep[] { new AbilityReviewAiStep(this) };
        }

        public override string Title { get; }
        public override IReadOnlyList<WizardStep> Steps => m_Steps;
    }

    /// <summary>
    /// The review step's UI: shows the intent, any existing-class match and from-scratch guidance, and the
    /// editable spec, then offers two ways to finish: create a stub to fill in by hand, or ask the Assistant
    /// to write the logic.
    /// </summary>
    sealed class AbilityReviewAiStep : WizardStep
    {
        readonly AbilityReviewAi m_R;
        public AbilityReviewAiStep(AbilityReviewAi review) => m_R = review;
        public override string Label => "Review";

        #region Body & actions

        public override VisualElement BuildBody()
        {
            AbilityScriptSpec spec = m_R.Spec;
            string className = AbilityScriptGenerator.SanitizeClassName(spec.ClassName, spec.Kind);
            string folder = string.IsNullOrEmpty(spec.OutputFolderPath) ? "(no folder)" : spec.OutputFolderPath;
            string assetPath = $"{folder}/{className}.cs";

            var root = new VisualElement();
            root.Add(Heading.Title("Review the proposal"));
            root.Add(Heading.Subtitle("The Assistant inferred this. Tweak anything below, then create the script."));

            AddIfNotNull(root, Callout.Intent(m_R.Prompt));
            AddIfNotNull(root, AiExistingMatchCard(m_R.ExistingMatch));
            AddIfNotNull(root, AiFromScratchCard(m_R.Rationale, m_R.Snippets, m_R.ExistingMatch != null));

            root.Add(new AbilitySpecView(spec));

            root.Add(BuildActions(spec));

            if (m_R.ScriptWriteRequested)
                root.Add(BuildPendingBanner(assetPath, className));

            return root;
        }

        VisualElement BuildActions(AbilityScriptSpec spec)
        {
            bool pending = m_R.ScriptWriteRequested;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 12;

            var manual = Buttons.Secondary("Create stub — I'll write the logic", () => AbilityScriptWriter.Create(spec));
            manual.style.height = 36;
            manual.style.flexGrow = 1;
            manual.style.marginRight = 8;
            manual.SetEnabled(!pending);
            row.Add(manual);

            var aiWrite = Buttons.Primary(pending ? "Asking the Assistant…" : "Let AI write the logic", OnAiWrite);
            aiWrite.style.height = 36;
            aiWrite.style.flexGrow = 1;
            aiWrite.SetEnabled(!pending);
            row.Add(aiWrite);

            return row;
        }

        #endregion

        #region AI write flow

        void OnAiWrite()
        {
            if (m_R.ScriptWriteRequested) return;

            AbilityScriptSpec spec = m_R.Spec;

            // Same folder check the stub button gets through AbilityScriptWriter. The Assistant writes to
            // whatever path we hand it, so a folder outside the project would leave a file on disk that
            // Unity never imports — and a created-script panel whose buttons all quietly do nothing.
            if (!AbilityScriptWriter.ValidateOutputFolder(spec)) return;

            string className = AbilityScriptGenerator.SanitizeClassName(spec.ClassName, spec.Kind);
            string userPrompt = BuildCodegenUserPrompt(spec, className);
            string attachmentBody = BuildApprovedSpecAttachmentBody(spec, className);
            string attachmentName = $"Approved spec — {className}.cs";

            // Record where the file will land before handing off: the write triggers a domain reload, and
            // these two fields are how the Creator window knows to restore onto the Review step with its
            // created-script panel instead of resetting to the home screen.
            spec.CreatedScriptPath = $"{spec.OutputFolderPath}/{className}.cs";
            spec.AiWritePending = true;

            m_R.ScriptWriteRequested = true;
            CreatorWindow.Refresh();

            AssistantChat.Codegen(userPrompt, attachmentBody, attachmentName);
        }

        static VisualElement BuildPendingBanner(string assetPath, string className)
        {
            var banner = new VisualElement();
            banner.AddToClassList("blocks-prompt-card");
            banner.style.marginTop = 10;

            var msg = new Label($"Asked the Assistant to write {className}.cs at {assetPath}. Watch the chat — the file will appear in your Project window when it's done.");
            msg.AddToClassList("blocks-muted");
            msg.style.whiteSpace = WhiteSpace.Normal;
            banner.Add(msg);

            return banner;
        }

        string BuildCodegenUserPrompt(AbilityScriptSpec spec, string className)
        {
            string folder = string.IsNullOrEmpty(spec.OutputFolderPath) ? "(no folder)" : spec.OutputFolderPath;
            string assetPath = $"{folder}/{className}.cs";

            return AbilityProposalPrompts.BuildCodegenPrompt(assetPath);
        }

        string BuildApprovedSpecAttachmentBody(AbilityScriptSpec spec, string className)
        {
            bool isMovement = spec.Kind == AbilityKind.Movement;
            string folder = string.IsNullOrEmpty(spec.OutputFolderPath) ? "(no folder)" : spec.OutputFolderPath;
            string assetPath = $"{folder}/{className}.cs";
            string intent = string.IsNullOrWhiteSpace(m_R.Prompt) ? "(no intent recorded)" : m_R.Prompt.Trim();

            var sb = new StringBuilder();
            sb.AppendLine("ORIGINAL INTENT:");
            sb.AppendLine($"\"{intent}\"");
            sb.AppendLine();
            sb.AppendLine("APPROVED SPEC:");
            sb.AppendLine($"  Kind:             {(isMovement ? "movement" : "attack")}");
            sb.AppendLine($"  Template:         {spec.Template}");
            sb.AppendLine($"  ClassName:        {className}");
            sb.AppendLine($"  Namespace:        {AbilityScriptGenerator.SanitizeNamespace(spec.Namespace, spec.Kind)}");
            sb.AppendLine($"  OutputFolderPath: {folder}");
            sb.AppendLine($"  InputFieldName:   {spec.InputFieldName}");
            sb.AppendLine($"  Trigger:          {spec.Trigger}");
            sb.AppendLine($"  Activation:       {spec.Activation}");
            sb.AppendLine($"  UseTimer:         {spec.UseTimer}");
            sb.AppendLine($"  AutoStop:         {spec.AutoStop}");
            sb.AppendLine($"  Duration:         {Float(spec.Duration)}");
            sb.AppendLine($"  UseCooldown:      {spec.UseCooldown}");
            sb.AppendLine($"  Cooldown:         {Float(spec.Cooldown)}");
            sb.AppendLine($"  UseInputBuffer:   {spec.UseInputBuffer}");
            sb.AppendLine($"  InputBuffer:      {Float(spec.InputBuffer)}");
            sb.AppendLine($"  UseStat:          {spec.UseStat}");
            sb.AppendLine($"  StatType:         {spec.StatType}");
            sb.AppendLine($"  StatCost:         {Float(spec.StatCost)}");
            sb.AppendLine($"  StatPerSecond:    {spec.StatPerSecond}");
            if (!isMovement)
            {
                sb.AppendLine($"  Damage:           {Float(spec.Damage)}");
                sb.AppendLine($"  Range:            {Float(spec.Range)}");
            }
            sb.AppendLine();
            sb.AppendLine($"TARGET FILE: {assetPath}");
            return sb.ToString();
        }

        static string Float(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        #endregion

        #region Card builders

        static void AddIfNotNull(VisualElement parent, VisualElement child)
        {
            if (child != null) parent.Add(child);
        }

        static VisualElement AiExistingMatchCard(ExistingAbilityMatch match)
        {
            if (match == null || string.IsNullOrWhiteSpace(match.ClassName)) return null;

            var strip = new Callout("YOU ALREADY HAVE THIS", null, StripTone.Success);

            var className = new Label(match.ClassName.Trim());
            className.AddToClassList("blocks-classname");
            strip.Add(className);

            if (!string.IsNullOrWhiteSpace(match.Summary))
            {
                var summary = Callout.Body(match.Summary.Trim());
                summary.style.marginTop = 2;
                strip.Add(summary);
            }

            bool hasConfig = match.ConfigurationTips != null && HasAnyNonEmpty(match.ConfigurationTips);
            bool hasMods = match.ModificationTips != null && HasAnyNonEmpty(match.ModificationTips);

            if (hasConfig)
                strip.WithSection("Drop it on as-is:", match.ConfigurationTips);
            if (hasMods)
                strip.WithSection(hasConfig ? "Or tweak it to fit:" : "Ways to extend it:", match.ModificationTips, topMargin: hasConfig ? 6f : 0f);

            return strip;
        }

        static VisualElement AiFromScratchCard(string rationale, string[] snippets, bool hasExistingMatch)
        {
            bool hasRationale = !string.IsNullOrWhiteSpace(rationale);
            bool hasSnippets = snippets != null && HasAnyNonEmpty(snippets);
            if (!hasRationale && !hasSnippets) return null;

            var strip = new Callout(
                hasExistingMatch ? "OR — BUILD A NEW ONE FROM SCRATCH" : "WHY THIS BUILD",
                hasRationale ? rationale.Trim() : null);

            if (hasSnippets)
                strip.WithSection("Snippets to use in the body:", snippets, topMargin: hasRationale ? 6f : 0f);

            return strip;
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
    /// The Creator card that starts the AI ability flow for one kind (movement or attack). Supplies the
    /// card's copy and placeholder, builds the design prompt, and wires the agent with the ability and
    /// reference tools.
    /// </summary>
    sealed class AbilityAiCard : AiCard
    {
        readonly AbilityKind m_Kind;
        public AbilityAiCard(AbilityKind kind) => m_Kind = kind;

        protected override string Title => m_Kind == AbilityKind.Movement ? "Movement Ability" : "Attack Ability";

        protected override string Summary => AbilityWizard.CardInfo(m_Kind).Summary;

        protected override string Placeholder => m_Kind == AbilityKind.Movement
            ? "e.g. \"a dash that goes through enemies\""
            : "e.g. \"a fireball that explodes on impact\"";

        protected override bool Wide => false;

        protected override string BusyLabel => "Drafting your ability…";

        protected override string BuildPrompt(string intent) => AbilityProposalPrompts.BuildDesignAbility(intent);

        protected override IAgent BuildAgent() =>
            BuildingBlocksAgent.New()
                .WithToolsFrom<AbilityProposalAgentTools>()
                .WithToolsFrom<ReferenceScriptsAgentTools>();

        protected override bool Owns(Wizard review) => review is AbilityReviewAi a && a.Spec.Kind == m_Kind;
    }
}

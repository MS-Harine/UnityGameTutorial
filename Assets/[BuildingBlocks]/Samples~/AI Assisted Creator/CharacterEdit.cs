using System;
using UnityEditor;
using UnityEngine;
using System.Text;
using System.Threading;
using Blocks.Attack;
using Blocks.Movement;
using Blocks.Character;
using System.Threading.Tasks;
using System.Collections.Generic;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.Editor.Api;
using Unity.AI.Assistant.FunctionCalling;
using static Blocks.SerializedRead;

namespace Blocks
{
    /// <summary>One stat edit in a character edit proposal: type name, max value, and optional regen rate.</summary>
    public sealed class CharacterStatEdit
    {
        public string Type { get; set; } = "Health";
        public float MaxValue { get; set; } = 100f;
        public float? RegenRate { get; set; }
    }

    /// <summary>
    /// A single field edit on a named component: which component (and index), the serialized field path,
    /// and the new value as text.
    /// </summary>
    public sealed class ComponentFieldEdit
    {
        public string Component { get; set; }
        public int Index { get; set; }
        public string Field { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// A patch the assistant proposes for the selected character. Every field is optional; only the ones
    /// set are changed. Covers name, stats, elimination, module toggles, gravity, targeting, abilities,
    /// per-component field edits, and any suggestions or tips.
    /// </summary>
    public sealed class CharacterEditProposal
    {
        public string Name { get; set; }

        public List<CharacterStatEdit> Stats { get; set; }

        public string OnEliminated { get; set; }
        public float? EliminationDelay { get; set; }

        public bool? MovementEnabled { get; set; }
        public bool? TargetingEnabled { get; set; }
        public bool? AttackEnabled { get; set; }

        public float? Gravity { get; set; }
        public float? FallGravityMultiplier { get; set; }
        public float? MaxFallSpeed { get; set; }
        public float? GroundSlopeLimit { get; set; }
        public float? WallSlopeLimit { get; set; }

        public string TargetingMode { get; set; }
        public string TargetTag { get; set; }
        public float? TargetRadius { get; set; }

        public List<string> AddMovementAbilities { get; set; }
        public List<string> RemoveMovementAbilities { get; set; }
        public string AttackAbilityName { get; set; }

        public List<ComponentFieldEdit> ComponentEdits { get; set; }

        public List<SuggestedAbilityProposal> Suggestions { get; set; }
        public List<string> Tips { get; set; }
    }

    /// <summary>
    /// Agent tools for editing the selected character from the Inspector. Reads the character and a
    /// component's fields for the assistant, then renders a proposed edit or a plain answer as an inline
    /// diff or note for the user to apply.
    /// </summary>
    public class CharacterEditAgentTools
    {
        #region Read selected character

        [AgentTool(
            CharacterEditPrompts.ToolReadSelectedCharacter,
            "BuildingBlocks.ReadSelectedCharacter")]
        public static string ReadSelectedCharacter()
        {
            var character = SelectedCharacter();
            if (character == null)
                return "No Building Blocks character is selected. Ask the user to select the character GameObject in the Hierarchy.";

            var so = new SerializedObject(character);
            var sb = new StringBuilder();

            sb.Append("Character: ").AppendLine(character.gameObject.name);
            sb.Append("Tag: ").Append(character.gameObject.tag)
                .Append(character.gameObject.CompareTag("Player") ? "  (player)" : "  (enemy / untagged)").AppendLine();
            sb.AppendLine();

            sb.AppendLine("Stats:");
            bool anyStat = false;
            var statsProp = so.FindProperty(CharacterFields.Stats);
            if (statsProp != null)
            {
                for (int i = 0; i < statsProp.arraySize; i++)
                {
                    var el = statsProp.GetArrayElementAtIndex(i);
                    var typeProp = el.FindPropertyRelative(CharacterFields.Stat.Type);
                    var maxProp = el.FindPropertyRelative(CharacterFields.Stat.MaxValue);
                    var regenProp = el.FindPropertyRelative(CharacterFields.Stat.RegenRate);
                    if (typeProp == null || maxProp == null) continue;
                    anyStat = true;
                    sb.Append("  - ").Append(StatTypeName(typeProp))
                        .Append(": max ").Append(maxProp.floatValue.ToString("0.#"));
                    if (regenProp != null) sb.Append(", regen ").Append(regenProp.floatValue.ToString("0.#")).Append("/s");
                    sb.AppendLine();
                }
            }
            if (!anyStat) sb.AppendLine("  (none)");

            sb.AppendLine();
            sb.Append("On eliminated: ").Append(EnumName(so, "onEliminated"))
                .Append(" · delay ").Append(FloatVal(so, "delay").ToString("0.#")).AppendLine("s");

            sb.Append("Movement enabled: ").Append(BoolVal(so, "isMovementEnabled"))
                .Append(" · Targeting enabled: ").Append(BoolVal(so, "isTargetingEnabled"))
                .Append(" · Attack enabled: ").Append(BoolVal(so, "isAttackEnabled")).AppendLine();

            sb.Append("Gravity ").Append(FloatVal(so, "gravity").ToString("0.#"))
                .Append(" · fall × ").Append(FloatVal(so, "fallGravityMultiplier").ToString("0.#"))
                .Append(" · max fall ").Append(FloatVal(so, "maxFallSpeed").ToString("0.#"))
                .Append(" · ground slope ").Append(FloatVal(so, "groundSlopeLimit").ToString("0.#"))
                .Append(" · wall slope ").Append(FloatVal(so, "wallSlopeLimit").ToString("0.#")).AppendLine();

            sb.Append("Targeting: ").Append(EnumName(so, "targetingMode"))
                .Append(" · tag '").Append(StringVal(so, "targetTag")).Append('\'')
                .Append(" · radius ").Append(FloatVal(so, "targetRadius").ToString("0.#")).Append('m')
                .Append(" · memory ").Append(FloatVal(so, "targetMemoryDuration").ToString("0.#")).AppendLine("s");

            sb.AppendLine();
            sb.AppendLine("Attached movement abilities:");
            var movement = new List<MovementAbility>();
            character.GetComponentsInChildren(true, movement);
            if (movement.Count == 0) sb.AppendLine("  (none)");
            else foreach (var ability in movement)
                if (ability != null) sb.Append("  - ").AppendLine(ability.GetType().Name);

            var attack = character.GetComponentInChildren<AttackAbility>(true);
            sb.Append("Attack ability: ").AppendLine(attack != null ? attack.GetType().Name : "(none)");

            AppendInspectableComponents(sb, character);

            sb.AppendLine();
            sb.AppendLine(CharacterEditPrompts.ReadSelectedCharacterFooter);
            return sb.ToString();
        }

        static void AppendInspectableComponents(StringBuilder sb, BuildingBlocksCharacter character)
        {
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var c in character.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c is Transform || c is BuildingBlocksCharacter) continue;
                string name = c.GetType().Name;
                if (counts.TryGetValue(name, out int n)) counts[name] = n + 1;
                else { counts[name] = 1; order.Add(name); }
            }

            sb.AppendLine();
            sb.AppendLine(CharacterEditPrompts.InspectableComponentsInstruction);
            if (order.Count == 0)
            {
                sb.AppendLine("  (none)");
                return;
            }
            foreach (string name in order)
            {
                int n = counts[name];
                sb.Append("  - ").Append(name);
                if (n > 1) sb.Append(" (x").Append(n).Append(", use Index 0..").Append(n - 1).Append(')');
                sb.AppendLine();
            }
        }

        #endregion

        #region Read component fields

        [AgentTool(
            CharacterEditPrompts.ToolReadComponentFields,
            "BuildingBlocks.ReadComponentFields")]
        public static string ReadComponentFields(
            [ToolParameter(CharacterEditPrompts.ParamReadComponentFieldsComponentType)]
            string componentType,
            [ToolParameter(CharacterEditPrompts.ParamReadComponentFieldsIndex)]
            int index = 0)
        {
            var character = SelectedCharacter();
            if (character == null)
                return "No Building Blocks character is selected. Ask the user to select the character GameObject in the Hierarchy.";

            if (string.IsNullOrWhiteSpace(componentType))
                return "componentType was empty. Call ReadSelectedCharacter to see which components are available.";

            var matches = ComponentPatch.FindAll(character.gameObject, componentType.Trim());
            if (matches.Count == 0)
                return $"No component named '{componentType.Trim()}' was found on '{character.gameObject.name}' or its children. Call ReadSelectedCharacter for the available component type names.";
            if (index < 0 || index >= matches.Count)
                return $"Index {index} is out of range — there are {matches.Count} '{componentType.Trim()}' component(s) (valid indices 0..{matches.Count - 1}).";

            var component = matches[index];
            var so = new SerializedObject(component);

            var sb = new StringBuilder();
            sb.Append("Editable fields on ").Append(component.GetType().Name);
            if (matches.Count > 1) sb.Append(" [Index ").Append(index).Append(']');
            sb.Append(" (on '").Append(component.gameObject.name).Append("'):").AppendLine();

            string warning = OverrideWarningFor(component);
            if (warning != null) sb.Append("Note: ").AppendLine(warning);
            sb.AppendLine();

            int listed = 0;
            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;
                if (!ComponentPatch.IsCoercible(it.propertyType)) continue;

                listed++;
                sb.Append("  - ").Append(it.displayName)
                    .Append("  (Field \"").Append(it.propertyPath).Append("\", ").Append(it.propertyType).Append(')')
                    .Append(" = ").Append(ComponentPatch.Read(it));
                if (it.propertyType == SerializedPropertyType.Enum)
                    sb.Append("   options: ").Append(string.Join(" | ", it.enumDisplayNames));
                sb.AppendLine();
            }

            if (listed == 0)
                sb.AppendLine("  (no simple value fields to edit — this component only exposes object references, curves, or arrays)");
            else
                sb.AppendLine().AppendLine(CharacterEditPrompts.ReadComponentFieldsEditInstruction);

            return sb.ToString();
        }

        static string OverrideWarningFor(Component component)
        {
            switch (component)
            {
                case Rigidbody2D _:
                    return CharacterEditPrompts.Rigidbody2DOverrideWarning;
                default:
                    return null;
            }
        }

        #endregion

        #region Render edit & answer

        [AgentTool(
            CharacterEditPrompts.ToolRenderCharacterEdit,
            "BuildingBlocks.RenderCharacterEdit")]
        public static string RenderCharacterEdit(
            [ToolParameter(CharacterEditPrompts.ParamRenderCharacterEditUserPrompt)]
            string userPrompt,
            [ToolParameter(CharacterEditPrompts.ParamRenderCharacterEditRationale)]
            string rationale,
            [ToolParameter(CharacterEditPrompts.ParamRenderCharacterEditProposal)]
            CharacterEditProposal proposal)
        {
            var character = SelectedCharacter();
            if (character == null)
                return "No Building Blocks character is selected anymore — nothing to edit. Ask the user to re-select the character.";

            if (proposal == null)
                return "Proposal was null — nothing to render.";

            var result = new CharacterEditResult
            {
                TargetId = character.GetEntityId().ToString(),
                UserPrompt = userPrompt,
                Rationale = rationale,
            };

            if (!string.IsNullOrWhiteSpace(proposal.Name) && proposal.Name.Trim() != character.gameObject.name)
                result.NewName = proposal.Name.Trim();

            BuildStatChanges(character, proposal, result);

            result.OnEliminated = AgentToolHelpers.ParseEnumOrNull<EliminationBehavior>(proposal.OnEliminated);
            result.EliminationDelay = NonNegative(proposal.EliminationDelay);

            result.MovementEnabled = proposal.MovementEnabled;
            result.TargetingEnabled = proposal.TargetingEnabled;
            result.AttackEnabled = proposal.AttackEnabled;

            result.Gravity = proposal.Gravity;
            result.FallGravityMultiplier = proposal.FallGravityMultiplier;
            result.MaxFallSpeed = proposal.MaxFallSpeed;
            result.GroundSlopeLimit = proposal.GroundSlopeLimit;
            result.WallSlopeLimit = proposal.WallSlopeLimit;

            result.TargetingMode = AgentToolHelpers.ParseEnumOrNull<TargetingMode>(proposal.TargetingMode);
            if (!string.IsNullOrWhiteSpace(proposal.TargetTag)) result.TargetTag = proposal.TargetTag.Trim();
            result.TargetRadius = NonNegative(proposal.TargetRadius);

            var unresolved = new List<string>();
            ResolveAbilities(proposal, result, unresolved);

            if (proposal.Suggestions != null)
            {
                foreach (var suggestion in proposal.Suggestions)
                {
                    var s = AgentToolHelpers.Sanitize(suggestion);
                    if (s != null) result.Suggestions.Add(s);
                }
            }
            if (proposal.Tips != null)
            {
                foreach (var tip in proposal.Tips)
                    if (!string.IsNullOrWhiteSpace(tip)) result.Tips.Add(tip.Trim());
            }

            ResolveComponentEdits(character, proposal, result);

            if (!result.HasAnyChange && result.Suggestions.Count == 0)
                return "The proposal didn't change anything relative to the current character. Re-read the character and propose concrete deltas.";

            CharacterEditDispatch.Route(result);

            string label = character.gameObject.name;
            if (unresolved.Count > 0)
                return $"Rendered edit for '{label}'. {unresolved.Count} ability name(s) didn't resolve and were turned into suggestions. Waiting for user to Apply or Discard.";
            return $"Rendered edit for '{label}'. Waiting for user to Apply or Discard.";
        }

        [AgentTool(
            CharacterEditPrompts.ToolRenderCharacterAnswer,
            "BuildingBlocks.RenderCharacterAnswer")]
        public static string RenderCharacterAnswer(
            [ToolParameter(CharacterEditPrompts.ParamRenderCharacterAnswerUserPrompt)]
            string userPrompt,
            [ToolParameter(CharacterEditPrompts.ParamRenderCharacterAnswerAnswer)]
            string answer)
        {
            var character = SelectedCharacter();
            if (character == null)
                return "No Building Blocks character is selected anymore — nothing to answer about. Ask the user to re-select the character.";

            if (string.IsNullOrWhiteSpace(answer))
                return "Answer was empty — nothing to show. Re-read the character and answer the question concretely.";

            var result = new CharacterAnswerResult
            {
                TargetId = character.GetEntityId().ToString(),
                UserPrompt = userPrompt,
                Answer = answer.Trim(),
            };

            CharacterEditDispatch.RouteAnswer(result);

            return $"Answered question about '{character.gameObject.name}'.";
        }

        #endregion

        #region Edit resolution

        static void BuildStatChanges(BuildingBlocksCharacter character, CharacterEditProposal proposal, CharacterEditResult result)
        {
            if (proposal.Stats == null || proposal.Stats.Count == 0) return;

            var current = ReadCurrentStats(character);
            foreach (var edit in proposal.Stats)
            {
                if (edit == null) continue;
                if (!AgentToolHelpers.TryParseEnum(edit.Type, out StatType type)) continue;

                float newMax = Mathf.Max(0f, edit.MaxValue);
                bool isNew = !current.TryGetValue(type, out var cur);
                float newRegen = edit.RegenRate.HasValue
                    ? Mathf.Max(0f, edit.RegenRate.Value)
                    : (isNew ? 0f : cur.regen);

                var change = new StatChange
                {
                    Type = type,
                    IsNew = isNew,
                    NewMax = newMax,
                    NewRegen = newRegen,
                    OldMax = isNew ? 0f : cur.max,
                    OldRegen = isNew ? 0f : cur.regen,
                };

                if (!isNew && Mathf.Approximately(change.OldMax, change.NewMax) && Mathf.Approximately(change.OldRegen, change.NewRegen))
                    continue;

                result.StatChanges.Add(change);
            }
        }

        static void ResolveAbilities(CharacterEditProposal proposal, CharacterEditResult result, List<string> unresolved)
        {
            if (proposal.AddMovementAbilities != null)
            {
                foreach (var name in proposal.AddMovementAbilities)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var t = AgentToolHelpers.FindAbility<MovementAbility>(name.Trim());
                    if (t == null)
                    {
                        unresolved.Add(name.Trim());
                        result.Suggestions.Add(new SuggestedAbility
                        {
                            Title = name.Trim(),
                            Prompt = $"A movement ability: {name.Trim()}",
                        });
                    }
                    else if (!result.AddMovementAbilities.Contains(t))
                    {
                        result.AddMovementAbilities.Add(t);
                    }
                }
            }

            if (proposal.RemoveMovementAbilities != null)
            {
                foreach (var name in proposal.RemoveMovementAbilities)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var t = AgentToolHelpers.FindAbility<MovementAbility>(name.Trim());
                    if (t != null && !result.RemoveMovementAbilities.Contains(t))
                        result.RemoveMovementAbilities.Add(t);
                }
            }

            string attackName = proposal.AttackAbilityName?.Trim();
            if (!string.IsNullOrEmpty(attackName))
            {
                if (attackName.Equals("none", StringComparison.OrdinalIgnoreCase)
                    || attackName.Equals("clear", StringComparison.OrdinalIgnoreCase)
                    || attackName.Equals("(none)", StringComparison.OrdinalIgnoreCase))
                {
                    result.ClearAttack = true;
                }
                else
                {
                    var t = AgentToolHelpers.FindAbility<AttackAbility>(attackName);
                    if (t == null)
                    {
                        unresolved.Add(attackName);
                        result.Suggestions.Add(new SuggestedAbility
                        {
                            Title = attackName,
                            Prompt = $"An attack ability: {attackName}",
                        });
                    }
                    else
                    {
                        result.SetAttackAbility = t;
                    }
                }
            }
        }

        static void ResolveComponentEdits(BuildingBlocksCharacter character, CharacterEditProposal proposal, CharacterEditResult result)
        {
            if (proposal.ComponentEdits == null || proposal.ComponentEdits.Count == 0) return;

            foreach (var edit in proposal.ComponentEdits)
            {
                if (edit == null || string.IsNullOrWhiteSpace(edit.Component) || string.IsNullOrWhiteSpace(edit.Field))
                    continue;

                string typeName = edit.Component.Trim();
                var component = ComponentPatch.Find(character.gameObject, typeName, edit.Index);
                if (component == null)
                {
                    result.Tips.Add($"Couldn't find component '{typeName}' (index {edit.Index}) to edit '{edit.Field}' — skipped.");
                    continue;
                }

                var so = new SerializedObject(component);
                var prop = so.FindProperty(edit.Field.Trim());
                if (prop == null || !ComponentPatch.IsCoercible(prop.propertyType))
                {
                    result.Tips.Add($"'{edit.Field}' isn't an editable field on {component.GetType().Name} — skipped. Use BuildingBlocks.ReadComponentFields(\"{component.GetType().Name}\") for valid field names.");
                    continue;
                }

                string oldDisplay = ComponentPatch.Read(prop);
                if (!ComponentPatch.TryApply(prop, edit.Value, out string newDisplay))
                {
                    result.Tips.Add($"Couldn't read '{edit.Value}' as a {prop.propertyType} for {component.GetType().Name}.{edit.Field} — skipped.");
                    continue;
                }

                if (string.Equals(oldDisplay, newDisplay, StringComparison.Ordinal))
                    continue;

                result.ComponentChanges.Add(new ComponentFieldChange
                {
                    ComponentTypeName = component.GetType().Name,
                    ComponentIndex = edit.Index,
                    PropertyPath = edit.Field.Trim(),
                    Label = $"{component.GetType().Name} · {prop.displayName}",
                    OldDisplay = oldDisplay,
                    NewDisplay = newDisplay,
                    RawValue = edit.Value,
                });
            }
        }

        #endregion

        #region Helpers

        static BuildingBlocksCharacter SelectedCharacter()
        {
            var go = Selection.activeGameObject;
            return go != null ? go.GetComponent<BuildingBlocksCharacter>() : null;
        }

        static Dictionary<StatType, (float max, float regen)> ReadCurrentStats(BuildingBlocksCharacter character)
        {
            var map = new Dictionary<StatType, (float, float)>();
            var so = new SerializedObject(character);
            var statsProp = so.FindProperty(CharacterFields.Stats);
            if (statsProp == null) return map;

            for (int i = 0; i < statsProp.arraySize; i++)
            {
                var el = statsProp.GetArrayElementAtIndex(i);
                var typeProp = el.FindPropertyRelative(CharacterFields.Stat.Type);
                var maxProp = el.FindPropertyRelative(CharacterFields.Stat.MaxValue);
                var regenProp = el.FindPropertyRelative(CharacterFields.Stat.RegenRate);
                if (typeProp == null || maxProp == null) continue;
                if (!Enum.IsDefined(typeof(StatType), typeProp.intValue)) continue;
                var type = (StatType)typeProp.intValue;
                map[type] = (maxProp.floatValue, regenProp?.floatValue ?? 0f);
            }
            return map;
        }

        static float? NonNegative(float? value) => value.HasValue ? Mathf.Max(0f, value.Value) : null;

        #endregion
    }

    /// <summary>
    /// Lazily builds and runs the headless agent used by the Inspector edit flow, wired with the
    /// character-edit and reference tools.
    /// </summary>
    static class InspectorAgent
    {
        static IAgent s_Agent;

        public static Task<string> Run(string prompt, AssistantApi.AttachedContext context = null, CancellationToken ct = default)
            => GetAgent().RunHeadless(prompt, context, ct);

        static IAgent GetAgent() =>
            s_Agent ??= BuildingBlocksAgent.New()
                .WithToolsFrom<CharacterEditAgentTools>()
                .WithToolsFrom<ReferenceScriptsAgentTools>();
    }

    /// <summary>One stat's before and after values in an edit, and whether the stat is newly added.</summary>
    sealed class StatChange
    {
        public StatType Type;
        public bool IsNew;
        public float OldMax;
        public float NewMax;
        public float OldRegen;
        public float NewRegen;
    }

    /// <summary>
    /// The resolved, validated edit ready to show as a diff: concrete deltas, ability changes, component
    /// field changes, and any suggestions or tips. HasAnyChange reports whether there is anything to apply.
    /// </summary>
    sealed class CharacterEditResult
    {
        public string TargetId;
        public string UserPrompt;
        public string Rationale;

        public string NewName;

        public readonly List<StatChange> StatChanges = new();

        public EliminationBehavior? OnEliminated;
        public float? EliminationDelay;

        public bool? MovementEnabled;
        public bool? TargetingEnabled;
        public bool? AttackEnabled;

        public float? Gravity;
        public float? FallGravityMultiplier;
        public float? MaxFallSpeed;
        public float? GroundSlopeLimit;
        public float? WallSlopeLimit;

        public TargetingMode? TargetingMode;
        public string TargetTag;
        public float? TargetRadius;

        public readonly List<Type> AddMovementAbilities = new();
        public readonly List<Type> RemoveMovementAbilities = new();
        public Type SetAttackAbility;
        public bool ClearAttack;

        public readonly List<ComponentFieldChange> ComponentChanges = new();

        public readonly List<SuggestedAbility> Suggestions = new();
        public readonly List<string> Tips = new();

        public bool HasAnyChange =>
            StatChanges.Count > 0
            || NewName != null
            || OnEliminated.HasValue || EliminationDelay.HasValue
            || MovementEnabled.HasValue || TargetingEnabled.HasValue || AttackEnabled.HasValue
            || Gravity.HasValue || FallGravityMultiplier.HasValue || MaxFallSpeed.HasValue
            || GroundSlopeLimit.HasValue || WallSlopeLimit.HasValue
            || TargetingMode.HasValue || TargetTag != null || TargetRadius.HasValue
            || AddMovementAbilities.Count > 0 || RemoveMovementAbilities.Count > 0
            || SetAttackAbility != null || ClearAttack
            || ComponentChanges.Count > 0;
    }

    /// <summary>One resolved component field change with its old and new display values, ready to show in the diff and apply.</summary>
    sealed class ComponentFieldChange
    {
        public string ComponentTypeName;
        public int ComponentIndex;
        public string PropertyPath;
        public string Label;
        public string OldDisplay;
        public string NewDisplay;
        public string RawValue;
    }

    /// <summary>A read-only answer about the selected character, shown in the Inspector instead of an edit.</summary>
    sealed class CharacterAnswerResult
    {
        public string TargetId;
        public string UserPrompt;
        public string Answer;
    }
}

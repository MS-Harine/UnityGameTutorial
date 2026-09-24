using System;
using UnityEditor;
using Blocks.Attack;
using Blocks.Movement;
using Blocks.Character;
using Unity.Pipeline.Commands;
using System.Collections.Generic;

namespace Blocks.Cli
{
    /// <summary>
    /// bb_character_draft — turns command arguments into a character draft and opens it on the Creator's
    /// Review step.
    ///
    /// Nothing is created. The command fills in the same <see cref="CharacterSpec"/> the manual wizard and
    /// the AI Assisted sample build, hands it to the same wizard, and stops on the last step — so the
    /// Create button is still yours to press. That is the whole shape of this sample: a caller outside the
    /// Editor can propose, but only a person in the Editor can approve.
    ///
    /// It also sets how many arguments this command needs, which is: few. The draft arrives on a screen
    /// where every field is editable, so a flag only earns its place if a caller would want to state that
    /// choice up front. Everything else — elimination delay, targeting, sprite — is one click away in the
    /// wizard and is left out of the command surface deliberately.
    /// </summary>
    static class CharacterDraftCommand
    {
        [CliCommand("bb_character_draft", "Draft a character and open it on the Creator's Review step for approval. Creates nothing on its own.")]
        public static string Draft(
            [CliArg("name", "Name for the character.")]
            string name = "NewCharacter",
            [CliArg("kind", "Which preset to start from: player, enemy, or empty.")]
            string kind = "enemy",
            [CliArg("health", "Max health. Omit to keep the preset's value.")]
            float health = -1f,
            [CliArg("stamina", "Max stamina. Omit to keep the preset's value.")]
            float stamina = -1f,
            [CliArg("movement", "Movement ability class names, comma separated. Replaces the preset's. Run bb_list --detail true for the names in this project.")]
            string movement = null,
            [CliArg("attack", "Attack ability class name. Replaces the preset's. Run bb_list --detail true for the names in this project.")]
            string attack = null,
            [CliArg("on_eliminated", "What happens when health runs out: Respawn, Disable, or Destroy.")]
            string onEliminated = null)
        {
            var spec = new CharacterSpec();
            CharacterTemplates.ApplyTo(spec, ParseKind(kind));

            if (!string.IsNullOrWhiteSpace(name)) spec.Name = name.Trim();

            // A stat is only touched when a value was supplied, so --health 25 on the enemy preset changes
            // health and leaves everything else as the preset had it.
            if (health > 0f) SetStat(spec, StatType.Health, health, regen: 0f);
            if (stamina > 0f) SetStat(spec, StatType.Stamina, stamina, regen: 10f);

            if (!string.IsNullOrWhiteSpace(onEliminated))
                spec.OnEliminated = Flag.Parse<EliminationBehavior>(onEliminated, "--on_eliminated", "Respawn, Disable, or Destroy");

            var notes = new List<string>();
            ApplyAbilities(spec, movement, attack, notes);

            var wizard = CharacterWizard.Restore(spec);
            wizard.CurrentIndex = wizard.Steps.Count - 1;
            CreatorWindow.ShowWizard(wizard);

            return Report(spec, notes);
        }

        #region Spec assembly

        static void SetStat(CharacterSpec spec, StatType type, float max, float regen)
        {
            for (int i = 0; i < spec.Stats.Count; i++)
            {
                if (spec.Stats[i].Type != type) continue;
                spec.Stats[i].MaxValue = max;
                spec.Stats[i].StartValue = max;
                return;
            }
            spec.Stats.Add(new StatEntry(type, max, regen));
        }

        /// <summary>
        /// Resolves ability class names against the project.
        ///
        /// An unknown name is a note rather than a failure, unlike a misspelled enum. The difference is what
        /// the caller can do about it: enum values are fixed and listed in the error, while ability classes
        /// are project-specific and one may well have been generated minutes ago. Reporting the names that
        /// do exist, on a draft that opened anyway, beats refusing to draft anything.
        /// </summary>
        static void ApplyAbilities(CharacterSpec spec, string movement, string attack, List<string> notes)
        {
            if (!string.IsNullOrWhiteSpace(movement))
            {
                spec.MovementAbilityTypes.Clear();
                string[] names = movement.Split(',');
                for (int i = 0; i < names.Length; i++)
                {
                    string wanted = names[i].Trim();
                    if (wanted.Length == 0) continue;

                    Type resolved = AbilityLookup.Find<MovementAbility>(wanted);
                    if (resolved == null) notes.Add(Unknown<MovementAbility>(wanted, "movement"));
                    else if (!spec.MovementAbilityTypes.Contains(resolved)) spec.MovementAbilityTypes.Add(resolved);
                }
            }

            if (!string.IsNullOrWhiteSpace(attack))
            {
                string wanted = attack.Trim();
                Type resolved = AbilityLookup.Find<AttackAbility>(wanted);
                if (resolved == null) notes.Add(Unknown<AttackAbility>(wanted, "attack"));
                else spec.AttackAbilityType = resolved;
            }
        }

        static string Unknown<T>(string wanted, string kind) where T : CharacterAbility =>
            $"no {kind} ability named \"{wanted}\" — pick one in the wizard. " +
            $"Available: {Layout.Join(AbilityLookup.Names<T>())}";

        #endregion

        #region Parsing and reporting

        static CharacterTemplate ParseKind(string raw)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "player": return CharacterTemplate.Player;
                case "enemy": return CharacterTemplate.MeleeEnemy;
                case "empty": return CharacterTemplate.Empty;
                default: throw new ArgumentException($"Could not read --kind \"{raw}\". Use player, enemy, or empty.");
            }
        }

        static string Report(CharacterSpec spec, List<string> notes)
        {
            var stats = new List<string>(spec.Stats.Count);
            for (int i = 0; i < spec.Stats.Count; i++)
                stats.Add($"{spec.Stats[i].Type} {spec.Stats[i].MaxValue:0.#}");

            var movement = new List<string>(spec.MovementAbilityTypes.Count);
            for (int i = 0; i < spec.MovementAbilityTypes.Count; i++)
                movement.Add(spec.MovementAbilityTypes[i].Name);

            var labels = new List<string> { "kind", "stats", "movement", "attack", "on eliminated", "status" };
            var values = new List<string>
            {
                spec.Mode.ToString().ToLowerInvariant(),
                Layout.Join(stats),
                Layout.Join(movement),
                spec.AttackAbilityType != null ? spec.AttackAbilityType.Name : "none",
                spec.EliminationDelay > 0f ? $"{spec.OnEliminated} after {spec.EliminationDelay:0.##}s" : spec.OnEliminated.ToString(),
                "awaiting approval in the Editor · Creator window, Review step",
            };

            return DraftReport.Render($"draft  ·  {spec.Name}", labels, values, notes);
        }

        #endregion
    }

    /// <summary>
    /// bb_ability_draft — drafts a new ability script and opens it on the Creator's Review step, where the
    /// generated source is shown before anything is written to disk.
    ///
    /// Same contract as the character draft: the command proposes, the Create button in the wizard writes
    /// the <c>.cs</c> file. That ordering matters more here than for a character, because writing a script
    /// triggers a recompile — so a caller cannot chain work onto this and expect the same session.
    ///
    /// The lifecycle knobs a generated ability has — duration, input buffer, stat cost, which namespace and
    /// folder it lands in — are set by <c>--template</c> and shown on the Review step. They are not command
    /// arguments: a caller that cares about the buffer window is already in the wizard looking at the code.
    /// </summary>
    static class AbilityDraftCommand
    {
        [CliCommand("bb_ability_draft", "Draft a new ability script and open it on the Creator's Review step for approval. Writes nothing on its own.")]
        public static string Draft(
            [CliArg("name", "Class name for the ability.")]
            string name = "NewAbility",
            [CliArg("kind", "movement or attack.")]
            string kind = "movement",
            [CliArg("template", "Lifecycle shape: instant, burst, or hold. Sets the timing defaults.")]
            string template = "instant",
            [CliArg("trigger", "What starts it: input or auto. Omit to keep the template's.")]
            string trigger = null,
            [CliArg("cooldown", "Seconds between uses. 0 turns the cooldown off. Omit to keep the template's value.")]
            float cooldown = -1f,
            [CliArg("damage", "Damage per hit. Attack abilities only.")]
            float damage = -1f,
            [CliArg("range", "Reach in units. Attack abilities only.")]
            float range = -1f)
        {
            AbilityKind abilityKind = ParseKind(kind);

            // Constructing through the wizard first gives the spec the kind's default namespace and output
            // folder, so a caller that names neither still lands the script where the manual flow would.
            var wizard = new AbilityWizard(abilityKind);
            AbilityScriptSpec spec = wizard.Spec;
            AbilityTemplates.ApplyTo(spec, ParseTemplate(template));

            if (!string.IsNullOrWhiteSpace(name)) spec.ClassName = name.Trim();
            if (!string.IsNullOrWhiteSpace(trigger)) spec.Trigger = ParseTrigger(trigger);

            // Zero is meaningful — it turns the cooldown off — so "not supplied" has to be a negative value.
            if (cooldown >= 0f)
            {
                spec.Cooldown = cooldown;
                spec.UseCooldown = cooldown > 0f;
            }

            // Zero is a value the manual Creator accepts for both (its fields clamp with Mathf.Max(0f, v)),
            // so "not supplied" is the negative default and anything from zero up is the caller's choice.
            var notes = new List<string>();
            if (abilityKind == AbilityKind.Attack)
            {
                if (damage >= 0f) spec.Damage = damage;
                if (range >= 0f) spec.Range = range;
            }
            else if (damage >= 0f || range >= 0f)
            {
                notes.Add("--damage and --range are ignored for a movement ability");
            }

            wizard.CurrentIndex = wizard.Steps.Count - 1;
            CreatorWindow.ShowWizard(wizard);

            return Report(spec, notes);
        }

        #region Parsing and reporting

        static AbilityKind ParseKind(string raw) =>
            Flag.Parse<AbilityKind>(raw, "--kind", "movement or attack");

        static AbilityTemplate ParseTemplate(string raw)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "instant": return AbilityTemplate.InstantAction;
                case "burst": return AbilityTemplate.TimedBurst;
                case "hold": return AbilityTemplate.ContinuousHold;
                default: throw new ArgumentException($"Could not read --template \"{raw}\". Use instant, burst, or hold.");
            }
        }

        static AbilityTrigger ParseTrigger(string raw)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "input": return AbilityTrigger.OnInput;
                case "auto": return AbilityTrigger.Auto;
                default: throw new ArgumentException($"Could not read --trigger \"{raw}\". Use input or auto.");
            }
        }

        static string Report(AbilityScriptSpec spec, List<string> notes)
        {
            string className = AbilityScriptGenerator.SanitizeClassName(spec.ClassName, spec.Kind);

            var labels = new List<string> { "kind", "template", "trigger", "timing", "writes to", "status" };
            var values = new List<string>
            {
                spec.Kind.ToString().ToLowerInvariant(),
                spec.Template.ToString(),
                spec.Trigger == AbilityTrigger.OnInput ? $"input ({spec.Activation})" : "auto",
                Timing(spec),
                $"{spec.OutputFolderPath}/{className}.cs",
                "awaiting approval in the Editor · Creator window, Review step",
            };

            if (spec.Kind == AbilityKind.Attack)
            {
                labels.Add("attack");
                values.Add($"{spec.Damage:0.#} damage, {spec.Range:0.#} range");
            }

            return DraftReport.Render($"draft  ·  {className}", labels, values, notes);
        }

        static string Timing(AbilityScriptSpec spec)
        {
            var parts = new List<string>();
            if (spec.UseTimer) parts.Add($"{spec.Duration:0.##}s duration");
            if (spec.UseCooldown) parts.Add($"{spec.Cooldown:0.##}s cooldown");
            if (spec.UseInputBuffer) parts.Add($"{spec.InputBuffer:0.##}s buffer");
            return Layout.Join(parts);
        }

        #endregion
    }

    /// <summary>Reading an enum off a command line, the same way for every command that takes one.</summary>
    static class Flag
    {
        /// <summary>
        /// Parses an enum argument, or throws. Throwing is what makes the CLI report the call as failed —
        /// returning the message as text would print the complaint inside a success envelope, and a caller
        /// reading `success` would believe a draft it never got.
        ///
        /// The IsDefined check is the load-bearing half. Enum.TryParse also accepts a number, and it accepts
        /// numbers with no name in the enum: --kind 2 would parse to an AbilityKind that is neither Movement
        /// nor Attack, and nothing downstream tests for that. The generated script would derive from
        /// AttackAbility (codegen branches on "is it Movement"), while the Review step would hide the
        /// attack-only fields (it branches on "is it Attack"), leaving damage and range at their defaults
        /// with nowhere to see or change them. Rejecting the value here is the only place that is cheap.
        /// </summary>
        public static T Parse<T>(string raw, string flag, string allowed) where T : struct, Enum
        {
            string trimmed = (raw ?? string.Empty).Trim();
            if (Enum.TryParse(trimmed, ignoreCase: true, out T value) && Enum.IsDefined(typeof(T), value))
                return value;

            throw new ArgumentException($"Could not read {flag} \"{raw}\". Use {allowed}.");
        }
    }

    /// <summary>Shared rendering for the two draft reports, so both read the same in a terminal.</summary>
    static class DraftReport
    {
        public static string Render(string title, IReadOnlyList<string> labels, IReadOnlyList<string> values, IReadOnlyList<string> notes)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.Append(Layout.Section(title));
            sb.AppendLine();
            sb.Append(Layout.Rows(labels, values));

            if (notes == null || notes.Count == 0) return sb.ToString();

            sb.AppendLine();
            var noteLabels = new List<string>(notes.Count);
            for (int i = 0; i < notes.Count; i++)
                noteLabels.Add(i == 0 ? "note" : string.Empty);

            sb.Append(Layout.Rows(noteLabels, notes));
            return sb.ToString();
        }
    }

    /// <summary>
    /// Resolves ability classes by name. Abilities are discovered through <see cref="TypeCache"/> rather
    /// than a list, so anything the Creator generated is findable the moment it compiles — which is what
    /// lets bb_ability_draft and bb_character_draft be used one after the other.
    /// </summary>
    static class AbilityLookup
    {
        /// <summary>
        /// Finds the concrete <typeparamref name="T"/> named <paramref name="typeName"/>, preferring an exact
        /// match and accepting a case-insensitive one so a caller typing from memory still lands it.
        /// </summary>
        public static Type Find<T>(string typeName) where T : CharacterAbility
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            string wanted = typeName.Trim();

            Type loose = null;
            foreach (Type candidate in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (candidate.IsAbstract) continue;
                if (string.Equals(candidate.Name, wanted, StringComparison.Ordinal)) return candidate;
                if (loose == null && string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    loose = candidate;
            }
            return loose;
        }

        /// <summary>Every concrete <typeparamref name="T"/> in the project, sorted, for listing to a caller.</summary>
        public static List<string> Names<T>() where T : CharacterAbility
        {
            var names = new List<string>();
            foreach (Type candidate in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (candidate.IsAbstract) continue;
                names.Add(candidate.Name);
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }
}

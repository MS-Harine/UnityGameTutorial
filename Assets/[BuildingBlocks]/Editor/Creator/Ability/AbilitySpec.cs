using Blocks.Character;

namespace Blocks
{
    /// <summary>Whether an ability moves the character or deals damage.</summary>
    enum AbilityKind
    {
        Movement,
        Attack
    }

    /// <summary>The lifecycle shape of an ability: fire once, run for a fixed burst, or stay active while held.</summary>
    enum AbilityTemplate
    {
        InstantAction,
        TimedBurst,
        ContinuousHold
    }

    /// <summary>How an input-driven ability reacts to its key: act on press, or stay active while held.</summary>
    enum AbilityActivation
    {
        Press,
        Hold
    }

    /// <summary>What starts the ability: player input, or game logic with no input (enemies, ambient effects).</summary>
    enum AbilityTrigger
    {
        OnInput,
        Auto
    }

    /// <summary>
    /// All the choices that define a generated ability script: kind, template, naming, trigger, timing,
    /// stat cost, and attack values. Shared by the manual wizard and the AI flow, and consumed by the code
    /// generator. Marked serializable so the Creator window can carry an in-progress ability across a
    /// domain reload; every field here is a Unity-serializable primitive, string, or enum.
    /// </summary>
    [System.Serializable]
    sealed class AbilityScriptSpec
    {
        public AbilityKind Kind = AbilityKind.Movement;
        public AbilityTemplate Template = AbilityTemplate.InstantAction;

        public string ClassName = "NewAbility";
        public string Namespace = "Blocks.Movement.Examples";
        public string OutputFolderPath = "Assets/[BuildingBlocks]/Runtime/Movement/Examples";
        public string InputFieldName = "abilityAction";

        public AbilityTrigger Trigger = AbilityTrigger.OnInput;
        public AbilityActivation Activation = AbilityActivation.Press;

        public bool UseTimer;
        public bool AutoStop;
        public float Duration = 0.25f;

        public bool UseCooldown;
        public float Cooldown = 0.5f;

        public bool UseInputBuffer;
        public float InputBuffer = 0.1f;

        public bool UseStat;
        public StatType StatType = StatType.Stamina;
        public float StatCost = 10f;
        public bool StatPerSecond;

        public float Damage = 1f;
        public float Range = 1.5f;

        /// <summary>
        /// Where the last Create wrote this ability's script — or, while <see cref="AiWritePending"/> is
        /// set, where the Assistant's write will land. Empty before the first Create. Drives the Review
        /// step's post-creation panel, and being serialized it survives the domain reload the new script
        /// itself triggers — without it the panel would vanish the moment the script compiled.
        /// </summary>
        public string CreatedScriptPath = "";

        /// <summary>
        /// True from "Let AI write the logic" until the Assistant's file shows up at
        /// <see cref="CreatedScriptPath"/>. While set, the Review step explains the wait instead of
        /// claiming the script exists, and the Creator window knows to restore this spec onto the Review
        /// step after the reload the write triggers.
        /// </summary>
        public bool AiWritePending;
    }

    /// <summary>
    /// Implemented by the AI sample's ability review wizard so the Creator window can carry its spec
    /// across a domain reload without the core assembly referencing the sample. The Assistant's script
    /// write triggers that reload; the window restores the spec onto the standard wizard's Review step,
    /// whose created-script panel takes over from there.
    /// </summary>
    interface IAiAbilityWizard
    {
        AbilityScriptSpec Spec { get; }
    }

    /// <summary>
    /// Applies a template's default timing, cooldown, and activation values to a spec when the user picks it.
    /// </summary>
    static class AbilityTemplates
    {
        public static void ApplyTo(AbilityScriptSpec spec, AbilityTemplate template)
        {
            spec.Template = template;

            switch (template)
            {
                case AbilityTemplate.InstantAction:
                    spec.Activation = AbilityActivation.Press;
                    spec.UseTimer = false;
                    spec.AutoStop = false;
                    spec.UseCooldown = true;
                    spec.Cooldown = 0.5f;
                    spec.UseInputBuffer = true;
                    spec.InputBuffer = 0.1f;
                    break;

                case AbilityTemplate.TimedBurst:
                    spec.Activation = AbilityActivation.Press;
                    spec.UseTimer = true;
                    spec.AutoStop = true;
                    spec.Duration = 0.25f;
                    spec.UseCooldown = true;
                    spec.Cooldown = 1.0f;
                    spec.UseInputBuffer = true;
                    spec.InputBuffer = 0.1f;
                    break;

                case AbilityTemplate.ContinuousHold:
                    spec.Activation = AbilityActivation.Hold;
                    spec.UseTimer = false;
                    spec.AutoStop = false;
                    spec.UseCooldown = false;
                    spec.UseInputBuffer = false;
                    break;
            }
        }
    }
}

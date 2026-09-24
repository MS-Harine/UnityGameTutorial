using UnityEditor;

namespace Blocks
{
    /// <summary>
    /// Wires the AI Assisted layer into the Creator and the character inspector, and owns the card instances
    /// so their draft state survives window rebuilds.
    ///
    /// This is the whole seam between the base template and the AI layer: nothing in the Creator or the
    /// inspector names anything here, so with this code absent the AI tab and the Assistant card simply do
    /// not exist — which is what lets the AI layer ship as an optional sample.
    ///
    /// The menu item lives in <c>Setup/AiSetup.cs</c>, the sample's ungated half, so it exists before the
    /// Assistant package is installed and can open the stand-in tab that installs it. Both register the tab
    /// under the same name, never at the same time.
    /// </summary>
    [InitializeOnLoad]
    static class AiHome
    {
        public const string TabName = "AI Assisted";

        // Held so StartAbility drives the same instances the tab shows, drafts and all.
        static readonly AbilityAiCard k_Movement = new AbilityAiCard(AbilityKind.Movement);
        static readonly AbilityAiCard k_Attack = new AbilityAiCard(AbilityKind.Attack);

        static AiHome()
        {
            CreatorWindow.RegisterTab(TabName, 10, new CharacterAiCard(), k_Movement, k_Attack);
            BuildingBlocksCharacterEditor.RegisterCard(-10, () => new AssistantCard());
        }

        /// <summary>Opens the Creator on the AI Assisted tab.</summary>
        public static void Show() => CreatorWindow.ShowHomeOnTab(TabName);

        /// <summary>
        /// Submits a prompt to the ability card for the given kind, so the character flow can hand off an
        /// ability the assistant suggested without the user retyping it.
        /// </summary>
        public static void StartAbility(AbilityKind kind, string prompt) =>
            (kind == AbilityKind.Movement ? k_Movement : k_Attack).Start(prompt);
    }
}

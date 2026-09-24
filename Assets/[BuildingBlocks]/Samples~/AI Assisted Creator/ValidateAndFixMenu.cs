using System.Text;
using UnityEditor;
using UnityEngine;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// Adds "Skill: Validate and fix" to the Building Blocks Character component's context menu. Selects the
    /// character, snapshots its GameObject, and hands the Assistant a prompt that runs the validation skill
    /// against it — the skill audits the object and repairs it with the Assistant's built-in editor tools.
    /// </summary>
    static class ValidateAndFixMenu
    {
        const string MenuPath = "CONTEXT/BuildingBlocksCharacter/◆ Skill: Validate and fix";

        [MenuItem(MenuPath, false, 2000)]
        static void ValidateAndFix(MenuCommand command)
        {
            if (command.context is not BuildingBlocksCharacter character || character == null)
                return;

            var go = character.gameObject;
            Selection.activeGameObject = go;

            AssistantChat.Run(
                AgentPrompts.ValidatePrompt,
                DescribeGameObject(go),
                $"{go.name} — current setup");
        }

        /// <summary>
        /// A text snapshot of the character for the Assistant: name, active state, tag, and the components on
        /// the object and its children (flagging disabled ones). This carries the context so the chat message
        /// itself can stay short.
        /// </summary>
        static string DescribeGameObject(GameObject go)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Building Blocks character to validate and fix:");
            sb.AppendLine();
            sb.Append("GameObject: ").AppendLine(go.name);
            sb.Append("Active: ").AppendLine(go.activeSelf ? "true" : "false (disabled)");
            sb.Append("Tag: ").AppendLine(go.CompareTag("Untagged") ? "Untagged" : go.tag);
            sb.AppendLine();
            AppendComponents(sb, go.transform, 0);
            return sb.ToString();
        }

        /// <summary>Lists a transform's components, then recurses into its children with deeper indentation.</summary>
        static void AppendComponents(StringBuilder sb, Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);

            if (depth == 0)
                sb.AppendLine("Components:");
            else
                sb.Append(indent).Append("Child \"").Append(t.name).AppendLine("\":");

            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) { sb.Append(indent).AppendLine("  - <missing script>"); continue; }
                sb.Append(indent).Append("  - ").Append(c.GetType().Name);
                if (c is Behaviour behaviour && !behaviour.enabled) sb.Append("  (disabled)");
                else if (c is Renderer renderer && !renderer.enabled) sb.Append("  (disabled)");
                sb.AppendLine();
            }

            foreach (Transform child in t)
                AppendComponents(sb, child, depth + 1);
        }
    }
}

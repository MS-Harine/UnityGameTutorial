using System.Text;
using UnityEngine.InputSystem;

namespace Blocks.HUD
{
    /// <summary>
    /// Writes an action's bindings out as text for the UI ("WASD, Arrow Keys"), so nothing on screen has
    /// to be typed out by hand and kept in step with InputSystem_Actions.
    ///
    /// Keyboard and mouse only: gamepad prompts aren't handled yet, so a gamepad binding is left out
    /// rather than printed as a raw control path.
    /// </summary>
    public static class InputBindingText
    {
        static readonly string[] k_AllowedDeviceLayouts = { "Keyboard", "Mouse" };
        static readonly StringBuilder s_Builder = new StringBuilder();

        /// <summary>Every keyboard and mouse binding on the action, joined by <paramref name="separator"/>.</summary>
        public static string ForAction(InputAction action, string separator = ", ")
        {
            if (action == null) return string.Empty;

            s_Builder.Clear();

            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];

                // A composite (Move's WASD) carries no device of its own, only its parts do, so the
                // composite itself is skipped and its parts supply the keys.
                if (binding.isComposite) continue;
                if (!IsAllowedDevice(binding.effectivePath)) continue;

                if (s_Builder.Length > 0) s_Builder.Append(separator);
                s_Builder.Append(action.GetBindingDisplayString(i));
            }

            return s_Builder.ToString();
        }

        /// <summary>
        /// Just the first keyboard or mouse binding, for places with room for one key: the corner hint
        /// reads better as "Esc" than as every key that happens to be bound to pause.
        /// </summary>
        public static string FirstBinding(InputAction action)
        {
            if (action == null) return string.Empty;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite) continue;
                if (!IsAllowedDevice(binding.effectivePath)) continue;

                return action.GetBindingDisplayString(i);
            }

            return string.Empty;
        }

        static bool IsAllowedDevice(string effectivePath)
        {
            string layout = InputControlPath.TryGetDeviceLayout(effectivePath);
            if (layout == null) return false;

            foreach (string allowed in k_AllowedDeviceLayouts)
            {
                if (layout == allowed) return true;
            }

            return false;
        }
    }
}

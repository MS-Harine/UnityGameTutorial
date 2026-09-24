using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// Attaches this sample's stylesheet to the elements it builds. The sheet ships with the sample rather
    /// than with the template, so the base stylesheets carry no rules for UI that isn't there.
    ///
    /// Applied per element instead of to the window root, because the sample doesn't own either root it
    /// draws into. That also puts its rules after the base sheets in the cascade, so the AI card's styling
    /// wins ties against the generic card rules it builds on — which is what the single shared sheet used to
    /// do by import order. The colour tokens still resolve from the root, so light and dark keep working.
    /// </summary>
    static class AiStyles
    {
        const string k_SheetName = "Styles/ai-input.uss";

        static StyleSheet s_Sheet;
        static bool s_Searched;

        public static void Apply(VisualElement element)
        {
            StyleSheet sheet = Sheet();
            if (sheet != null && !element.styleSheets.Contains(sheet)) element.styleSheets.Add(sheet);
        }

        // Located through the sample's own asmdef rather than a hardcoded path, because a sample lands under
        // Assets/Samples/<package>/<version>/<sample>/ — a path that carries the package version in it.
        static StyleSheet Sheet()
        {
            if (s_Searched) return s_Sheet;
            s_Searched = true;

            foreach (string guid in AssetDatabase.FindAssets("Blocks.AI.Editor t:asmdef"))
            {
                string folder = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
                if (string.IsNullOrEmpty(folder)) continue;

                s_Sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    $"{folder.Replace('\\', '/')}/{k_SheetName}");
                if (s_Sheet != null) return s_Sheet;
            }

            return s_Sheet;
        }
    }
}

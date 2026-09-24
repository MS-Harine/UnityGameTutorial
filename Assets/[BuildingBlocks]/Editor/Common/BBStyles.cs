using UnityEditor;
using UnityEngine.UIElements;

namespace Blocks
{

    /// <summary>
    /// Attaches the Building Blocks editor stylesheets to a UI Toolkit root, one entry point per surface
    /// (shared editor chrome, the Creator window, and the Readme).
    ///
    /// core.uss holds the dark color tokens; core-light.uss redefines them for the Unity light skin.
    /// When the editor is in light mode the light sheet is layered on top of the base sheets so its
    /// :root wins; in dark mode it is removed and the dark tokens show through. The choice re-checks
    /// live when the user changes Settings > Editor Theme.
    /// </summary>
    public static class BBStyles
    {
        // Unity raises no "skin changed" event, so themed roots poll on their own scheduler; the
        // schedule stops automatically when the panel closes.
        const long k_ThemePollMs = 500;

        public static void Apply(VisualElement root)
        {
            AddSheet(root, "Common/Styles/BlocksEditor.uss");
            ApplyThemeSwitching(root);
        }

        public static void ApplyCreatorWindow(VisualElement root)
        {
            AddSheet(root, "Common/Styles/BlocksCreator.uss");
            ApplyThemeSwitching(root);
        }

        public static void ApplyReadme(VisualElement root)
        {
            AddSheet(root, "Common/Styles/BlocksReadme.uss");
            ApplyThemeSwitching(root);
        }

        static void ApplyThemeSwitching(VisualElement root)
        {
            StyleSheet lightSheet = LoadSheet("Common/Styles/core-light.uss");
            if (lightSheet == null) return;

            SyncLightSheet(root, lightSheet);
            root.schedule.Execute(() => SyncLightSheet(root, lightSheet)).Every(k_ThemePollMs);
        }

        // Present core-light.uss only in light mode so the dark tokens in core.uss show through
        // otherwise. Add/remove is idempotent, so this is safe to call on every poll.
        static void SyncLightSheet(VisualElement root, StyleSheet lightSheet)
        {
            bool wantLight = !EditorGUIUtility.isProSkin;
            bool hasLight = root.styleSheets.Contains(lightSheet);

            if (wantLight && !hasLight) root.styleSheets.Add(lightSheet);
            else if (!wantLight && hasLight) root.styleSheets.Remove(lightSheet);
        }

        static StyleSheet LoadSheet(string relativePath)
        {
            return AssetDatabase.LoadAssetAtPath<StyleSheet>($"{EditorPaths.EditorFolder}/{relativePath}");
        }

        static void AddSheet(VisualElement root, string relativePath)
        {
            StyleSheet sheet = LoadSheet(relativePath);
            if (sheet != null) root.styleSheets.Add(sheet);
        }
    }
}

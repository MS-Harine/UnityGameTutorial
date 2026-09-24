using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// Custom inspector for a Readme asset. Owns the surface — auto-selecting the readme once per session,
    /// the menu item that brings it back, and the footer — and hands the content to
    /// <see cref="ReadmeView"/> to draw.
    /// </summary>
    [CustomEditor(typeof(Readme))]
    [InitializeOnLoad]
    sealed class ReadmeEditor : Editor
    {
        const string k_ShowedReadmeSessionStateName = "ReadmeEditor.showedReadme";

        const string k_ReadmeFolderPath = "Assets/[BuildingBlocks]/Editor/Readme";

        const string k_LayoutAssetPath = k_ReadmeFolderPath + "/Layout.wlt";

        const string k_ReadmeAssetPath = "Assets/Readme.asset";

        #region Opening

        const string k_OpenMenuPath = "Building Blocks/Open Readme";

        static ReadmeEditor()
        {
            EditorApplication.delayCall += SelectReadmeAutomatically;
        }

        /// <summary>
        /// Brings the readme back after it has been clicked away. Selecting an asset is the only route
        /// back to a custom inspector, and without this there was none — testers who dismissed the readme
        /// on day one never saw it again.
        ///
        /// The menu entry lives and dies with this script: "Remove Readme Assets" deletes the whole
        /// readme folder, and the recompile that follows drops the entry with it.
        /// </summary>
        [MenuItem(k_OpenMenuPath)]
        public static void Open()
        {
            if (SelectReadme() == null)
                Debug.LogWarning("[BuildingBlocks] No Readme asset found in this project.");
        }

        /// <summary>
        /// Pulls the menu entry the moment removal runs, covering the gap between deleting the readme
        /// folder and the recompile that drops the attribute-registered entry for good. Menu.RemoveMenuItem
        /// and the native menu-bar sync are internal, hence the reflection; if either lookup fails the
        /// entry simply lingers until that recompile lands seconds later.
        /// </summary>
        static void RemoveOpenMenu()
        {
            typeof(Menu).GetMethod("RemoveMenuItem", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, new object[] { k_OpenMenuPath });
            typeof(EditorUtility).GetMethod("Internal_UpdateAllMenus", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, null);
        }

        static void SelectReadmeAutomatically()
        {
            if (SessionState.GetBool(k_ShowedReadmeSessionStateName, false)) return;

            Readme readme = SelectReadme();
            SessionState.SetBool(k_ShowedReadmeSessionStateName, true);

            if (readme == null || ReadmeProgress.HasLoadedLayout) return;

            LoadLayout();
            ReadmeProgress.HasLoadedLayout = true;
        }

        static void LoadLayout()
        {
            // UnityEditor.WindowLayout is internal, so the layout is loaded through reflection.
            // The overload must be resolved by explicit parameter types: Unity 6000.5 ships two
            // public LoadWindowLayout overloads, and a name-only lookup throws AmbiguousMatchException.
            // If the API drifts again, the null checks skip the load quietly — the readme still opens,
            // the user just keeps their current layout.
            var windowLayoutType = typeof(EditorApplication).Assembly.GetType("UnityEditor.WindowLayout", true);
            var flagsType = windowLayoutType.GetNestedType("LoadWindowLayoutFlags");
            if (flagsType == null) return;

            var method = windowLayoutType.GetMethod("LoadWindowLayout",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), flagsType }, null);
            if (method == null) return;

            object flags = System.Enum.Parse(flagsType, "KeepMainWindow, LogsErrorToConsole");
            method.Invoke(null, new object[] { Path.GetFullPath(k_LayoutAssetPath), flags });
        }

        static Readme SelectReadme()
        {
            string[] readmeGuids = AssetDatabase.FindAssets("t:Readme");

            foreach (string guid in readmeGuids)
            {
                if (AssetDatabase.GUIDToAssetPath(guid) != k_ReadmeAssetPath) continue;

                Readme readme = Show(k_ReadmeAssetPath);
                if (readme != null) return readme;
            }

            if (readmeGuids.Length > 0)
                return Show(AssetDatabase.GUIDToAssetPath(readmeGuids[0]));

            return null;
        }

        static Readme Show(string path)
        {
            var readme = AssetDatabase.LoadAssetAtPath<Readme>(path);
            if (readme == null) return null;

            Selection.objects = new UnityEngine.Object[] { readme };
            return readme;
        }

        #endregion

        #region Inspector UI

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList("blocks-readme");
            BBStyles.ApplyReadme(root);

            Rebuild(root);
            return root;
        }

        // Marking a step done changes the progress count and can hide a card, so the view is rebuilt
        // whole rather than patched. Cheap at this size, and nothing can drift out of sync.
        void Rebuild(VisualElement root)
        {
            root.Clear();
            root.Add(ReadmeView.Build((Readme)target, () => Rebuild(root)));
            root.Add(BuildFooter());
        }

        #endregion

        #region Footer

        static VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList("blocks-readme__footer");

            var hint = new Label("Reopen any time: Building Blocks › Open Readme");
            hint.AddToClassList("blocks-readme__hint");
            footer.Add(hint);

            var remove = new Button(RemoveTutorial) { text = "Remove Readme Assets" };
            remove.AddToClassList("blocks-button--quiet");
            footer.Add(remove);

            return footer;
        }

        static void RemoveTutorial()
        {
            if (!EditorUtility.DisplayDialog("Remove Readme Assets",
                "This removes the tutorial completely: the Readme, its saved progress, the Creator " +
                "banner and menu entry that open it, and the readme scripts and window layout " +
                "(the whole Editor/Readme folder). The Creator and its wizards are unaffected. Continue?",
                "Proceed",
                "Cancel"))
            {
                return;
            }

            // The readme being removed is what the Inspector is showing right now — deselect first so no
            // ghost editor is left pointing at a destroyed asset.
            Selection.activeObject = null;

            string[] readmeGuids = AssetDatabase.FindAssets("t:Readme");
            foreach (string guid in readmeGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                // Progress keys are derived from the readme's section ids, so they must be cleared while
                // the asset can still be loaded.
                ReadmeProgress.Forget(AssetDatabase.LoadAssetAtPath<Readme>(path));
                AssetDatabase.DeleteAsset(path);
            }

            SessionState.EraseBool(k_ShowedReadmeSessionStateName);
            RemoveOpenMenu();
            CreatorWindow.RefreshHomeIfShowing();

            // Deleted last, because this folder contains the very scripts running this method — including
            // this one. That is safe: the loaded assembly stays in memory until the recompile that follows
            // the deletion, by which point nothing outside the folder references these types. The layout
            // file lives inside, so this also removes it.
            if (!AssetDatabase.DeleteAsset(k_ReadmeFolderPath))
                Debug.Log($"Could not delete the readme folder at {k_ReadmeFolderPath}");
        }

        #endregion
    }
}

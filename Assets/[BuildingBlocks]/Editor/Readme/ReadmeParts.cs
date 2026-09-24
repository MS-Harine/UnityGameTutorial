using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// The non-visual services the Readme view calls out to.
//   - ReadmeProgress: which steps this user has marked done, and the two other per-user flags.
//   - ReadmeLink: turns a target string into something clickable, and imports the optional samples.
//   - ReadmeText: renders body text, resolving inline [[label|target]] links.
namespace Blocks
{
    #region Progress

    /// <summary>
    /// Per-user, per-project Readme state: which steps are marked done, whether completed steps are
    /// hidden, and whether the tutorial window layout has been loaded once.
    ///
    /// All of it lives in <see cref="EditorUserSettings"/> — the project's own UserSettings folder —
    /// rather than on the Readme asset or in <see cref="EditorPrefs"/>. A flag serialized on the asset
    /// ships with whatever the dev project last wrote to it — which is exactly how the template came to
    /// ship with its layout already marked as loaded. EditorPrefs is machine-global, so state keyed by
    /// project path resurrects when a deleted project is recreated at the same path — which is exactly
    /// how a freshly created test project came to show every step already done. UserSettings dies with
    /// the project folder and is excluded from the template pack, so neither leak can happen.
    /// </summary>
    static class ReadmeProgress
    {
        const string k_Prefix = "Blocks.Readme";

        public static bool IsDone(string sectionId) =>
            !string.IsNullOrEmpty(sectionId) && GetFlag($"done.{sectionId}");

        public static void SetDone(string sectionId, bool isDone)
        {
            if (string.IsNullOrEmpty(sectionId)) return;

            SetFlag($"done.{sectionId}", isDone);
        }

        public static bool HideCompleted
        {
            get => GetFlag("hideCompleted");
            set => SetFlag("hideCompleted", value);
        }

        /// <summary>
        /// Whether a collapsible card is open. Persisted so that marking a step done — which rebuilds the
        /// whole inspector — doesn't close a reference card the reader is reading.
        /// </summary>
        public static bool IsOpen(string sectionId) =>
            !string.IsNullOrEmpty(sectionId) && GetFlag($"open.{sectionId}");

        public static void SetOpen(string sectionId, bool isOpen)
        {
            if (string.IsNullOrEmpty(sectionId)) return;

            SetFlag($"open.{sectionId}", isOpen);
        }

        /// <summary>Whether the tutorial window layout has already been loaded for this project.</summary>
        public static bool HasLoadedLayout
        {
            get => GetFlag("loadedLayout");
            set => SetFlag("loadedLayout", value);
        }

        /// <summary>
        /// Deletes every flag stored for the given readme. Called by "Remove Readme Assets" so nothing
        /// lingers in the project's UserSettings after the readme itself is gone.
        /// </summary>
        public static void Forget(Readme readme)
        {
            if (readme != null && readme.sections != null)
            {
                foreach (var section in readme.sections)
                {
                    if (section == null || string.IsNullOrEmpty(section.id)) continue;

                    SetFlag($"done.{section.id}", false);
                    SetFlag($"open.{section.id}", false);
                }
            }

            SetFlag("hideCompleted", false);
            SetFlag("loadedLayout", false);
        }

        static bool GetFlag(string name) => EditorUserSettings.GetConfigValue(Key(name)) == "1";

        // Storing false as null removes the entry, so an unset flag and a cleared flag look the same.
        static void SetFlag(string name, bool value) =>
            EditorUserSettings.SetConfigValue(Key(name), value ? "1" : null);

        static string Key(string name) => $"{k_Prefix}.{name}";
    }

    #endregion

    #region Links

    /// <summary>
    /// Resolves a Readme link target to the action it names, and handles the two-state sample button.
    /// See <see cref="Readme.Link.target"/> for the target forms.
    /// </summary>
    static class ReadmeLink
    {
        /// <summary>Whether the target names something this can actually open.</summary>
        public static bool CanFollow(string target) => !string.IsNullOrEmpty(target);

        public static void Follow(string target)
        {
            if (string.IsNullOrEmpty(target)) return;

            int split = target.IndexOf(':');
            if (split <= 0)
            {
                Debug.LogWarning($"[BuildingBlocks] Readme link target \"{target}\" has no scheme. " +
                                 "Expected one of menu:, guid:, path:, type:, url:, settings:.");
                return;
            }

            string scheme = target.Substring(0, split);
            string value = target.Substring(split + 1);

            switch (scheme)
            {
                case "menu":
                    // A sample's menu item can lag its import: the AI sample's real tab compiles only once
                    // the Assistant package has been added, and until then its menu path does not exist.
                    if (!EditorApplication.ExecuteMenuItem(value))
                        Debug.LogWarning($"[BuildingBlocks] Menu item \"{value}\" is not available yet. " +
                                         "If a sample was just imported, wait for Unity to finish " +
                                         "compiling and try again.");
                    break;

                case "url":
                    // Split on the *first* colon, so value still carries the "https://" intact.
                    Application.OpenURL(value);
                    break;

                case "guid":
                    Reveal(AssetDatabase.GUIDToAssetPath(value), target);
                    break;

                case "path":
                    Reveal(value, target);
                    break;

                case "type":
                    RevealScript(value);
                    break;

                case "settings":
                    SettingsService.OpenProjectSettings(value);
                    break;

                default:
                    Debug.LogWarning($"[BuildingBlocks] Readme link target \"{target}\" uses an unknown " +
                                     $"scheme \"{scheme}\".");
                    break;
            }
        }

        static void Reveal(string assetPath, string target)
        {
            var asset = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

            if (asset == null)
            {
                Debug.LogWarning($"[BuildingBlocks] Readme link target \"{target}\" points at nothing.");
                return;
            }

            if (asset is SceneAsset)
            {
                OpenScene(assetPath);
                return;
            }

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                OpenFolder(asset);
                return;
            }

            Ping(asset);
        }

        /// <summary>
        /// A folder link opens the folder's contents in the Project window — the double-click behavior —
        /// rather than pinging its closed icon inside the parent, which left readers one step short of
        /// the files the button promised. Unity has no public API for this (AssetDatabase.OpenAsset
        /// selects the folder, which would replace this readme in the Inspector), so it drives the
        /// browser's internal ShowFolderContents. In the one-column layout, or if the internals ever
        /// move, it falls back to the ping, which there expands the same folder in the tree.
        /// </summary>
        static void OpenFolder(UnityEngine.Object folder)
        {
            EditorUtility.FocusProjectWindow();

            var browserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
            MethodInfo showContents = browserType?.GetMethod("ShowFolderContents",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo viewMode = browserType?.GetField("m_ViewMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            EditorWindow browser = browserType != null ? EditorWindow.GetWindow(browserType) : null;

            bool isTwoColumns = browser != null && viewMode != null
                && viewMode.GetValue(browser)?.ToString() == "TwoColumns";

            if (showContents == null || !isTwoColumns)
            {
                Ping(folder);
                return;
            }

            showContents.Invoke(browser, new object[] { folder.GetEntityId(), true });
        }

        /// <summary>
        /// Highlights an asset in the Project window without selecting it. Selecting would replace this
        /// readme in the Inspector, making the first link click the end of the tutorial. From the ping the
        /// reader clicks through themselves when they mean to leave.
        /// </summary>
        static void Ping(UnityEngine.Object asset)
        {
            EditorUtility.FocusProjectWindow();
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>A scene link opens the scene, which is what its button says, and leaves the readme up.</summary>
        static void OpenScene(string path)
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[BuildingBlocks] Exit Play mode before opening a scene from the Readme.");
                return;
            }

            if (SceneManager.GetActiveScene().path == path) return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.OpenScene(path);
        }

        /// <summary>
        /// Finds the script defining a type and pings it. Resolved by type name rather than by a
        /// hardcoded GUID so that a renamed script, or one the user wrote themselves, still resolves,
        /// which is what lets the composition table eventually list a user's own abilities.
        /// </summary>
        static void RevealScript(string typeName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{typeName} t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;

                Type type = script.GetClass();
                if (type == null || type.Name != typeName) continue;

                Ping(script);
                return;
            }

            Debug.LogWarning($"[BuildingBlocks] Readme could not find a script defining \"{typeName}\".");
        }

        /// <summary>
        /// What a sample link should read and do right now. Returns false when there is nothing to show:
        /// the sample is already in and the link has no follow-up action, or it is not shipped in this
        /// project at all.
        /// The import itself is <see cref="SampleImporter"/>'s; a link only says which sample it wants.
        /// </summary>
        public static bool TryResolveSample(Readme.Link link, out string label, out Action onClick)
        {
            label = link.text;
            onClick = null;

            if (!SampleImporter.IsImported(link.sampleName))
            {
                if (!SampleImporter.IsAvailable(link.sampleName)) return false;

                string name = link.sampleName;
                onClick = () => SampleImporter.Import(name);
                return true;
            }

            if (!CanFollow(link.target)) return false;

            if (!string.IsNullOrEmpty(link.importedText)) label = link.importedText;

            string target = link.target;
            onClick = () => Follow(target);
            return true;
        }
    }

    #endregion

    #region Inline text

    /// <summary>
    /// Renders Readme body text, resolving inline links written as <c>[[label|target]]</c>.
    ///
    /// UI Toolkit has no public API for clicking a rich-text <c>&lt;link&gt;</c> tag — the events exist in
    /// UnityEngine.UIElementsModule but are internal — so a linked sentence has to be assembled from real
    /// elements: one label per word plus a button per link, in a wrapping row. Text with no link markup
    /// takes the plain path and stays a single <see cref="Label"/>, which is both cheaper and wraps
    /// better, so this only costs anything on the handful of sentences that need it.
    ///
    /// One constraint follows from the split: a rich-text tag cannot span a space inside marked-up text,
    /// because each word label parses its own tags. Keep <c>&lt;b&gt;</c> to single words in any paragraph
    /// that also carries a link, or split it into two blocks.
    /// </summary>
    static class ReadmeText
    {
        const string k_Open = "[[";
        const string k_Close = "]]";

        /// <summary>Space added after a word or link that was followed by whitespace in the source.</summary>
        const float k_SpaceWidth = 4f;

        public static VisualElement Build(string text, string bodyClass, Action<string> onFollow)
        {
            if (string.IsNullOrEmpty(text)) return null;

            if (text.IndexOf(k_Open, StringComparison.Ordinal) < 0)
            {
                var plain = new Label(text);
                plain.AddToClassList(bodyClass);
                return plain;
            }

            // The body class goes on the row, not on each word: font size and colour inherit, while the
            // class's own margin is applied once. Stamping it per word would give every wrapped line the
            // paragraph's top margin.
            var row = new VisualElement();
            row.AddToClassList(bodyClass);
            row.AddToClassList("blocks-readme__inline");

            int cursor = 0;
            while (cursor < text.Length)
            {
                int open = text.IndexOf(k_Open, cursor, StringComparison.Ordinal);
                if (open < 0)
                {
                    AddWords(row, text.Substring(cursor));
                    break;
                }

                int close = text.IndexOf(k_Close, open, StringComparison.Ordinal);
                if (close < 0)
                {
                    // Unbalanced markup: show the rest verbatim rather than swallowing it.
                    AddWords(row, text.Substring(cursor));
                    break;
                }

                AddWords(row, text.Substring(cursor, open - cursor));

                string body = text.Substring(open + k_Open.Length, close - open - k_Open.Length);
                cursor = close + k_Close.Length;

                int pipe = body.IndexOf('|');
                string label = pipe < 0 ? body : body.Substring(0, pipe);
                string target = pipe < 0 ? null : body.Substring(pipe + 1);

                bool hasSpace = cursor < text.Length && char.IsWhiteSpace(text[cursor]);
                row.Add(BuildLink(label, target, hasSpace, onFollow));
            }

            return row;
        }

        static VisualElement BuildLink(string label, string target, bool hasTrailingSpace, Action<string> onFollow)
        {
            if (!ReadmeLink.CanFollow(target))
            {
                var dead = new Label(label);
                dead.AddToClassList("blocks-readme__word");
                if (hasTrailingSpace) dead.style.marginRight = k_SpaceWidth;
                return dead;
            }

            var link = new Button(() => onFollow(target)) { text = label };
            link.AddToClassList("blocks-readme__inline-link");
            if (hasTrailingSpace) link.style.marginRight = k_SpaceWidth;
            return link;
        }

        // One label per word, because a wrapping row wraps between children and nowhere else — a single
        // label holding the whole run would refuse to break and push the row wider than the inspector.
        // The words carry no type styling of their own; they inherit the row's.
        static void AddWords(VisualElement row, string run)
        {
            if (string.IsNullOrEmpty(run)) return;

            int i = 0;
            while (i < run.Length)
            {
                while (i < run.Length && char.IsWhiteSpace(run[i])) i++;
                if (i >= run.Length) break;

                int start = i;
                while (i < run.Length && !char.IsWhiteSpace(run[i])) i++;

                var word = new Label(run.Substring(start, i - start));
                word.AddToClassList("blocks-readme__word");
                if (i < run.Length) word.style.marginRight = k_SpaceWidth;
                row.Add(word);
            }
        }
    }

    #endregion
}

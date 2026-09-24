using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Blocks
{
    /// <summary>
    /// Gets Unity AI Assistant into a project that imported this sample without it.
    ///
    /// The rest of the sample (<c>Blocks.AI.Editor</c>) is compiled only while <c>com.unity.ai.assistant</c>
    /// is in the project, so on a fresh import none of it exists yet and nothing could ask for the package.
    /// This assembly has no such gate: it offers to add the package, registers a stand-in "AI Assisted" tab
    /// that says what is happening, and owns the menu item — so the entry points a reader was promised exist
    /// from the moment the sample is imported. Once the package lands Unity recompiles, the gated assembly
    /// registers the real tab under the same name, and this class registers nothing at all.
    ///
    /// The template itself does not depend on the Assistant, on purpose: it has no stable release, and a
    /// template cannot ship a pre-release dependency. Adding it here, with a prompt, keeps that choice with
    /// the person who imported the sample.
    /// </summary>
    [InitializeOnLoad]
    static class AiSetup
    {
        // Must match AiHome.TabName in the gated assembly, which this one cannot reference. The two never
        // register at the same time: this one only while the package is absent, AiHome only while present.
        public const string TabName = "AI Assisted";

        public const string PackageName = "com.unity.ai.assistant";
        public const string PackageVersion = "2.18.0-pre.2";

        // One offer per editor session. A refusal is remembered so the dialog does not return on every
        // recompile; the stand-in tab keeps an Install button for when they change their mind.
        const string k_PromptedKey = "Blocks.AI.Setup.Prompted";

        static AddRequest s_Add;
        static string s_Error;

        /// <summary>Whether the Assistant package is in the project. When it is, the gated assembly takes over.</summary>
        public static bool IsInstalled => PackageInfo.FindForPackageName(PackageName) != null;

        /// <summary>Whether an install started here is still running.</summary>
        public static bool IsInstalling => s_Add != null && !s_Add.IsCompleted;

        /// <summary>The Package Manager's message if the last install failed, or null.</summary>
        public static string Error => s_Error;

        static AiSetup()
        {
            if (IsInstalled) return;

            CreatorWindow.RegisterTab(TabName, 10, new AiSetupCard());

            if (SessionState.GetBool(k_PromptedKey, false)) return;

            // Deferred: a modal dialog during a domain reload is unsafe, and this runs from one.
            EditorApplication.delayCall += PromptToInstall;
        }

        [MenuItem("Building Blocks/Creator (AI Assisted)")]
        static void OpenMenuItem() => CreatorWindow.ShowHomeOnTab(TabName);

        static void PromptToInstall()
        {
            SessionState.SetBool(k_PromptedKey, true);

            bool install = EditorUtility.DisplayDialog(
                "Install Unity AI Assistant?",
                "The AI Assisted Creator sample runs on Unity AI Assistant, which is not in this project.\n\n" +
                $"Install {PackageName} {PackageVersion} now? It is a pre-release package. " +
                "You can also do this later from the AI Assisted tab in the Building Blocks Creator.",
                "Install", "Not now");

            if (install) Install();
        }

        /// <summary>Adds the Assistant package through the Package Manager. Safe to call twice.</summary>
        public static void Install()
        {
            if (IsInstalled || IsInstalling) return;

            s_Error = null;
            s_Add = Client.Add($"{PackageName}@{PackageVersion}");
            EditorApplication.update += PollInstall;

            Debug.Log($"[BuildingBlocks] Installing {PackageName} {PackageVersion}. The AI Assisted tab " +
                      "appears in the Building Blocks Creator once Unity has recompiled.");
            CreatorWindow.RefreshHomeIfShowing();
        }

        static void PollInstall()
        {
            if (s_Add == null || !s_Add.IsCompleted) return;

            EditorApplication.update -= PollInstall;

            if (s_Add.Status == StatusCode.Failure)
            {
                s_Error = s_Add.Error != null ? s_Add.Error.message : "Unknown Package Manager error.";
                Debug.LogError($"[BuildingBlocks] Could not install {PackageName}: {s_Error}\n" +
                               "Add it yourself: Window > Package Manager > + > Install package by name, " +
                               $"name \"{PackageName}\", version \"{PackageVersion}\".");
            }

            // On success the Package Manager triggers a recompile; the gated assembly takes it from there.
            s_Add = null;
            CreatorWindow.RefreshHomeIfShowing();
        }
    }

    /// <summary>
    /// The stand-in for the AI Assisted tab while the Assistant package is missing: what is missing, and a
    /// button that installs it. Rebuilt on every state change, so it reads as installing, failed, or waiting.
    /// </summary>
    sealed class AiSetupCard : HomeCard
    {
        // The home cards space their own parts (header, pills, disclosure); this one stacks a callout and a
        // button under the header instead, so it spaces those itself.
        const float k_Gap = 14f;

        public override VisualElement Build()
        {
            var card = new VisualElement();
            card.AddToClassList("blocks-pick");
            card.AddToClassList("blocks-pick--home");
            card.AddToClassList("blocks-pick--wide");

            HomeCardParts.AddHead(card, "Unity AI Assistant is not in this project yet",
                "The AI Assisted Creator describes characters and abilities to the Assistant and opens its " +
                "drafts on the Review step. It needs the Assistant package before any of that can appear here.");

            Callout status;
            if (AiSetup.IsInstalling)
            {
                status = new Callout("INSTALLING",
                    $"The Package Manager is adding {AiSetup.PackageName} {AiSetup.PackageVersion}. Unity " +
                    "recompiles when it lands, and this tab becomes the AI Assisted Creator.");
            }
            else if (!string.IsNullOrEmpty(AiSetup.Error))
            {
                status = new Callout("INSTALL FAILED",
                    $"{AiSetup.Error}\n\nTry again below, or add it yourself from Window > Package Manager > " +
                    $"+ > Install package by name: {AiSetup.PackageName}, version {AiSetup.PackageVersion}.",
                    StripTone.Warning);
            }
            else
            {
                status = new Callout("WHAT INSTALL DOES",
                    $"Adds {AiSetup.PackageName} {AiSetup.PackageVersion}, a pre-release package, to this " +
                    "project through the Package Manager. Using it also needs a Unity account with Unity AI enabled.");
            }
            status.style.marginTop = k_Gap;
            card.Add(status);

            if (AiSetup.IsInstalling) return card;

            Button install = Buttons.Primary("Install Unity AI Assistant", AiSetup.Install);
            install.style.marginTop = k_Gap;
            install.style.alignSelf = Align.FlexStart;
            install.style.paddingLeft = 18;
            install.style.paddingRight = 18;
            card.Add(install);

            return card;
        }
    }
}

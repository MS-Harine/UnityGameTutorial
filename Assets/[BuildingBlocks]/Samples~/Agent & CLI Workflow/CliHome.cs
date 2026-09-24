using System;
using UnityEditor;

namespace Blocks.Cli
{
    /// <summary>
    /// Wires the Agent &amp; CLI guide into the Creator as its own tab.
    ///
    /// This is the whole seam between the base template and this sample: nothing in the Creator names
    /// anything here, so with this code absent the tab and the menu item simply do not exist — which is what
    /// lets the CLI layer ship as an optional sample.
    ///
    /// The guide lives in the Creator rather than in a window of its own because that is where it leads. A
    /// drafted character opens on the Review step of this same window, so the instructions for producing one
    /// and the screen that receives it are now one place instead of two.
    /// </summary>
    [InitializeOnLoad]
    static class CliHome
    {
        public const string TabName = "Agent & CLI";

        static CliHome()
        {
            CreatorWindow.RegisterTab(TabName, 20, new WizardLaunchCard(
                "Build from the terminal", CliWizard.CardInfo, wide: true, () => new CliWizard()));
        }

        [MenuItem("Building Blocks/Creator (Agent & CLI)")]
        static void OpenMenuItem() => Show();

        /// <summary>Opens the Creator on the Agent &amp; CLI tab.</summary>
        public static void Show() => CreatorWindow.ShowHomeOnTab(TabName);

        #region Listing

        // Set by ListCommand when Commands/ is compiled — which is only once com.unity.pipeline is
        // installed. The wizard offers a button that prints the bb_ listing to the Console, and the code
        // that produces that text lives on the far side of the version define, so it cannot be called from
        // here directly. Handing it over on load keeps the dependency pointing the one way it can: the
        // optional assembly knows about this one, never the reverse.
        static Func<string> s_Listing;

        /// <summary>Whether the bb_ commands are compiled in this project, and can produce a listing.</summary>
        public static bool HasListing => s_Listing != null;

        /// <summary>Called by the commands assembly on load. Nothing calls it when that assembly is absent.</summary>
        public static void ProvideListing(Func<string> listing) => s_Listing = listing;

        /// <summary>The bb_ listing, or empty when the commands are not in this project.</summary>
        public static string Listing() => s_Listing != null ? s_Listing() : string.Empty;

        #endregion
    }
}

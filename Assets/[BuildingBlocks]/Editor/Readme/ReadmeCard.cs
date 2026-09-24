using UnityEditor;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// The Creator home's pointer back to the Readme.
    ///
    /// The readme lives in the Inspector, so the first click on any GameObject replaces it and there is no
    /// route back except the menu — one PV tester never found it at all. The Creator is the surface people
    /// do reach, so the two point at each other. It sits last and draws as a slim banner rather than a
    /// fourth look-alike card: the home screen asks "What are you building?", which a readme does not
    /// answer, and a row that looks different reads as the fallback for someone who does not yet know.
    /// </summary>
    [InitializeOnLoad]
    sealed class ReadmeCard : HomeCard
    {
        // Self-registered rather than named in CreatorWindow's card list, so that "Remove Readme Assets"
        // can delete this script along with the rest of the readme without leaving a dangling reference.
        static ReadmeCard()
        {
            CreatorWindow.AppendCard(CreatorWindow.StandardTab, new ReadmeCard());
        }

        public override VisualElement Build()
        {
            // Once "Remove Readme Assets" has run there is nothing to open, so the banner declines to
            // render rather than surviving as dead UI pointing at a warning.
            if (AssetDatabase.FindAssets("t:Readme").Length == 0) return null;

            var banner = new VisualElement();
            banner.AddToClassList("blocks-pick");
            banner.AddToClassList("blocks-pick--wide");
            banner.AddToClassList("blocks-pick--launch");
            banner.AddToClassList("blocks-pick--readme");

            var text = new VisualElement();
            text.AddToClassList("blocks-pick__banner-text");

            var title = new Label("New here? Read this first");
            title.AddToClassList("blocks-pick__title");
            text.Add(title);

            var desc = new Label(
                "Four short steps, from playing the level to building your own character. " +
                "Opens in the Inspector.");
            desc.AddToClassList("blocks-pick__desc");
            text.Add(desc);

            banner.Add(text);

            var arrow = new Label("→");
            arrow.AddToClassList("blocks-pick__banner-arrow");
            banner.Add(arrow);

            banner.RegisterCallback<ClickEvent>(_ => ReadmeEditor.Open());
            return banner;
        }
    }
}

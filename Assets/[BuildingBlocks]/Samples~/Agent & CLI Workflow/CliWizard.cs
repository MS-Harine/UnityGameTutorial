using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Blocks.Cli
{
    /// <summary>
    /// The Agent &amp; CLI guide: four steps that take someone from "I have a terminal" to a drafted
    /// character waiting for approval.
    ///
    /// It is a wizard rather than a page of instructions because the order is the point. Connecting comes
    /// before listing, listing comes before drafting — ability class names are project-specific, so a caller
    /// who skips <c>bb_list</c> guesses a name and gets a note instead of an ability. Steps enforce that
    /// sequence; a wall of text invites reading the last block first.
    ///
    /// It also uses the same wizard chrome as the character and ability flows, so the thing that explains
    /// the Creator looks like the Creator.
    /// </summary>
    sealed class CliWizard : Wizard
    {
        readonly WizardStep[] m_Steps = new WizardStep[]
        {
            new ConnectStep(),
            new LookAroundStep(),
            new DraftStep(),
            new AgentStep(),
        };

        // Rendered by the header as "Building  CLI Workflow", so it reads as a phrase rather than a label.
        public override string Title => "CLI Workflow";
        public override IReadOnlyList<WizardStep> Steps => m_Steps;

        public static HomeCardInfo CardInfo => k_CardInfo;

        static readonly HomeCardInfo k_CardInfo = new HomeCardInfo(
            "Drive this Creator from a terminal. Built for tools rather than people: an AI agent, a build " +
            "script, or a CI job can draft a character or an ability, and every draft lands here for your " +
            "approval.",
            new[] { "AI agents", "Scripts", "Batch drafting", "No Editor clicks" },
            new LearnSection(
                "How this works",
                "This project registers its own Unity CLI commands. They run inside this Editor, build the " +
                "same specs the manual wizards build, and stop on the Review step — so a caller outside the " +
                "Editor can propose, and only a person in the Editor can approve.",
                new[]
                {
                    "Needs the Unity CLI, a separate install that is not the editor, and this Editor left open.",
                    "Nothing is created behind your back. A draft is a filled-in wizard, not a result.",
                    "The commands are plain C# in the sample folder. Add your own the same way.",
                }));
    }

    #region Steps

    /// <summary>
    /// Step one: the three things a machine and project need before a command can reach this Editor, and
    /// how to tell that it worked. This exists because the failure modes are otherwise silent or misleading:
    /// someone assumes "unity" means the editor executable they already have (it is a separate CLI install),
    /// or types a command and gets "No Unity Editor instances found", which names neither remaining cause.
    ///
    /// This step is also the reason the sample is split across two assemblies. The commands themselves need
    /// the Pipeline package to compile, so they live in Commands/ behind a version define and are simply
    /// absent until step 2 is done. This screen is not, so it can still be here to say what is missing —
    /// which is the whole point of importing a sample that then appears to do nothing.
    /// </summary>
    sealed class ConnectStep : WizardStep
    {
        public override string Label => "Connect";

        public override VisualElement BuildBody()
        {
            var root = new VisualElement();
            root.Add(Heading.Title("Get the CLI talking to this project"));
            root.Add(Heading.Subtitle("Three things. The CLI installs once per machine; the rest is once per project."));

#if !BLOCKS_UNITY_CLI
            // Shown only while com.unity.pipeline is absent, which is also when Commands/ is not compiled.
            root.Add(new Callout(
                "THE BB_ COMMANDS ARE NOT IN THIS PROJECT YET",
                "This project does not have the Pipeline package, so the commands below do not exist here " +
                "yet. Step 2 adds the package, and they compile with it.",
                StripTone.Warning));
#endif

            var cli = new Card("1. Install the Unity CLI");
            cli.Add(Callout.Body(
                "The unity command is its own small install. It is not the Unity editor you already have, " +
                "and nothing in the editor installs it for you; the docs cover each platform. If the " +
                "terminal cannot find unity afterwards, the install folder is not on your PATH yet."));
            cli.Add(Buttons.Secondary("Open the Unity CLI docs",
                () => Application.OpenURL("https://docs.unity.com/en-us/hub/unity-cli")));
            root.Add(cli);

            var install = new Card("2. Install the Pipeline package");
            install.Add(Callout.Body(
                "Run this in the project folder. It adds the package that lets the CLI reach a running Editor."));
            install.Add(new CommandRow("unity pipeline install"));
            root.Add(install);

            var open = new Card("3. Leave this Editor open");
            open.Add(Callout.Body(
                "The commands do their work inside Unity. A closed Editor has nothing to talk to."));
            root.Add(open);

            var check = new Card("Check it worked");
            check.Add(Callout.Body("The bb_ commands should appear in the listing."));
            check.Add(new CommandRow("unity list"));
            root.Add(check);

            root.Add(new Callout(
                "IF IT CANNOT CONNECT",
                "A terminal that cannot find unity, or one that opens the editor instead of printing a " +
                "result, means step 1: the CLI is not installed or not on your PATH. \"No Unity Editor " +
                "instances found with reachable Pipeline servers\" means step 2 or 3: the package is not " +
                "installed, or this Editor is closed.",
                StripTone.Warning));

            return root;
        }
    }

    /// <summary>
    /// Step two: reading the project before writing to it. The detailed listing is where ability class names
    /// come from, and those are the one argument a caller cannot guess.
    /// </summary>
    sealed class LookAroundStep : WizardStep
    {
        public override string Label => "Look around";

        public override VisualElement BuildBody()
        {
            var root = new VisualElement();
            root.Add(Heading.Title("See what this project offers"));
            root.Add(Heading.Subtitle("Start here. The listing is also what a caller reads to learn this project's ability names."));

            var list = new Card("Every command");
            list.Add(new CommandRow("unity command bb_list"));
            root.Add(list);

            var detail = new Card("…with arguments, and this project's abilities");
            detail.Add(Callout.Body(
                "Ability classes are specific to a project — one the Creator generated last week counts as " +
                "much as one that shipped with the template. This is how a caller learns their names."));
            detail.Add(new CommandRow("unity command bb_list --detail true"));
            root.Add(detail);

            // Only offered when the commands are actually in this project. They live behind the Pipeline
            // version define, so until it is installed there is no listing to print and a button here would
            // do nothing.
            if (CliHome.HasListing)
            {
                var console = new Card("Prefer to read it here?");
                console.Add(Callout.Body("Prints the same listing to the Unity Console."));
                console.Add(Buttons.Secondary("Print the command list to the Console",
                    () => Debug.Log(CliHome.Listing())));
                root.Add(console);
            }

            return root;
        }
    }

    /// <summary>
    /// Step three: the two commands that produce something, and what happens when they land. The point being
    /// made here is that a draft is a handoff, not a result.
    /// </summary>
    sealed class DraftStep : WizardStep
    {
        public override string Label => "Draft";

        public override VisualElement BuildBody()
        {
            var root = new VisualElement();
            root.Add(Heading.Title("Ask for a character or an ability"));
            root.Add(Heading.Subtitle("Each command fills in a draft and opens it here, on the Review step."));

            var character = new Card("Draft a character");
            character.Add(Callout.Body("Start from a preset — player, enemy, or empty — and override what you like."));
            character.Add(new CommandRow("unity command bb_character_draft --name Goblin --kind enemy --health 25"));
            character.Add(new CommandRow(
                "unity command bb_character_draft --name Goblin --attack EnemyMeleeAttackAbility"));
            root.Add(character);

            var ability = new Card("Draft an ability");
            ability.Add(Callout.Body(
                "--template sets the shape: instant fires on a press, burst runs for a set time, hold runs while held."));
            ability.Add(new CommandRow(
                "unity command bb_ability_draft --name GlideAbility --kind movement --template hold"));
            root.Add(ability);

            root.Add(new Callout(
                "YOU APPROVE, NOT THE CLI",
                "The draft opens in this window with every field editable. Change anything, then press " +
                "Create. Until you do, nothing has been made.",
                StripTone.Success));

            root.Add(new Callout(
                "ONE THING TO WATCH",
                "Approving an ability writes a .cs file, which triggers a recompile. The domain reloads and " +
                "the CLI connection drops for a moment, so do not chain another command onto it.",
                StripTone.Warning));

            return root;
        }
    }

    /// <summary>
    /// Step four: pointing a tool at all this. Short on purpose — the useful content is the two things an
    /// agent gets wrong without being told, not a tour of agent tooling.
    /// </summary>
    sealed class AgentStep : WizardStep
    {
        public override string Label => "Agents";

        public override VisualElement BuildBody()
        {
            var root = new VisualElement();
            root.Add(Heading.Title("Hand it to an AI tool"));
            root.Add(Heading.Subtitle("Anything that can run a terminal can now build in this project, with you approving."));

            var tell = new Card("Two things worth telling it");
            tell.Add(Bullets.List(new[]
            {
                "Run bb_list --detail true first. Ability class names are specific to this project, so guessing one produces a note instead of an ability.",
                "A draft is waiting for a person, not done. Report it as opened for approval, and stop there.",
            }));
            root.Add(tell);

            var mcp = new Card("Or connect it over MCP");
            mcp.Add(Callout.Body(
                "The CLI can serve the same commands to an MCP client, and write the client's config for " +
                "you. Swap in the name of the coding agent you use — unity mcp --help lists the supported ones."));
            mcp.Add(new CommandRow("unity mcp configure <your-agent>"));
            root.Add(mcp);

            var extend = new Card("Add your own command");
            extend.Add(Callout.Body(
                "Each command is a static method tagged [CliCommand] in the sample's Commands folder. The " +
                "Pipeline package finds new ones by reflection after a recompile, and bb_list picks them up " +
                "by the same scan — there is no registration step."));
            root.Add(extend);

            return root;
        }
    }

    #endregion

    #region Parts

    /// <summary>
    /// One runnable command: the text as a selectable read-only field, and a button that copies it.
    ///
    /// Read-only rather than a plain label so the command can be selected and edited in place before being
    /// pasted — most people want to change the name in the example rather than run it verbatim.
    /// </summary>
    sealed class CommandRow : VisualElement
    {
        public CommandRow(string command)
        {
            style.flexDirection = FlexDirection.Row;
            style.marginTop = 4f;

            var field = new TextField { value = command, isReadOnly = true };
            field.style.flexGrow = 1f;
            Add(field);

            var copy = Buttons.Secondary("Copy", () => EditorGUIUtility.systemCopyBuffer = command);
            copy.style.width = 56f;
            Add(copy);
        }
    }

    #endregion
}

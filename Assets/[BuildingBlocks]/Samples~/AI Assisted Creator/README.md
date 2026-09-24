# AI Assisted Creator

Unity Assistant, inside the Building Blocks Creator. Describe the character or ability you want and the
Assistant proposes a draft. You review it, change what you like, and approve before anything is created.

## What importing this adds

- An **AI Assisted** tab on the Creator's home screen, with a prompt card for characters, movement abilities,
  and attack abilities. Each one hands your description to the Assistant and opens the resulting draft in the
  Creator's usual Review step, so nothing lands in your project until you approve it.
- An **Assistant** card at the top of the Building Blocks Character inspector, for describing a change to a
  character you already have, or asking a question about it.
- A **Skill: Validate and fix** entry on the Building Blocks Character component menu, which audits a
  character's setup and repairs it. The skill itself imports with the sample (its `TemplateSkills` folder)
  and the Assistant discovers it automatically.
- A **Building Blocks > Creator (AI Assisted)** menu item that opens the Creator on the AI tab.

None of that exists until you import this sample. The template's Creator holds no reference to any of it, so
the AI tab is genuinely absent rather than greyed out.

## Requirements

**Unity AI Assistant** (`com.unity.ai.assistant`, currently a pre-release package) and an account with Unity
AI enabled.

The template does not depend on the Assistant, so importing this sample offers to install it: a dialog asks
once, and the **AI Assisted** tab in the Creator holds an **Install Unity AI Assistant** button until the
package is in. While it is missing, that tab is a stand-in that says so; the moment the package lands Unity
recompiles and the real tab takes its place. If the install fails, the tab shows the Package Manager's
message and how to add the package by name.

## How it fits together

The sample is two assemblies, because it has to do something useful before its own dependency exists.

`Setup/` is `Blocks.AI.Setup.Editor`, and it always compiles. `AiSetup.cs` checks for the Assistant package
on load; if it is missing it registers the stand-in tab, offers the install (`Client.Add`), and owns the
**Building Blocks > Creator (AI Assisted)** menu item so the Readme's button has somewhere to go.

Everything else is `Blocks.AI.Editor`, gated on the package: its asmdef carries a `versionDefines` entry for
`com.unity.ai.assistant` and a matching `defineConstraints`, so without the package Unity skips the assembly
entirely instead of erroring. `AiRegistration.cs` is its seam. On load it registers the AI tab through
`CreatorWindow.RegisterTab` (replacing the stand-in, since both use the same name) and the Assistant card
through `BuildingBlocksCharacterEditor.RegisterCard`. The template never names anything in this folder, so
the dependency runs one way only, which is what lets the sample be optional.

Delete the imported folder and the AI features go away cleanly, leaving the Creator as it ships. The
Assistant package stays in the project; remove it from the Package Manager if you no longer want it.

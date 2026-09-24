# Optional samples

Each folder here is one optional sample. This folder ends in `~`, so Unity leaves it alone: nothing in a
sample compiles or shows in the Project window until you import it, and the template itself references none
of them.

Import a sample from the **Optional samples** step of the Readme (**Building Blocks > Open Readme**), or from
**Building Blocks > Import Sample...**. Importing copies the folder to `Assets/Samples/<sample name>/`; delete
that folder to remove the sample again.

| Sample | What it adds | Needs |
|---|---|---|
| AI Assisted Creator | An **AI Assisted** tab in the Building Blocks Creator, an **Assistant** card on the character inspector, and a validate-and-fix skill. Describe a character or ability; the Assistant drafts it, you review and approve. | Unity AI Assistant. The sample offers to install it (`com.unity.ai.assistant`, pre-release) when you import it. |
| Agent & CLI Workflow | An **Agent & CLI** tab in the Creator and `bb_*` Unity CLI commands, so agents, scripts, and CI jobs can draft characters and abilities for your approval. | The Unity CLI, a separate small install, plus `unity pipeline install` in this project. The sample's README walks through it. |

## For template developers

**The folder name is the sample's name everywhere**: the import destination and the `sampleName` a Readme
link refers to. Renaming a folder renames both. Every file and folder carries its `.meta`, and the folder
itself has a sibling `<name>.meta`, so an import keeps the same GUIDs every time.

**Why folders and not `.unitypackage` files.** Unity 6 verifies a `.unitypackage`'s signature on import and
puts a "Missing Signature" dialog in front of anything not exported by an editor signed in to Unity Cloud,
whichever import API is used. A template built in CI cannot carry that signature. A folder copy has no such
gate and no build step. ADR 0002, Decision 9.

**Working on a sample.** The code here does not compile in the dev project. Import the sample, edit the copy
under `Assets/Samples/<name>/` where the compiler and IDE can see it, then:

1. `Tools/pull-sample.sh "<name>"` copies the folder (with `.meta` files) back over this one.
2. Delete `Assets/Samples/<name>/` and its `.meta` from the dev project before committing, so the sources
   ship only once.
3. Run `Tools/sample-compile-check.sh` after touching an asmdef. It builds every sample assembly offline in
   each present/absent state, including the CLI tab with no Pipeline assembly and the AI setup with no
   Assistant assembly, which is the only check that catches a gating mistake without a round trip.

**Gating on optional packages.** A sample that needs a package the template does not declare gates its
assembly with `versionDefines` plus `defineConstraints`, so it compiles out instead of erroring, and is split
so the half a user meets first never needs the package: the CLI sample's tab (`Blocks.Cli.Editor`) is ungated
and only `Commands/` (`Blocks.Cli.Commands.Editor`) needs `com.unity.pipeline`; the AI sample's `Setup/`
(`Blocks.AI.Setup.Editor`) is ungated, owns the menu item, offers to install `com.unity.ai.assistant`, and
holds a stand-in tab until the gated `Blocks.AI.Editor` takes over. ADR 0002 Decision 8 has the full rule.

**Adding a sample.** Create it under `Assets/Samples/<name>/` in the dev project so Unity writes the `.meta`
files, `pull-sample.sh` it here, delete the working copy, and add a Readme link with `sampleName` set to the
folder name. The CI verify step picks up every folder automatically.

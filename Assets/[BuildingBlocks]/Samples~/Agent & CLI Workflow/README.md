# Agent & CLI Workflow

Building Blocks commands for the [Unity CLI](https://docs.unity.com/en-us/hub/unity-cli). This sample is
built for tools instead of people: it lets an AI agent, a build script, or a CI job draft a character or
an ability from a terminal. If you are working by hand, the Creator window already does all of this, and
you can skip this sample.

Every draft lands in the Creator window on its Review step, for a person to change and approve. Nothing is
created behind your back.

The sample adds an **Agent & CLI** tab to the Creator window, with a four-step guide that walks through all
of this with copy buttons. Open it from **Building Blocks ▸ Creator (Agent & CLI)**.

## Setup

**1. Install the Unity CLI** (once per machine). The `unity` command is its own small install. It is
**not the Unity editor executable** you already have, and nothing in the editor installs it for you; the
[Unity CLI docs](https://docs.unity.com/en-us/hub/unity-cli) cover each platform. If your terminal cannot
find `unity` afterwards, the install folder (for example `~/.unity/bin`) is not on your PATH yet; add it.

**2. Connect this project** (once per project). Run this in the project folder:

```bash
unity pipeline install
```

**3. Leave the Editor open.** The commands run inside it. Confirm the connection with `unity list`; the `bb_`
commands should be in the listing.

Step 2 is not optional, and it is also not something this sample can do for you: `com.unity.pipeline`
is experimental, so the template cannot declare it as a dependency. Until you run it, the `bb_` commands
are **not compiled in your project**. The Creator tab is still there and says so, and the commands appear
once the package lands and Unity recompiles.

## Commands

```bash
unity command bb_list --detail true
```

| Command | Does |
|---|---|
| `bb_list` | Lists the commands. `--detail true` adds each one's arguments and this project's ability classes. |
| `bb_character_draft` | Drafts a character onto the Creator's Review step. Creates nothing. |
| `bb_ability_draft` | Drafts an ability script onto the Creator's Review step. Writes nothing. |

Run them plain, without `--json`. Each command returns a single laid-out string, and the CLI prints a string
result with its newlines intact; `--json` wraps it in the response envelope, where the same layout comes back
escaped onto one line.

## Draft a character

```bash
unity command bb_list --detail true
unity command bb_character_draft --name Goblin --kind enemy --health 25 \
  --attack EnemyMeleeAttackAbility --movement EnemyMovementAbility
```

The Creator window opens on its Review step with the draft filled in. Change anything you like, then press
Create.

Run `bb_list --detail true` first: ability class names are specific to a project — one the Creator generated
last week counts as much as one that shipped with the template — and that listing is what tells a caller
which ones exist. Naming one that doesn't is a note on the draft, not a failure, so the wizard still opens
with everything else filled in.

`--kind` picks the preset the draft starts from: `player`, `enemy`, or `empty`. Everything a preset sets is
then editable, both by flag and in the wizard.

## Draft an ability

```bash
unity command bb_ability_draft --name GlideAbility --kind movement --template hold
```

`--template` sets the lifecycle shape and its timing defaults:

| Template | Shape |
|---|---|
| `instant` | Fires on a key press, with a short cooldown. |
| `burst` | Runs for a set duration, then stops itself. |
| `hold` | Runs while the key is held. |

The Review step shows the generated source before anything is written. Approving it writes a `.cs` file,
which triggers a recompile — the domain reloads and the CLI connection drops for a moment, so don't chain
work onto it.

## Argument surface

Both commands take few arguments on purpose. The draft opens on a screen where every field is editable, so a
flag only exists where a caller would want to state that choice up front. Elimination delay, targeting,
input buffer, stat costs, output folder — all present in the wizard, none of them flags.

## How it fits together

`CliHome.cs` registers the **Agent & CLI** tab from an `[InitializeOnLoad]` hook, and `CliWizard.cs` is the
guide it opens — built from the Creator's own wizard, card and callout pieces, so the tab that explains the
Creator looks like the Creator. Nothing in the base template names either file, which is what lets this ship
as an optional sample.

`Layout.cs` renders the terminal output in the Unity CLI's own style: spaced-caps heading, a horizontal rule
as the separator, two-column rows. The commands are in `Commands/`, each a `static` method tagged
`[CliCommand]` — the Pipeline package finds them by reflection after a recompile, so adding one needs no
registration step, and `bb_list` finds it by the same scan.

`Commands/` is a **second assembly**, and that is the reason the tab still works before the Pipeline package
is installed. `Blocks.Cli.Commands.Editor` is the only part that references `Unity.Pipeline`, and its asmdef
carries a `versionDefines` entry for `com.unity.pipeline` plus a matching `defineConstraints`, so Unity drops
it from the build entirely when the package is missing — no unresolved references, no errors. `Blocks.Cli.Editor`
(the tab, the wizard, the layout helpers) references nothing optional and always compiles. The dependency runs
one way: the commands hand their listing to the tab through `CliHome.ProvideListing` on load, so the tab's
"print to the Console" button appears only when there is something to print. Add a new command to `Commands/`
and it inherits the gate; put one outside and you break the sample for anyone without the CLI.

The draft commands build the same `CharacterSpec` and `AbilityScriptSpec` the manual Creator wizard uses and
hand them to the same wizard, positioned on its last step. That is why an approved draft behaves exactly like
something made by hand: it *is* the same code path.

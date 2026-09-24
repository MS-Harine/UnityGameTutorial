using System.Runtime.CompilerServices;

// The commands live in their own assembly so that this one does not have to reference the Pipeline package.
// Only Commands/ needs Unity.Pipeline, and only that assembly is compiled out when the package is absent —
// which is what lets the tab and its instructions still appear in a project that has not installed the CLI
// yet. See Commands/Blocks.Cli.Commands.Editor.asmdef for the version define that gates it.
//
// The split leaves the commands on the far side of an assembly boundary from the rendering helpers they
// print through, so they need to see Layout.
[assembly: InternalsVisibleTo("Blocks.Cli.Commands.Editor")]

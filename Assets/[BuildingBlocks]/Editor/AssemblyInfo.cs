using System.Runtime.CompilerServices;

// The optional samples extend the Creator window and the character inspector from their own assemblies, so
// they need to see the pieces they build on: HomeCard, Wizard, ModuleCard, and the shared UI helpers.
// Granting them access keeps all of that internal rather than promoting the Creator's UI internals to
// public API the template would then have to keep stable for everyone.
//
// It also keeps the samples looking like the rest of the window. Without it a sample can still register a
// tab — RegisterTab and Wizard are public — but it would have to reimplement Card, Callout and the buttons,
// and a tab built from a second set of widgets reads as not quite belonging.
//
// If those extension points are ever meant for users to build on too, these are the lines to delete — and
// the types the samples touch become the public surface instead.
//
// Blocks.AI.Setup.Editor is the AI sample's ungated half: it installs Unity AI Assistant and holds the tab's
// place until the package lands, so it needs the same card pieces the real tab is built from.
[assembly: InternalsVisibleTo("Blocks.AI.Editor")]
[assembly: InternalsVisibleTo("Blocks.AI.Setup.Editor")]
[assembly: InternalsVisibleTo("Blocks.Cli.Editor")]
[assembly: InternalsVisibleTo("Blocks.Cli.Commands.Editor")]

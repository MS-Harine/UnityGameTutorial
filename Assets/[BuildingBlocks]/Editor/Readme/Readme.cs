using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Blocks
{
    /// <summary>
    /// ScriptableObject holding the Readme content shown in the inspector: a hero, and a list of sections.
    /// Created from Assets, Create, Tutorial/Readme.
    ///
    /// A section is one card. Numbered sections are the steps a reader works through in order; whether a
    /// step has been completed is per-user state and lives in <see cref="ReadmeProgress"/>, never here — a
    /// flag serialized on this asset ships with whatever value the dev project last wrote to it.
    /// </summary>
    [CreateAssetMenu(fileName = "Readme", menuName = "Tutorial/Readme", order = 1)]
    public class Readme : ScriptableObject
    {
        public Texture2D icon;
        public string title;
        public string tagline;
        public Section[] sections;

        /// <summary>How a callout is coloured: neutral information, or something to watch out for.</summary>
        public enum Tone
        {
            Info,
            Warning,
        }

        /// <summary>
        /// What a <see cref="Block"/> draws. Each kind reads a different subset of the block's fields;
        /// the rest stay empty, the same way a link only ever uses one of its target forms.
        /// </summary>
        public enum BlockKind
        {
            /// <summary>A paragraph of body text.</summary>
            Body,

            /// <summary>Body text in a tinted box, coloured by <see cref="Block.tone"/>.</summary>
            Callout,

            /// <summary>A grid: <see cref="Block.columns"/> as headers, <see cref="Block.rows"/> beneath.</summary>
            Table,

            /// <summary>A definition list. One cell per row: its text is the term, its note the definition.</summary>
            Terms,

            /// <summary>A command in an inset box with a button that copies <see cref="Block.copyText"/>.</summary>
            Copy,
        }

        /// <summary>
        /// One Readme card: an optional heading followed by a list of blocks.
        ///
        /// Set <see cref="stepNumber"/> above zero to draw it as a numbered step. Set
        /// <see cref="isCompletable"/> to give it a Mark done button, which folds the card down to a single
        /// line and counts toward the progress bar. Completion is a bookmark and nothing more: steps stay
        /// available in any order, and Undo brings one back.
        /// </summary>
        [Serializable]
        public class Section
        {
            /// <summary>
            /// Stable key for this section's completion state. Set by hand rather than derived from the
            /// heading, so editing copy does not reset anyone's progress.
            /// </summary>
            public string id;

            public string heading;

            /// <summary>The badge drawn beside the heading. Zero draws no badge, for unnumbered cards.</summary>
            public int stepNumber;

            public bool isCompletable;

            /// <summary>Draws the card closed behind a caret, for reference material a reader opens on demand.</summary>
            public bool isCollapsible;

            /// <summary>
            /// Appends the "n of m done" count, the progress bar, and the Hide completed toggle to this
            /// card. Belongs on the intro, above the steps it is counting.
            /// </summary>
            public bool hasProgress;

            [FormerlySerializedAs("steps")]
            public Block[] blocks;
        }

        /// <summary>
        /// One piece of a section: a paragraph, a callout, a table, a definition list, or a copyable
        /// command, followed by any buttons that belong with it.
        /// </summary>
        [Serializable]
        public class Block
        {
            public BlockKind kind;

            /// <summary>
            /// Body or callout text. May carry inline links as <c>[[label|target]]</c>, where target takes
            /// the same forms as <see cref="Link.target"/>. For <see cref="BlockKind.Terms"/> it is the
            /// caption above the list.
            /// </summary>
            [TextArea(2, 8)]
            public string text;

            /// <summary>Only read by <see cref="BlockKind.Callout"/>.</summary>
            public Tone tone;

            /// <summary>Column headers. Only read by <see cref="BlockKind.Table"/>.</summary>
            public string[] columns;

            /// <summary>Read by <see cref="BlockKind.Table"/> and <see cref="BlockKind.Terms"/>.</summary>
            public Row[] rows;

            /// <summary>The command to put on the clipboard. Only read by <see cref="BlockKind.Copy"/>.</summary>
            public string copyText;

            public Link[] links;
        }

        [Serializable]
        public class Row
        {
            public Cell[] cells;
        }

        /// <summary>
        /// One table cell, or one entry in a definition list: a label that may link somewhere, and a
        /// description under it. Either half can stand alone.
        /// </summary>
        [Serializable]
        public class Cell
        {
            public string text;

            /// <summary>Makes <see cref="text"/> clickable. Same forms as <see cref="Link.target"/>.</summary>
            public string target;

            /// <summary>Description under the label. May carry the same inline link markup as body text.</summary>
            [TextArea(1, 4)]
            public string note;
        }

        /// <summary>
        /// A button. <see cref="target"/> is a scheme-prefixed string naming where it goes:
        ///
        /// <list type="bullet">
        /// <item><c>menu:Building Blocks/Creator</c> — runs a menu item.</item>
        /// <item><c>guid:f0dde04f…</c> — pings an asset by GUID, which survives a move. Pings rather than
        /// selects, because selecting replaces this readme in the Inspector. A scene asset opens
        /// instead, which is what a scene button says it does, and a folder opens to its contents
        /// in the Project window.</item>
        /// <item><c>path:Assets/…</c> — the same by path, which reads better for folders.</item>
        /// <item><c>type:DashAbility</c> — the script defining that type, found by name so a renamed or
        /// user-written ability still resolves.</item>
        /// <item><c>url:https://…</c> — opens a browser.</item>
        /// <item><c>settings:Project/Services</c> — opens that page of the Project Settings window.</item>
        /// </list>
        ///
        /// Set <see cref="sampleName"/> to turn the button into an import for that optional sample — the
        /// name of a folder under <c>Assets/[BuildingBlocks]/Samples~/</c>, which imports to
        /// <c>Assets/Samples/&lt;name&gt;/</c>. While that folder is missing the button imports the sample;
        /// once it is in, the button switches to <see cref="target"/> under <see cref="importedText"/> —
        /// usually a menu path the sample itself adds. A sample button with no target hides itself once the
        /// sample is in, rather than offering an import that would do nothing.
        /// </summary>
        [Serializable]
        public class Link
        {
            public string text;
            public string target;
            public string sampleName;
            public string importedText;

            /// <summary>Draws as the filled call-to-action rather than a quiet outline button.</summary>
            public bool isPrimary;
        }
    }
}

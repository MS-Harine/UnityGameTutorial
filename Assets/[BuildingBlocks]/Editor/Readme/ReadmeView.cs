using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// Draws a <see cref="Readme"/>: the hero, then one card per section — numbered steps a reader works
    /// through, and unnumbered cards for the intro and reference material.
    ///
    /// Marking a step done rebuilds the whole view through the caller's callback rather than patching
    /// pieces of it. The readme is a dozen cards, so a rebuild is cheap, and it keeps the count, the
    /// progress bar and which cards are hidden consistent by construction.
    /// </summary>
    static class ReadmeView
    {
        const string k_Body = "blocks-readme__body";

        public static VisualElement Build(Readme readme, Action rebuild)
        {
            var root = new VisualElement();
            root.Add(BuildHero(readme));

            if (readme.sections == null) return root;

            CountSteps(readme, out int total, out int done);

            foreach (Readme.Section section in readme.sections)
            {
                VisualElement card = BuildSection(section, total, done, rebuild);
                if (card != null) root.Add(card);
            }

            return root;
        }

        static void CountSteps(Readme readme, out int total, out int done)
        {
            total = 0;
            done = 0;

            foreach (Readme.Section section in readme.sections)
            {
                if (section == null || !section.isCompletable) continue;

                total++;
                if (ReadmeProgress.IsDone(section.id)) done++;
            }
        }

        #region Hero

        static VisualElement BuildHero(Readme readme)
        {
            var hero = new VisualElement();
            hero.AddToClassList("blocks-readme__hero");

            if (readme.icon != null)
            {
                var icon = new VisualElement();
                icon.AddToClassList("blocks-readme__icon");
                icon.style.backgroundImage = Background.FromTexture2D(readme.icon);
                hero.Add(icon);
            }

            var text = new VisualElement();
            text.AddToClassList("blocks-readme__hero-text");

            var title = new Label(readme.title);
            title.AddToClassList("blocks-readme__title");
            text.Add(title);

            if (!string.IsNullOrEmpty(readme.tagline))
            {
                var tagline = new Label(readme.tagline);
                tagline.AddToClassList("blocks-readme__tagline");
                text.Add(tagline);
            }

            hero.Add(text);
            return hero;
        }

        #endregion

        #region Sections

        /// <summary>
        /// Builds one card, or null when it is a completed step and the reader has chosen to hide those.
        /// </summary>
        static VisualElement BuildSection(Readme.Section section, int total, int done, Action rebuild)
        {
            if (section == null) return null;

            bool isDone = section.isCompletable && ReadmeProgress.IsDone(section.id);
            if (isDone && ReadmeProgress.HideCompleted) return null;

            var card = new VisualElement();
            card.AddToClassList("blocks-readme__section");

            if (isDone)
            {
                card.AddToClassList("blocks-readme__section--done");
                card.Add(BuildDoneBar(section, rebuild));
                return card;
            }

            if (section.isCollapsible) return BuildCollapsible(card, section);

            // The intro has neither a number nor a heading — it is the paragraph above the steps.
            if (section.hasProgress) card.AddToClassList("blocks-readme__section--intro");
            if (section.stepNumber > 0 || !string.IsNullOrEmpty(section.heading))
                card.Add(BuildStepHead(section));

            VisualElement lastRow = BuildBlocks(card, section.blocks);

            if (section.hasProgress && total > 0)
                card.Add(BuildProgress(total, done, rebuild));

            if (section.isCompletable)
            {
                VisualElement row = lastRow ?? AddButtonRow(card);
                row.Add(BuildDoneButton(section, rebuild));
            }

            return card;
        }

        static VisualElement BuildStepHead(Readme.Section section)
        {
            var head = new VisualElement();
            head.AddToClassList("blocks-readme__stephead");

            if (section.stepNumber > 0)
                head.Add(StepBadge(section.stepNumber.ToString(), isDone: false));

            var heading = new Label(section.heading);
            heading.AddToClassList("blocks-readme__heading");
            head.Add(heading);

            return head;
        }

        static Label StepBadge(string text, bool isDone)
        {
            var badge = new Label(text);
            badge.AddToClassList("blocks-readme__stepnum");
            if (isDone) badge.AddToClassList("blocks-readme__stepnum--done");
            return badge;
        }

        /// <summary>The single line a completed step folds down to: a tick, its heading, and Undo.</summary>
        static VisualElement BuildDoneBar(Readme.Section section, Action rebuild)
        {
            var bar = new VisualElement();
            bar.AddToClassList("blocks-readme__donebar");

            bar.Add(StepBadge("✓", isDone: true));

            var label = new Label(section.heading);
            label.AddToClassList("blocks-readme__donelabel");
            bar.Add(label);

            var undo = new Button(() =>
            {
                ReadmeProgress.SetDone(section.id, false);
                rebuild();
            })
            { text = "Undo" };
            undo.AddToClassList("blocks-readme__undo");
            bar.Add(undo);

            return bar;
        }

        static Button BuildDoneButton(Readme.Section section, Action rebuild)
        {
            var button = new Button(() =>
            {
                ReadmeProgress.SetDone(section.id, true);
                rebuild();
            })
            { text = "Mark done" };

            button.AddToClassList("blocks-readme__done");
            return button;
        }

        static VisualElement BuildCollapsible(VisualElement card, Readme.Section section)
        {
            bool isOpen = ReadmeProgress.IsOpen(section.id);
            card.EnableInClassList("blocks-readme__section--open", isOpen);

            var header = new VisualElement();
            header.AddToClassList("blocks-readme__collapsed");

            var caret = new Label("▶");
            caret.AddToClassList("blocks-readme__caret");
            header.Add(caret);

            var heading = new Label(section.heading);
            heading.AddToClassList("blocks-readme__heading");
            header.Add(heading);

            card.Add(header);

            var body = new VisualElement();
            body.AddToClassList("blocks-readme__refbody");
            BuildBlocks(body, section.blocks);
            card.Add(body);

            header.RegisterCallback<ClickEvent>(_ =>
            {
                bool nowOpen = !card.ClassListContains("blocks-readme__section--open");
                card.EnableInClassList("blocks-readme__section--open", nowOpen);
                ReadmeProgress.SetOpen(section.id, nowOpen);
            });

            return card;
        }

        /// <summary>The "n of m done" count, the bar, and the toggle that hides completed steps.</summary>
        static VisualElement BuildProgress(int total, int done, Action rebuild)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-readme__progress");

            var count = new Label($"{done} of {total} done");
            count.AddToClassList("blocks-readme__progress-count");
            row.Add(count);

            var track = new VisualElement();
            track.AddToClassList("blocks-readme__progress-track");

            var fill = new VisualElement();
            fill.AddToClassList("blocks-readme__progress-fill");
            fill.style.width = new StyleLength(new Length(done / (float)total * 100f, LengthUnit.Percent));
            track.Add(fill);

            row.Add(track);

            // Until something is done there is nothing to hide, so the toggle waits — except while it is
            // active, when it must stay reachable or hidden steps could never come back.
            bool isHiding = ReadmeProgress.HideCompleted;
            if (done == 0 && !isHiding) return row;

            var toggle = new Button(() =>
            {
                ReadmeProgress.HideCompleted = !isHiding;
                rebuild();
            })
            { text = isHiding ? "Show completed" : "Hide completed" };

            toggle.AddToClassList("blocks-readme__toggle");
            row.Add(toggle);

            return row;
        }

        #endregion

        #region Blocks

        /// <summary>
        /// Adds every block to the card and returns the final block's button row, so a step's Mark done
        /// button can join that row instead of starting a lonely one below it. Null when the final block
        /// has no buttons — a row built earlier in the card is not offered, because appending Mark done
        /// there would land it in the middle of the card.
        /// </summary>
        static VisualElement BuildBlocks(VisualElement card, Readme.Block[] blocks)
        {
            if (blocks == null) return null;

            VisualElement lastRow = null;

            foreach (Readme.Block block in blocks)
            {
                if (block == null) continue;

                switch (block.kind)
                {
                    case Readme.BlockKind.Body:
                        Add(card, ReadmeText.Build(block.text, k_Body, ReadmeLink.Follow));
                        break;

                    case Readme.BlockKind.Callout:
                        Add(card, BuildCallout(block));
                        break;

                    case Readme.BlockKind.Table:
                        Add(card, BuildTable(block));
                        break;

                    case Readme.BlockKind.Terms:
                        Add(card, BuildTerms(block));
                        break;

                    case Readme.BlockKind.Copy:
                        Add(card, BuildCopy(block));
                        break;
                }

                VisualElement row = BuildLinks(block.links);
                lastRow = row;
                if (row != null) card.Add(row);
            }

            return lastRow;
        }

        static void Add(VisualElement parent, VisualElement child)
        {
            if (child != null) parent.Add(child);
        }

        static VisualElement BuildCallout(Readme.Block block)
        {
            var callout = new VisualElement();
            callout.AddToClassList("blocks-readme__callout");
            if (block.tone == Readme.Tone.Warning)
                callout.AddToClassList("blocks-readme__callout--warn");

            Add(callout, ReadmeText.Build(block.text, k_Body, ReadmeLink.Follow));
            return callout;
        }

        static VisualElement BuildCopy(Readme.Block block)
        {
            if (string.IsNullOrEmpty(block.copyText)) return null;

            var row = new VisualElement();
            row.AddToClassList("blocks-readme__copyrow");

            var code = new Label(block.copyText);
            code.AddToClassList("blocks-readme__code");
            row.Add(code);

            string payload = block.copyText;
            var copy = new Button(() => EditorGUIUtility.systemCopyBuffer = payload) { text = "Copy" };
            copy.AddToClassList("blocks-readme__copybtn");
            row.Add(copy);

            return row;
        }

        static VisualElement BuildTerms(Readme.Block block)
        {
            if (block.rows == null || block.rows.Length == 0) return null;

            var list = new VisualElement();
            list.AddToClassList("blocks-readme__terms");

            if (!string.IsNullOrEmpty(block.text))
            {
                var caption = new Label(block.text);
                caption.AddToClassList("blocks-readme__terms-head");
                list.Add(caption);
            }

            foreach (Readme.Row row in block.rows)
            {
                if (row?.cells == null || row.cells.Length == 0) continue;

                Readme.Cell cell = row.cells[0];

                if (ReadmeLink.CanFollow(cell.target))
                {
                    string target = cell.target;
                    var link = new Button(() => ReadmeLink.Follow(target)) { text = cell.text };
                    link.AddToClassList("blocks-readme__term");
                    link.AddToClassList("blocks-readme__term--link");
                    list.Add(link);
                }
                else
                {
                    var term = new Label(cell.text);
                    term.AddToClassList("blocks-readme__term");
                    list.Add(term);
                }

                Add(list, ReadmeText.Build(cell.note, "blocks-readme__def", ReadmeLink.Follow));
            }

            return list;
        }

        #endregion

        #region Table

        // Below this the inspector is too narrow for real columns, so each row stacks and every cell
        // shows the column it belongs to. Mirrors how the Creator's home grid drops to one column.
        const float k_NarrowTable = 380f;

        static VisualElement BuildTable(Readme.Block block)
        {
            if (block.rows == null || block.rows.Length == 0) return null;

            var table = new VisualElement();
            table.AddToClassList("blocks-readme__table");

            if (block.columns != null && block.columns.Length > 0)
            {
                var head = new VisualElement();
                head.AddToClassList("blocks-readme__table-head");

                for (int i = 0; i < block.columns.Length; i++)
                {
                    var label = new Label(block.columns[i]);
                    label.AddToClassList("blocks-readme__th");
                    if (i == 0) label.AddToClassList("blocks-readme__th--lead");
                    head.Add(label);
                }

                table.Add(head);
            }

            foreach (Readme.Row row in block.rows)
            {
                if (row?.cells == null) continue;

                var line = new VisualElement();
                line.AddToClassList("blocks-readme__table-row");

                for (int i = 0; i < row.cells.Length; i++)
                    line.Add(BuildCell(row.cells[i], Column(block.columns, i), isLead: i == 0));

                table.Add(line);
            }

            table.RegisterCallback<GeometryChangedEvent>(evt =>
                table.EnableInClassList("blocks-readme__table--narrow", evt.newRect.width < k_NarrowTable));

            return table;
        }

        static string Column(string[] columns, int index) =>
            columns != null && index < columns.Length ? columns[index] : null;

        static VisualElement BuildCell(Readme.Cell cell, string columnName, bool isLead)
        {
            var element = new VisualElement();
            element.AddToClassList("blocks-readme__cell");
            if (isLead) element.AddToClassList("blocks-readme__cell--lead");

            if (cell == null) return element;

            // Only visible once the table drops to its narrow layout, where the header row is gone.
            if (!string.IsNullOrEmpty(columnName))
            {
                var column = new Label(columnName);
                column.AddToClassList("blocks-readme__cell-column");
                element.Add(column);
            }

            if (!string.IsNullOrEmpty(cell.text))
            {
                if (ReadmeLink.CanFollow(cell.target))
                {
                    string target = cell.target;
                    var link = new Button(() => ReadmeLink.Follow(target)) { text = cell.text };
                    link.AddToClassList("blocks-readme__cell-link");
                    if (isLead) link.AddToClassList("blocks-readme__cell-link--lead");
                    element.Add(link);
                }
                else
                {
                    var label = new Label(cell.text);
                    label.AddToClassList("blocks-readme__cell-text");
                    element.Add(label);
                }
            }

            Add(element, ReadmeText.Build(cell.note, "blocks-readme__cell-note", ReadmeLink.Follow));

            return element;
        }

        #endregion

        #region Buttons

        static VisualElement AddButtonRow(VisualElement card)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-readme__btnrow");
            card.Add(row);
            return row;
        }

        /// <summary>
        /// Builds the row of buttons belonging to a block, or null when none of them have anywhere to go.
        /// A sample link that is already imported and has no follow-up drops out here.
        /// </summary>
        static VisualElement BuildLinks(Readme.Link[] links)
        {
            if (links == null || links.Length == 0) return null;

            VisualElement row = null;

            foreach (Readme.Link link in links)
            {
                Button button = BuildLink(link);
                if (button == null) continue;

                if (row == null)
                {
                    row = new VisualElement();
                    row.AddToClassList("blocks-readme__btnrow");
                }

                row.Add(button);
            }

            return row;
        }

        static Button BuildLink(Readme.Link link)
        {
            if (link == null || string.IsNullOrEmpty(link.text)) return null;

            string label = link.text;
            Action onClick;

            if (!string.IsNullOrEmpty(link.sampleName))
            {
                if (!ReadmeLink.TryResolveSample(link, out label, out onClick)) return null;
            }
            else
            {
                if (!ReadmeLink.CanFollow(link.target)) return null;

                string target = link.target;
                onClick = () => ReadmeLink.Follow(target);
            }

            if (link.isPrimary)
            {
                Button cta = Buttons.Primary(label, onClick);
                cta.AddToClassList("blocks-readme__cta");
                return cta;
            }

            var ghost = new Button(onClick) { text = label };
            ghost.AddToClassList("blocks-readme__ghost");
            return ghost;
        }

        #endregion
    }
}

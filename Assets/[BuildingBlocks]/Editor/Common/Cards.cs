using System;
using UnityEngine.UIElements;
using System.Collections.Generic;

// Reusable UI Toolkit elements for the Building Blocks editor windows.
//   - Card: titled container with an optional summary and a header row of actions.
//   - Callout: highlighted prompt box, tinted by StripTone, that can carry bullets and labeled sections.
//   - Bullets: builds a bulleted list of strings (used by Callout).
//   - OptionCard: selectable panel or tile (OptionLayout), optionally tagged, with a click handler.
//   - OptionRow: selectable row with optional leading/trailing elements and a click handler.
//   - StripTone (Callout tint) and OptionLayout (OptionCard panel vs tile): Enums
namespace Blocks
{
    #region Card

    sealed class Card : VisualElement
    {
        Label m_Title;
        Label m_Summary;
        VisualElement m_HeaderRow;
        bool m_Promoted;

        public Card()
        {
            AddToClassList("blocks-card");
        }

        public Card(string title) : this()
        {
            m_Title = new Label(title);
            m_Title.AddToClassList("blocks-card__title");
            Add(m_Title);
        }

        public Card AddAction(VisualElement action)
        {
            EnsureHeaderRow();
            m_HeaderRow.Add(action);
            return this;
        }

        public Card SetSummary(string summary)
        {
            EnsureHeaderRow();
            if (m_Summary == null)
            {
                m_Summary = new Label(summary);
                m_Summary.AddToClassList("blocks-card__summary");
                m_HeaderRow.Add(m_Summary);
            }
            else
            {
                m_Summary.text = summary;
            }
            return this;
        }

        void EnsureHeaderRow()
        {
            if (m_Promoted) return;
            m_Promoted = true;

            m_HeaderRow = new VisualElement();
            m_HeaderRow.AddToClassList("blocks-card__section-row");

            if (m_Title != null)
            {
                m_Title.RemoveFromHierarchy();
                m_Title.style.marginBottom = 0;
                m_HeaderRow.Add(m_Title);
            }

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            m_HeaderRow.Add(spacer);

            Insert(0, m_HeaderRow);
        }
    }

    #endregion

    #region Callouts & bullet lists

    enum StripTone
    {
        Default,
        Success,
        Warning,
    }

    sealed class Callout : VisualElement
    {
        public Callout(string label, string body = null, StripTone tone = StripTone.Default)
        {
            AddToClassList("blocks-prompt-card");
            if (tone == StripTone.Success) AddToClassList("blocks-prompt-card--success");
            else if (tone == StripTone.Warning) AddToClassList("blocks-prompt-card--warning");

            if (!string.IsNullOrEmpty(label))
            {
                var labelEl = new Label(label);
                labelEl.AddToClassList("blocks-prompt-card__label");
                Add(labelEl);
            }

            if (!string.IsNullOrEmpty(body))
                Add(Body(body));
        }

        public static Label Body(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-prompt-card__body");
            return label;
        }

        public static VisualElement Intent(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;
            return new Callout("YOUR INTENT", $"“{prompt.Trim()}”");
        }

        public Callout WithBullets(IEnumerable<string> items)
        {
            Add(Bullets.List(items));
            return this;
        }

        public Callout WithSection(string heading, IEnumerable<string> items, float topMargin = 0f)
        {
            var wrap = new VisualElement();
            if (topMargin > 0f) wrap.style.marginTop = topMargin;

            var label = new Label(heading);
            label.AddToClassList("blocks-subsection-label");
            wrap.Add(label);

            wrap.Add(Bullets.List(items));
            Add(wrap);
            return this;
        }
    }

    static class Bullets
    {
        public static VisualElement List(IEnumerable<string> items)
        {
            var list = new VisualElement();
            list.AddToClassList("blocks-actions-list");
            if (items != null)
            {
                foreach (string item in items)
                {
                    if (string.IsNullOrWhiteSpace(item)) continue;
                    list.Add(Row(item.Trim()));
                }
            }
            return list;
        }

        static VisualElement Row(string text)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-bullet-row");

            var dot = new Label("•");
            dot.AddToClassList("blocks-bullet-row__dot");
            row.Add(dot);

            var label = new Label(text);
            label.AddToClassList("blocks-bullet-row__text");
            row.Add(label);

            return row;
        }
    }

    #endregion

    #region Selectable options

    enum OptionLayout
    {
        Panel,
        Tile,
    }

    sealed class OptionCard : VisualElement
    {
        public OptionCard(
            string title,
            string description,
            string[] tags,
            bool selected,
            Action onClick,
            OptionLayout layout = OptionLayout.Panel)
        {
            bool tile = layout == OptionLayout.Tile;
            AddToClassList(tile ? "blocks-tpl" : "blocks-pick");
            if (selected) AddToClassList(tile ? "blocks-tpl--selected" : "blocks-pick--selected");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList(tile ? "blocks-tpl__name" : "blocks-pick__title");
            Add(titleLabel);

            if (!string.IsNullOrEmpty(description))
            {
                var desc = new Label(description);
                desc.AddToClassList(tile ? "blocks-tpl__desc" : "blocks-pick__desc");
                Add(desc);
            }

            if (tags != null && tags.Length > 0)
            {
                if (tile)
                {
                    var examples = new Label(string.Join(" · ", tags));
                    examples.AddToClassList("blocks-tpl__example");
                    Add(examples);
                }
                else
                {
                    Add(Pill.Row(tags));
                }
            }

            if (onClick != null) RegisterCallback<ClickEvent>(_ => onClick());
        }
    }

    sealed class OptionRow : VisualElement
    {
        public OptionRow(string title, string body = null, bool selected = false)
        {
            AddToClassList("blocks-option-row");
            if (selected) AddToClassList("blocks-option-row--selected");

            var col = new VisualElement();
            col.style.flexGrow = 1;

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("blocks-option-row__title");
            col.Add(titleLabel);

            if (!string.IsNullOrEmpty(body))
            {
                var bodyLabel = new Label(body);
                bodyLabel.AddToClassList("blocks-option-row__body");
                col.Add(bodyLabel);
            }

            Add(col);
        }

        public OptionRow WithLeading(VisualElement element)
        {
            Insert(0, element);
            return this;
        }

        public OptionRow WithTrailing(VisualElement element)
        {
            Add(element);
            return this;
        }

        public OptionRow Clickable(Action onClick)
        {
            if (onClick != null) RegisterCallback<ClickEvent>(_ => onClick());
            return this;
        }
    }

    #endregion
}

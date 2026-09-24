using System;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

// Reusable UI Toolkit controls for the Building Blocks editor windows.
//   - Buttons: primary / secondary / link button factories.
//   - Heading: title, subtitle, and muted hint labels.
//   - NumberField: float fields (bare, labeled, or compact inline).
//   - Pill: tag pills, a pill row, and a removable chip.
//   - LabeledRow: a key label paired with a control that fills the rest of the row.
//   - TabBar: a segmented row of tabs with one active.
//   - StepIndicator: a wizard progress bar; completed steps can be clicked to jump back.
namespace Blocks
{
    #region Basic controls

    static class Buttons
    {
        public static Button Primary(string label, Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("blocks-button--primary");
            return button;
        }

        public static Button Secondary(string label, Action onClick)
        {
            return new Button(onClick) { text = label };
        }

        public static Button Link(string label, Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("blocks-link");
            return button;
        }
    }

    static class Heading
    {
        public static Label Title(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-stage__title");
            return label;
        }

        public static Label Subtitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-stage__subtitle");
            return label;
        }

        public static Label Hint(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-muted");
            return label;
        }
    }

    static class NumberField
    {
        public static FloatField Float(float value, Action<float> onChange, float maxWidth = 0f)
        {
            var field = new FloatField { value = value };
            if (maxWidth > 0f) field.style.maxWidth = maxWidth;
            field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return field;
        }

        public static FloatField Labeled(string label, float value, Action<float> onChange, float maxWidth = 0f)
        {
            var field = new FloatField(label) { value = value };
            if (maxWidth > 0f) field.style.maxWidth = maxWidth;
            field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return field;
        }

        public static FloatField Inline(string label, float value, float fieldWidth, float labelWidth, Action<float> onChange, float marginLeft = 0f)
        {
            var field = new FloatField(label) { value = value };
            field.style.width = fieldWidth;
            if (marginLeft > 0f) field.style.marginLeft = marginLeft;
            field.CompactInlineLabel(labelWidth);
            field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return field;
        }
    }

    static class Pill
    {
        public static Label Tag(string text)
        {
            var pill = new Label(text);
            pill.AddToClassList("blocks-pill");
            return pill;
        }

        public static VisualElement Row(IEnumerable<string> tags)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-pill-row");
            if (tags != null)
                foreach (string tag in tags)
                    row.Add(Tag(tag));
            return row;
        }

        public static VisualElement Removable(string text, Action onRemove)
        {
            var chip = new VisualElement();
            chip.AddToClassList("blocks-pill");
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.marginBottom = 2;

            chip.Add(new Label(text));

            var x = new Button(onRemove) { text = "×" };
            x.style.width = 18;
            x.style.height = 16;
            x.style.marginLeft = 4;
            x.style.paddingLeft = 0;
            x.style.paddingRight = 0;
            chip.Add(x);

            return chip;
        }
    }

    #endregion

    #region Layout & navigation

    sealed class LabeledRow : VisualElement
    {
        public LabeledRow(string label, VisualElement control)
        {
            AddToClassList("blocks-kv");
            style.marginBottom = 8;
            style.alignItems = Align.Center;

            var key = new Label(label);
            key.AddToClassList("blocks-kv__key");
            Add(key);

            control.style.flexGrow = 1;
            Add(control);
        }
    }

    sealed class TabBar : VisualElement
    {
        public TabBar(string[] labels, int selectedIndex, Action<int> onSelect)
        {
            AddToClassList("blocks-segmented");
            if (labels == null) return;

            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var tab = new Button(() =>
                {
                    if (index == selectedIndex) return;
                    onSelect?.Invoke(index);
                }) { text = labels[i] };

                tab.AddToClassList("blocks-segmented__tab");
                if (i == selectedIndex) tab.AddToClassList("blocks-segmented__tab--active");
                Add(tab);
            }
        }
    }

    sealed class StepIndicator : VisualElement
    {
        public StepIndicator(string[] labels, int current, Action<int> onJumpToDone = null)
        {
            AddToClassList("blocks-wizard__progress");

            for (int i = 0; i < labels.Length; i++)
            {
                bool done = i < current;
                bool isCurrent = i == current;

                var node = new VisualElement();
                node.AddToClassList("blocks-step");
                if (done) node.AddToClassList("blocks-step--done");
                if (isCurrent) node.AddToClassList("blocks-step--current");

                var num = new Label(done ? "✓" : (i + 1).ToString());
                num.AddToClassList("blocks-step__num");
                node.Add(num);

                var label = new Label(labels[i]);
                label.AddToClassList("blocks-step__label");
                node.Add(label);

                if (done && onJumpToDone != null)
                {
                    int captured = i;
                    node.RegisterCallback<ClickEvent>(_ => onJumpToDone(captured));
                }

                Add(node);

                if (i < labels.Length - 1)
                {
                    var bar = new VisualElement();
                    bar.AddToClassList("blocks-step-bar");
                    if (done) bar.AddToClassList("blocks-step-bar--done");
                    Add(bar);
                }
            }
        }
    }

    #endregion
}

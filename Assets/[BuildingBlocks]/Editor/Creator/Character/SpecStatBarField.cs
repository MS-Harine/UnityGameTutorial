using System;
using UnityEngine;
using Blocks.Character;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// An editable stat row for the Creator: a type dropdown, a draggable value bar, and max, regen, and
    /// color fields, all bound to a StatEntry. Dragging the bar sets the start value; changing the max keeps
    /// the start value in step.
    /// </summary>
    sealed class SpecStatBarField : VisualElement
    {
        const float k_MinHeight = 26f;

        readonly EnumField m_TypeField;
        readonly VisualElement m_BarBackground;
        readonly VisualElement m_BarFill;
        readonly Label m_BarValueLabel;
        readonly FloatField m_MaxField;
        readonly FloatField m_RegenField;
        readonly ColorField m_ColorField;

        StatEntry m_Entry;
        Action m_OnChanged;

        #region Setup

        public SpecStatBarField()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.minHeight = k_MinHeight;
            style.marginTop = 2;
            style.marginBottom = 2;

            m_TypeField = new EnumField(StatType.Health);
            m_TypeField.style.width = 90;
            m_TypeField.RegisterValueChangedCallback(OnTypeChanged);
            Add(m_TypeField);

            m_BarBackground = new VisualElement();
            m_BarBackground.style.flexGrow = 1;
            m_BarBackground.style.height = 18;
            m_BarBackground.style.marginLeft = 6;
            m_BarBackground.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
            m_BarBackground.style.borderTopLeftRadius = 3;
            m_BarBackground.style.borderTopRightRadius = 3;
            m_BarBackground.style.borderBottomLeftRadius = 3;
            m_BarBackground.style.borderBottomRightRadius = 3;
            m_BarBackground.style.overflow = Overflow.Hidden;
            m_BarBackground.RegisterCallback<PointerDownEvent>(OnBarPointerDown);
            m_BarBackground.RegisterCallback<PointerMoveEvent>(OnBarPointerMove);
            m_BarBackground.RegisterCallback<PointerUpEvent>(OnBarPointerUp);
            Add(m_BarBackground);

            m_BarFill = new VisualElement();
            m_BarFill.style.position = Position.Absolute;
            m_BarFill.style.left = 0;
            m_BarFill.style.top = 0;
            m_BarFill.style.bottom = 0;
            m_BarFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            m_BarFill.style.backgroundColor = new Color(0.85f, 0.25f, 0.30f, 1f);
            m_BarBackground.Add(m_BarFill);

            m_BarValueLabel = new Label();
            m_BarValueLabel.style.position = Position.Absolute;
            m_BarValueLabel.style.left = 0;
            m_BarValueLabel.style.right = 0;
            m_BarValueLabel.style.top = 0;
            m_BarValueLabel.style.bottom = 0;
            m_BarValueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_BarValueLabel.style.color = Color.white;
            m_BarValueLabel.style.fontSize = 11;
            m_BarValueLabel.pickingMode = PickingMode.Ignore;
            m_BarBackground.Add(m_BarValueLabel);

            m_MaxField = new FloatField("Max") { isDelayed = true };
            m_MaxField.style.width = 92;
            m_MaxField.style.marginLeft = 8;
            m_MaxField.CompactInlineLabel(32);
            m_MaxField.RegisterValueChangedCallback(OnMaxChanged);
            Add(m_MaxField);

            m_RegenField = new FloatField("Regen/s");
            m_RegenField.style.width = 96;
            m_RegenField.style.marginLeft = 4;
            m_RegenField.CompactInlineLabel(48);
            m_RegenField.RegisterValueChangedCallback(OnRegenChanged);
            Add(m_RegenField);

            m_ColorField = new ColorField { showAlpha = false, showEyeDropper = true };
            m_ColorField.style.width = 36;
            m_ColorField.style.marginLeft = 6;
            m_ColorField.RegisterValueChangedCallback(OnColorChanged);
            Add(m_ColorField);
        }

        public void Bind(StatEntry entry, Action onChanged)
        {
            m_Entry = entry;
            m_OnChanged = onChanged;
            if (m_Entry == null) return;

            m_TypeField.SetValueWithoutNotify(m_Entry.Type);
            m_MaxField.SetValueWithoutNotify(m_Entry.MaxValue);
            m_RegenField.SetValueWithoutNotify(m_Entry.RegenRate);
            m_ColorField.SetValueWithoutNotify(m_Entry.Color);
            RefreshFill();
        }

        #endregion

        #region Value callbacks

        void OnTypeChanged(ChangeEvent<Enum> evt)
        {
            if (m_Entry == null) return;
            var newType = (StatType)evt.newValue;
            bool wasDefaultColor = ApproximatelyEqual(m_Entry.Color, StatTypeDefaults.ColorFor(m_Entry.Type));
            m_Entry.Type = newType;
            if (wasDefaultColor)
            {
                m_Entry.Color = StatTypeDefaults.ColorFor(newType);
                m_ColorField.SetValueWithoutNotify(m_Entry.Color);
                RefreshFill();
            }
            m_OnChanged?.Invoke();
        }

        void OnMaxChanged(ChangeEvent<float> evt)
        {
            if (m_Entry == null) return;
            float value = Mathf.Max(0f, evt.newValue);
            m_Entry.MaxValue = value;
            m_Entry.StartValue = value;
            RefreshFill();
            m_OnChanged?.Invoke();
        }

        void OnRegenChanged(ChangeEvent<float> evt)
        {
            if (m_Entry == null) return;
            m_Entry.RegenRate = Mathf.Max(0f, evt.newValue);
            m_OnChanged?.Invoke();
        }

        void OnColorChanged(ChangeEvent<Color> evt)
        {
            if (m_Entry == null) return;
            m_Entry.Color = evt.newValue;
            RefreshFill();
            m_OnChanged?.Invoke();
        }

        #endregion

        #region Bar fill & drag

        void RefreshFill()
        {
            if (m_Entry == null) return;
            float ratio = m_Entry.MaxValue > 0f ? Mathf.Clamp01(m_Entry.StartValue / m_Entry.MaxValue) : 0f;
            m_BarFill.style.width = new StyleLength(new Length(ratio * 100f, LengthUnit.Percent));
            m_BarFill.style.backgroundColor = m_Entry.Color;
            m_BarValueLabel.text = $"{Mathf.Round(m_Entry.StartValue)} / {Mathf.Round(m_Entry.MaxValue)}";
        }

        void OnBarPointerDown(PointerDownEvent evt)
        {
            m_BarBackground.CapturePointer(evt.pointerId);
            ApplyDragValue(evt.localPosition.x);
            evt.StopPropagation();
        }

        void OnBarPointerMove(PointerMoveEvent evt)
        {
            if (!m_BarBackground.HasPointerCapture(evt.pointerId)) return;
            ApplyDragValue(evt.localPosition.x);
        }

        void OnBarPointerUp(PointerUpEvent evt)
        {
            if (m_BarBackground.HasPointerCapture(evt.pointerId))
            {
                m_BarBackground.ReleasePointer(evt.pointerId);
            }
        }

        void ApplyDragValue(float localX)
        {
            if (m_Entry == null) return;
            float width = m_BarBackground.resolvedStyle.width;
            if (width <= 0f) return;

            float ratio = Mathf.Clamp01(localX / width);
            float next = Mathf.Round(ratio * m_Entry.MaxValue);
            if (Mathf.Approximately(next, m_Entry.StartValue)) return;

            m_Entry.StartValue = next;
            RefreshFill();
            m_OnChanged?.Invoke();
        }

        static bool ApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                && Mathf.Approximately(a.g, b.g)
                && Mathf.Approximately(a.b, b.b)
                && Mathf.Approximately(a.a, b.a);
        }

        #endregion
    }
}

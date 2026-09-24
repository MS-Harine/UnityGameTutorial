using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// Inspector card for the character's stats, shown as a reorderable list of stat bars with add and
    /// remove. New entries get their type's default color. The summary line previews the first few stats.
    /// </summary>
    sealed class StatsCard : ModuleCard
    {
        const string k_StatsFieldName = CharacterFields.Stats;

        static readonly FieldInfo k_StatsField = typeof(BuildingBlocksCharacter)
            .GetField(k_StatsFieldName, BindingFlags.NonPublic | BindingFlags.Instance);

        SerializedProperty m_StatsProp;
        ListView m_ListView;

        public StatsCard() : base("stats", "Stats") { }

        #region Card

        protected override void BuildBody(VisualElement body, SerializedObject serializedObject)
        {
            m_StatsProp = FindForSummary(k_StatsFieldName);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd;
            actions.style.marginBottom = 4;

            var createTypeBtn = new Button(NewStatTypePopup.ShowPopup)
            {
                text = "Create new stat",
                tooltip = "Add a new entry to the StatType enum",
            };
            actions.Add(createTypeBtn);
            body.Add(actions);

            m_ListView = new ListView
            {
                bindingPath = k_StatsFieldName,
                showFoldoutHeader = false,
                showAddRemoveFooter = true,
                showBorder = false,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                makeItem = () => new StatBarField(),
                bindItem = (element, index) =>
                {
                    if (index < 0 || index >= m_StatsProp.arraySize) return;
                    var elementProp = m_StatsProp.GetArrayElementAtIndex(index);
                    ((StatBarField)element).BindElement(elementProp, () => GetLiveStat(index));
                },
            };
            m_ListView.itemsAdded += OnItemsAdded;
            m_ListView.Bind(serializedObject);
            body.Add(m_ListView);

            m_ListView.style.backgroundColor = Color.clear;
            m_ListView.RegisterCallback<GeometryChangedEvent>(_ => ClearListViewChromeBackgrounds());
            ClearListViewChromeBackgrounds();
        }

        protected override string BuildSummary()
        {
            if (m_StatsProp == null || m_StatsProp.arraySize == 0) return "No stats";

            int count = m_StatsProp.arraySize;
            var sb = new System.Text.StringBuilder();
            sb.Append(count).Append(count == 1 ? " stat" : " stats");

            int shown = 0;
            for (int i = 0; i < count && shown < 3; i++)
            {
                var element = m_StatsProp.GetArrayElementAtIndex(i);
                var typeProp = element.FindPropertyRelative(CharacterFields.Stat.Type);
                var maxProp = element.FindPropertyRelative(CharacterFields.Stat.MaxValue);
                if (typeProp == null || maxProp == null) continue;

                sb.Append(" · ");
                sb.Append((StatType)typeProp.intValue);
                sb.Append(' ');
                sb.Append(Mathf.Round(maxProp.floatValue));
                shown++;
            }
            return sb.ToString();
        }

        #endregion

        #region List management

        void OnItemsAdded(IEnumerable<int> indices)
        {
            if (m_StatsProp == null) return;
            m_StatsProp.serializedObject.Update();
            foreach (int index in indices)
            {
                if (index < 0 || index >= m_StatsProp.arraySize) continue;
                var elementProp = m_StatsProp.GetArrayElementAtIndex(index);
                var typeProp = elementProp.FindPropertyRelative(CharacterFields.Stat.Type);
                var colorProp = elementProp.FindPropertyRelative(CharacterFields.Stat.Color);
                if (typeProp == null || colorProp == null) continue;
                colorProp.colorValue = StatTypeDefaults.ColorFor((StatType)typeProp.intValue);
            }
            m_StatsProp.serializedObject.ApplyModifiedProperties();
        }

        void ClearListViewChromeBackgrounds()
        {
            if (m_ListView == null) return;
            foreach (string className in k_ListChromeClasses)
            {
                foreach (var element in m_ListView.Query(className: className).Build())
                {
                    element.style.backgroundColor = Color.clear;
                }
            }
        }

        static readonly string[] k_ListChromeClasses =
        {
            "unity-list-view__size-field",
            "unity-list-view__footer",
            "unity-list-view__scroll-view--with-footer",
            "unity-scroll-view",
            "unity-scroll-view__content-container",
        };

        Stat GetLiveStat(int index)
        {
            if (k_StatsField == null) return null;
            if (SerializedObject == null || SerializedObject.targetObject == null) return null;

            var stats = k_StatsField.GetValue(SerializedObject.targetObject) as List<Stat>;
            if (stats == null || index < 0 || index >= stats.Count) return null;
            return stats[index];
        }

        #endregion
    }

    /// <summary>
    /// One stat row in the stats list: a type dropdown, a draggable fill bar, and max, regen, and color
    /// fields bound to the serialized stat. In play mode it shows the live current value and locks editing.
    /// </summary>
    sealed class StatBarField : VisualElement
    {
        const float k_MinHeight = 26f;

        readonly EnumField m_TypeField;
        readonly VisualElement m_BarBackground;
        readonly VisualElement m_BarFill;
        readonly Label m_BarValueLabel;
        readonly FloatField m_MaxField;
        readonly FloatField m_RegenField;
        readonly ColorField m_ColorField;

        SerializedProperty m_TypeProp;
        SerializedProperty m_StartProp;
        SerializedProperty m_MaxProp;
        SerializedProperty m_RegenProp;
        SerializedProperty m_ColorProp;
        Func<Stat> m_GetLiveStat;
        IVisualElementScheduledItem m_PlayModeRefresh;

        #region Setup

        public StatBarField()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.minHeight = k_MinHeight;
            style.marginTop = 2;
            style.marginBottom = 2;

            m_TypeField = new EnumField(StatType.Health);
            m_TypeField.style.width = 90;
            Add(m_TypeField);

            m_BarBackground = new VisualElement();
            m_BarBackground.AddToClassList("blocks-stat-bar");
            m_BarBackground.RegisterCallback<PointerDownEvent>(OnBarPointerDown);
            m_BarBackground.RegisterCallback<PointerMoveEvent>(OnBarPointerMove);
            m_BarBackground.RegisterCallback<PointerUpEvent>(OnBarPointerUp);
            Add(m_BarBackground);

            m_BarFill = new VisualElement();
            m_BarFill.AddToClassList("blocks-stat-bar__fill");
            m_BarFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            m_BarBackground.Add(m_BarFill);

            m_BarValueLabel = new Label();
            m_BarValueLabel.AddToClassList("blocks-stat-bar__label");
            m_BarValueLabel.pickingMode = PickingMode.Ignore;
            m_BarBackground.Add(m_BarValueLabel);

            m_MaxField = new FloatField("Max") { isDelayed = true };
            m_MaxField.style.width = 92;
            m_MaxField.style.marginLeft = 8;
            m_MaxField.CompactInlineLabel(32);
            Add(m_MaxField);

            m_RegenField = new FloatField("Regen/s");
            m_RegenField.style.width = 96;
            m_RegenField.style.marginLeft = 4;
            m_RegenField.CompactInlineLabel(48);
            Add(m_RegenField);

            m_ColorField = new ColorField { showAlpha = false, showEyeDropper = true };
            m_ColorField.style.width = 36;
            m_ColorField.style.marginLeft = 6;
            Add(m_ColorField);
        }

        public void BindElement(SerializedProperty statElement, Func<Stat> getLiveStat)
        {
            m_TypeProp = statElement.FindPropertyRelative(CharacterFields.Stat.Type);
            m_StartProp = statElement.FindPropertyRelative(CharacterFields.Stat.StartValue);
            m_MaxProp = statElement.FindPropertyRelative(CharacterFields.Stat.MaxValue);
            m_RegenProp = statElement.FindPropertyRelative(CharacterFields.Stat.RegenRate);
            m_ColorProp = statElement.FindPropertyRelative(CharacterFields.Stat.Color);
            m_GetLiveStat = getLiveStat;

            m_TypeField.BindProperty(m_TypeProp);
            m_MaxField.BindProperty(m_MaxProp);
            if (m_RegenProp != null) m_RegenField.BindProperty(m_RegenProp);
            m_ColorField.BindProperty(m_ColorProp);

            this.TrackPropertyValue(m_StartProp, _ => RefreshDisplay());
            this.TrackPropertyValue(m_MaxProp, _ => OnMaxPropChanged());
            this.TrackPropertyValue(m_ColorProp, _ => RefreshFillColor());

            RefreshFillColor();
            RefreshDisplay();

            m_PlayModeRefresh?.Pause();
            if (EditorApplication.isPlaying)
            {
                m_PlayModeRefresh = schedule.Execute(RefreshDisplay).Every(50);
            }
        }

        #endregion

        #region Display

        void RefreshFillColor()
        {
            if (m_ColorProp == null) return;
            m_BarFill.style.backgroundColor = m_ColorProp.colorValue;
        }

        void OnMaxPropChanged()
        {
            if (m_StartProp == null || m_MaxProp == null) return;
            if (!EditorApplication.isPlaying && !Mathf.Approximately(m_StartProp.floatValue, m_MaxProp.floatValue))
            {
                m_StartProp.floatValue = m_MaxProp.floatValue;
                m_StartProp.serializedObject.ApplyModifiedProperties();
            }
            RefreshDisplay();
        }

        void RefreshDisplay()
        {
            if (m_MaxProp == null) { return; }
            float max = m_MaxProp.floatValue;
            float current = ResolveCurrent();
            float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;

            m_BarFill.style.width = new StyleLength(new Length(ratio * 100f, LengthUnit.Percent));
            m_BarValueLabel.text = $"{Mathf.Round(current)} / {Mathf.Round(max)}";

            bool isLive = EditorApplication.isPlaying;
            m_BarBackground.SetEnabled(!isLive);
            m_MaxField.SetEnabled(!isLive);
            m_RegenField.SetEnabled(!isLive);
            m_TypeField.SetEnabled(!isLive);
            m_ColorField.SetEnabled(!isLive);
        }

        float ResolveCurrent()
        {
            if (EditorApplication.isPlaying && m_GetLiveStat != null)
            {
                Stat live = m_GetLiveStat.Invoke();
                if (live != null) return live.CurrentValue;
            }
            return m_StartProp.floatValue;
        }

        #endregion

        #region Bar drag

        void OnBarPointerDown(PointerDownEvent evt)
        {
            if (EditorApplication.isPlaying) return;
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
            float width = m_BarBackground.resolvedStyle.width;
            if (width <= 0f) return;

            float ratio = Mathf.Clamp01(localX / width);
            float max = m_MaxProp.floatValue;
            float next = Mathf.Round(ratio * max);

            if (Mathf.Approximately(next, m_StartProp.floatValue)) return;

            m_StartProp.floatValue = next;
            m_StartProp.serializedObject.ApplyModifiedProperties();
        }

        #endregion
    }
}

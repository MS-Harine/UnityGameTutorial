using UnityEngine;
using Blocks.Character;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Blocks.HUD
{
    /// <summary>
    /// One bar per stat, stacked in the container it is handed. Uses Unity's ProgressBar control so the
    /// bars pick up the shared <c>blocks-progress-bar</c> styling from the theme; each bar's fill is then
    /// tinted with that stat's own colour.
    /// </summary>
    public sealed class StatBarStack
    {
        const string k_BarClass = "blocks-progress-bar";
        const string k_FillClass = "unity-progress-bar__progress";
        const string k_WorldModifier = "blocks-progress-bar--world";

        readonly BuildingBlocksCharacter m_Character;
        readonly VisualElement m_Container;
        readonly Dictionary<StatType, ProgressBar> m_BarByType = new();

        bool m_Bound;

        public StatBarStack(BuildingBlocksCharacter character, VisualElement container)
        {
            m_Character = character;
            m_Container = container;
        }

        public void Build()
        {
            m_Container.Clear();
            m_BarByType.Clear();

            // World-space stacks want the slimmer bars; the stack carries the marker, the bars need it.
            bool isWorld = m_Container.ClassListContains("stat-bar-stack--world");

            m_Character.ForEachStat(stat =>
            {
                ProgressBar bar = new ProgressBar
                {
                    lowValue = 0f,
                    highValue = 1f,
                    value = Mathf.Clamp01(stat.Ratio),
                };

                bar.AddToClassList(k_BarClass);
                if (isWorld) bar.AddToClassList(k_WorldModifier);
                bar.pickingMode = PickingMode.Ignore;

                m_Container.Add(bar);
                TintFill(bar, stat.Color);

                m_BarByType[stat.Type] = bar;
            });
        }

        public void Bind()
        {
            if (m_Bound) return;
            m_Character.OnAnyStatChanged += HandleStatChanged;
            m_Bound = true;
        }

        public void Unbind()
        {
            if (!m_Bound) return;
            m_Character.OnAnyStatChanged -= HandleStatChanged;
            m_Bound = false;
        }

        public void SetVisible(bool visible)
        {
            m_Container.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void RefreshAll()
        {
            m_Character.ForEachStat(stat =>
            {
                if (m_BarByType.TryGetValue(stat.Type, out ProgressBar bar))
                {
                    bar.value = Mathf.Clamp01(stat.Ratio);
                }
            });
        }

        void HandleStatChanged(StatType type, float oldValue, float newValue)
        {
            if (!m_BarByType.TryGetValue(type, out ProgressBar bar)) return;

            float max = m_Character.GetMax(type);
            bar.value = max > 0f ? Mathf.Clamp01(newValue / max) : 0f;
        }

        /// <summary>
        /// Colours the fill the control builds for itself. The element only exists once the bar has been
        /// added to a tree, which is why this runs after the Add rather than during construction.
        /// </summary>
        static void TintFill(ProgressBar bar, Color color)
        {
            VisualElement fill = bar.Q(className: k_FillClass);
            if (fill == null) return;
            fill.style.backgroundColor = color;
        }
    }
}

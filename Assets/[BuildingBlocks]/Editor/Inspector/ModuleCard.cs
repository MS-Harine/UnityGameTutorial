using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// Base for a collapsible inspector card with a title, summary line, and optional enable toggle.
    /// Subclasses fill the body and the summary text; the base handles expand and collapse, dimming when
    /// disabled, and helpers for binding serialized fields. Each card remembers its expanded state per
    /// object in SessionState.
    /// </summary>
    abstract class ModuleCard : VisualElement
    {
        protected SerializedObject SerializedObject { get; private set; }

        readonly string m_CardId;
        readonly Label m_SummaryLabel;
        readonly Toggle m_EnableToggle;
        readonly Label m_Chevron;
        readonly VisualElement m_Body;

        SerializedProperty m_EnableProperty;

        #region Setup

        protected ModuleCard(string cardId, string title)
        {
            m_CardId = cardId;
            AddToClassList("blocks-module-card");

            var header = new VisualElement();
            header.AddToClassList("blocks-module-card__header");
            header.RegisterCallback<ClickEvent>(OnHeaderClicked);
            Add(header);

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("blocks-module-card__title");
            header.Add(titleLabel);

            m_SummaryLabel = new Label(string.Empty);
            m_SummaryLabel.AddToClassList("blocks-module-card__summary");
            header.Add(m_SummaryLabel);

            m_EnableToggle = new Toggle();
            m_EnableToggle.AddToClassList("blocks-module-card__enable");
            m_EnableToggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            header.Add(m_EnableToggle);

            m_Chevron = new Label("▸");
            m_Chevron.AddToClassList("blocks-module-card__chevron");
            header.Add(m_Chevron);

            m_Body = new VisualElement();
            m_Body.AddToClassList("blocks-module-card__body");
            Add(m_Body);
        }

        public void Bind(SerializedObject serializedObject)
        {
            SerializedObject = serializedObject;
            BuildBody(m_Body, serializedObject);

            bool isExpanded = SessionState.GetBool(ExpandedKey(serializedObject), DefaultExpanded);
            ApplyExpanded(isExpanded);
            RefreshSummary();
        }

        protected virtual bool DefaultExpanded => false;

        #endregion

        #region Subclass API

        protected void BindEnableToggle(string enablePropertyPath)
        {
            m_EnableProperty = SerializedObject.FindProperty(enablePropertyPath);
            if (m_EnableProperty == null) return;

            m_EnableToggle.AddToClassList("blocks-module-card__enable--shown");
            m_EnableToggle.BindProperty(m_EnableProperty);
            m_EnableToggle.TrackPropertyValue(m_EnableProperty, _ =>
            {
                ApplyDimming();
                RefreshSummary();
            });
            ApplyDimming();
        }

        protected void TrackForSummary(SerializedProperty property)
        {
            if (property == null) return;
            this.TrackPropertyValue(property, _ => RefreshSummary());
        }

        protected void RefreshSummary()
        {
            m_SummaryLabel.text = BuildSummary();
        }

        protected bool IsEnabled => m_EnableProperty == null || m_EnableProperty.boolValue;

        protected bool IsMultiEditing => SerializedObject != null && SerializedObject.targetObjects.Length > 1;

        protected T Target<T>() where T : UnityEngine.Object =>
            IsMultiEditing ? null : SerializedObject?.targetObject as T;

        protected PropertyField AddField(VisualElement container, string path, string label)
        {
            var prop = SerializedObject.FindProperty(path);
            if (prop == null) return null;

            var field = new PropertyField(prop, label);
            field.Bind(SerializedObject);
            container.Add(field);
            return field;
        }

        protected SerializedProperty FindForSummary(string path)
        {
            var prop = SerializedObject.FindProperty(path);
            TrackForSummary(prop);
            return prop;
        }

        protected void ShowWhen(VisualElement field, SerializedProperty controller, Func<bool> isVisible)
        {
            if (field == null || controller == null) return;

            void Apply() => field.style.display = isVisible() ? DisplayStyle.Flex : DisplayStyle.None;
            Apply();
            this.TrackPropertyValue(controller, _ => Apply());
        }

        protected static Label MutedLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-text--dim");
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        protected abstract void BuildBody(VisualElement body, SerializedObject serializedObject);
        protected abstract string BuildSummary();

        #endregion

        #region Expand & dim

        void OnHeaderClicked(ClickEvent evt)
        {
            string key = ExpandedKey(SerializedObject);
            bool next = !SessionState.GetBool(key, false);
            SessionState.SetBool(key, next);
            ApplyExpanded(next);
        }

        void ApplyExpanded(bool isExpanded)
        {
            m_Body.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            m_Chevron.text = isExpanded ? "▾" : "▸";
        }

        void ApplyDimming()
        {
            bool isEnabled = m_EnableProperty == null || m_EnableProperty.boolValue;
            EnableInClassList("blocks-module-card--dimmed", !isEnabled);
        }

        string ExpandedKey(SerializedObject so)
        {
            string id = so != null && so.targetObject != null
                ? so.targetObject.GetEntityId().ToString()
                : "0";
            return $"BBCard.{id}.{m_CardId}.expanded";
        }

        #endregion
    }
}

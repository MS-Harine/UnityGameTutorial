using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// Inspector card for targeting: mode, target tag, search range, and memory duration. The tag field
    /// shows only in TaggedDamageable mode, and the enable toggle gates the whole targeting module.
    /// </summary>
    sealed class TargetingCard : ModuleCard
    {
        SerializedProperty m_ModeProp;
        SerializedProperty m_TagProp;
        SerializedProperty m_RadiusProp;
        TagField m_TagField;

        public TargetingCard() : base("targeting", "Targeting") { }

        protected override void BuildBody(VisualElement body, SerializedObject serializedObject)
        {
            m_ModeProp = FindForSummary(CharacterFields.TargetingMode);
            m_TagProp = FindForSummary(CharacterFields.TargetTag);
            m_RadiusProp = FindForSummary(CharacterFields.TargetRadius);

            AddField(body, CharacterFields.TargetingMode, "Mode");

            m_TagField = new TagField("Target Tag", m_TagProp.stringValue);
            m_TagField.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            m_TagField.RegisterValueChangedCallback(evt =>
            {
                m_TagProp.stringValue = evt.newValue;
                m_TagProp.serializedObject.ApplyModifiedProperties();
            });
            this.TrackPropertyValue(m_TagProp, prop => m_TagField.SetValueWithoutNotify(prop.stringValue));
            body.Add(m_TagField);

            AddField(body, CharacterFields.TargetRadius, "Range");
            AddField(body, CharacterFields.TargetMemoryDuration, "Memory (s)");

            BindEnableToggle(CharacterFields.IsTargetingEnabled);

            ShowWhen(m_TagField, m_ModeProp,
                () => (TargetingMode)m_ModeProp.intValue == TargetingMode.TaggedDamageable);
        }

        protected override string BuildSummary()
        {
            if (!IsEnabled) return "Disabled";

            var mode = (TargetingMode)m_ModeProp.intValue;
            float range = m_RadiusProp.floatValue;
            if (mode == TargetingMode.TaggedDamageable)
            {
                return $"Tag: {m_TagProp.stringValue} · {range:0.#}m";
            }
            return $"Nearest · {range:0.#}m";
        }
    }
}

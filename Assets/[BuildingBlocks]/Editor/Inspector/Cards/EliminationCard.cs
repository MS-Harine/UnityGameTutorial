using UnityEditor;
using UnityEngine.UIElements;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// Inspector card for the elimination behavior and its delay. Hides the delay field when the behavior
    /// is Disable, and the control delay unless the character respawns — it is the only behavior that ever
    /// hands control back.
    /// </summary>
    sealed class EliminationCard : ModuleCard
    {
        SerializedProperty m_BehaviorProp;
        SerializedProperty m_DelayProp;

        public EliminationCard() : base("elimination", "On Eliminated") { }

        protected override void BuildBody(VisualElement body, SerializedObject serializedObject)
        {
            m_BehaviorProp = FindForSummary(CharacterFields.OnEliminated);
            m_DelayProp = FindForSummary(CharacterFields.Delay);

            AddField(body, CharacterFields.OnEliminated, "Behavior");
            var delayField = AddField(body, CharacterFields.Delay, "Delay (s)");
            var controlDelayField = AddField(body, CharacterFields.ControlDelayAfterRespawn, "Control Delay (s)");

            ShowWhen(delayField, m_BehaviorProp,
                () => (EliminationBehavior)m_BehaviorProp.intValue != EliminationBehavior.Disable);
            ShowWhen(controlDelayField, m_BehaviorProp,
                () => (EliminationBehavior)m_BehaviorProp.intValue == EliminationBehavior.Respawn);
        }

        protected override string BuildSummary()
        {
            if (m_BehaviorProp == null) return string.Empty;

            var behavior = (EliminationBehavior)m_BehaviorProp.intValue;
            if (behavior == EliminationBehavior.Disable) return "Disable";

            float delay = m_DelayProp?.floatValue ?? 0f;
            return $"{behavior} · {delay:0.0}s";
        }
    }
}

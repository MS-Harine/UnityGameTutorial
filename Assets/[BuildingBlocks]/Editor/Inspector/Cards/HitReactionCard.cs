using UnityEditor;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// Inspector card for the character's hit reaction. Only the invulnerability window lives on the
    /// character — how a hit moves it is authored by the attacking ability and scaled by its mass.
    /// </summary>
    sealed class HitReactionCard : ModuleCard
    {
        SerializedProperty m_InvulnerabilityProp;

        public HitReactionCard() : base("hitReaction", "Hit Reaction") { }

        protected override void BuildBody(VisualElement body, SerializedObject serializedObject)
        {
            m_InvulnerabilityProp = FindForSummary(CharacterFields.InvulnerabilityDuration);

            AddField(body, CharacterFields.InvulnerabilityDuration, "Invulnerability (s)");

            body.Add(MutedLabel(
                "Push-back, fly-away, and stun are authored by the attacking ability (HitReaction) and " +
                "divided by this Rigidbody2D's mass — mass 1 takes the full hit, heavy bosses (~6+) " +
                "won't budge. Animation events (BeginStun/EndStun) can still drive stun by hand."));
        }

        protected override string BuildSummary()
        {
            if (m_InvulnerabilityProp == null) return string.Empty;

            float invulnerability = m_InvulnerabilityProp.floatValue;
            return invulnerability > 0f ? $"{invulnerability:0.##}s i-frames" : "Off";
        }
    }
}

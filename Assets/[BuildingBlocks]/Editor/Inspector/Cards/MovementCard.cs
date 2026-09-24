using Blocks.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// Inspector card for the character's gravity and slope tuning, plus the list of movement abilities.
    /// The enable toggle gates the whole movement module.
    /// </summary>
    sealed class MovementCard : ModuleCard
    {
        SerializedProperty m_GravityProp;

        public MovementCard() : base("movement", "Movement") { }

        protected override void BuildBody(VisualElement body, SerializedObject serializedObject)
        {
            m_GravityProp = FindForSummary(CharacterFields.Gravity);

            AddField(body, CharacterFields.Gravity, "Gravity");
            AddField(body, CharacterFields.FallGravityMultiplier, "Fall Gravity Multiplier");
            AddField(body, CharacterFields.MaxFallSpeed, "Max Fall Speed");
            AddField(body, CharacterFields.GroundSlopeLimit, "Ground Slope Limit");
            AddField(body, CharacterFields.WallSlopeLimit, "Wall Slope Limit");

            var abilityHeader = new Label("Movement Abilities");
            abilityHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            abilityHeader.style.marginTop = 10;
            abilityHeader.style.marginBottom = 4;
            body.Add(abilityHeader);

            body.Add(new AbilityListView<MovementAbility>(Target<BuildingBlocksCharacter>()));

            BindEnableToggle(CharacterFields.IsMovementEnabled);
        }

        protected override string BuildSummary()
        {
            float gravity = m_GravityProp?.floatValue ?? 0f;
            return IsEnabled ? $"Enabled · gravity {gravity:0.#}" : "Disabled";
        }
    }
}

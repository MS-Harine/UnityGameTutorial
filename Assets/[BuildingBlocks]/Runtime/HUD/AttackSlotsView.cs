using System.Collections.Generic;
using Blocks.Attack;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Blocks.HUD
{
    /// <summary>
    /// The pair of attack slots under the player's stat bars. Two side by side says the player has two
    /// attacks without them having to open the pause menu. Each is a compact version of the pause menu's
    /// own button, named for its attack ("Melee") with the keys that fire it captioned underneath
    /// ("E, Enter"); the charge sweeps across the pill while that attack is held, and at full charge it
    /// flips to the same solid gold <c>AttackFeedback</c> flashes the player sprite, so the two read as
    /// one event.
    ///
    /// Built from the ability rather than from inspector data: the names come from the input actions
    /// ("Melee", "Ranged") and the keys from <see cref="InputBindingText.ForAction"/> — the same call the
    /// pause menu's rows make, so the two lists can't drift apart.
    /// </summary>
    public sealed class AttackSlotsView
    {
        const string k_RowClass = "attack-slots__row";
        const string k_HintClass = "attack-slots__hint";
        const string k_HintHiddenModifier = "attack-slots__hint--hidden";
        const string k_SlotClass = "attack-slot";
        const string k_DimModifier = "attack-slot--dim";
        const string k_FullModifier = "attack-slot--full";
        const string k_PillClass = "attack-slot__pill";
        const string k_FillClass = "attack-slot__fill";
        const string k_NameClass = "attack-slot__name";
        const string k_KeysClass = "attack-slot__keys";

        // The pause menu's button, one size down; the shared theme owns both.
        const string k_ButtonClass = "blocks-button";
        const string k_ButtonSmallModifier = "blocks-button--sm";

        const string k_HintText = "Hold to charge";

        readonly VisualElement m_Container;
        readonly List<Slot> m_Slots = new List<Slot>();

        Label m_Hint;
        bool m_HasEverFullyCharged;

        /// <summary>One slot: which attack it stands for, and the two elements that change with its state.</summary>
        readonly struct Slot
        {
            public readonly PlayerAttackAbility.AttackKind Kind;
            public readonly VisualElement Root;
            public readonly VisualElement Fill;

            public Slot(PlayerAttackAbility.AttackKind kind, VisualElement root, VisualElement fill)
            {
                Kind = kind;
                Root = root;
                Fill = fill;
            }
        }

        public AttackSlotsView(VisualElement container)
        {
            m_Container = container;
        }

        /// <summary>
        /// Fills the container with one slot per attack the ability actually has. A character with no
        /// player attack ability, or one whose actions aren't assigned, gets no slots at all.
        /// </summary>
        public void Build(PlayerAttackAbility attack)
        {
            m_Container.Clear();
            m_Slots.Clear();
            m_Hint = null;

            if (attack == null)
            {
                m_Container.style.display = DisplayStyle.None;
                return;
            }

            VisualElement row = CreateElement(k_RowClass);
            m_Container.Add(row);

            AddSlot(row, PlayerAttackAbility.AttackKind.Melee, attack.MeleeAction);
            AddSlot(row, PlayerAttackAbility.AttackKind.Projectile, attack.RangedAction);

            // With neither action assigned, an empty row would just be a gap at the bottom of the screen.
            if (m_Slots.Count == 0)
            {
                m_Container.Clear();
                m_Container.style.display = DisplayStyle.None;
                return;
            }

            m_Container.style.display = DisplayStyle.Flex;

            m_Hint = new Label(k_HintText) { pickingMode = PickingMode.Ignore };
            m_Hint.AddToClassList(k_HintClass);
            m_Container.Add(m_Hint);
        }

        /// <summary>
        /// Polled every frame, the way <c>AttackFeedback</c> polls the same ability: the charge is a value
        /// that changes continuously, so there is no event worth raising for it.
        /// </summary>
        /// <param name="charging">Which attack is being charged, or <c>None</c>.</param>
        /// <param name="chargeRatio">How far that charge has come, 0–1.</param>
        /// <param name="isAvailable">False when the attacks can't be used at all: they are grounded-only,
        /// and a mid-air press is dropped without a sound.</param>
        public void Refresh(PlayerAttackAbility.AttackKind charging, float chargeRatio, bool isAvailable)
        {
            bool isAnyCharging = charging != PlayerAttackAbility.AttackKind.None;
            bool isFull = isAnyCharging && chargeRatio >= 1f;

            // The hint has done its job once a charge has been held all the way once: a player who never
            // holds the button is exactly the one who still needs telling, so it stays until they do.
            if (isFull) m_HasEverFullyCharged = true;
            m_Hint?.EnableInClassList(k_HintHiddenModifier, m_HasEverFullyCharged);

            for (int i = 0; i < m_Slots.Count; i++)
            {
                Slot slot = m_Slots[i];
                bool isThisCharging = slot.Kind == charging;

                slot.Fill.style.width = Length.Percent(isThisCharging ? chargeRatio * 100f : 0f);
                slot.Root.EnableInClassList(k_FullModifier, isThisCharging && isFull);

                // Dimmed while airborne, and while the *other* attack is charging: the two share one
                // charge, so during a hold the idle one really is unavailable.
                slot.Root.EnableInClassList(k_DimModifier, !isAvailable || (isAnyCharging && !isThisCharging));
            }
        }

        void AddSlot(VisualElement row, PlayerAttackAbility.AttackKind kind, InputActionReference reference)
        {
            InputAction action = reference != null ? reference.action : null;
            if (action == null) return;

            VisualElement slot = CreateElement(k_SlotClass);

            VisualElement pill = CreateElement(k_ButtonClass);
            pill.AddToClassList(k_ButtonSmallModifier);
            pill.AddToClassList(k_PillClass);

            // The fill goes in before the name, because later siblings paint over earlier ones, and the
            // sweep has to pass behind the text rather than across it.
            VisualElement fill = CreateElement(k_FillClass);
            pill.Add(fill);
            pill.Add(CreateLabel(action.name, k_NameClass));

            slot.Add(pill);

            // Under the pill rather than inside it, so the key list has the slot's full width to itself
            // and the pill keeps the one-line height blocks-button--sm gives it. Left off entirely for a
            // gamepad-only action rather than added empty, which would still take up its line.
            string keys = InputBindingText.ForAction(action);
            if (!string.IsNullOrEmpty(keys)) slot.Add(CreateLabel(keys, k_KeysClass));

            row.Add(slot);

            m_Slots.Add(new Slot(kind, slot, fill));
        }

        // Everything here is decoration over the gameplay, so nothing in it should ever eat a click.
        static VisualElement CreateElement(string className)
        {
            VisualElement element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }

        static Label CreateLabel(string text, string className)
        {
            Label label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
    }
}

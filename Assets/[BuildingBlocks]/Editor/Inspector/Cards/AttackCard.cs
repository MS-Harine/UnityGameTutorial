using System;
using System.Collections.Generic;
using Blocks.Attack;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// Inspector card for the character's single attack ability. A dropdown picks the attack class (or
    /// none), swapping the component through Undo, and the card flags any extra attack abilities so only one
    /// stays on the root. Polls to stay in sync.
    /// </summary>
    sealed class AttackCard : ModuleCard
    {
        readonly List<Type> m_Types = new();
        readonly List<AttackAbility> m_Buffer = new();

        BuildingBlocksCharacter m_Character;
        PopupField<string> m_Dropdown;
        VisualElement m_StatusRow;
        int m_LastHash;

        #region Card

        public AttackCard() : base("attack", "Attack") { }

        protected override void BuildBody(VisualElement body, SerializedObject serializedObject)
        {
            m_Character = Target<BuildingBlocksCharacter>();

            var header = new Label("Attack Ability");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4;
            body.Add(header);

            BuildDropdown(body);

            m_StatusRow = new VisualElement();
            m_StatusRow.style.marginTop = 4;
            body.Add(m_StatusRow);

            BindEnableToggle(CharacterFields.IsAttackEnabled);

            if (m_Character == null)
            {
                m_Dropdown.SetEnabled(false);
                m_StatusRow.Add(MutedLabel("(Multi-edit not supported)"));
                return;
            }

            body.schedule.Execute(RefreshIfChanged).Every(500);
            Refresh();
        }

        protected override string BuildSummary() => IsEnabled ? "Enabled" : "Disabled";

        #endregion

        #region Dropdown

        void BuildDropdown(VisualElement body)
        {
            m_Types.Clear();
            var labels = new List<string> { "(none)" };
            foreach (Type t in TypeCache.GetTypesDerivedFrom<AttackAbility>())
            {
                if (t.IsAbstract) continue;
                m_Types.Add(t);
                labels.Add(t.Name);
            }

            m_Dropdown = new PopupField<string>(labels, 0);
            m_Dropdown.RegisterValueChangedCallback(_ => OnDropdownChanged());
            body.Add(m_Dropdown);
        }

        void OnDropdownChanged()
        {
            if (m_Character == null) return;

            int idx = m_Dropdown.index;
            Type desired = idx <= 0 ? null : m_Types[idx - 1];

            CollectAbilities();
            Type currentRoot = null;
            foreach (var a in m_Buffer)
            {
                if (a != null && a.gameObject == m_Character.gameObject) { currentRoot = a.GetType(); break; }
            }

            if (desired == currentRoot && AllOnRoot()) return;

            foreach (var a in m_Buffer)
            {
                if (a != null) Undo.DestroyObjectImmediate(a);
            }

            if (desired != null) AbilityDefaults.Apply(Undo.AddComponent(m_Character.gameObject, desired));

            Refresh();
        }

        bool AllOnRoot()
        {
            if (m_Buffer.Count != 1) return m_Buffer.Count == 0;
            var a = m_Buffer[0];
            return a != null && a.gameObject == m_Character.gameObject;
        }

        #endregion

        #region Refresh

        void RefreshIfChanged()
        {
            if (m_Character == null) return;
            int hash = ComputeHash();
            if (hash == m_LastHash) return;
            m_LastHash = hash;
            Refresh();
        }

        int ComputeHash()
        {
            CollectAbilities();
            int hash = 17;
            foreach (var c in m_Buffer)
            {
                hash = unchecked(hash * 31 + (c != null ? c.GetEntityId().GetHashCode() : 0));
            }
            return hash;
        }

        void CollectAbilities()
        {
            m_Buffer.Clear();
            if (m_Character != null) m_Character.GetComponentsInChildren(true, m_Buffer);
        }

        void Refresh()
        {
            CollectAbilities();
            m_StatusRow.Clear();

            int dropdownIdx = 0;
            if (m_Buffer.Count > 0 && m_Buffer[0] != null)
            {
                int found = m_Types.IndexOf(m_Buffer[0].GetType());
                if (found >= 0) dropdownIdx = found + 1;
            }
            m_Dropdown.SetValueWithoutNotify(m_Dropdown.choices[dropdownIdx]);

            for (int i = 1; i < m_Buffer.Count; i++)
            {
                var extra = m_Buffer[i];
                if (extra == null) continue;
                m_StatusRow.Add(BuildExtraRow(extra));
            }
        }

        VisualElement BuildExtraRow(AttackAbility ability)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 22;

            var warn = new Label($"⚠ extra: {ability.GetType().Name}");
            warn.AddToClassList("blocks-text--warn");
            warn.style.flexGrow = 1;
            row.Add(warn);

            var remove = new Button(() =>
            {
                Undo.DestroyObjectImmediate(ability);
                Refresh();
            }) { text = "Remove" };
            row.Add(remove);

            return row;
        }

        #endregion
    }
}

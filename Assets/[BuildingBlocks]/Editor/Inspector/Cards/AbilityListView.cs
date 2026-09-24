using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// Inspector list of a character's abilities of type T, with Add and Remove buttons. It polls a few
    /// times a second and rebuilds when the set of components changes; adding and removing go through Undo.
    /// Disabled during multi-object editing.
    /// </summary>
    sealed class AbilityListView<T> : VisualElement where T : CharacterAbility
    {
        readonly BuildingBlocksCharacter m_Character;
        readonly VisualElement m_RowContainer;
        readonly List<T> m_Buffer = new();

        int m_LastHash;

        #region Setup

        public AbilityListView(BuildingBlocksCharacter character)
        {
            m_Character = character;

            m_RowContainer = new VisualElement();
            Add(m_RowContainer);

            var addButton = new Button(ShowAddMenu) { text = "+ Add Ability" };
            addButton.AddToClassList("blocks-button--primary");
            addButton.style.marginTop = 4;
            addButton.style.alignSelf = Align.FlexStart;
            Add(addButton);

            if (m_Character == null)
            {
                var hint = new Label("(Multi-edit not supported)");
                hint.AddToClassList("blocks-text--dim");
                m_RowContainer.Add(hint);
                addButton.SetEnabled(false);
                return;
            }

            schedule.Execute(RefreshIfChanged).Every(500);
            Rebuild();
        }

        #endregion

        #region Refresh

        void RefreshIfChanged()
        {
            if (m_Character == null) return;
            int hash = ComputeHash();
            if (hash == m_LastHash) return;
            m_LastHash = hash;
            Rebuild();
        }

        int ComputeHash()
        {
            m_Buffer.Clear();
            m_Character.GetComponentsInChildren(true, m_Buffer);
            int hash = 17;
            foreach (var c in m_Buffer)
            {
                hash = unchecked(hash * 31 + (c != null ? c.GetEntityId().GetHashCode() : 0));
            }
            return hash;
        }

        void Rebuild()
        {
            m_RowContainer.Clear();
            m_Buffer.Clear();
            m_Character.GetComponentsInChildren(true, m_Buffer);

            if (m_Buffer.Count == 0)
            {
                var empty = new Label("(none)");
                empty.AddToClassList("blocks-text--dim");
                empty.style.marginBottom = 2;
                m_RowContainer.Add(empty);
                return;
            }

            foreach (var ability in m_Buffer)
            {
                m_RowContainer.Add(BuildRow(ability));
            }
        }

        VisualElement BuildRow(T ability)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 24;

            var bullet = new Label("•");
            bullet.AddToClassList("blocks-bullet");
            row.Add(bullet);

            var nameLabel = new Label(ability.GetType().Name);
            nameLabel.style.flexGrow = 1;
            row.Add(nameLabel);

            if (ability.gameObject != m_Character.gameObject)
            {
                var child = new Label("(child)");
                child.AddToClassList("blocks-text--dim");
                child.style.marginRight = 4;
                row.Add(child);
            }

            var removeButton = new Button(() => RemoveAbility(ability)) { text = "Remove" };
            row.Add(removeButton);

            return row;
        }

        #endregion

        #region Add & remove

        void RemoveAbility(T ability)
        {
            if (ability == null) return;
            if (!EditorUtility.DisplayDialog("Remove Ability",
                    $"Remove {ability.GetType().Name} from {ability.gameObject.name}?",
                    "Remove", "Cancel"))
            {
                return;
            }
            Undo.DestroyObjectImmediate(ability);
            Rebuild();
        }

        void ShowAddMenu()
        {
            var menu = new GenericMenu();
            var subclasses = TypeCache.GetTypesDerivedFrom<T>();

            var existing = new HashSet<Type>();
            m_Buffer.Clear();
            m_Character.GetComponentsInChildren(true, m_Buffer);
            foreach (var ability in m_Buffer)
            {
                if (ability != null) existing.Add(ability.GetType());
            }

            int added = 0;
            foreach (var type in subclasses)
            {
                if (type.IsAbstract) continue;
                if (existing.Contains(type))
                {
                    menu.AddDisabledItem(new GUIContent(type.Name));
                }
                else
                {
                    Type captured = type;
                    menu.AddItem(new GUIContent(type.Name), false, () => AddAbility(captured));
                }
                added++;
            }
            if (added == 0)
            {
                menu.AddDisabledItem(new GUIContent("(no subclasses found)"));
            }
            menu.ShowAsContext();
        }

        void AddAbility(Type type)
        {
            if (m_Character == null) return;
            AbilityDefaults.Apply(Undo.AddComponent(m_Character.gameObject, type));
            Rebuild();
        }

        #endregion
    }
}

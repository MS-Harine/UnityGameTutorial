using System;
using UnityEngine;
using UnityEditor;
using Blocks.Attack;
using Blocks.Movement;
using Blocks.Character;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Blocks
{
    /// <summary>
    /// An editable summary of a character spec, shown as a card of labeled rows (name, sprite, stats,
    /// movement, attack, targeting, elimination). Edits write straight back to the spec, and it rebuilds
    /// when a change affects which rows apply.
    /// </summary>
    sealed class CharacterSpecView : VisualElement
    {
        readonly CharacterSpec m_Spec;

        public CharacterSpecView(CharacterSpec spec)
        {
            m_Spec = spec;
            AddToClassList("blocks-card");
            Rebuild();
        }

        #region Layout

        void Rebuild()
        {
            Clear();
            Add(NameRow());
            Add(SpriteRow());
            Add(StatsRow());
            Add(MovementRow());
            Add(AttackRow());
            if (m_Spec.IsTargetingEnabled) Add(TargetingRow());
            Add(EliminationRow());
        }

        #endregion

        #region Row builders

        VisualElement NameRow()
        {
            var field = new TextField { value = m_Spec.Name };
            field.RegisterValueChangedCallback(evt => { m_Spec.Name = evt.newValue; });
            return new LabeledRow("Name", field);
        }

        VisualElement SpriteRow()
        {
            var sprite = new ObjectField
            {
                objectType = typeof(Sprite),
                allowSceneObjects = false,
                value = m_Spec.Sprite,
            };
            sprite.RegisterValueChangedCallback(evt => { m_Spec.Sprite = evt.newValue as Sprite; });
            return new LabeledRow("Sprite", sprite);
        }

        VisualElement StatsRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;

            if (m_Spec.Stats.Count == 0)
            {
                var none = new Label("None — most characters need at least Health.");
                none.AddToClassList("blocks-muted");
                col.Add(none);
            }

            for (int i = 0; i < m_Spec.Stats.Count; i++)
            {
                int index = i;
                StatEntry entry = m_Spec.Stats[index];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;

                var typeField = new EnumField(entry.Type);
                typeField.style.width = 100;
                typeField.RegisterValueChangedCallback(evt => { entry.Type = (StatType)evt.newValue; });
                row.Add(typeField);

                var maxField = new FloatField("Max") { value = entry.MaxValue };
                maxField.style.width = 90;
                maxField.style.marginLeft = 6;
                maxField.CompactInlineLabel(30);
                maxField.RegisterValueChangedCallback(evt =>
                {
                    float v = Mathf.Max(0f, evt.newValue);
                    entry.MaxValue = v;
                    if (entry.StartValue > v) entry.StartValue = v;
                });
                row.Add(maxField);

                var regenField = new FloatField("Regen") { value = entry.RegenRate };
                regenField.style.width = 100;
                regenField.style.marginLeft = 4;
                regenField.CompactInlineLabel(44);
                regenField.RegisterValueChangedCallback(evt => { entry.RegenRate = Mathf.Max(0f, evt.newValue); });
                row.Add(regenField);

                var del = new Button(() =>
                {
                    m_Spec.Stats.RemoveAt(index);
                    Rebuild();
                }) { text = "×" };
                del.style.width = 24;
                del.style.marginLeft = 4;
                row.Add(del);

                col.Add(row);
            }

            var addBtn = new Button(() =>
            {
                m_Spec.Stats.Add(new StatEntry(StatType.Health, 100f));
                Rebuild();
            }) { text = "+ Add stat" };
            addBtn.style.alignSelf = Align.FlexStart;
            addBtn.style.marginTop = 4;
            col.Add(addBtn);

            return new LabeledRow("Stats", col);
        }

        VisualElement MovementRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexDirection = FlexDirection.Row;
            col.style.flexWrap = Wrap.Wrap;
            col.style.alignItems = Align.Center;

            if (m_Spec.MovementAbilityTypes.Count == 0)
            {
                var none = new Label("None");
                none.AddToClassList("blocks-muted");
                none.style.marginRight = 6;
                col.Add(none);
            }
            else
            {
                for (int i = 0; i < m_Spec.MovementAbilityTypes.Count; i++)
                {
                    int index = i;
                    col.Add(Pill.Removable(m_Spec.MovementAbilityTypes[index].Name, () =>
                    {
                        m_Spec.MovementAbilityTypes.RemoveAt(index);
                        Rebuild();
                    }));
                }
            }

            var available = new List<Type>();
            foreach (Type t in TypeCache.GetTypesDerivedFrom<MovementAbility>())
            {
                if (t.IsAbstract) continue;
                if (!m_Spec.MovementAbilityTypes.Contains(t)) available.Add(t);
            }
            var addBtn = new Button(() => ShowAddMenu(available, t =>
            {
                if (!m_Spec.MovementAbilityTypes.Contains(t)) m_Spec.MovementAbilityTypes.Add(t);
                Rebuild();
            })) { text = "+" };
            addBtn.style.width = 24;
            addBtn.SetEnabled(available.Count > 0);
            col.Add(addBtn);

            return new LabeledRow("Movement", col);
        }

        VisualElement AttackRow()
        {
            var types = new List<Type>();
            var labels = new List<string> { "(none)" };
            foreach (Type t in TypeCache.GetTypesDerivedFrom<AttackAbility>())
            {
                if (t.IsAbstract) continue;
                types.Add(t);
                labels.Add(t.Name);
            }

            int currentIndex = 0;
            if (m_Spec.AttackAbilityType != null)
            {
                int idx = types.IndexOf(m_Spec.AttackAbilityType);
                if (idx >= 0) currentIndex = idx + 1;
            }

            var popup = new PopupField<string>(labels, currentIndex);
            popup.RegisterValueChangedCallback(_ =>
            {
                int sel = popup.index;
                m_Spec.AttackAbilityType = sel <= 0 ? null : types[sel - 1];
                Rebuild();
            });

            return new LabeledRow("Attack", popup);
        }

        VisualElement TargetingRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexDirection = FlexDirection.Row;
            col.style.flexWrap = Wrap.Wrap;
            col.style.alignItems = Align.Center;

            var modeField = new EnumField(m_Spec.TargetingMode);
            modeField.style.width = 170;
            modeField.RegisterValueChangedCallback(evt =>
            {
                m_Spec.TargetingMode = (TargetingMode)evt.newValue;
                Rebuild();
            });
            col.Add(modeField);

            if (m_Spec.TargetingMode == TargetingMode.TaggedDamageable)
            {
                var tagField = new TextField("Tag") { value = m_Spec.TargetTag };
                tagField.style.width = 140;
                tagField.style.marginLeft = 6;
                tagField.CompactInlineLabel(26);
                tagField.RegisterValueChangedCallback(evt => { m_Spec.TargetTag = evt.newValue; });
                col.Add(tagField);
            }

            var radius = new FloatField("Radius") { value = m_Spec.TargetRadius };
            radius.style.width = 120;
            radius.style.marginLeft = 6;
            radius.CompactInlineLabel(48);
            radius.RegisterValueChangedCallback(evt => { m_Spec.TargetRadius = Mathf.Max(0f, evt.newValue); });
            col.Add(radius);

            return new LabeledRow("Targeting", col);
        }

        VisualElement EliminationRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexDirection = FlexDirection.Row;
            col.style.alignItems = Align.Center;

            var modeField = new EnumField(m_Spec.OnEliminated);
            modeField.style.width = 140;
            modeField.RegisterValueChangedCallback(evt =>
            {
                m_Spec.OnEliminated = (EliminationBehavior)evt.newValue;
                Rebuild();
            });
            col.Add(modeField);

            bool needsDelay = m_Spec.OnEliminated == EliminationBehavior.Respawn || m_Spec.OnEliminated == EliminationBehavior.Destroy;
            if (needsDelay)
            {
                var delay = new FloatField("Delay (s)") { value = m_Spec.EliminationDelay };
                delay.style.width = 140;
                delay.style.marginLeft = 6;
                delay.CompactInlineLabel(56);
                delay.RegisterValueChangedCallback(evt => { m_Spec.EliminationDelay = Mathf.Max(0f, evt.newValue); });
                col.Add(delay);
            }

            return new LabeledRow("On eliminated", col);
        }

        static void ShowAddMenu(List<Type> available, Action<Type> onPick)
        {
            var menu = new GenericMenu();
            if (available.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("All abilities already added"));
            }
            else
            {
                foreach (Type t in available)
                {
                    Type captured = t;
                    menu.AddItem(new GUIContent(t.Name), false, () => onPick(captured));
                }
            }
            menu.ShowAsContext();
        }

        #endregion
    }
}

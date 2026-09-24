using Blocks.Attack;
using Blocks.Movement;
using System.Collections.Generic;

namespace Blocks.Character
{
    /// <summary>
    /// The movement and attack abilities found under the character, and the tick loops that drive them.
    /// </summary>
    sealed class AbilitiesModule
    {
        readonly List<MovementAbility> m_MovementAbilities = new List<MovementAbility>();
        readonly List<AttackAbility> m_AttackAbilities = new List<AttackAbility>();

        /// <summary>
        /// Finds and initializes the character's abilities. Every ability comes up with its input live;
        /// the character reconciles that against its module switches immediately afterwards, which keeps
        /// one owner for that decision instead of two that can drift.
        /// </summary>
        public void Discover(BuildingBlocksCharacter character)
        {
            DiscoverMovementAbilities(character);
            DiscoverAttackAbilities(character);
        }

        public void TickMovement()
        {
            foreach (var t in m_MovementAbilities)
            {
                if (!t.enabled) continue;
                t.Tick();
            }
        }

        public void TickAttack()
        {
            foreach (var t in m_AttackAbilities)
            {
                if (!t.enabled) continue;
                t.Tick();
            }
        }

        public void FixedTickMovement()
        {
            foreach (var t in m_MovementAbilities)
            {
                if (!t.enabled) continue;
                t.FixedTick();
            }
        }

        public void FixedTickAttack()
        {
            foreach (var t in m_AttackAbilities)
            {
                if (!t.enabled) continue;
                t.FixedTick();
            }
        }

        /// <summary>
        /// Tells every ability the character has respawned. Unlike the tick loops above this one
        /// deliberately ignores <c>enabled</c>: a disabled ability still holds whatever motion state it
        /// had when the character went down, and would apply it the moment something re-enables it.
        /// </summary>
        public void NotifyRespawn()
        {
            foreach (var t in m_MovementAbilities)
            {
                t.NotifyRespawn();
            }

            foreach (var t in m_AttackAbilities)
            {
                t.NotifyRespawn();
            }
        }

        public void SetMovementInputEnabled(bool isEnabled)
        {
            SetAbilitiesInputEnabled(m_MovementAbilities, isEnabled);
        }

        public void SetAttackInputEnabled(bool isEnabled)
        {
            SetAbilitiesInputEnabled(m_AttackAbilities, isEnabled);
        }

        public bool Remove(MovementAbility ability)
        {
            int index = m_MovementAbilities.IndexOf(ability);

            if (index < 0) return false;
            CleanupAbility(m_MovementAbilities[index]);
            m_MovementAbilities.RemoveAt(index);
            return true;
        }

        public bool Remove(AttackAbility ability)
        {
            int index = m_AttackAbilities.IndexOf(ability);

            if (index < 0) return false;
            CleanupAbility(m_AttackAbilities[index]);
            m_AttackAbilities.RemoveAt(index);
            return true;
        }

        public void Cleanup()
        {
            foreach (var t in m_MovementAbilities)
            {
                CleanupAbility(t);
            }
            m_MovementAbilities.Clear();

            foreach (var t in m_AttackAbilities)
            {
                CleanupAbility(t);
            }
            m_AttackAbilities.Clear();
        }

        void DiscoverMovementAbilities(BuildingBlocksCharacter character)
        {
            foreach (var t in character.GetComponentsInChildren<MovementAbility>())
            {
                m_MovementAbilities.Add(t);
            }

            foreach (var t in m_MovementAbilities)
            {
                InitializeAbility(character, t);
            }
        }

        void DiscoverAttackAbilities(BuildingBlocksCharacter character)
        {
            foreach (var t in character.GetComponentsInChildren<AttackAbility>())
            {
                m_AttackAbilities.Add(t);
            }

            foreach (var t in m_AttackAbilities)
            {
                InitializeAbility(character, t);
            }
        }

        static void InitializeAbility(BuildingBlocksCharacter character, CharacterAbility ability)
        {
            ability.Initialize(character);
        }

        static void CleanupAbility(CharacterAbility ability)
        {
            ability.Cleanup();
        }

        static void SetAbilitiesInputEnabled<T>(List<T> abilities, bool isEnabled) where T : CharacterAbility
        {
            foreach (var ability in abilities)
            {
                SetAbilityInputEnabled(ability, isEnabled);
            }
        }

        static void SetAbilityInputEnabled(CharacterAbility ability, bool isEnabled)
        {
            ability.SetInputEnabled(isEnabled);
        }
    }
}

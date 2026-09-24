using System;
using UnityEngine;
using System.Collections.Generic;

namespace Blocks.Character
{
    /// <summary>
    /// The character's stats: lookup by type, per-frame regeneration, and the Health shortcuts
    /// elimination reads.
    /// </summary>
    sealed class StatsModule
    {
        readonly Dictionary<StatType, Stat> m_Lookup = new Dictionary<StatType, Stat>();

        Action<StatType, float, float> m_StatChanged;
        Action<StatType> m_StatDepleted;
        Stat m_HealthStat;

        public bool IsDepleted => m_HealthStat == null || m_HealthStat.IsDepleted;
        public float HealthRatio => m_HealthStat?.Ratio ?? 0f;
        public float CurrentHealth => m_HealthStat?.CurrentValue ?? 0f;
        public float MaxHealth => m_HealthStat?.MaxValue ?? 0f;

        public void Initialize(List<Stat> stats, UnityEngine.Object context, string name, Action<StatType, float, float> statChanged, Action<StatType> statDepleted)
        {
            m_StatChanged = statChanged;
            m_StatDepleted = statDepleted;
            BuildLookup(stats, context, name);
            BindHealthStat(context, name);
        }

        public void Tick(List<Stat> stats, float deltaTime)
        {
            foreach (var stat in stats)
            {
                stat?.OnUpdate(deltaTime);
            }
        }

        public bool Has(StatType type) => m_Lookup.ContainsKey(type);

        public float GetValue(StatType type)
        {
            return m_Lookup.TryGetValue(type, out var stat) ? stat.CurrentValue : 0f;
        }

        public float GetMax(StatType type)
        {
            return m_Lookup.TryGetValue(type, out var stat) ? stat.MaxValue : 0f;
        }

        public Stat GetStat(StatType type)
        {
            m_Lookup.TryGetValue(type, out var stat);
            return stat;
        }

        public bool HasAtLeast(StatType type, float amount)
        {
            if (amount <= 0f) return true;
            if (!m_Lookup.TryGetValue(type, out var stat)) return false;
            return stat.HasEnough(amount);
        }

        public bool Consume(StatType type, float amount)
        {
            if (amount <= 0f) return true;
            if (!m_Lookup.TryGetValue(type, out var stat)) return false;
            if (!stat.HasEnough(amount)) return false;

            stat.Apply(-amount);
            return true;
        }

        public void Apply(StatType type, float delta)
        {
            if (delta == 0f) return;
            if (!m_Lookup.TryGetValue(type, out var stat)) return;
            stat.Apply(delta);
        }

        public void SetAllToMax(List<Stat> stats)
        {
            foreach (var stat in stats)
            {
                stat?.SetToMax();
            }
        }

        public void ForEachStat(List<Stat> stats, Action<Stat> action)
        {
            foreach (var stat in stats)
            {
                if (stat != null) action(stat);
            }
        }

        void BuildLookup(List<Stat> stats, UnityEngine.Object context, string name)
        {
            m_Lookup.Clear();

            foreach (var stat in stats)
            {
                if (stat == null) continue;

                if (m_Lookup.ContainsKey(stat.Type))
                {
                    Debug.LogWarning(
                        $"[BuildingBlocksCharacter] Duplicate stat type '{stat.Type}' on {name}. Skipping.",
                        context);
                    continue;
                }

                stat.Initialize();
                m_Lookup.Add(stat.Type, stat);

                StatType type = stat.Type;
                stat.OnValueChanged += (oldVal, newVal) => m_StatChanged(type, oldVal, newVal);
                stat.OnDepleted += () => m_StatDepleted(type);
            }
        }

        void BindHealthStat(UnityEngine.Object context, string name)
        {
            if (!m_Lookup.TryGetValue(StatType.Health, out m_HealthStat))
            {
                Debug.LogError(
                    $"[BuildingBlocksCharacter] {name}: No Health stat configured. Actor cannot be eliminated. Add a stat with type=Health.",
                    context);
            }
        }
    }
}

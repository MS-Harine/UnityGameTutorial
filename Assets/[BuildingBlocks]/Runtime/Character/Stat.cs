using System;
using UnityEngine;

namespace Blocks.Character
{
    [Serializable]
    public class Stat
    {
        [SerializeField] StatType type;
        [SerializeField] Color color = new(0.85f, 0.25f, 0.30f, 1f);

        [Header("Values")]
        [SerializeField] float maxValue = 100f;
        [SerializeField] float startValue = 100f;

        [Header("Regeneration")]
        [SerializeField] float regenRate = 0f;
        [SerializeField] float regenDelay = 0f;

        float m_CurrentValue;
        float m_RegenDelayTimer;
        bool m_IsInitialized;

        public Stat()
        {
            color = StatTypeDefaults.ColorFor(type);
        }

        public Stat(StatType type, float maxValue = 100f, float startValue = 100f, float regenRate = 0f, float regenDelay = 0f, Color? color = null)
        {
            this.type = type;
            this.maxValue = maxValue;
            this.startValue = startValue;
            this.regenRate = regenRate;
            this.regenDelay = regenDelay;
            this.color = color ?? StatTypeDefaults.ColorFor(type);
        }

        public StatType Type => type;
        public Color Color => color;
        public float CurrentValue => m_CurrentValue;
        public float MaxValue => maxValue;
        public float StartValue => startValue;
        public float RegenRate => regenRate;

        public float Ratio => maxValue > 0f ? m_CurrentValue / maxValue : 0f;
        public bool IsDepleted => m_CurrentValue <= 0f;
        public bool IsFull => Mathf.Approximately(m_CurrentValue, maxValue);
        public event Action<float, float> OnValueChanged;
        public event Action OnDepleted;

        /// <summary>
        /// Raised when this stat fills, the counterpart to <see cref="OnDepleted"/>. Nothing in the
        /// sample reacts to a stat topping out; use it for a shield that pops as it recharges, or a
        /// stamina bar that stops flashing once it is back.
        /// </summary>
        public event Action OnMaxReached;

        internal void Initialize()
        {
            m_CurrentValue = Mathf.Clamp(startValue, 0f, maxValue);
            m_RegenDelayTimer = 0f;
            m_IsInitialized = true;
        }

        internal void OnUpdate(float deltaTime)
        {
            if (regenRate <= 0f || IsFull)
            {
                return;
            }

            if (m_RegenDelayTimer > 0f)
            {
                m_RegenDelayTimer -= deltaTime;
                return;
            }

            Apply(regenRate * deltaTime);
        }

        internal void Apply(float amount)
        {
            if (!m_IsInitialized) return;

            float oldValue = m_CurrentValue;
            m_CurrentValue = Mathf.Clamp(m_CurrentValue + amount, 0f, maxValue);

            if (Mathf.Approximately(oldValue, m_CurrentValue)) return;

            if (amount < 0f && regenDelay > 0f)
            {
                m_RegenDelayTimer = regenDelay;
            }

            OnValueChanged?.Invoke(oldValue, m_CurrentValue);

            if (m_CurrentValue <= 0f && oldValue > 0f)
            {
                OnDepleted?.Invoke();
            }
            else if (Mathf.Approximately(m_CurrentValue, maxValue) && !Mathf.Approximately(oldValue, maxValue))
            {
                OnMaxReached?.Invoke();
            }
        }

        public void SetToMax()
        {
            Apply(maxValue - m_CurrentValue);
        }

        /// <summary>
        /// Empties this stat, raising <see cref="OnValueChanged"/> and <see cref="OnDepleted"/>. The
        /// counterpart to <see cref="SetToMax"/>, for a drain effect or a special that spends a whole
        /// bar. The sample depletes through <c>TakeDamage</c> instead, so the hit arrives as a real
        /// damage event carrying a reaction.
        /// </summary>
        public void SetToZero()
        {
            Apply(-m_CurrentValue);
        }

        public bool HasEnough(float amount) => m_CurrentValue >= amount;
    }
}

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using Blocks.Extras;

namespace Blocks.Character
{
    public abstract class CharacterAbility : MonoBehaviour
    {
        readonly List<InputHandle> m_RegisteredInputs = new List<InputHandle>();
        bool m_Cleaned;

        public bool IsInputEnabled { get; private set; }

        protected BuildingBlocksCharacter Character { get; private set; }

        internal void Initialize(BuildingBlocksCharacter character)
        {
            m_Cleaned = false;
            Character = character;

            OnBeforeInitialize();
            OnInitialize();
            SetInputEnabled(true);
        }

        internal void Cleanup()
        {
            if (m_Cleaned) return;
            m_Cleaned = true;

            SetInputEnabled(false);
            OnBeforeCleanup();
            OnCleanup();

            foreach (var t in m_RegisteredInputs)
            {
                t.Cleanup();
            }

            m_RegisteredInputs.Clear();
            Character = null;
        }

        internal void Tick()
        {
            float deltaTime = Time.deltaTime;

            foreach (var t in m_RegisteredInputs)
            {
                t.OnUpdate(deltaTime);
            }

            OnUpdate();
        }

        internal void FixedTick()
        {
            OnFixedUpdate();
        }

        internal void NotifyRespawn()
        {
            OnRespawn();
        }

        protected virtual void OnInitialize() { }
        protected virtual void OnUpdate() { }
        protected virtual void OnFixedUpdate() { }
        protected virtual void OnCleanup() { }

        /// <summary>
        /// The character has been put back at its spawn point and is about to start ticking again.
        /// Clear anything that describes motion already under way (accumulated speed, a dash still
        /// counting down), or it resumes on the first tick after the respawn.
        /// </summary>
        protected virtual void OnRespawn() { }

        // For the bases that sit between this class and a concrete ability: AttackAbility wires its
        // interruption handling here so that OnInitialize/OnCleanup stay free for the ability being
        // written. Nothing a concrete ability needs to touch; override OnInitialize/OnCleanup instead.
        protected virtual void OnBeforeInitialize() { }
        protected virtual void OnBeforeCleanup() { }

        /// <summary>
        /// Spawns one copy of the effect prefab at a world position, mirrored to the character's facing.
        /// For the scale to show, the prefab's particle systems need Scaling Mode set to Hierarchy.
        /// </summary>
        protected void SpawnVfx(OneShotVfx effectPrefab, Vector2 position, float scale = 1f)
        {
            if (effectPrefab == null) return;

            // Mirror by rotating around Y: unlike a negative scale, this flips the particle
            // shapes and velocities regardless of each system's Scaling Mode.
            Quaternion rotation = Character.FacingDirection < 0f
                ? Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.identity;

            OneShotVfx instance = Instantiate(effectPrefab, position, rotation);
            if (!Mathf.Approximately(scale, 1f))
            {
                instance.transform.localScale *= scale;
            }
        }

        protected InputHandle BindInput(InputActionReference action, Action<InputHandle> configure = null)
        {
            var handle = new InputHandle(action);
            configure?.Invoke(handle);

            m_RegisteredInputs.Add(handle);
            return handle;
        }

        public void SetInputEnabled(bool isEnabled)
        {
            if (IsInputEnabled == isEnabled) return;

            IsInputEnabled = isEnabled;

            foreach (var t in m_RegisteredInputs)
            {
                t.SetEnabled(isEnabled);
            }
        }

        protected virtual void OnDestroy()
        {
            Cleanup();
        }

        protected class InputHandle
        {
            readonly InputActionReference m_Action;

            Action<InputAction.CallbackContext> m_Performed;
            Action<InputAction.CallbackContext> m_Started;
            Action<InputAction.CallbackContext> m_Canceled;

            Func<bool> m_CanExecute;

            float m_BufferTime;
            float m_BufferLeft;

            float m_CooldownTime;
            float m_CooldownLeft;

            InputAction.CallbackContext m_BufferedContext;
            bool m_HasBufferedInput;

            internal InputHandle(InputActionReference action)
            {
                m_Action = action;
            }

            public T GetValue<T>() where T : struct
            {
                if (m_Action == null || m_Action.action == null)
                {
                    return default;
                }

                return m_Action.action.ReadValue<T>();
            }

            public bool IsPressed =>
                m_Action != null &&
                m_Action.action != null &&
                m_Action.action.IsPressed();

            public Action Performed
            {
                set => m_Performed = _ => value();
            }

            public Action Started
            {
                set => m_Started = _ => value();
            }

            public Action Canceled
            {
                set => m_Canceled = _ => value();
            }

            public Func<bool> CanExecute
            {
                set => m_CanExecute = value;
            }

            public float BufferTime
            {
                set => m_BufferTime = value;
            }

            public float CooldownTime
            {
                set => m_CooldownTime = value;
            }

            internal void OnUpdate(float deltaTime)
            {
                if (m_CooldownLeft > 0f)
                {
                    m_CooldownLeft -= deltaTime;
                }

                if (!m_HasBufferedInput) return;
                m_BufferLeft -= deltaTime;

                if (m_BufferLeft <= 0f)
                {
                    m_HasBufferedInput = false;
                    return;
                }

                if (m_CooldownLeft > 0f) return;
                if (m_CanExecute != null && !m_CanExecute()) return;

                m_HasBufferedInput = false;
                m_CooldownLeft = m_CooldownTime;
                m_Performed?.Invoke(m_BufferedContext);
            }

            internal void SetEnabled(bool isEnabled)
            {
                if (m_Action == null || m_Action.action == null) return;
                if (isEnabled)
                {
                    Subscribe();
                    m_Action.action.Enable();
                }
                else
                {
                    Unsubscribe();
                    // Do NOT Disable the underlying action: InputActionReference.action is shared,
                    // so disabling here would silently steal input from any other listener still
                    // bound to the same reference. Unsubscribing the callbacks is enough to stop
                    // this ability from reacting.
                }
            }

            internal void Cleanup()
            {
                Unsubscribe();

                m_Performed = null;
                m_Started = null;
                m_Canceled = null;
                m_CanExecute = null;

                m_HasBufferedInput = false;
                m_CooldownLeft = 0f;
            }

            void Subscribe()
            {
                InputAction action = m_Action.action;

                if (m_CanExecute != null || m_BufferTime > 0f || m_CooldownTime > 0f)
                {
                    action.performed += HandlePerformedBuffered;
                }
                else if (m_Performed != null)
                {
                    action.performed += m_Performed;
                }

                if (m_Started != null) action.started += m_Started;
                if (m_Canceled != null) action.canceled += m_Canceled;
            }

            void Unsubscribe()
            {
                if (m_Action == null || m_Action.action == null) return;
                InputAction action = m_Action.action;

                action.performed -= HandlePerformedBuffered;
                if (m_Performed != null) action.performed -= m_Performed;
                if (m_Started != null) action.started -= m_Started;
                if (m_Canceled != null) action.canceled -= m_Canceled;
            }

            void HandlePerformedBuffered(InputAction.CallbackContext context)
            {
                if (m_CooldownLeft > 0f) return;

                bool conditionMet = m_CanExecute == null || m_CanExecute();

                if (conditionMet)
                {
                    m_CooldownLeft = m_CooldownTime;
                    m_Performed?.Invoke(context);
                    return;
                }

                if (m_BufferTime > 0f)
                {
                    m_HasBufferedInput = true;
                    m_BufferLeft = m_BufferTime;
                    m_BufferedContext = context;
                }
            }
        }
    }
}

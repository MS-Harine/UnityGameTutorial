using UnityEngine;

namespace Blocks.Extras
{
    /// <summary>
    /// Plays every ParticleSystem on this object and its children when spawned, then destroys the
    /// object once they finish. Put it on the root of a one-shot effect prefab (hit sparks, bursts).
    /// </summary>
    [DisallowMultipleComponent]
    public class OneShotVfx : MonoBehaviour
    {
        ParticleSystem[] m_Systems;

        void Awake()
        {
            m_Systems = GetComponentsInChildren<ParticleSystem>();
        }

        void Start()
        {
            if (m_Systems.Length == 0)
            {
                Debug.LogWarning(
                    $"[OneShotVfx] {name}: No ParticleSystem on this object or its children — nothing to play.",
                    this);
                Destroy(gameObject);
                return;
            }

            foreach (ParticleSystem system in m_Systems)
            {
                system.Play();
            }
        }

        void Update()
        {
            foreach (ParticleSystem system in m_Systems)
            {
                if (system != null && system.IsAlive(false)) return;
            }

            Destroy(gameObject);
        }

        void OnValidate()
        {
            foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>())
            {
                ParticleSystem.MainModule main = system.main;
                if (main.loop)
                {
                    Debug.LogWarning(
                        $"[OneShotVfx] {name}: '{system.name}' has Looping enabled — the effect will never finish, so this object will never clean itself up. Disable Looping on the ParticleSystem.",
                        this);
                }
            }
        }
    }
}

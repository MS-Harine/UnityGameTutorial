using UnityEngine;

namespace Blocks.Character
{
    /// <summary>
    /// Remembers where the character started and puts it back: scene presence, position, rotation, and
    /// the rigidbody motion of every body underneath it.
    /// </summary>
    sealed class RespawnModule
    {
        SpriteRenderer[] m_SpriteRenderers;
        Collider2D[] m_Colliders;
        Rigidbody2D[] m_Rigidbodies;
        Vector3 m_StartPosition;
        Quaternion m_StartRotation;

        public Vector3 StartPosition => m_StartPosition;
        public Quaternion StartRotation => m_StartRotation;

        public void Initialize(Transform transform)
        {
            m_StartPosition = transform.position;
            m_StartRotation = transform.rotation;
            m_SpriteRenderers = transform.GetComponentsInChildren<SpriteRenderer>(true);
            m_Colliders = transform.GetComponentsInChildren<Collider2D>(true);
            m_Rigidbodies = transform.GetComponentsInChildren<Rigidbody2D>(true);
        }

        public void SetActiveInScene(bool isOn)
        {
            if (m_SpriteRenderers != null)
            {
                foreach (var spriteRenderer in m_SpriteRenderers)
                {
                    if (spriteRenderer != null) spriteRenderer.enabled = isOn;
                }
            }

            if (m_Colliders != null)
            {
                foreach (var col in m_Colliders)
                {
                    if (col != null) col.enabled = isOn;
                }
            }

            if (m_Rigidbodies != null)
            {
                foreach (var rb in m_Rigidbodies)
                {
                    if (rb != null) rb.simulated = isOn;
                }
            }
        }

        public void StopRigidbodyMotion()
        {
            if (m_Rigidbodies == null) return;
            foreach (var rb in m_Rigidbodies)
            {
                if (rb == null) continue;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
        }
    }
}

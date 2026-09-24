using Blocks.Character;
using UnityEngine;

namespace Blocks.Movement
{
    public enum AggroBehavior
    {
        Chase,
        Stand,
    }

    /// <summary>
    /// Enemy AI movement. Wanders the spawn point and chases or holds ground when a target appears.
    /// </summary>
    public class EnemyMovementAbility : MovementAbility
    {
        [Header("Movement")]
        [SerializeField] float wanderSpeed = 3f;
        [SerializeField] float chaseSpeed = 5f;
        [SerializeField] float stopDistance = 1.2f;

        [Header("Wander")]
        [SerializeField] float wanderDistance = 3f;

        [Header("On Target Detected")]
        [SerializeField] AggroBehavior aggroBehavior = AggroBehavior.Chase;

        [Header("Ledge Detection")]
        [SerializeField] float ledgeProbeDistance = 0.4f;
        [SerializeField] float ledgeProbeDepth = 1.0f;

        readonly RaycastHit2D[] m_GroundProbeBuffer = new RaycastHit2D[8];

        Vector2 m_StartPosition;
        float m_WanderDirection = 1f;

        protected override void OnInitialize()
        {
            m_StartPosition = transform.position;
        }

        protected override void OnUpdate()
        {
            if (Character.HasTarget)
            {
                if (aggroBehavior == AggroBehavior.Chase)
                {
                    ChaseOrStopNearTarget();
                }
                return;
            }

            if (IsOutsideWanderBounds())
            {
                ReturnToWanderBounds();
                return;
            }

            Wander();
        }

        void OnValidate()
        {
            if (wanderSpeed < 0f) wanderSpeed = 0f;
            if (chaseSpeed < 0f) chaseSpeed = 0f;
            if (stopDistance < 0f) stopDistance = 0f;
            if (wanderDistance < 0f) wanderDistance = 0f;
            if (ledgeProbeDistance < 0f) ledgeProbeDistance = 0f;
            if (ledgeProbeDepth < 0f) ledgeProbeDepth = 0f;
        }

        void ChaseOrStopNearTarget()
        {
            float xOffset = Character.CurrentTargetTransform.position.x - transform.position.x;
            if (Mathf.Abs(xOffset) <= stopDistance) return;

            float direction = xOffset > 0f ? 1f : -1f;

            if (!HasGroundAhead(direction)) return;

            Move(direction, chaseSpeed);
        }

        bool IsOutsideWanderBounds()
        {
            return Mathf.Abs(transform.position.x - m_StartPosition.x) > wanderDistance;
        }

        void ReturnToWanderBounds()
        {
            float direction = m_StartPosition.x > transform.position.x ? 1f : -1f;
            m_WanderDirection = direction;

            if (!HasGroundAhead(direction)) return;

            Move(direction, wanderSpeed);
        }

        void Wander()
        {
            float distanceFromStart = transform.position.x - m_StartPosition.x;

            if (distanceFromStart >= wanderDistance)
            {
                m_WanderDirection = -1f;
            }
            else if (distanceFromStart <= -wanderDistance)
            {
                m_WanderDirection = 1f;
            }

            if (!HasGroundAhead(m_WanderDirection))
            {
                m_WanderDirection = -m_WanderDirection;
                return;
            }

            Move(m_WanderDirection, wanderSpeed);
        }

        void Move(float direction, float speed)
        {
            if (direction < 0f && Character.IsTouchingLeftWall)
            {
                m_WanderDirection = 1f;
                return;
            }

            if (direction > 0f && Character.IsTouchingRightWall)
            {
                m_WanderDirection = -1f;
                return;
            }

            Character.AddMovement(new Vector2(direction * speed, 0f));
        }

        bool HasGroundAhead(float direction)
        {
            Bounds bounds = Character.ColliderBounds;
            if (bounds.size == Vector3.zero) return true;

            Vector2 origin = new Vector2(
                bounds.center.x + (bounds.extents.x + ledgeProbeDistance) * direction,
                bounds.min.y + 0.05f);

            int hitCount = Physics2D.Raycast(origin, Vector2.down, ContactFilter2D.noFilter, m_GroundProbeBuffer, ledgeProbeDepth);

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = m_GroundProbeBuffer[i].collider;
                if (hit == null) continue;
                if (hit == Character.Collider) continue;
                if (Character.Collider != null && hit.transform.IsChildOf(Character.Collider.transform)) continue;
                if (hit.isTrigger) continue;
                if (hit.GetComponentInParent<IDamageable>() != null) continue;

                return true;
            }

            return false;
        }

        void OnDrawGizmosSelected()
        {
            if (Character == null) return;

            Bounds bounds = Character.ColliderBounds;
            if (bounds.size == Vector3.zero) return;

            Gizmos.color = Color.cyan;
            DrawProbe(bounds, +1f);
            DrawProbe(bounds, -1f);
        }

        void DrawProbe(Bounds bounds, float direction)
        {
            Vector2 origin = new Vector2(
                bounds.center.x + (bounds.extents.x + ledgeProbeDistance) * direction,
                bounds.min.y + 0.05f);

            Gizmos.DrawLine(origin, origin + Vector2.down * ledgeProbeDepth);
        }
    }
}

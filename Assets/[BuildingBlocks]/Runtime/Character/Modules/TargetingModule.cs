using System;
using UnityEngine;
using System.Collections.Generic;

namespace Blocks.Character
{
    /// <summary>
    /// Scans for the character's target each frame, and keeps the last one for targetMemoryDuration
    /// after it leaves range so a target stepping briefly out of reach isn't dropped.
    /// </summary>
    sealed class TargetingModule
    {
        readonly Collider2D[] m_TargetingBuffer = new Collider2D[32];
        readonly RaycastHit2D[] m_LineOfSightBuffer = new RaycastHit2D[16];
        readonly HashSet<IDamageable> m_TargetCandidates = new HashSet<IDamageable>();

        Func<IDamageable, bool> m_IsSelf;
        Action<IDamageable> m_TargetAcquired;
        Action m_TargetLost;
        IDamageable m_CurrentTarget;
        float m_TargetMemoryLeft;

        public IDamageable CurrentTarget => m_CurrentTarget;
        public Transform CurrentTargetTransform => m_CurrentTarget?.Transform;
        public bool HasTarget => m_CurrentTarget != null;

        public void Tick(Transform transform, Collider2D selfCollider, bool isTargetingEnabled, TargetingMode targetingMode, string targetTag, float targetRadius, float targetMemoryDuration, float deltaTime, Func<IDamageable, bool> isSelf, Action<IDamageable> targetAcquired, Action targetLost)
        {
            m_IsSelf = isSelf;
            m_TargetAcquired = targetAcquired;
            m_TargetLost = targetLost;
            if (!isTargetingEnabled)
            {
                if (m_CurrentTarget != null) ClearTarget(m_TargetLost);
                return;
            }

            Vector2 myPos = transform.position;

            if (m_CurrentTarget != null && (!IsValidTarget(m_CurrentTarget, targetingMode, targetTag) || !HasLineOfSight(myPos, m_CurrentTarget, selfCollider)))
            {
                ClearTarget(m_TargetLost);
            }

            m_TargetCandidates.Clear();

            int count = Physics2D.OverlapCircle(
                transform.position,
                targetRadius,
                ContactFilter2D.noFilter,
                m_TargetingBuffer);

            IDamageable nearest = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider2D col = m_TargetingBuffer[i];
                if (col == null) continue;
                IDamageable target = col.GetComponentInParent<IDamageable>();
                if (target == null) continue;
                if (!m_TargetCandidates.Add(target)) continue;
                if (!IsValidTarget(target, targetingMode, targetTag)) continue;
                if (!HasLineOfSight(myPos, target, selfCollider)) continue;

                Vector2 diff = (Vector2)target.Transform.position - myPos;
                float sqr = diff.sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = target;
                }
            }

            if (nearest != null)
            {
                SetTarget(nearest, m_TargetAcquired);
                m_TargetMemoryLeft = targetMemoryDuration;
                return;
            }

            if (m_CurrentTarget == null) return;

            m_TargetMemoryLeft -= deltaTime;
            if (m_TargetMemoryLeft <= 0f) ClearTarget(m_TargetLost);
        }

        public void ClearTarget(Action targetLost)
        {
            m_TargetLost = targetLost;
            if (m_CurrentTarget == null) return;

            m_CurrentTarget = null;
            m_TargetMemoryLeft = 0f;
            m_TargetLost();
        }

        public void SetTarget(IDamageable target, Action<IDamageable> targetAcquired)
        {
            m_TargetAcquired = targetAcquired;
            if (m_CurrentTarget == target) return;

            m_CurrentTarget = target;
            m_TargetAcquired(m_CurrentTarget);
        }

        bool HasLineOfSight(Vector2 origin, IDamageable target, Collider2D selfCollider)
        {
            Transform targetTransform = target.Transform;
            if (targetTransform == null) return false;

            Vector2 targetPos = targetTransform.position;
            int hitCount = Physics2D.Linecast(origin, targetPos, ContactFilter2D.noFilter, m_LineOfSightBuffer);

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = m_LineOfSightBuffer[i].collider;
                if (hit == null) continue;
                if (selfCollider != null && (hit == selfCollider || hit.transform.IsChildOf(selfCollider.transform))) continue;
                if (hit.isTrigger) continue;
                if (hit.GetComponentInParent<IDamageable>() != null) continue;

                return false;
            }

            return true;
        }

        bool IsValidTarget(IDamageable target, TargetingMode targetingMode, string targetTag)
        {
            if (target == null) return false;
            if (target is UnityEngine.Object unityObject && unityObject == null) return false;
            if (m_IsSelf(target)) return false;
            if (!target.IsDamageable) return false;

            if (targetingMode == TargetingMode.TaggedDamageable)
            {
                return target.Transform.CompareTag(targetTag);
            }

            return true;
        }
    }
}

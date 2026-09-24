using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blocks.Extras
{
    /// <summary>
    /// What a <see cref="MovingPlatform"/> does when it runs out of path.
    /// </summary>
    public enum MovingPlatformType
    {
        /// <summary>Turn around at each end and retrace the path, forever.</summary>
        BackForth,

        /// <summary>Jump from the last waypoint straight back to the first and carry on the same way.</summary>
        Loop,

        /// <summary>Stop on the last waypoint and stay there.</summary>
        Once
    }

    /// <summary>
    /// One waypoint on a <see cref="MovingPlatform"/>'s path: where the platform goes, and how long it
    /// waits once it arrives. The two live together so a path can never end up with more waypoints
    /// than waits.
    /// </summary>
    [Serializable]
    public struct PlatformNode
    {
        [SerializeField] Vector3 localPosition;
        [SerializeField, Min(0f)] float waitTime;

        /// <summary>Where this waypoint sits, relative to the platform's starting position.</summary>
        public Vector3 LocalPosition => localPosition;

        /// <summary>Seconds the platform waits here before setting off for the next waypoint.</summary>
        public float WaitTime => waitTime;
    }

    /// <summary>
    /// Kinematic platform that walks a path of waypoints. Lay the path out by dragging the handles in
    /// the Scene view. Waypoint 0 is always the platform's own starting position.
    /// A platform authored with <c>isMovingAtStart</c> off waits for a call to
    /// <see cref="StartMoving"/>, so a switch or a lever can set it going.
    /// </summary>
    [SelectionBase]
    [RequireComponent(typeof(Rigidbody2D))]
    [DisallowMultipleComponent]
    public sealed class MovingPlatform : MonoBehaviour
    {
        [Header("Path")]
        [SerializeField] PlatformNode[] nodes = new PlatformNode[1];

        [Header("Movement")]
        [Tooltip("Units per second along the path.")]
        [SerializeField, Min(0f)] float speed = 1f;
        [SerializeField] MovingPlatformType platformType;

        [Header("Start")]
        [Tooltip("Off leaves the platform parked on waypoint 0 until something calls StartMoving().")]
        [SerializeField] bool isMovingAtStart = true;

        Rigidbody2D m_Rigidbody2D;

        // The path is authored in local space so a designer can drag the platform and its path together.
        // The platform itself moves during play, which would drag the path along with it, so Start bakes
        // the waypoints to world space once and gameplay only ever reads these.
        Vector3[] m_WorldNodes;

        int m_Current;
        int m_Next;
        int m_Direction = 1;
        float m_WaitTime = -1f;
        bool m_IsMoving;

        /// <summary>
        /// The path in world space, valid from <c>Start</c> onward. The custom editor reads it to draw
        /// the path during play, when the local waypoints no longer line up with the moved platform.
        /// </summary>
        public IReadOnlyList<Vector3> WorldNodes => m_WorldNodes;

        void Reset()
        {
            GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        }

        void Start()
        {
            m_Rigidbody2D = GetComponent<Rigidbody2D>();
            m_Rigidbody2D.bodyType = RigidbodyType2D.Kinematic;

            m_WorldNodes = new Vector3[nodes.Length];
            for (int i = 0; i < m_WorldNodes.Length; ++i)
                m_WorldNodes[i] = transform.TransformPoint(nodes[i].LocalPosition);

            InitializePath();
        }

        void InitializePath()
        {
            m_Current = 0;
            m_Direction = 1;
            m_Next = nodes.Length > 1 ? 1 : 0;

            m_WaitTime = nodes[0].WaitTime;
            m_IsMoving = isMovingAtStart;
        }

        /// <summary>Sends the platform along its path: the hook for a switch or a lever.</summary>
        public void StartMoving()
        {
            m_IsMoving = true;
        }

        /// <summary>
        /// Stops the platform where it stands. <see cref="StartMoving"/> picks up from that point.
        /// </summary>
        public void StopMoving()
        {
            m_IsMoving = false;
        }

        void FixedUpdate()
        {
            if (!m_IsMoving) return;

            // A single-waypoint path has nowhere to go.
            if (m_Current == m_Next) return;

            if (m_WaitTime > 0f)
            {
                m_WaitTime -= Time.fixedDeltaTime;
                return;
            }

            float distanceToGo = speed * Time.fixedDeltaTime;

            while (distanceToGo > 0f)
            {
                Vector2 direction = m_WorldNodes[m_Next] - transform.position;

                float distance = distanceToGo;
                if (direction.sqrMagnitude < distance * distance)
                {
                    // The next waypoint is closer than this step: land exactly on it, pick a new goal,
                    // then spend whatever distance is left this frame heading there.
                    distance = direction.magnitude;

                    m_Current = m_Next;
                    m_WaitTime = nodes[m_Current].WaitTime;

                    AdvanceToNextNode();
                }

                m_Rigidbody2D.MovePosition(m_Rigidbody2D.position + direction.normalized * distance);
                distanceToGo -= distance;

                // Landed on a waypoint that asks for a wait, so hold here instead of moving on.
                if (m_WaitTime > 0.001f) break;
            }
        }

        /// <summary>
        /// Steps <c>m_Next</c> along the path, and decides what to do when it runs off either end.
        /// </summary>
        void AdvanceToNextNode()
        {
            if (m_Direction > 0)
            {
                m_Next += 1;
                if (m_Next < m_WorldNodes.Length) return;

                switch (platformType)
                {
                    case MovingPlatformType.BackForth:
                        m_Next = m_WorldNodes.Length - 2;
                        m_Direction = -1;
                        break;
                    case MovingPlatformType.Loop:
                        m_Next = 0;
                        break;
                    case MovingPlatformType.Once:
                        m_Next -= 1;
                        StopMoving();
                        break;
                }
            }
            else
            {
                m_Next -= 1;
                if (m_Next >= 0) return;

                switch (platformType)
                {
                    case MovingPlatformType.BackForth:
                        m_Next = 1;
                        m_Direction = 1;
                        break;
                    case MovingPlatformType.Loop:
                        m_Next = m_WorldNodes.Length - 1;
                        break;
                    case MovingPlatformType.Once:
                        m_Next += 1;
                        StopMoving();
                        break;
                }
            }
        }
    }
}

using System;
using UnityEngine;

namespace Blocks.Character
{
    /// <summary>
    /// The character's 2D physics: gravity, grounded/ceiling/wall detection, coyote time, being carried
    /// by a moving platform, and the additive velocity composition written to the Rigidbody2D each tick.
    /// </summary>
    sealed class MovementModule
    {
        // How far below the feet ground can be and still count as stood-on (see SnapToGround).
        const float k_GroundSnapDistance = 0.3f;

        readonly ContactPoint2D[] m_Contacts = new ContactPoint2D[16];
        readonly RaycastHit2D[] m_SnapHits = new RaycastHit2D[8];

        ContactFilter2D m_SnapFilter;

        Action<float> m_Landed;
        Action<bool> m_GroundedStateChanged;
        Action m_CeilingHit;
        Rigidbody2D m_Rigidbody;
        Collider2D m_Collider;
        float m_VerticalVelocity;
        float m_TimeLastGrounded;
        bool m_CoyoteInvalidated;
        int m_ContactCount;
        Vector2 m_AdditiveMovement;
        Collider2D m_GroundCollider;
        Collider2D m_LastGroundCollider;
        Vector2 m_LastGroundPosition;
        Vector2 m_GroundVelocity;
        Collider2D m_ResolvedCharacterCollider;
        BuildingBlocksCharacter m_ResolvedCharacter;
        float m_SlideOffVelocityX;

        public bool IsGrounded { get; private set; }
        public bool IsTouchingCeiling { get; private set; }
        public float VerticalVelocity => m_VerticalVelocity;
        public float CurrentSpeed { get; private set; }
        public float FacingDirection { get; private set; } = 1f;
        public Vector2 Velocity => m_Rigidbody != null ? m_Rigidbody.linearVelocity : Vector2.zero;

        public Bounds ColliderBounds => m_Collider != null
            ? m_Collider.bounds
            : new Bounds(Vector3.zero, Vector3.zero);

        public Collider2D Collider => m_Collider;

        public void Initialize(Rigidbody2D rigidbody, Collider2D collider, Action<float> landed, Action<bool> groundedStateChanged, Action ceilingHit)
        {
            m_Rigidbody = rigidbody;
            m_Collider = collider;
            m_Landed = landed;
            m_GroundedStateChanged = groundedStateChanged;
            m_CeilingHit = ceilingHit;

            m_SnapFilter.useTriggers = false;

            if (m_Rigidbody != null)
            {
                m_Rigidbody.gravityScale = 0f;
            }
        }

        public bool IsTouchingLeftWall(float wallDotThreshold) => HasContactWithNormal(Vector2.right, wallDotThreshold);
        public bool IsTouchingRightWall(float wallDotThreshold) => HasContactWithNormal(Vector2.left, wallDotThreshold);

        public void RefreshContacts()
        {
            m_ContactCount = m_Rigidbody != null
                ? m_Rigidbody.GetContacts(m_Contacts)
                : 0;
        }

        public void CheckGrounded(float groundDotThreshold)
        {
            bool wasGrounded = IsGrounded;

            IsGrounded = false;
            m_GroundCollider = null;
            m_SlideOffVelocityX = 0f;

            BuildingBlocksCharacter slideOffSource = null;
            for (int i = 0; i < m_ContactCount; i++)
            {
                if (Vector2.Dot(m_Contacts[i].normal, Vector2.up) < groundDotThreshold) continue;

                Collider2D otherCollider = GetOtherCollider(in m_Contacts[i]);
                BuildingBlocksCharacter other = ResolveCharacter(otherCollider);
                if (other != null && !other.CanBeStoodOn)
                {
                    // Keep scanning: real ground found in the same frame wins, so standing half on
                    // an enemy and half on a ledge stays grounded with no shove.
                    if (slideOffSource == null) slideOffSource = other;
                    continue;
                }

                IsGrounded = true;
                m_GroundCollider = otherCollider;
                break;
            }

            // A descending platform (e.g. reversing direction at a path node) outruns the feet for a few
            // frames before gravity closes the gap: contact is gone but the ground is still just below,
            // so stay grounded rather than flicker (spurious fall animation, land VFX). Upward velocity
            // means a real jump or launch, which must leave the ground immediately.
            if (!IsGrounded && wasGrounded && m_VerticalVelocity <= 0f)
            {
                SnapToGround(groundDotThreshold);
            }

            // Nothing but another character underfoot: shove sideways off it instead of standing.
            if (!IsGrounded && slideOffSource != null)
            {
                m_SlideOffVelocityX = GetSlideOffVelocityX(slideOffSource);
            }

            if (wasGrounded != IsGrounded)
            {
                m_GroundedStateChanged(IsGrounded);
            }

            if (!wasGrounded && IsGrounded)
            {
                m_CoyoteInvalidated = false;

                if (m_VerticalVelocity < -2f)
                {
                    m_Landed(m_VerticalVelocity);
                }
            }

            if (IsGrounded)
            {
                m_TimeLastGrounded = Time.time;
            }
        }

        void SnapToGround(float groundDotThreshold)
        {
            if (m_Collider == null) return;

            int hitCount = m_Collider.Cast(Vector2.down, m_SnapFilter, m_SnapHits, k_GroundSnapDistance);
            for (int i = 0; i < hitCount; i++)
            {
                if (Vector2.Dot(m_SnapHits[i].normal, Vector2.up) < groundDotThreshold) continue;

                // The snap must honour the same rule as contact: a character is never a platform,
                // or walking off a ledge onto an enemy's head would re-grab it as ground.
                BuildingBlocksCharacter other = ResolveCharacter(m_SnapHits[i].collider);
                if (other != null && !other.CanBeStoodOn) continue;

                // Keeping the ground collider tracked keeps m_GroundVelocity valid, so ApplyMovement
                // carries the character down with the platform and contact re-establishes.
                IsGrounded = true;
                m_GroundCollider = m_SnapHits[i].collider;
                return;
            }
        }

        /// <summary>
        /// The character a collider belongs to, or null. Cached against the last collider asked
        /// about: standing on one thing for many frames costs a single lookup, not one per frame.
        /// </summary>
        BuildingBlocksCharacter ResolveCharacter(Collider2D other)
        {
            if (other == null) return null;
            if (other == m_ResolvedCharacterCollider) return m_ResolvedCharacter;

            m_ResolvedCharacterCollider = other;

            // Every character requires a Rigidbody2D, so a collider without one (all static level
            // geometry) can be dismissed before walking any hierarchy.
            Rigidbody2D otherBody = other.attachedRigidbody;
            m_ResolvedCharacter = otherBody != null
                ? otherBody.GetComponentInParent<BuildingBlocksCharacter>()
                : null;

            return m_ResolvedCharacter;
        }

        float GetSlideOffVelocityX(BuildingBlocksCharacter other)
        {
            float speed = other.PushOffSpeed;
            if (speed <= 0f) return 0f;

            float ownX = m_Collider != null
                ? m_Collider.bounds.center.x
                : (m_Rigidbody != null ? m_Rigidbody.position.x : 0f);
            float deltaX = ownX - other.ColliderBounds.center.x;

            // Landing dead centre on the head has no "away" side, so leave the way we already face.
            float direction = Mathf.Abs(deltaX) > 0.0001f ? Mathf.Sign(deltaX) : FacingDirection;
            return direction * speed;
        }

        public void CheckCeiling(float groundDotThreshold)
        {
            bool wasTouching = IsTouchingCeiling;
            IsTouchingCeiling = HasContactWithNormal(Vector2.down, groundDotThreshold);

            if (!wasTouching && IsTouchingCeiling)
            {
                m_CeilingHit();
            }
        }

        public void BeginMovementAbilities()
        {
            m_AdditiveMovement = Vector2.zero;
        }

        /// <summary>
        /// Call from FixedUpdate. Measures how far the ground moved during the last physics step so
        /// moving platforms carry the character. Position deltas are used because kinematic bodies
        /// driven by MovePosition don't report a linearVelocity.
        /// </summary>
        public void SampleGroundVelocity()
        {
            // While airborne, keep tracking the last ground touched: a descending platform briefly
            // out-runs the feet, and the regrab must resume with a valid velocity on its first grounded
            // frame. Starting from zero would separate it again, in an endless loop.
            Collider2D trackedGround = IsGrounded && m_GroundCollider != null ? m_GroundCollider : m_LastGroundCollider;
            if (trackedGround == null)
            {
                m_GroundVelocity = Vector2.zero;
                return;
            }

            Vector2 groundPosition = GetGroundPosition(trackedGround);

            // A delta is only meaningful across two samples of the same collider; fresh ground starts at zero.
            m_GroundVelocity = trackedGround == m_LastGroundCollider
                ? (groundPosition - m_LastGroundPosition) / Time.fixedDeltaTime
                : Vector2.zero;

            m_LastGroundCollider = trackedGround;
            m_LastGroundPosition = groundPosition;
        }

        public void ApplyMovement(float gravity, float fallGravityMultiplier, float maxFallSpeed, bool lockFacing = false,
                                 bool hasHitReaction = false)
        {
            // Sliding off a head counts as resting for the fall: the collider underneath blocks the
            // descent anyway, and letting gravity pile up there would plummet on clearing the edge.
            if ((IsGrounded || m_SlideOffVelocityX != 0f) && m_VerticalVelocity < 0f)
            {
                m_VerticalVelocity = -2f;
            }
            ApplyGravity(gravity, fallGravityMultiplier, maxFallSpeed);

            Vector2 finalVelocity = new Vector2(
                m_AdditiveMovement.x,
                m_VerticalVelocity + m_AdditiveMovement.y);

            if (!lockFacing && Mathf.Abs(finalVelocity.x) > 0.01f)
            {
                FacingDirection = finalVelocity.x > 0f ? 1f : -1f;
            }

            CurrentSpeed = Mathf.Abs(finalVelocity.x);

            // Also applied after facing and speed: sliding off an enemy's head shouldn't flip the
            // sprite or drive the run animation. It replaces horizontal movement rather than adding
            // to it, so holding the stick into the enemy can't cancel the shove and hover up there.
            // A hit reaction outranks it, since that push is already moving the character.
            if (m_SlideOffVelocityX != 0f && !hasHitReaction)
            {
                finalVelocity.x = m_SlideOffVelocityX;
            }

            // Added after facing and speed are computed: being carried by a platform shouldn't
            // flip the sprite or play run animations while the character stands still.
            if (IsGrounded)
            {
                finalVelocity += m_GroundVelocity;
            }

            if (m_Rigidbody != null && !float.IsNaN(finalVelocity.x) && !float.IsNaN(finalVelocity.y))
            {
                m_Rigidbody.linearVelocity = finalVelocity;
            }
        }

        /// <summary>
        /// A character that has temporarily lost its controls: no movement of its own, but gravity still
        /// applies, so a pause in mid-air falls rather than hovering. See <see cref="Freeze"/> for a
        /// character whose movement is switched off outright.
        /// </summary>
        public void ProcessPaused(float gravity, float fallGravityMultiplier, float maxFallSpeed)
        {
            ApplyGravity(gravity, fallGravityMultiplier, maxFallSpeed);

            Vector2 pausedVelocity = new Vector2(0f, m_VerticalVelocity);

            // A paused character still rides the platform it stands on.
            if (IsGrounded)
            {
                pausedVelocity += m_GroundVelocity;
            }

            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = pausedVelocity;
            }

            CurrentSpeed = 0f;
        }

        /// <summary>
        /// A character whose movement module is switched off: held still, with no gravity. The vertical
        /// velocity is zeroed rather than left to accumulate, so switching movement back on doesn't hand
        /// the character a fall speed it built up while frozen.
        /// </summary>
        public void Freeze()
        {
            m_VerticalVelocity = 0f;
            CurrentSpeed = 0f;

            // Still carried by whatever it stands on: a platform moving is the world's doing, not the
            // character's, and being left behind by one would read as a different bug.
            Vector2 frozenVelocity = IsGrounded ? m_GroundVelocity : Vector2.zero;

            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = frozenVelocity;
            }
        }

        public bool HasContactWithNormal(Vector2 direction, float threshold = 0.7f)
        {
            for (int i = 0; i < m_ContactCount; i++)
            {
                if (Vector2.Dot(m_Contacts[i].normal, direction) >= threshold)
                {
                    return true;
                }
            }

            return false;
        }

        public void SetVerticalVelocity(float newVerticalVelocity) => m_VerticalVelocity = newVerticalVelocity;

        public bool IsGroundedOrInCoyote(float coyoteTime)
        {
            return IsGrounded || (!m_CoyoteInvalidated && Time.time - m_TimeLastGrounded < coyoteTime);
        }

        public void Jump(float height, float gravity)
        {
            float velocity = Mathf.Sqrt(height * -2f * gravity);
            SetVerticalVelocity(velocity);
            m_CoyoteInvalidated = true;
        }

        public void AddMovement(Vector2 movement) => m_AdditiveMovement += movement;

        /// <summary>
        /// Clears every piece of state that describes motion in progress. Nulling the ground collider
        /// matters as much as the velocities: SampleGroundVelocity differences it against the last
        /// sampled position, so a character eliminated on a moving platform would otherwise measure the
        /// platform's whole travel during the respawn delay as one frame of ground velocity.
        /// </summary>
        public void ResetVelocityAndForces()
        {
            m_VerticalVelocity = 0f;
            CurrentSpeed = 0f;
            m_GroundVelocity = Vector2.zero;
            m_LastGroundCollider = null;
            m_SlideOffVelocityX = 0f;

            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = Vector2.zero;
            }
        }

        public void FacePosition(Vector2 currentPosition, Vector2 position)
        {
            float delta = position.x - currentPosition.x;
            if (Mathf.Abs(delta) <= 0.01f) return;

            FacingDirection = delta > 0f ? 1f : -1f;
        }

        Collider2D GetOtherCollider(in ContactPoint2D contact)
        {
            return contact.collider.attachedRigidbody == m_Rigidbody ? contact.otherCollider : contact.collider;
        }

        static Vector2 GetGroundPosition(Collider2D ground)
        {
            Rigidbody2D groundBody = ground.attachedRigidbody;
            return groundBody != null ? groundBody.position : (Vector2)ground.transform.position;
        }

        void ApplyGravity(float gravity, float fallGravityMultiplier, float maxFallSpeed)
        {
            float effectiveGravity = m_VerticalVelocity < 0f
                ? gravity * fallGravityMultiplier
                : gravity;

            if (m_VerticalVelocity > -maxFallSpeed)
            {
                m_VerticalVelocity += effectiveGravity * Time.deltaTime;
            }
        }
    }
}

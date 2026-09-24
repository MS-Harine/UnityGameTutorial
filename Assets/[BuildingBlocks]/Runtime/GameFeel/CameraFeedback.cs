using UnityEngine;
using Unity.Cinemachine;

namespace Blocks.GameFeel
{
    /// <summary>
    /// Camera-side game feel: shake and zoom driven by gameplay events (see <see cref="AttackFeedback"/>).
    /// Framing — damping, dead zone, screen position, lookahead — is *not* set here. Tune it directly on
    /// the CinemachinePositionComposer sitting next to this component.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera), typeof(CinemachineImpulseSource))]
    public class CameraFeedback : CinemachineExtension
    {
        [Header("Zoom Effects")]
        [Tooltip("How quickly the camera eases toward a requested zoom multiplier (higher = snappier).")]
        [SerializeField] float zoomSmoothing = 8f;

        // Never let effects zoom in past half the normal view.
        const float k_MinZoomMultiplier = 0.5f;

        CinemachineImpulseSource m_ImpulseSource;
        float m_TargetZoomMultiplier = 1f;
        float m_CurrentZoomMultiplier = 1f;

        /// <summary>
        /// Points the camera at a new follow target.
        /// </summary>
        public void SetTarget(Transform target)
        {
            if (ComponentOwner is CinemachineCamera cam)
            {
                cam.Follow = target;
            }
        }

        /// <summary>
        /// Kicks the camera. Needs a CinemachineImpulseListener on the Cinemachine Brain to be visible.
        /// </summary>
        public void Shake(float force)
        {
            if (m_ImpulseSource == null)
            {
                m_ImpulseSource = GetComponent<CinemachineImpulseSource>();
            }

            if (m_ImpulseSource != null)
            {
                m_ImpulseSource.GenerateImpulseWithForce(force);
            }
        }

        /// <summary>
        /// Eases the camera toward a zoom multiplier (1 = normal, 0.9 = 10% closer). Sustained
        /// effects call this every frame and reset it to 1 when done.
        /// </summary>
        public void SetZoomMultiplier(float multiplier)
        {
            m_TargetZoomMultiplier = Mathf.Clamp(multiplier, k_MinZoomMultiplier, 2f);
        }

        /// <summary>
        /// Instantly zooms in by <paramref name="punch"/> (0.05 = 5% closer), then eases back out.
        /// </summary>
        public void PunchZoom(float punch)
        {
            m_CurrentZoomMultiplier = Mathf.Max(k_MinZoomMultiplier, m_CurrentZoomMultiplier - punch);
        }

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize) return;

            if (deltaTime >= 0f)
            {
                // Exponential ease: framerate-independent, and it holds still through a hit-stop
                // freeze (deltaTime 0) so a zoom punch releases only after the freeze ends.
                float blend = 1f - Mathf.Exp(-zoomSmoothing * deltaTime);
                m_CurrentZoomMultiplier = Mathf.Lerp(m_CurrentZoomMultiplier, m_TargetZoomMultiplier, blend);
            }
            else
            {
                // Negative deltaTime is a camera cut, so snap instead of easing.
                m_CurrentZoomMultiplier = m_TargetZoomMultiplier;
            }

            if (Mathf.Approximately(m_CurrentZoomMultiplier, 1f)) return;

            LensSettings lens = state.Lens;
            lens.OrthographicSize *= m_CurrentZoomMultiplier;
            lens.FieldOfView *= m_CurrentZoomMultiplier;
            state.Lens = lens;
        }

        void OnValidate()
        {
            if (zoomSmoothing < 0.1f) zoomSmoothing = 0.1f;
        }
    }
}

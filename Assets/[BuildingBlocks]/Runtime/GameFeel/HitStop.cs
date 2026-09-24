using UnityEngine;

namespace Blocks.GameFeel
{
    /// <summary>
    /// Classic hit-stop: <see cref="Play"/> freezes the game for a moment (real-time seconds).
    /// Drives <c>Time.timeScale</c> directly; route any slow-motion/pause system through here.
    /// </summary>
    public static class HitStop
    {
        static HitStopRunner s_Runner;
        static bool s_IsSuspended;

        /// <summary>
        /// Set by a pause system that takes ownership of <c>Time.timeScale</c>. While suspended,
        /// hit-stop drops any freeze in progress and never writes the time scale; otherwise a freeze
        /// that ran out mid-pause would resume the game behind the menu.
        /// </summary>
        public static bool IsSuspended
        {
            get => s_IsSuspended;
            set
            {
                if (s_IsSuspended == value) return;
                s_IsSuspended = value;

                if (value && s_Runner != null) s_Runner.Cancel();
            }
        }

        public static void Play(float duration)
        {
            if (duration <= 0f) return;
            if (s_IsSuspended) return;

            if (s_Runner == null)
            {
                GameObject runnerObject = new GameObject("[HitStop]");
                runnerObject.hideFlags = HideFlags.HideInHierarchy;
                Object.DontDestroyOnLoad(runnerObject);
                s_Runner = runnerObject.AddComponent<HitStopRunner>();
            }

            s_Runner.Freeze(duration);
        }

        sealed class HitStopRunner : MonoBehaviour
        {
            float m_FreezeEndTime;
            bool m_IsFrozen;

            public void Freeze(float duration)
            {
                float endTime = Time.unscaledTime + duration;
                if (endTime > m_FreezeEndTime) m_FreezeEndTime = endTime;

                if (m_IsFrozen) return;
                m_IsFrozen = true;
                Time.timeScale = 0f;
            }

            internal void Cancel()
            {
                // The pause owns the time scale now, so drop the freeze without restoring it.
                m_IsFrozen = false;
                m_FreezeEndTime = 0f;
            }

            void Update()
            {
                if (s_IsSuspended) return;
                if (!m_IsFrozen) return;
                if (Time.unscaledTime < m_FreezeEndTime) return;

                m_IsFrozen = false;
                Time.timeScale = 1f;
            }

            void OnDestroy()
            {
                // Leaving play mode (or a scene teardown) mid-freeze must not strand the editor at scale 0.
                if (m_IsFrozen) Time.timeScale = 1f;
            }
        }
    }
}

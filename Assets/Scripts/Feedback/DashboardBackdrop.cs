using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace StageReadyVR.Feedback
{
    /// <summary>
    /// A full-view dark panel that fades in behind the dashboard to dim the
    /// scene and remove audience occlusion. Sits between the user and the
    /// environment (in front of the audience, behind the dashboard content).
    /// Built and wired by DashboardBuilder; driven by DashboardPanel.
    /// </summary>
    public class DashboardBackdrop : MonoBehaviour
    {
        [Tooltip("The dark overlay image that fades in")]
        public Image overlay;
        [Tooltip("Target alpha when fully dimmed (0-1). Lower = scene more visible.")]
        [Range(0f, 1f)] public float targetAlpha = 0.7f;
        [Tooltip("Fade duration in seconds")]
        public float fadeDuration = 0.6f;

        public void Hide()
        {
            if (overlay != null)
            {
                var c = overlay.color; c.a = 0f; overlay.color = c;
            }
            gameObject.SetActive(false);
        }

        public void FadeIn()
        {
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(targetAlpha));
        }

        public void FadeOut()
        {
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(0f, deactivateAtEnd: true));
        }

        private IEnumerator FadeRoutine(float to, bool deactivateAtEnd = false)
        {
            if (overlay == null) yield break;
            float from = overlay.color.a;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                float a = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / fadeDuration));
                var c = overlay.color; c.a = a; overlay.color = c;
                yield return null;
            }
            var final = overlay.color; final.a = to; overlay.color = final;
            if (deactivateAtEnd) gameObject.SetActive(false);
        }
    }
}

using UnityEngine;

namespace TreeMotion
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TreeMotionFade : MonoBehaviour, ITreeMotionPresentationHandler
    {
        [SerializeField] private CanvasGroup _canvasGroup;

        public void ResetPresentation()
        {
            _canvasGroup.alpha = 1f;
        }

        public void ApplyPresentation(in TreeMotionPresentation presentation)
        {
            var alpha = presentation.Role == TreeMotionPresentationRole.Entering ? presentation.Progress :
                presentation.Role == TreeMotionPresentationRole.Exiting ? 1f - presentation.Progress : 1f;
            _canvasGroup.alpha = alpha;
            _canvasGroup.blocksRaycasts = !presentation.IsAnimating;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }
#endif
    }
}

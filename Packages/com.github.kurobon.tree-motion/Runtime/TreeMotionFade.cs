using UnityEngine;

namespace TreeMotion
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TreeMotionFade : MonoBehaviour, ITreeMotionPresentationHandler
    {
        [SerializeField] private CanvasGroup _canvasGroup;

        private float _initialAlpha;
        private bool _initialized;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            _initialAlpha = _canvasGroup.alpha;
            _initialized = true;
        }

        public void ResetPresentation()
        {
            Initialize();
            _canvasGroup.alpha = _initialAlpha;
        }

        public void SetTreeMotionPresentation(in TreeMotionPresentation presentation)
        {
            Initialize();
            var factor = presentation.Role == TreeMotionPresentationRole.Entering ? presentation.Progress :
                presentation.Role == TreeMotionPresentationRole.Exiting ? 1f - presentation.Progress : 1f;
            _canvasGroup.alpha = _initialAlpha * factor;
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

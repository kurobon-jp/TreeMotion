using UnityEngine;

namespace TreeMotion
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TreeMotionInteractionBlocker : MonoBehaviour, ITreeMotionPresentationHandler
    {
        [SerializeField] private CanvasGroup _canvasGroup;

        private bool _initialInteractable;
        private bool _initialRaycasts;
        private bool _initialized;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            _initialInteractable = _canvasGroup.interactable;
            _initialRaycasts = _canvasGroup.blocksRaycasts;
            _initialized = true;
        }

        public void ResetPresentation()
        {
            Initialize();
            _canvasGroup.interactable = _initialInteractable;
            _canvasGroup.blocksRaycasts = _initialRaycasts;
        }

        public void SetTreeMotionPresentation(in TreeMotionPresentation presentation)
        {
            Initialize();
            var allowed = presentation.Role != TreeMotionPresentationRole.Exiting &&
                          (presentation.Role != TreeMotionPresentationRole.Entering || presentation.Progress >= 1f);
            _canvasGroup.interactable = allowed && _initialInteractable;
            _canvasGroup.blocksRaycasts = allowed && _initialRaycasts;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }
#endif
    }
}

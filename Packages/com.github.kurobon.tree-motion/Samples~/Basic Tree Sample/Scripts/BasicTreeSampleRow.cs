using System;
using TMPro;
using UnityEngine;

namespace TreeMotion.Samples.Basic
{
    public sealed class BasicTreeSampleRow : MonoBehaviour, ITreeMotionItemView
    {
        [SerializeField] private UnityEngine.UI.Button _button;
        [SerializeField] private UnityEngine.UI.Image _background;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private RectTransform _indentTarget;
        [SerializeField, Min(0f)] private float _indentPerDepth = 24f;

        private static TMP_FontAsset _runtimeFont;
        private CanvasGroup _canvasGroup;
        private Action<int> _clicked;
        private int _id;
        private float _baseLeftInset;

        public RectTransform RectTransform => (RectTransform)transform;

        public void Configure(UnityEngine.UI.Button button, UnityEngine.UI.Image background,
            TextMeshProUGUI label)
        {
            _button = button;
            _background = background;
            _label = label;
            _indentTarget = label.rectTransform;
        }

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            if (_indentTarget == null)
                _indentTarget = _label.rectTransform;
            _baseLeftInset = _indentTarget.offsetMin.x;
            _button.onClick.AddListener(() => _clicked?.Invoke(_id));
        }

        public void Bind(int id, string item, VisibleRow<int> row, Action<int> clicked)
        {
            _id = id;
            _clicked = clicked;
            var offsetMin = _indentTarget.offsetMin;
            offsetMin.x = _baseLeftInset + row.Depth * _indentPerDepth;
            _indentTarget.offsetMin = offsetMin;
            _button.interactable = row.HasChildren;
            _label.text = row.HasChildren
                ? $"{(row.IsExpanded ? "▼" : "▶")}  {item}"
                : item;
            _background.color = row.HasChildren
                ? new Color(0.10f, 0.47f, 0.52f, 1f)
                : new Color(0.12f, 0.25f, 0.68f, 1f);
        }

        public void SetTreeMotionPresentation(in TreeMotionPresentation presentation)
        {
            var progress = presentation.Progress;
            var entering = presentation.Role == TreeMotionPresentationRole.Entering;
            var exiting = presentation.Role == TreeMotionPresentationRole.Exiting;
            var opacity = entering ? progress : exiting ? 1f - progress : 1f;
            var scale = entering
                ? Mathf.LerpUnclamped(0.92f, 1f, progress)
                : exiting
                    ? Mathf.LerpUnclamped(1f, 0.92f, progress)
                    : 1f;
            _canvasGroup.alpha = opacity;
            _canvasGroup.blocksRaycasts = opacity >= 0.999f;
            transform.localScale = new Vector3(1f, scale, 1f);
        }
    }
}

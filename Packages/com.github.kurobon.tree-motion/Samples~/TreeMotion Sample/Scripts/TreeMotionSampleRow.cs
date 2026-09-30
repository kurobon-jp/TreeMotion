using TMPro;
using UnityEngine;

namespace TreeMotion.Samples
{
    public sealed class TreeMotionSampleRow : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button _rowButton;
        [SerializeField] private UnityEngine.UI.Button _toggleButton;
        [SerializeField] private UnityEngine.UI.Image _background;
        [SerializeField] private TextMeshProUGUI _toggleLabel;
        [SerializeField] private TextMeshProUGUI _label;

        private TreeMotionSampleController _controller;
        private UnityEngine.UI.Outline _outline;
        private CanvasGroup _canvasGroup;
        private int _id;

        internal RectTransform RectTransform => (RectTransform)transform;

        public void Configure(UnityEngine.UI.Button rowButton, UnityEngine.UI.Button toggleButton,
            UnityEngine.UI.Image background, TextMeshProUGUI toggleLabel, TextMeshProUGUI label)
        {
            _rowButton = rowButton;
            _toggleButton = toggleButton;
            _background = background;
            _toggleLabel = toggleLabel;
            _label = label;
        }

        internal void Initialize(TreeMotionSampleController controller)
        {
            _controller = controller;
            _rowButton.onClick.AddListener(Select);
            _toggleButton.onClick.AddListener(Toggle);
            _outline = GetComponent<UnityEngine.UI.Outline>();
            if (_outline == null)
                _outline = gameObject.AddComponent<UnityEngine.UI.Outline>();
            _outline.effectColor = new Color(0.02f, 0.05f, 0.12f, 0.95f);
            _outline.effectDistance = new Vector2(2f, -2f);
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        internal void SetPresentation(in TreeMotionPresentation presentation)
        {
            var progress = presentation.Progress;
            var entering = presentation.Role == TreeMotionPresentationRole.Entering;
            var exiting = presentation.Role == TreeMotionPresentationRole.Exiting;
            var opacity = entering ? progress : exiting ? 1f - progress : 1f;
            var scale = entering
                ? Mathf.LerpUnclamped(0.96f, 1f, progress)
                : exiting
                    ? Mathf.LerpUnclamped(1f, 0.96f, progress)
                    : 1f;
            _canvasGroup.alpha = opacity;
            _canvasGroup.blocksRaycasts = opacity >= 0.999f;
            transform.localScale = new Vector3(1f, scale, 1f);
        }

        internal void Bind(int id, string label, bool isGroup, bool isExpanded, bool isSelected)
        {
            _id = id;
            _label.text = label;
            _toggleButton.gameObject.SetActive(isGroup);
            _toggleLabel.text = isGroup ? (isExpanded ? "v" : ">") : string.Empty;
            _outline.enabled = !isGroup;

            if (isGroup)
            {
                _background.color = isSelected
                    ? new Color(0.04f, 0.30f, 0.38f, 0.92f)
                    : new Color(0.03f, 0.22f, 0.28f, 0.62f);
                _label.alignment = TextAlignmentOptions.Center;
                _label.fontStyle = FontStyles.Bold;
                _label.rectTransform.offsetMin = new Vector2(56f, 4f);
                _label.rectTransform.offsetMax = new Vector2(-56f, -4f);
            }
            else
            {
                _background.color = isSelected
                    ? new Color(0.20f, 0.42f, 0.96f, 1f)
                    : new Color(0.08f, 0.16f, 0.78f, 1f);
                _label.alignment = TextAlignmentOptions.MidlineLeft;
                _label.fontStyle = FontStyles.Normal;
                _label.rectTransform.offsetMin = new Vector2(24f, 4f);
                _label.rectTransform.offsetMax = new Vector2(-20f, -4f);
            }
        }

        private void Select() => _controller.Select(_id);
        private void Toggle() => _controller.Toggle(_id);
    }
}

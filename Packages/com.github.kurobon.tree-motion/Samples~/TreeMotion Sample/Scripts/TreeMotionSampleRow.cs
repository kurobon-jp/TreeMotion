using TMPro;
using UnityEngine;

namespace TreeMotion.Samples
{
    public sealed class TreeMotionSampleRow : MonoBehaviour, ITreeMotionPresentationHandler
    {
        [SerializeField] private UnityEngine.UI.Button _rowButton;
        [SerializeField] private UnityEngine.UI.Button _toggleButton;
        [SerializeField] private UnityEngine.UI.Image _background;
        [SerializeField] private TextMeshProUGUI _toggleLabel;
        [SerializeField] private TextMeshProUGUI _label;

        private TreeMotionSampleController _controller;
        private UnityEngine.UI.Outline _outline;
        private bool _initialized;
        private int _id;


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
            if (_initialized) return;
            _initialized = true;
            _rowButton.onClick.AddListener(Select);
            _toggleButton.onClick.AddListener(Toggle);
            _outline = GetComponent<UnityEngine.UI.Outline>();
            if (_outline == null)
                _outline = gameObject.AddComponent<UnityEngine.UI.Outline>();
            _outline.effectColor = new Color(0.67f, 0.75f, 0.76f, 1f);
            _outline.effectDistance = new Vector2(2f, -2f);
        }

        public void ResetPresentation() => transform.localScale = Vector3.one;

        public void SetTreeMotionPresentation(in TreeMotionPresentation presentation)
        {
            var progress = presentation.Progress;
            var entering = presentation.Role == TreeMotionPresentationRole.Entering;
            var exiting = presentation.Role == TreeMotionPresentationRole.Exiting;
            var scale = entering
                ? Mathf.LerpUnclamped(0.96f, 1f, progress)
                : exiting
                    ? Mathf.LerpUnclamped(1f, 0.96f, progress)
                    : 1f;
            transform.localScale = new Vector3(1f, scale, 1f);
        }

        internal void Bind(int id, string label, bool isGroup, bool isExpanded, bool isSelected)
        {
            Debug.Log($"Bind {id} {label}");
            _id = id;
            _label.text = label;
            _toggleButton.gameObject.SetActive(isGroup);
            _toggleLabel.text = isGroup ? (isExpanded ? "v" : ">") : string.Empty;
            _outline.enabled = !isGroup;

            if (isGroup)
            {
                _background.color = isSelected
                    ? new Color(0.65f, 0.82f, 0.84f, 1f)
                    : new Color(0.80f, 0.88f, 0.88f, 1f);
                _label.alignment = TextAlignmentOptions.Center;
                _label.fontStyle = FontStyles.Bold;
                _label.rectTransform.offsetMin = new Vector2(56f, 4f);
                _label.rectTransform.offsetMax = new Vector2(-56f, -4f);
            }
            else
            {
                _background.color = isSelected
                    ? new Color(0.81f, 0.90f, 0.97f, 1f)
                    : Color.white;
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

using System;
using TMPro;
using UnityEngine;

namespace TreeMotion.Samples.ScrollView
{
    public sealed class ScrollViewTreeSampleRow : MonoBehaviour, ITreeMotionPresentationHandler
    {
        [SerializeField] private UnityEngine.UI.Button _button;
        [SerializeField] private UnityEngine.UI.Image _background;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private TextMeshProUGUI _toggleLabel;

        private Action<int> _clicked;
        private int _id;


        public void Configure(UnityEngine.UI.Button button, UnityEngine.UI.Image background,
            TextMeshProUGUI label)
        {
            _button = button;
            _background = background;
            _label = label;
        }

        private void Awake()
        {
            _button.onClick.AddListener(() => _clicked?.Invoke(_id));
        }

        public void Bind(int id, string item, VisibleRow<int> row, Action<int> clicked, bool isGroup = false)
        {
            _id = id;
            _clicked = clicked;
            _button.interactable = isGroup;
            _label.text = item;
            _label.alignment = isGroup ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            _label.fontStyle = isGroup ? FontStyles.Bold : FontStyles.Normal;
            _label.rectTransform.offsetMin = new Vector2(isGroup ? 24f : 12f, 1f);
            _label.rectTransform.offsetMax = new Vector2(isGroup ? -24f : -12f, -1f);
            _toggleLabel.transform.parent.gameObject.SetActive(isGroup);
            _toggleLabel.text = row.IsExpanded ? "v" : ">";

        }

        public void ResetPresentation() => transform.localScale = Vector3.one;

        public void SetTreeMotionPresentation(in TreeMotionPresentation presentation)
        {
            var progress = presentation.Progress;
            var entering = presentation.Role == TreeMotionPresentationRole.Entering;
            var exiting = presentation.Role == TreeMotionPresentationRole.Exiting;
            var scale = entering
                ? Mathf.LerpUnclamped(0.92f, 1f, progress)
                : exiting
                    ? Mathf.LerpUnclamped(1f, 0.92f, progress)
                    : 1f;
            transform.localScale = new Vector3(1f, scale, 1f);
        }
    }
}

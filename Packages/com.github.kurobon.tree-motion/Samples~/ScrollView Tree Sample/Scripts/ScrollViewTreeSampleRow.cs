using System;
using UnityEngine;
using UnityEngine.UI;

namespace TreeMotion.Samples.ScrollView
{
    public sealed class ScrollViewTreeSampleRow : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private Text _label;
        [SerializeField] private Text _toggleLabel;

        private Action<int> _clicked;
        private int _id;

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
            _toggleLabel.transform.parent.gameObject.SetActive(isGroup);
            _toggleLabel.text = row.IsExpanded ? "v" : ">";
        }
    }
}
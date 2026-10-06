using System;
using UnityEngine;
using UnityEngine.UI;

namespace TreeMotion.Samples
{
    public sealed class SampleView : MonoBehaviour
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

        public void Bind(int id, SampleItem item, Action<int> clicked, bool isExpanded)
        {
            _id = id;
            _label.text = item.Label;
            _toggleLabel.gameObject.SetActive(false);
            _clicked = clicked;
            _toggleLabel.gameObject.SetActive(item.Type == SampleItemType.Group);
            _toggleLabel.text = isExpanded ? "v" : ">";
        }
    }
}
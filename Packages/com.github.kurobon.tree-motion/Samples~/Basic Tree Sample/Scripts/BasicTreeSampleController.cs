using UnityEngine;

namespace TreeMotion.Samples.Basic
{
    public sealed class BasicTreeSampleController : MonoBehaviour
    {
        [SerializeField] private TreeMotionScrollView _scrollView;
        [SerializeField] private BasicTreeSampleRow _itemPrefab;

        private readonly TreeStore<int, string> _tree = new TreeStore<int, string>();
        private TreeMotionViewController<int> _view;

        public void Configure(TreeMotionScrollView scrollView, BasicTreeSampleRow itemPrefab)
        {
            _scrollView = scrollView;
            _itemPrefab = itemPrefab;
        }

        private void Start()
        {
            _tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Root(1, "First group", 0, true),
                TreeNodeRecord<int, string>.Child(2, 1, "First item", 0),
                // TreeNodeRecord<int, string>.Child(7, 1, "Second item", 0),
                TreeNodeRecord<int, string>.Child(3, 1, "Nested group", 1, true),
                TreeNodeRecord<int, string>.Child(4, 3, "Nested item", 0),
                TreeNodeRecord<int, string>.Root(5, "Second group", 1, true),
                TreeNodeRecord<int, string>.Child(6, 5, "Another item", 0),

            });

            _view = _scrollView.SetDataSource(_tree, _itemPrefab, 56f, Bind);
        }

        private void Bind(BasicTreeSampleRow view, int id, string item, VisibleRow<int> row)
            => view.Bind(id, item, row, Toggle);

        private void Toggle(int id)
        {
            if (_tree.GetChildCount(id) == 0)
                return;
            var changes = _tree.BeginUpdate()
                .SetExpanded(id, !_tree.IsExpanded(id))
                .Commit();
            _view.Apply(changes);
        }
    }
}

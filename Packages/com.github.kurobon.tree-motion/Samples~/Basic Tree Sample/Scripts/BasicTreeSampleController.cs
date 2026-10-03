using UnityEngine;

namespace TreeMotion.Samples.Basic
{
    public sealed class BasicTreeSampleController : MonoBehaviour, ITreeMotionDataSource<int, string>
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
            var nodes = new TreeNodeRecord<int, string>[100];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new TreeNodeRecord<int, string>(i, $"{i}", siblingIndex: i, isExpanded: true);
            }
            _tree.LoadSnapshot(nodes);
            _view = _scrollView.SetDataSource(_tree, this);
        }

        public int GetItemType(int id, string item) => 0;
        public GameObject GetItemPrefab(int itemType) => _itemPrefab.gameObject;
        public float GetItemHeight(int itemType) => 56f;
        public void Bind(GameObject view, int id, string item, VisibleRow<int> row)
            => (view.GetComponent<BasicTreeSampleRow>()).Bind(id, item, row, Toggle);

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

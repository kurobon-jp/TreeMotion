using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TreeMotion.Samples.ScrollView
{
    // TreeMotionScrollView owns all layout, pooling, culling and animation.
    public sealed class ScrollViewTreeSampleController : MonoBehaviour, ITreeMotionDataSource<int, SampleItem>
    {
        [SerializeField] private TreeMotionScrollView _scrollView;
        [SerializeField] private ScrollViewTreeSampleRow _itemPrefab;
        [SerializeField] private ScrollViewTreeSampleRow _alternateItemPrefab;
        [SerializeField] private TreeMotionGroupView _groupPrefab;
        [SerializeField] private TreeMotionGroupView _nestedGroupPrefab;

        [SerializeField, Min(0f)] private float _openingHold = 2f;
        [SerializeField, Min(0f)] private float _beatDuration = 0.5f;
        [SerializeField, Min(0f)] private float _loopHold = 2f;

        [SerializeField] private bool _animateChanges = true;

        private readonly TreeStore<int, SampleItem> _tree = new();
        private TreeMotionViewController<int> _view;
        private CancellationTokenSource _showcaseCancellation;

        public void Configure(TreeMotionScrollView scrollView, ScrollViewTreeSampleRow itemPrefab)
        {
            _scrollView = scrollView;
            _itemPrefab = itemPrefab;
        }

        private void Start()
        {
            _tree.LoadSnapshot(ScrollViewShowcaseSequence.InitialSnapshot());
            _view = _scrollView.SetDataSource(_tree, this);
            if (_animateChanges)
                StartShowcase();
        }

        private void OnDestroy()
        {
            _showcaseCancellation?.Cancel();
        }

        public int GetItemType(int id, SampleItem item) => (int)item.Type;

        public GameObject GetItemPrefab(int itemType)
        {
            switch ((SampleItemType)itemType)
            {
                case SampleItemType.Item: return _itemPrefab.gameObject;
                case SampleItemType.Card: return _alternateItemPrefab.gameObject;
                case SampleItemType.Group: return _groupPrefab.gameObject;
                case SampleItemType.NestedGroup: return _nestedGroupPrefab.gameObject;
                case SampleItemType.ExpandedCard: return _alternateItemPrefab.gameObject;
                default: throw new ArgumentOutOfRangeException(nameof(itemType));
            }
        }

        public float GetItemHeight(int itemType) => (SampleItemType)itemType switch
        {
            SampleItemType.Item => 32f,
            SampleItemType.Card => 40f,
            SampleItemType.ExpandedCard => 80f,
            // Group height is calculated from its ChildrenFrame and children by TreeMotion.
            _ => throw new ArgumentOutOfRangeException(nameof(itemType))
        };

        public void Bind(GameObject view, int id, SampleItem item, VisibleRow<int> row)
        {
            var isGroup = item.Type == SampleItemType.Group || item.Type == SampleItemType.NestedGroup;
            view.GetComponent<ScrollViewTreeSampleRow>().Bind(id, item.Label, row, Toggle, isGroup);
        }

        private void Toggle(int id)
        {
            _showcaseCancellation?.Cancel();
            if (_tree.GetChildCount(id) > 0)
                _view.Apply(_tree.BeginUpdate().SetExpanded(id, !_tree.IsExpanded(id)).Commit());
        }

        // Unity event entry points cannot await a Task; observe cancellation and failures here.
        private async void StartShowcase()
        {
            try
            {
                _showcaseCancellation?.Cancel();
                _showcaseCancellation = new CancellationTokenSource();
                await DemonstrateChanges(_showcaseCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async Task DemonstrateChanges(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(_openingHold), cancellationToken);
            while (true)
            {
                for (var step = 0; step < ScrollViewShowcaseSequence.Captions.Length; step++)
                {
                    await _view.ApplyAsync(ScrollViewShowcaseSequence.ApplyStep(_tree, step), cancellationToken);
                    // Finish the transition before holding a readable, settled frame.
                    await Task.Delay(TimeSpan.FromSeconds(_beatDuration), cancellationToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(_loopHold), cancellationToken);
            }
        }
    }
}

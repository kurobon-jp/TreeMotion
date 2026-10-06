using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TreeMotion.Samples
{
    // TreeMotionScrollView owns all layout, pooling, culling and animation.
    public sealed class Showcase : MonoBehaviour, ITreeMotionAdapter<int, SampleItem>
    {
        [SerializeField] private TreeMotionScrollView _scrollView;
        [SerializeField] private GameObject _itemPrefab;
        [SerializeField] private GameObject _cardPrefab;
        [SerializeField] private GameObject _groupPrefab;

        [SerializeField, Min(0f)] private float _openingHold = 2f;
        [SerializeField, Min(0f)] private float _beatDuration = 0.5f;
        [SerializeField, Min(0f)] private float _loopHold = 2f;

        private readonly TreeStore<int, SampleItem> _tree = new();
        private TreeMotionBinding<int> _binding;
        private CancellationTokenSource _showcaseCancellation;

        private void Start()
        {
            _tree.LoadSnapshot(ShowcaseSequence.InitialSnapshot());
            _binding = _scrollView.Bind(_tree, this);
            StartShowcase();
        }

        private void OnDestroy()
        {
            _showcaseCancellation?.Cancel();
        }

        public GameObject GetItemPrefab(int id, SampleItem item)
        {
            switch (item.Type)
            {
                case SampleItemType.Item: return _itemPrefab;
                case SampleItemType.Group: return _groupPrefab;
                case SampleItemType.Card: return _cardPrefab;
                default: throw new ArgumentOutOfRangeException(nameof(item));
            }
        }

        public float GetItemSize(int id, SampleItem item)
        {
            return item.Size;
        }

        public void Bind(GameObject go, int id, SampleItem item, VisibleRow<int> row)
        {
            if (go.TryGetComponent(out SampleView view))
            {
                view.Bind(id, item, Toggle, row.IsExpanded);
            }
        }

        private void Toggle(int id)
        {
            _showcaseCancellation?.Cancel();
            if (_tree.GetChildCount(id) > 0)
                _binding.Apply(_tree
                    .BeginUpdate()
                    .Expand(id, !_tree.IsExpanded(id))
                    .Commit()
                );
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
                for (var i = 0; i < ShowcaseSequence.StepCount; i++)
                {
                    var changes = ShowcaseSequence.ApplyStep(_tree, i);
                    await _binding.ApplyAsync(changes, cancellationToken);
                    // Finish the transition before holding a readable, settled frame.
                    await Task.Delay(TimeSpan.FromSeconds(_beatDuration), cancellationToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(_loopHold), cancellationToken);
            }
        }
    }
}

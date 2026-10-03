using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TreeMotion.Samples.ScrollView
{
    // TreeMotionScrollView owns all layout, pooling, culling and animation.
    public sealed class ScrollViewTreeSampleController : MonoBehaviour, ITreeMotionDataSource<int, string>
    {
        [SerializeField] private TreeMotionScrollView _scrollView;
        [SerializeField] private ScrollViewTreeSampleRow _itemPrefab;
        [SerializeField] private ScrollViewTreeSampleRow _alternateItemPrefab;
        [SerializeField] private TreeMotionGroupView _groupPrefab;
        [SerializeField] private TreeMotionGroupView _nestedGroupPrefab;
        private readonly HashSet<int> _groupIds = new() { 1, 5, 7 };
        [SerializeField] private bool _animateChanges = true;

        private readonly TreeStore<int, string> _tree = new();
        private TreeMotionViewController<int> _view;
        [SerializeField, Min(0f)] private float _openingHold = 2f;
        [SerializeField, Min(0f)] private float _beatDuration = 0.9f;
        [SerializeField, Min(0f)] private float _loopHold = 2f;
        [SerializeField] private TMPro.TMP_Text _caption;
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

        public int GetItemType(int id, string item)
            => _groupIds.Contains(id) ? (id == 7 ? 3 : 2)
                : item.StartsWith("Expanded card ·") ? 4 : (id > 10 && id % 2 != 0 ? 1 : 0);

        public GameObject GetItemPrefab(int itemType)
        {
            switch (itemType)
            {
                case 0: return _itemPrefab.gameObject;
                case 1: return _alternateItemPrefab.gameObject;
                case 2: return _groupPrefab.gameObject;
                case 3: return _nestedGroupPrefab.gameObject;
                case 4: return _alternateItemPrefab.gameObject;
                default: throw new System.ArgumentOutOfRangeException(nameof(itemType));
            }
        }

        public float GetItemHeight(int itemType) =>
            itemType == 4 ? 80f : itemType >= 2 ? 20f : itemType == 1 ? 40f : 32f;

        public void Bind(GameObject view, int id, string item, VisibleRow<int> row)
        {
            view.GetComponent<ScrollViewTreeSampleRow>().Bind(id, item, row, Toggle, _groupIds.Contains(id));
        }

        private void Toggle(int id)
        {
            if (_showcaseCancellation != null)
            {
                StopShowcase();
                SetCaption("Manual control · Restart Showcase to replay");
            }

            if (_tree.GetChildCount(id) > 0)
                _view.Apply(_tree.BeginUpdate().SetExpanded(id, !_tree.IsExpanded(id)).Commit());
        }

        [ContextMenu("Restart Showcase")]
        public void RestartShowcase()
        {
            if (!Application.isPlaying || _view == null) return;
            StopShowcase();
            _tree.LoadSnapshot(ScrollViewShowcaseSequence.InitialSnapshot());
            _view.Reload();
            if (_animateChanges) StartShowcase();
            else SetCaption("Manual control");
        }

        private void OnDisable() => StopShowcase();
        private void OnDestroy() => StopShowcase();

        private void StopShowcase()
        {
            var cancellation = _showcaseCancellation;
            _showcaseCancellation = null;
            cancellation?.Cancel();
        }

        // Unity event entry points cannot await a Task; observe cancellation and failures here.
        private async void StartShowcase()
        {
            StopShowcase();
            var cancellation = new CancellationTokenSource();
            _showcaseCancellation = cancellation;
            try
            {
                await DemonstrateChanges(cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
            finally
            {
                if (ReferenceEquals(_showcaseCancellation, cancellation))
                    _showcaseCancellation = null;
                cancellation.Dispose();
            }
        }

        private void SetCaption(string text)
        {
            if (_caption != null) _caption.text = text;
        }

        private async Task DemonstrateChanges(CancellationToken cancellationToken)
        {
            SetCaption("TreeMotion · Animated hierarchy");
            await Task.Delay(TimeSpan.FromSeconds(_openingHold), cancellationToken);
            while (true)
            {
                for (var step = 0; step < ScrollViewShowcaseSequence.Captions.Length; step++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SetCaption(ScrollViewShowcaseSequence.Captions[step]);
                    await Task.Yield();
                    await _view.ApplyAsync(ScrollViewShowcaseSequence.ApplyStep(_tree, step), cancellationToken);
                    // Finish the transition before holding a readable, settled frame.
                    await Task.Delay(TimeSpan.FromSeconds(_beatDuration), cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                SetCaption("TreeMotion · Animated hierarchy");
                await Task.Delay(TimeSpan.FromSeconds(_loopHold), cancellationToken);
            }
        }
    }
}

using System;
using UnityEngine;

namespace TreeMotion
{
    [Serializable]
    public struct TreeMotionPadding
    {
        public float left, right, top, bottom;

        public TreeMotionPadding(float left, float right, float top, float bottom)
        {
            this.left = left;
            this.right = right;
            this.top = top;
            this.bottom = bottom;
        }
    }

    public class TreeMotionGroupView : MonoBehaviour
    {
        [SerializeField] private RectTransform _childrenFrame;
        [SerializeField, Min(0f)] private float _childrenSpacing = 3f;
        [SerializeField] private TreeMotionPadding _childrenPadding = new TreeMotionPadding(12, 12, 0, 11);
        public RectTransform ChildrenFrame => _childrenFrame;
        public float ChildrenSpacing => _childrenSpacing;
        public TreeMotionPadding ChildrenPadding => _childrenPadding;
        public RectTransform RectTransform => (RectTransform)transform;

        internal TreeMotionPadding FrameInsets => _childrenFrame == RectTransform
            ? default
            : new TreeMotionPadding(_childrenFrame.offsetMin.x, -_childrenFrame.offsetMax.x,
                -_childrenFrame.offsetMax.y, _childrenFrame.offsetMin.y);

        public void Configure(RectTransform childrenFrame, float childrenSpacing, TreeMotionPadding childrenPadding)
        {
            _childrenFrame = childrenFrame;
            _childrenSpacing = childrenSpacing;
            _childrenPadding = childrenPadding;
        }

        internal void SetGeometry(float totalHeight)
        {
            if (_childrenFrame == RectTransform) return;
            var insets = FrameInsets;
            var active = totalHeight > insets.top + insets.bottom + 0.001f;
            if (_childrenFrame.gameObject.activeSelf != active) _childrenFrame.gameObject.SetActive(active);
        }

        internal void ValidatePrefab(bool root = false)
        {
            if (!(transform is RectTransform) || _childrenFrame == null)
                throw new InvalidOperationException("Group requires RectTransform and ChildrenFrame.");
            if (root
                    ? _childrenFrame != RectTransform
                    : _childrenFrame == RectTransform || _childrenFrame.parent != transform)
                throw new InvalidOperationException(
                    "RootGroup uses Content itself; other Groups require a direct child ChildrenFrame.");
            if (!root && (_childrenFrame.anchorMin != Vector2.zero || _childrenFrame.anchorMax != Vector2.one))
                throw new InvalidOperationException("ChildrenFrame must stretch on both axes with nonnegative insets.");
            ValidatePadding(FrameInsets);
            ValidatePadding(_childrenPadding);
            TreeMotionScrollView.ValidateNumber(_childrenSpacing, "ChildrenSpacing");
            ValidateRect(RectTransform);
            ValidateRect(_childrenFrame);
            if (_childrenFrame.GetComponentInChildren<UnityEngine.UI.RectMask2D>(true) != null ||
                _childrenFrame.GetComponentInChildren<UnityEngine.UI.Mask>(true) != null)
                throw new InvalidOperationException("ChildrenFrame cannot mask sibling Views; use the Viewport mask.");
        }

        private static void ValidatePadding(TreeMotionPadding p)
        {
            TreeMotionScrollView.ValidateNumber(p.left, "Padding.Left");
            TreeMotionScrollView.ValidateNumber(p.right, "Padding.Right");
            TreeMotionScrollView.ValidateNumber(p.top, "Padding.Top");
            TreeMotionScrollView.ValidateNumber(p.bottom, "Padding.Bottom");
        }

        private static void ValidateRect(RectTransform rect)
        {
            if (rect.GetComponent<UnityEngine.UI.ContentSizeFitter>() != null ||
                rect.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
                throw new InvalidOperationException(
                    "Group geometry is managed by TreeMotion; remove LayoutGroup and ContentSizeFitter.");
        }
    }
}

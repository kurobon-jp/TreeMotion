using UnityEngine;

namespace TreeMotion
{
    /// <summary>Maps tree items to prefabs, layout sizes, and bound UI views.</summary>
    public interface ITreeMotionAdapter<TId, TItem>
    {
        GameObject GetItemPrefab(TId id, TItem item);
        /// <summary>Size along the layout axis for a leaf node. Not called for Group prefabs.</summary>
        float GetItemSize(TId id, TItem item);
        void Bind(GameObject go, TId id, TItem item, VisibleRow<TId> row);
    }

    public interface ITreeMotionPresentationHandler
    {
        void ResetPresentation();
        void ApplyPresentation(in TreeMotionPresentation presentation);
    }
}

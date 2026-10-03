using UnityEngine;

namespace TreeMotion
{
    public interface ITreeMotionDataSource<TId, TItem>
    {
        int GetItemType(TId id, TItem item);
        GameObject GetItemPrefab(int itemType);
        float GetItemHeight(int itemType);
        void Bind(GameObject view, TId id, TItem item, VisibleRow<TId> row);
    }

    public interface ITreeMotionPresentationHandler
    {
        void ResetPresentation();
        void SetTreeMotionPresentation(in TreeMotionPresentation presentation);
    }
}

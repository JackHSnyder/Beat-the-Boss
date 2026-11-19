using UnityEngine;
using UnityEngine.EventSystems;

namespace BeatTheBoss.Poses
{
    // One persistent hit surface supports both desktop pointers and Meta PointableCanvas pointers.
    public sealed class BossTimelineSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        internal BossTimelinePanel owner;
        private BossController.Cue dragging;
        private float offset, proposed;
        private float TimeAt(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out Vector2 local);
            return owner.ViewStart + local.x / owner.PixelsPerSecond;
        }
        public void OnPointerDown(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out Vector2 local);
            float time = TimeAt(e);
            int lane = Mathf.FloorToInt((-local.y - BossTimelinePanel.LaneTop) / BossTimelinePanel.LaneHeight);
            dragging = owner.SelectAt(time, lane);
            if (dragging != null)
            {
                offset = time - dragging.time; proposed = dragging.time;
            }
        }
        public void OnDrag(PointerEventData e)
        {
            if (dragging == null) owner.Workbench.Seek(owner.Workbench.Snapped(TimeAt(e)));
            else { proposed = owner.Workbench.Snapped(TimeAt(e) - offset); owner.ShowDrag(dragging, proposed); }
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (dragging != null && !Mathf.Approximately(proposed, dragging.time)) owner.Workbench.MoveSelected(proposed);
            dragging = null; owner.Redraw();
        }
    }
}

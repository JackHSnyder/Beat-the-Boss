using UnityEngine;
using UnityEngine.EventSystems;

// Keep a small sideways hand movement a button press. Forward deliberate drags to the list.
// Being the button's drag handler prevents the input module from cancelling its click prematurely.
public sealed class LevelListItemInput : MonoBehaviour, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private const float ScrollDistance = .015f;
    private UnityEngine.UI.ScrollRect scroll;
    private Vector3 pressPoint;
    private bool scrolling;

    public void Initialize(UnityEngine.UI.ScrollRect list) => scroll=list;

    public void OnInitializePotentialDrag(PointerEventData data)
    {
        scrolling=false;
        if (scroll==null) return;
        pressPoint=SurfacePoint(data);
        if (scroll.isActiveAndEnabled) scroll.OnInitializePotentialDrag(data);
    }
    public void OnBeginDrag(PointerEventData data) { }
    public void OnDrag(PointerEventData data)
    {
        if (scroll==null || !scroll.isActiveAndEnabled) return;
        if (!scrolling)
        {
            if (Vector3.Distance(pressPoint,SurfacePoint(data))<ScrollDistance) return;
            scrolling=true;
            data.eligibleForClick=false;
            ExecuteEvents.Execute(gameObject,data,ExecuteEvents.pointerUpHandler);
            Vector2 position=data.position;
            data.position=data.pressPosition;
            scroll.OnBeginDrag(data);
            data.position=position;
        }
        scroll.OnDrag(data);
    }
    public void OnEndDrag(PointerEventData data)
    {
        if (scrolling && scroll!=null && scroll.isActiveAndEnabled) scroll.OnEndDrag(data);
        scrolling=false;
    }
    private Vector3 SurfacePoint(PointerEventData data)
    {
        RectTransformUtility.ScreenPointToWorldPointInRectangle(scroll.viewport,data.position,data.pressEventCamera,out Vector3 point);
        return point;
    }
    private void OnDisable() => scrolling=false;
}

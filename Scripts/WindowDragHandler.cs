using UnityEngine;
using UnityEngine.EventSystems;

namespace SearchAnything
{
    /// <summary>
    /// Makes the Search Anything window relocatable: attached to the window's
    /// header bar, it drags the target window around the canvas.
    /// </summary>
    public sealed class WindowDragHandler : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public RectTransform Target;
        public Canvas Canvas;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Target != null)
                Target.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Target == null)
                return;

            float scale = Canvas != null ? Canvas.scaleFactor : 1f;
            if (scale <= 0f)
                scale = 1f;

            Target.anchoredPosition += eventData.delta / scale;
        }
    }
}

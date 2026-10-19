using UnityEngine;
using UnityEngine.EventSystems;

namespace ContinueGame
{
    internal sealed class ContinueButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal bool IsHovered { get; private set; }
        public void OnPointerEnter(PointerEventData eventData) { IsHovered = true; }
        public void OnPointerExit(PointerEventData eventData) { IsHovered = false; }
        private void OnDisable() { IsHovered = false; }
    }
}

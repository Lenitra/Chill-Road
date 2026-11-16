using UnityEngine;
using UnityEngine.EventSystems;

namespace NoHesi
{
    /// <summary>Survol à la souris = sélection (comme à la manette) : la liste suit le pointeur.</summary>
    public class HoverSelect : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData e)
        {
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ChillRoad
{
    /// <summary>
    /// Zone cliquable d'une ligne de réglage (un choix segmenté, une flèche de carrousel) : <see cref="Click"/> au clic
    /// gauche, <see cref="Hover"/> tant que la souris est dessus. Pas une Selectable : l'appui remonte à la ligne
    /// (<see cref="OptionSelector"/>), qui devient la ligne choisie.
    /// </summary>
    public class OptionHit : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Click;
        public bool Hover { get; private set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Click?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData) => Hover = true;
        public void OnPointerExit(PointerEventData eventData) => Hover = false;
        void OnDisable() => Hover = false;
    }
}

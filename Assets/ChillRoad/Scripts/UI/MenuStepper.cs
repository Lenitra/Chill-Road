using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Ligne de réglage à valeurs « ‹ valeur › » des menus construits en code : gauche/droite (manette, flèches du
    /// clavier) change la valeur, A / Entrée passe à la suivante ; à la souris, un clic sur la moitié gauche de la zone
    /// de valeur recule, ailleurs avance. Haut/bas suivent la navigation normale.
    /// </summary>
    public class MenuStepper : Selectable, IPointerClickHandler, ISubmitHandler
    {
        /// <summary>Appelé avec -1 (précédent) ou +1 (suivant).</summary>
        public Action<int> Step;
        /// <summary>Zone de la valeur (flèches comprises) : sert à choisir le sens d'un clic.</summary>
        public RectTransform valueArea;

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right)
            {
                if (IsInteractable()) Step?.Invoke(eventData.moveDir == MoveDirection.Left ? -1 : 1);
                eventData.Use();
                return;
            }
            base.OnMove(eventData);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
            int dir = 1;
            if (valueArea && RectTransformUtility.ScreenPointToLocalPointInRectangle(valueArea, eventData.position, eventData.pressEventCamera, out var local)
                && valueArea.rect.Contains(local))
                dir = local.x < valueArea.rect.center.x ? -1 : 1;
            Step?.Invoke(dir);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (IsInteractable()) Step?.Invoke(1);
        }
    }
}

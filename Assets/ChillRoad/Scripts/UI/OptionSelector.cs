using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChillRoad
{
    /// <summary>
    /// Ligne de réglage à choix des menus construits en code (interrupteur, choix segmentés, carrousel) : gauche/droite
    /// (manette, flèches du clavier) appelle <see cref="Move"/> avec -1 ou +1, A / Entrée appelle <see cref="Submit"/>,
    /// un clic sur la ligne hors d'une zone cliquable (<see cref="OptionHit"/>) appelle <see cref="Click"/>.
    /// Haut/bas suivent la navigation normale.
    /// </summary>
    public class OptionSelector : Selectable, ISubmitHandler, IPointerClickHandler
    {
        public Action<int> Move;
        public Action Submit;
        public Action Click;

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right)
            {
                if (IsInteractable()) Move?.Invoke(eventData.moveDir == MoveDirection.Left ? -1 : 1);
                eventData.Use();
                return;
            }
            base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (IsInteractable()) Submit?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && IsInteractable()) Click?.Invoke();
        }
    }
}

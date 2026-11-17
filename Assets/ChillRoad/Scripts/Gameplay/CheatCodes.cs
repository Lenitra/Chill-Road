using UnityEngine;
using UnityEngine.InputSystem;

namespace ChillRoad
{
    /// <summary>
    /// Codes de triche tapés au clavier pendant la partie (les lettres saisies, selon la disposition du clavier).
    /// « qzdsqzdsq » : +10 000 $ dans le portefeuille.
    /// </summary>
    public class CheatCodes : MonoBehaviour
    {
        const string MoneyCode = "qzdsqzdsq";
        const int MoneyAmount = 10000;

        GameManager game;
        Keyboard keyboard;
        string typed = "";

        public void Init(GameManager gm) => game = gm;

        void Update()
        {
            // Clavier branché, débranché ou remplacé : on suit le clavier courant
            if (Keyboard.current == keyboard) return;
            Unhook();
            keyboard = Keyboard.current;
            if (keyboard != null) keyboard.onTextInput += OnText;
        }

        void OnDisable() => Unhook();

        void Unhook()
        {
            if (keyboard != null) keyboard.onTextInput -= OnText;
            keyboard = null;
        }

        void OnText(char c)
        {
            if (!game || game.State != GameManager.Phase.Playing) { typed = ""; return; }
            typed += char.ToLowerInvariant(c);
            if (typed.Length > MoneyCode.Length) typed = typed.Substring(typed.Length - MoneyCode.Length);
            if (typed != MoneyCode) return;
            typed = "";
            Wallet.Add(MoneyAmount);
            if (game.Local != null && game.Local.Audio) game.Local.Audio.Chime();
        }
    }
}

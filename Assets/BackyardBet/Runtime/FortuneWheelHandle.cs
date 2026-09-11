using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Сам диск колеса в сцене: за него игрок и хватается.
    ///
    /// Обычный объект без сетевых компонентов - состояние и результат держит
    /// FortuneWheel на GameRoot. Сетевые объекты, расставленные в сцене
    /// заранее, у нас уже оживали ненадёжно, поэтому вся логика живёт на
    /// созданном в игре GameRoot, а здесь только точка взаимодействия.
    /// </summary>
    public class FortuneWheelHandle : MonoBehaviour, IInteractable
    {
        public float Range => 3.5f;

        public string Prompt(PlayerInteraction who)
        {
            var w = FortuneWheel.Instance;
            return (w != null && w.Spinning) ? "" : "крутить колесо";
        }

        public bool CanInteract(PlayerInteraction who)
        {
            var w = FortuneWheel.Instance;
            return w != null && !w.Spinning;
        }
    }
}

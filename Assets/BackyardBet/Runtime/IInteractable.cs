namespace BackyardBet
{
    /// <summary>
    /// Всё, с чем можно взаимодействовать по клавише E.
    ///
    /// Интерфейс, а не базовый класс: дверь - обычный MonoBehaviour (её
    /// состояние держит WorldState), а поднимаемый предмет - NetworkBehaviour
    /// (ему нужна своя сетевая позиция). Наследоваться от одного общего
    /// предка они не могут, а вести себя одинаково для игрока - должны.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Подсказка под прицелом. Пустая строка - взаимодействие скрыто.</summary>
        string Prompt(PlayerInteraction who);

        /// <summary>Можно ли сейчас: не занято, не в руках у другого.</summary>
        bool CanInteract(PlayerInteraction who);

        /// <summary>С какого расстояния можно дотянуться, м.</summary>
        float Range { get; }
    }
}

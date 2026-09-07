using UnityEngine;

namespace BackyardBet
{
    /// <summary>
    /// Сам стол: подойти и нажать E, чтобы сесть играть в блеф.
    /// Обычный MonoBehaviour - вся логика партии живёт в BluffTable на
    /// GameRoot, здесь только точка входа для игрока.
    /// </summary>
    public class BluffTableSeat : MonoBehaviour, IInteractable
    {
        public float Range => 3.5f;

        public string Prompt(PlayerInteraction who) => "сесть за стол";

        public bool CanInteract(PlayerInteraction who)
        {
            var table = BluffTable.Instance;
            if (table == null) return false;
            return !table.IsSeated(who.OwnerClientId);
        }
    }
}

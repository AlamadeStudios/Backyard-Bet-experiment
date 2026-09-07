using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Модель игрока. Камера стоит на уровне глаз, то есть внутри головы -
    /// поэтому владельцу голову и всё, что на ней, прячем: он видит своё тело
    /// и руки, если посмотрит вниз, но не изнанку собственного лица.
    /// Остальные игроки видят персонажа целиком.
    /// </summary>
    public class PlayerAvatar : NetworkBehaviour
    {
        static readonly string[] HeadParts =
        {
            "_head", "_hair", "_cap", "_brim", "_eye", "_brow",
            "_mouth", "_teeth", "_spike", "_ponytail"
        };

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                foreach (var part in HeadParts)
                {
                    if (n.Contains(part)) { r.enabled = false; break; }
                }
            }
        }
    }
}

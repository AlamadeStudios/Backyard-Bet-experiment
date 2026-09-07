using Unity.Netcode.Components;

namespace BackyardBet
{
    /// <summary>
    /// Перемещение игрока авторитетно у владельца, а не у сервера: движение
    /// без задержки на своём экране. Читерство в ходьбе для пати-игры не
    /// критично, а вот состояние карт и костей останется строго за хостом.
    /// </summary>
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}

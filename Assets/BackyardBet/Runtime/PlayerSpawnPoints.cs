using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Разводит подключившихся игроков по местам вокруг стола.
    ///
    /// Ориентиры - пустышки Seat_*, оставленные на местах декоративных
    /// персонажей при досборке сцены. Игрока ставим на шаг наружу от места
    /// и разворачиваем лицом к столу, чтобы он не появлялся в самом столе.
    /// </summary>
    public class PlayerSpawnPoints : MonoBehaviour
    {
        [Tooltip("На сколько метров отступить от места наружу.")]
        public float outwardOffset = 1.4f;

        [Tooltip("С какой высоты искать пол под точкой спавна.")]
        public float groundProbeHeight = 4f;

        int _taken;
        Transform _tableCenter;
        Transform[] _seats;

        void Awake()
        {
            // Мир и страховочный пол ставим сами, не требуя ручных шагов в
            // редакторе: правки сцены терялись, когда редактор держал её
            // открытой, и игра запускалась с пустым миром.
            if (FindAnyObjectByType<WorldBuilder>() == null)
                gameObject.AddComponent<WorldBuilder>();
            if (GetComponent<GroundGuard>() == null) gameObject.AddComponent<GroundGuard>();

            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback += PlaceAtFreeSeat;
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= PlaceAtFreeSeat;
        }

        Transform[] Seats()
        {
            if (_seats != null && _seats.Length > 0) return _seats;

            var found = new System.Collections.Generic.List<Transform>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Seat_")) found.Add(t);
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _seats = found.ToArray();
            return _seats;
        }

        void PlaceAtFreeSeat(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsServer) return;                    // расстановка - дело хоста
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client)) return;
            var player = client.PlayerObject;
            if (player == null) return;

            var seats = Seats();
            if (seats.Length == 0) return;

            var marker = seats[_taken % seats.Length];
            _taken++;

            if (_tableCenter == null)
            {
                var top = GameObject.Find("TableTop");
                if (top != null) _tableCenter = top.transform;
            }

            Vector3 seat = marker.position;
            Vector3 outward = _tableCenter != null
                ? (seat - _tableCenter.position) : marker.forward;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.back;

            Vector3 pos = DropToGround(seat + outward * outwardOffset);
            var rot = Quaternion.LookRotation(-outward, Vector3.up);

            // CharacterController игнорирует прямую установку transform, пока включён
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(pos, rot);
            if (cc != null) cc.enabled = true;
        }

        /// <summary>Точка, куда вернуть игрока, если он всё же провалился.</summary>
        public Vector3 SafeSpot()
        {
            var seats = Seats();
            Vector3 p = seats.Length > 0 ? seats[0].position : Vector3.zero;
            return DropToGround(p + Vector3.up * 2f);
        }

        /// <summary>Опустить точку на реальный пол, чтобы не спавниться в воздухе или в земле.</summary>
        Vector3 DropToGround(Vector3 pos)
        {
            var from = pos + Vector3.up * groundProbeHeight;
            if (Physics.Raycast(from, Vector3.down, out var hit, groundProbeHeight * 3f,
                                ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.15f;
            return pos + Vector3.up * 0.15f;
        }
    }
}

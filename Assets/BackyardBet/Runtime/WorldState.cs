using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Состояние мира, общее для всех: какие двери открыты.
    ///
    /// Живёт на GameRoot, который хост создаёт при старте игры. Двери в
    /// сцене - обычные объекты без сетевых компонентов, они лишь получают
    /// от сюда команду "ты открыта".
    ///
    /// Порядок дверей вычисляется одинаково на всех машинах (сортировка по
    /// имени и позиции), поэтому номер двери у хоста и у клиента совпадает.
    /// Все двери влезают в один uint - 32 штуки хватит с запасом.
    /// </summary>
    public class WorldState : NetworkBehaviour
    {
        public static WorldState Instance { get; private set; }

        readonly NetworkVariable<uint> _doorMask = new NetworkVariable<uint>(0);

        readonly List<DoorInteractable> _doors = new List<DoorInteractable>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            CollectDoors();
            _doorMask.OnValueChanged += (_, mask) => ApplyMask(mask);
            ApplyMask(_doorMask.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Собрать двери сцены в одинаковом на всех машинах порядке.
        /// Позицию округляем: она приходит из одного и того же файла карты,
        /// но сравнивать float как строку безопаснее с фиксированной точностью.
        /// </summary>
        void CollectDoors()
        {
            _doors.Clear();
            _doors.AddRange(FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None));
            _doors.Sort((a, b) => string.CompareOrdinal(SortKey(a), SortKey(b)));

            for (int i = 0; i < _doors.Count; i++) _doors[i].doorIndex = i;
            Debug.Log("[Backyard Bet] Дверей в мире: " + _doors.Count);
        }

        static string SortKey(DoorInteractable d)
        {
            Vector3 p = d.transform.position;
            return string.Format("{0}|{1:F2}|{2:F2}|{3:F2}", d.name, p.x, p.y, p.z);
        }

        /// <summary>Переключить дверь. Только на хосте.</summary>
        public void ServerToggleDoor(int index)
        {
            if (!IsServer) return;
            if (index < 0 || index >= _doors.Count || index >= 32) return;
            _doorMask.Value ^= 1u << index;
        }

        void ApplyMask(uint mask)
        {
            for (int i = 0; i < _doors.Count && i < 32; i++)
                _doors[i].SetOpen((mask & (1u << i)) != 0);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>Кто держит предмет: индекс предмета и владелец (clientId + 1).</summary>
    public struct PropHold : INetworkSerializable, IEquatable<PropHold>
    {
        public int index;
        public ulong holderPlusOne;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref index);
            s.SerializeValue(ref holderPlusOne);
        }

        public bool Equals(PropHold o) => index == o.index && holderPlusOne == o.holderPlusOne;
    }

    /// <summary>
    /// Физика и сеть для всего мелкого реквизита.
    ///
    /// Предметы в сцене - обычные объекты без сетевых компонентов. Считает
    /// их только хост, а клиентам рассылаются позиции тех, что реально
    /// двигались: гнать все сорок штук каждый кадр незачем, почти все они
    /// лежат неподвижно.
    /// </summary>
    public class PropNetwork : NetworkBehaviour
    {
        public static PropNetwork Instance { get; private set; }

        [Tooltip("Сколько раз в секунду рассылать позиции двигающихся предметов.")]
        public float syncRate = 15f;

        [Tooltip("Насколько предмет должен сдвинуться, чтобы его отправили.")]
        public float moveEpsilon = 0.01f;

        readonly List<PickupInteractable> _props = new List<PickupInteractable>();
        readonly NetworkList<PropHold> _holds = new NetworkList<PropHold>();

        Vector3[] _lastPos;
        Quaternion[] _lastRot;
        float _nextSync;

        public int Count => _props.Count;

        public PickupInteractable Prop(int index) =>
            index >= 0 && index < _props.Count ? _props[index] : null;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Collect();
            _holds.OnListChanged += _ => ApplyHolds();
            ApplyHolds();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Собрать предметы в одинаковом на всех машинах порядке - индекс
        /// предмета у хоста и у клиента должен совпадать.
        /// </summary>
        void Collect()
        {
            _props.Clear();
            _props.AddRange(FindObjectsByType<PickupInteractable>(FindObjectsSortMode.None));
            _props.Sort((a, b) => string.CompareOrdinal(SortKey(a), SortKey(b)));

            _lastPos = new Vector3[_props.Count];
            _lastRot = new Quaternion[_props.Count];

            for (int i = 0; i < _props.Count; i++)
            {
                _props[i].propIndex = i;
                _lastPos[i] = _props[i].transform.position;
                _lastRot[i] = _props[i].transform.rotation;

                // физику считает только хост: у клиента предмет иначе будет
                // дёргаться между своей симуляцией и присланной позицией
                if (!IsServer) _props[i].Body.isKinematic = true;
            }
            Debug.Log("[Backyard Bet] Предметов в мире: " + _props.Count);
        }

        static string SortKey(PickupInteractable p)
        {
            Vector3 v = p.transform.position;
            return string.Format("{0}|{1:F2}|{2:F2}|{3:F2}", p.name, v.x, v.y, v.z);
        }

        // ------------------------------------------------------------ владение

        void ApplyHolds()
        {
            foreach (var p in _props) p.HolderPlusOne = 0;
            for (int i = 0; i < _holds.Count; i++)
            {
                var h = _holds[i];
                var p = Prop(h.index);
                if (p != null) p.HolderPlusOne = h.holderPlusOne;
            }
        }

        int HoldSlot(int index)
        {
            for (int i = 0; i < _holds.Count; i++)
                if (_holds[i].index == index) return i;
            return -1;
        }

        /// <summary>Взять предмет. Только на хосте.</summary>
        public void ServerPickUp(int index, PlayerInteraction who)
        {
            if (!IsServer) return;
            var p = Prop(index);
            if (p == null || p.Held || who == null) return;
            if (who.HeldProp >= 0) return;                  // рука занята

            // хост перепроверяет расстояние: клиент мог соврать
            if (Vector3.Distance(who.transform.position, p.transform.position) > p.Range + 2.5f)
                return;

            if (HoldSlot(index) < 0)
                _holds.Add(new PropHold { index = index, holderPlusOne = who.OwnerClientId + 1 });

            p.Body.isKinematic = true;
            p.Body.detectCollisions = false;
            who.SetHeldProp(index);
        }

        /// <summary>Бросить предмет. Только на хосте.</summary>
        public void ServerThrow(int index, Vector3 direction, PlayerInteraction who)
        {
            if (!IsServer) return;
            var p = Prop(index);
            if (p == null || who == null) return;
            if (p.HolderPlusOne != who.OwnerClientId + 1) return;

            int slot = HoldSlot(index);
            if (slot >= 0) _holds.RemoveAt(slot);

            p.LastThrower = who.OwnerClientId;
            p.WasThrown = true;
            p.Body.isKinematic = false;
            p.Body.detectCollisions = true;
            p.Body.linearVelocity = direction.normalized * p.throwForce;
            p.Body.angularVelocity = Vector3.Cross(direction.normalized, Vector3.up) * p.throwSpin;

            who.SetHeldProp(-1);
        }

        /// <summary>Вернуть предмет на место - чтобы бросать снова, не бегая за ним.</summary>
        public void ServerReturnHome(int index)
        {
            if (!IsServer) return;
            var p = Prop(index);
            if (p == null) return;

            p.WasThrown = false;
            p.Body.isKinematic = false;
            p.Body.detectCollisions = true;
            p.Body.linearVelocity = Vector3.zero;
            p.Body.angularVelocity = Vector3.zero;
            p.transform.SetPositionAndRotation(p.HomePos, p.HomeRot);
        }

        // ------------------------------------------------------------ синхронизация

        void FixedUpdate()
        {
            if (!IsServer) return;

            // предметы в руках едут за точкой в руке владельца
            for (int i = 0; i < _holds.Count; i++)
            {
                var h = _holds[i];
                var p = Prop(h.index);
                if (p == null) continue;

                var hand = HandOf(h.holderPlusOne - 1);
                if (hand != null) p.transform.SetPositionAndRotation(hand.position, hand.rotation);
            }
        }

        Transform HandOf(ulong clientId)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var c)) return null;
            var obj = c.PlayerObject;
            if (obj == null) return null;
            var pi = obj.GetComponent<PlayerInteraction>();
            return pi != null ? pi.HandPoint : null;
        }

        void Update()
        {
            if (!IsServer || _props.Count == 0) return;
            if (Time.time < _nextSync) return;
            _nextSync = Time.time + 1f / Mathf.Max(1f, syncRate);

            // шлём только то, что реально сдвинулось
            var idx = new List<int>();
            var pos = new List<Vector3>();
            var rot = new List<Quaternion>();

            for (int i = 0; i < _props.Count; i++)
            {
                var t = _props[i].transform;
                if ((t.position - _lastPos[i]).sqrMagnitude < moveEpsilon * moveEpsilon &&
                    Quaternion.Angle(t.rotation, _lastRot[i]) < 1.5f)
                    continue;

                _lastPos[i] = t.position;
                _lastRot[i] = t.rotation;
                idx.Add(i);
                pos.Add(t.position);
                rot.Add(t.rotation);
            }

            if (idx.Count == 0) return;
            SyncClientRpc(idx.ToArray(), pos.ToArray(), rot.ToArray());
        }

        [ClientRpc]
        void SyncClientRpc(int[] indices, Vector3[] positions, Quaternion[] rotations)
        {
            if (IsServer) return;                      // у хоста это и есть источник
            for (int i = 0; i < indices.Length; i++)
            {
                var p = Prop(indices[i]);
                if (p != null) p.transform.SetPositionAndRotation(positions[i], rotations[i]);
            }
        }
    }
}

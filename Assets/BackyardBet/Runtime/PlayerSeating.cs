using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Посадка за стол: игрок пересаживается на своё место, ходьба
    /// отключается, курсор освобождается под интерфейс игры.
    ///
    /// Это и есть переключение «из мира во что-то отдельное»: пока сидишь,
    /// ты не бегаешь по двору, а играешь в карты.
    ///
    /// Двигает себя сам владелец, а не хост: перемещение игрока у нас
    /// авторитетно у владельца (ClientNetworkTransform), и правка с сервера
    /// до остальных просто не доехала бы.
    /// </summary>
    public class PlayerSeating : NetworkBehaviour
    {
        [Tooltip("Высота глаз сидящего, м.")]
        public float seatedEyeHeight = 1.62f;

        [Tooltip("Обзор за столом, градусов. Уже обычного - двор уходит из кадра.")]
        public float seatedFov = 38f;

        public bool Seated { get; private set; }

        NetworkPlayerMovement _move;
        Vector3 _standEye;
        Vector3 _standPos;
        Quaternion _standRot;
        float _standFov = 60f;

        void Awake() => _move = GetComponent<NetworkPlayerMovement>();

        /// <summary>Вызывает стол на хосте: усадить игрока на место seatIndex.</summary>
        public void ServerSeat(int seatIndex)
        {
            if (!IsServer) return;
            SeatClientRpc(seatIndex, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            });
        }

        [ClientRpc]
        void SeatClientRpc(int seatIndex, ClientRpcParams p = default)
        {
            if (!IsOwner) return;
            SitDown(seatIndex);
        }

        void SitDown(int seatIndex)
        {
            var seat = FindSeat(seatIndex);
            if (seat == null)
            {
                Debug.LogWarning("[Backyard Bet] Нет места " + seatIndex + " - садиться некуда.");
                return;
            }

            _standPos = transform.position;
            _standRot = transform.rotation;

            var table = GameObject.Find("TableTop");
            Vector3 look = table != null ? table.transform.position - seat.position : seat.forward;
            look.y = 0f;

            // CharacterController не даст переставить себя, пока включён
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.SetPositionAndRotation(
                seat.position,
                look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look, Vector3.up)
                                           : seat.rotation);
            if (cc != null) cc.enabled = true;

            FocusOnTable(table);

            Seated = true;
            if (_move != null) _move.enabled = false;   // ходьба и обзор выключены
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// Взять стол крупно: опустить глаза на высоту сидящего, наклонить
        /// взгляд на столешницу и сузить обзор.
        ///
        /// Без этого сидящий смотрит прямо перед собой и видит весь двор,
        /// а стол занимает нижнюю четверть экрана - играть в карты так
        /// невозможно. Сужение обзора убирает из кадра двор без всяких
        /// затемнений и шторок.
        /// </summary>
        void FocusOnTable(GameObject table)
        {
            if (_move == null || _move.cameraPivot == null) return;

            _standEye = _move.cameraPivot.localPosition;
            var eye = _standEye;
            eye.y = seatedEyeHeight;
            _move.cameraPivot.localPosition = eye;

            float pitch = 0f;
            if (table != null)
            {
                Vector3 eyeWorld = _move.cameraPivot.position;
                Vector3 toTable = table.transform.position - eyeWorld;
                float flat = new Vector2(toTable.x, toTable.z).magnitude;
                if (flat > 0.01f) pitch = Mathf.Atan2(-toTable.y, flat) * Mathf.Rad2Deg;
            }
            _move.cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            var cam = _move.cameraPivot.GetComponentInChildren<Camera>(true);
            if (cam != null)
            {
                _standFov = cam.fieldOfView;
                cam.fieldOfView = seatedFov;
            }
        }

        /// <summary>Встать из-за стола и вернуться к обычному управлению.</summary>
        public void StandUp()
        {
            if (!Seated) return;
            Seated = false;

            if (_move != null)
            {
                if (_move.cameraPivot != null)
                {
                    _move.cameraPivot.localPosition = _standEye;
                    _move.cameraPivot.localRotation = Quaternion.identity;

                    var cam = _move.cameraPivot.GetComponentInChildren<Camera>(true);
                    if (cam != null) cam.fieldOfView = _standFov;
                }
                _move.enabled = true;
            }

            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.SetPositionAndRotation(_standPos, _standRot);
            if (cc != null) cc.enabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (IsOwner) LeaveTableServerRpc();
        }

        [ServerRpc]
        void LeaveTableServerRpc()
        {
            if (BluffTable.Instance != null) BluffTable.Instance.ServerLeave(OwnerClientId);
        }

        void Update()
        {
            if (!IsOwner || !Seated) return;
            if (Input.GetKeyDown(KeyCode.Escape)) StandUp();
        }

        static Transform FindSeat(int index)
        {
            var found = new System.Collections.Generic.List<Transform>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Seat_")) found.Add(t);
            if (found.Count == 0) return null;

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found[index % found.Count];
        }
    }
}

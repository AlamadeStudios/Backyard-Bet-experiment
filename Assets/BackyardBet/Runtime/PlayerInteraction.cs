using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Взаимодействие игрока с миром: смотрит лучом из камеры, показывает
    /// подсказку, по E просит хост выполнить действие, по ЛКМ бросает то,
    /// что в руке.
    ///
    /// Клиент никогда не меняет мир сам - только шлёт запрос с номером
    /// двери или предмета. Хост проверяет расстояние и решает.
    /// </summary>
    public class PlayerInteraction : NetworkBehaviour
    {
        [Tooltip("Дальность луча взаимодействия, м.")]
        public float reach = 3.5f;

        [Tooltip("Откуда смотрим - обычно камера игрока.")]
        public Transform aim;

        [Tooltip("Где предмет висит в руке.")]
        public Transform handPoint;

        public Transform HandPoint => handPoint;

        /// <summary>Номер предмета в руке, -1 если рука пуста.</summary>
        public int HeldProp { get; private set; } = -1;

        IInteractable _target;
        string _prompt;
        readonly RaycastHit[] _hits = new RaycastHit[12];

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) { enabled = false; return; }
            if (aim == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null) aim = cam.transform;
            }
        }

        void Update()
        {
            if (!IsOwner) return;

            ScanForTarget();

            if (Input.GetKeyDown(KeyCode.E) && _target != null) SendInteract();
            if (Input.GetMouseButtonDown(0) && HeldProp >= 0)
                ThrowServerRpc(HeldProp, aim != null ? aim.forward : transform.forward);
        }

        /// <summary>
        /// Ищем ближайшее под прицелом. Луч стартует внутри капсулы самого
        /// игрока, поэтому берём все попадания и отбрасываем свои - иначе
        /// взаимодействие упирается в собственный коллайдер.
        /// </summary>
        void ScanForTarget()
        {
            _target = null;
            _prompt = null;
            if (aim == null) return;

            int n = Physics.RaycastNonAlloc(aim.position, aim.forward, _hits, reach,
                                            ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider.transform.IsChildOf(transform)) continue;   // своё тело
                if (h.distance >= best) continue;

                var it = h.collider.GetComponentInParent<IInteractable>();
                if (it == null || !it.CanInteract(this)) continue;

                string p = it.Prompt(this);
                if (string.IsNullOrEmpty(p)) continue;

                best = h.distance;
                _target = it;
                _prompt = "E — " + p;
            }
        }

        void SendInteract()
        {
            switch (_target)
            {
                case DoorInteractable door:
                    ToggleDoorServerRpc(door.doorIndex);
                    break;
                case PickupInteractable item:
                    PickUpServerRpc(item.propIndex);
                    break;
                case BluffTableSeat _:
                    JoinTableServerRpc();
                    break;
            }
        }

        // ------------------------------------------------------------ сеть

        [ServerRpc]
        void ToggleDoorServerRpc(int doorIndex)
        {
            if (WorldState.Instance == null)
            {
                Debug.LogWarning("[Backyard Bet] Нет WorldState - дверь открыть некому.");
                return;
            }
            WorldState.Instance.ServerToggleDoor(doorIndex);
        }

        [ServerRpc]
        void PickUpServerRpc(int propIndex)
        {
            if (PropNetwork.Instance == null) return;
            PropNetwork.Instance.ServerPickUp(propIndex, this);
        }

        [ServerRpc]
        void ThrowServerRpc(int propIndex, Vector3 direction)
        {
            if (PropNetwork.Instance == null) return;
            PropNetwork.Instance.ServerThrow(propIndex, direction, this);
        }

        [ServerRpc]
        void JoinTableServerRpc()
        {
            if (BluffTable.Instance == null)
            {
                Debug.LogWarning("[Backyard Bet] Нет BluffTable - сесть некуда.");
                return;
            }
            BluffTable.Instance.ServerJoin(OwnerClientId);
        }

        /// <summary>Вызывает PropNetwork на хосте; владельцу дублируем адресно.</summary>
        public void SetHeldProp(int propIndex)
        {
            HeldProp = propIndex;
            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            SetHeldPropClientRpc(propIndex, target);
        }

        [ClientRpc]
        void SetHeldPropClientRpc(int propIndex, ClientRpcParams p = default)
        {
            if (IsServer) return;
            HeldProp = propIndex;
        }

        // ------------------------------------------------------------ интерфейс

        void OnGUI()
        {
            if (!IsOwner) return;

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (HeldProp >= 0)
                Label(new Rect(cx - 300f, Screen.height - 150f, 600f, 30f), "ЛКМ — бросить");

            if (!string.IsNullOrEmpty(_prompt))
                Label(new Rect(cx - 300f, Screen.height - 120f, 600f, 40f), _prompt);
        }

        internal static void Label(Rect r, string text)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = new Color(1f, 0.92f, 0.7f);
            GUI.Label(r, text, style);
        }
    }
}

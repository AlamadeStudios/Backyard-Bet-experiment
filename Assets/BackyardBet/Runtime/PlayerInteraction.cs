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

        [Tooltip("За сколько секунд замах набирает полную силу.")]
        public float chargeTime = 1.1f;

        public Transform HandPoint => handPoint;

        /// <summary>Номер предмета в руке, -1 если рука пуста.</summary>
        public int HeldProp { get; private set; } = -1;

        IInteractable _target;
        string _prompt;
        float _charge;                 // сколько держим кнопку броска, с
        bool _charging;
        PlayerSeating _seating;
        readonly RaycastHit[] _hits = new RaycastHit[12];

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) { enabled = false; return; }
            _seating = GetComponent<PlayerSeating>();
            if (aim == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null) aim = cam.transform;
            }
        }

        void Update()
        {
            if (!IsOwner) return;

            // За столом игрок занят картами: курсор свободен, прицел не
            // нужен, а нажатия не должны улетать в мир мимо интерфейса.
            if (_seating != null && _seating.Seated)
            {
                _target = null;
                _prompt = null;
                return;
            }

            ScanForTarget();

            if (Input.GetKeyDown(KeyCode.E) && _target != null) SendInteract();
            Charge();
        }

        /// <summary>
        /// Замах: держишь кнопку - копится сила, отпускаешь - бросок.
        ///
        /// Одним нажатием всё летело с одинаковой силой, и попасть во что-то
        /// конкретное было делом случая. Здесь силу выбирает игрок, а шкала
        /// под прицелом показывает, сколько набрано.
        /// </summary>
        void Charge()
        {
            if (HeldProp < 0)
            {
                _charging = false;
                _charge = 0f;
                return;
            }

            if (Input.GetMouseButtonDown(0)) { _charging = true; _charge = 0f; }
            if (_charging && Input.GetMouseButton(0))
                _charge = Mathf.Min(_charge + Time.deltaTime, chargeTime);

            if (!_charging || !Input.GetMouseButtonUp(0)) return;

            _charging = false;
            ThrowServerRpc(HeldProp, aim != null ? aim.forward : transform.forward,
                           chargeTime > 0f ? _charge / chargeTime : 1f);
            _charge = 0f;
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
                case FortuneWheelHandle _:
                    SpinWheelServerRpc();
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
        void SpinWheelServerRpc()
        {
            var wheel = FortuneWheel.Instance;
            if (wheel == null) return;

            // хост перепроверяет, что игрок действительно стоит у колеса
            var disc = GameObject.Find("FortuneWheel");
            if (disc != null &&
                Vector3.Distance(transform.position, disc.transform.position) > 6f) return;

            wheel.ServerSpin(OwnerClientId);
        }

        [ServerRpc]
        void ThrowServerRpc(int propIndex, Vector3 direction, float power)
        {
            if (PropNetwork.Instance == null) return;
            PropNetwork.Instance.ServerThrow(propIndex, direction, power, this);
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
            if (_seating != null && _seating.Seated) return;   // за столом рисует стол

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (HeldProp >= 0)
            {
                Label(new Rect(cx - 300f, Screen.height - 150f, 600f, 30f),
                      "ЛКМ (удерживай) — бросить");
                DrawCharge(cx, cy);
            }

            if (!string.IsNullOrEmpty(_prompt))
                Label(new Rect(cx - 300f, Screen.height - 120f, 600f, 40f), _prompt);
        }

        /// <summary>
        /// Шкала замаха под прицелом. Без неё сила броска - вслепую: пока
        /// предмет не улетел, игрок не знает, сколько набрал.
        /// </summary>
        void DrawCharge(float cx, float cy)
        {
            if (!_charging || chargeTime <= 0f) return;

            float k = Mathf.Clamp01(_charge / chargeTime);
            const float w = 220f, h = 10f;
            var back = new Rect(cx - w * 0.5f, cy + 40f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);

            // от жёлтого к красному: полный замах должен читаться без цифр
            GUI.color = Color.Lerp(new Color(1f, 0.85f, 0.3f),
                                   new Color(1f, 0.32f, 0.12f), k);
            GUI.DrawTexture(new Rect(back.x + 2f, back.y + 2f,
                                     (w - 4f) * k, h - 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
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

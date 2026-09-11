using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Колесо фортуны: подходи и крути на свой страх.
    ///
    /// Восемь секторов - четыре "0", три "2x", один "3x". Множитель
    /// применяется к текущему счёту игрока: выпал ноль - теряешь всё
    /// накопленное, выпал 3x - утраиваешь. Поэтому крутить всегда страшно,
    /// и в этом вся суть механики.
    ///
    /// Сектор выбирает ХОСТ и рассылает номер. Клиент не крутит колесо сам
    /// и не вычисляет результат по своей анимации - иначе он мог бы
    /// "докрутить" себе нужный сектор. Анимация только показывает уже
    /// принятое решение, поэтому у всех она одинаковая.
    ///
    /// Живёт на GameRoot, а не на диске в сцене: диск - обычный объект,
    /// его мы находим по имени и вращаем.
    /// </summary>
    public class FortuneWheel : NetworkBehaviour
    {
        public static FortuneWheel Instance { get; private set; }

        /// <summary>
        /// Раскладка секторов. Тот же порядок задан в backyard_bet.py
        /// (WHEEL_PAYOUTS) - там он определяет цвет сектора, здесь выплату.
        /// Расходиться они не должны, иначе колесо встанет на жёлтом, а
        /// начислит как за красный.
        /// </summary>
        public static readonly int[] Payouts = { 0, 2, 0, 3, 0, 2, 0, 2 };

        [Tooltip("Сколько крутится до остановки, с.")]
        public float spinTime = 4.2f;

        [Tooltip("Сколько полных оборотов делает до остановки.")]
        public int fullTurns = 4;

        [Tooltip("Пауза, прежде чем колесо можно крутить снова, с.")]
        public float cooldown = 2.5f;

        readonly NetworkVariable<int> _sector = new NetworkVariable<int>(-1);
        readonly NetworkVariable<double> _spinStart = new NetworkVariable<double>(-1);
        readonly NetworkVariable<ulong> _spinner = new NetworkVariable<ulong>();

        Transform _disc;
        float _restAngle;
        float _fromAngle;
        float _toAngle;
        bool _animating;
        bool _payoutDone;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            var go = GameObject.Find("FortuneWheel");
            if (go != null) _disc = go.transform;
            else Debug.LogWarning("[Backyard Bet] Диск колеса не найден в карте.");

            _spinStart.OnValueChanged += (_, __) => BeginAnimation();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Крутится прямо сейчас либо ещё не остыло.</summary>
        public bool Spinning =>
            _spinStart.Value >= 0 &&
            NetworkManager.ServerTime.Time - _spinStart.Value < spinTime + cooldown;

        // ------------------------------------------------------------ бросок

        /// <summary>Крутануть. Только на хосте.</summary>
        public void ServerSpin(ulong clientId)
        {
            if (!IsServer || Spinning) return;

            _spinner.Value = clientId;
            _sector.Value = Random.Range(0, Payouts.Length);
            // время старта общее для всех: так анимация идёт синхронно и ни
            // у кого колесо не останавливается раньше
            _spinStart.Value = NetworkManager.ServerTime.Time;
        }

        // ------------------------------------------------------------ показ

        void BeginAnimation()
        {
            if (_sector.Value < 0 || _disc == null) return;

            int n = Payouts.Length;
            float step = 360f / n;
            float target = -(_sector.Value * step + step * 0.5f);   // середина сектора под стрелку

            _fromAngle = _restAngle;
            _toAngle = _fromAngle + fullTurns * 360f + Mathf.DeltaAngle(_fromAngle, target);
            _animating = true;
            _payoutDone = false;
        }

        void Update()
        {
            if (!_animating || _disc == null) return;

            double elapsed = NetworkManager.ServerTime.Time - _spinStart.Value;
            float t = Mathf.Clamp01((float)(elapsed / spinTime));

            // замедление к концу: колесо должно доползать до сектора,
            // а не втыкаться в него на полном ходу
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            _restAngle = Mathf.Lerp(_fromAngle, _toAngle, eased);
            _disc.localRotation = Quaternion.AngleAxis(_restAngle, Vector3.forward);

            if (t < 1f) return;

            _animating = false;
            _restAngle = _toAngle;
            if (IsServer && !_payoutDone) { _payoutDone = true; ApplyPayout(); }
        }

        void ApplyPayout()
        {
            if (MatchScore.Instance == null) return;

            int factor = Payouts[_sector.Value];
            ulong who = _spinner.Value;
            int before = MatchScore.Instance.ScoreOf(who);
            MatchScore.Instance.Multiply(who, factor);

            MatchScore.Instance.Announce(factor == 0
                ? "КОЛЕСО: ноль. Всё сгорело (" + before + " → 0)"
                : "КОЛЕСО: " + factor + "x  (" + before + " → " + before * factor + ")");
        }
    }
}

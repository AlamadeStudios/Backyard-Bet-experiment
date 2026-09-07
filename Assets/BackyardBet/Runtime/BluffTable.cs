using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>Публичная строка игрока за столом: сколько костей осталось.</summary>
    public struct SeatInfo : INetworkSerializable, IEquatable<SeatInfo>
    {
        public ulong clientId;
        public int diceCount;
        public bool eliminated;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref clientId);
            s.SerializeValue(ref diceCount);
            s.SerializeValue(ref eliminated);
        }

        public bool Equals(SeatInfo o) =>
            clientId == o.clientId && diceCount == o.diceCount && eliminated == o.eliminated;
    }

    /// <summary>
    /// Стол блефа: сетевая обёртка над правилами LiarsDice.
    ///
    /// Правила крутятся только на хосте. Наружу уходят две разные вещи:
    ///  * публичное состояние (чей ход, ставка, у кого сколько костей) -
    ///    через NetworkVariable, видно всем;
    ///  * свои кости - адресным ClientRpc лично игроку.
    /// Если разослать кости всем, блефовать станет не во что.
    /// </summary>
    public class BluffTable : NetworkBehaviour
    {
        public static BluffTable Instance { get; private set; }

        [Tooltip("Пауза на показ вскрытия перед новым раундом, с.")]
        public float revealPause = 4f;

        readonly NetworkList<SeatInfo> _seats = new NetworkList<SeatInfo>();
        readonly NetworkVariable<int> _state = new NetworkVariable<int>((int)DiceState.Idle);
        readonly NetworkVariable<int> _currentSeat = new NetworkVariable<int>(-1);
        readonly NetworkVariable<int> _bidQuantity = new NetworkVariable<int>(0);
        readonly NetworkVariable<int> _bidFace = new NetworkVariable<int>(0);
        readonly NetworkVariable<int> _round = new NetworkVariable<int>(0);
        readonly NetworkVariable<FixedString128Bytes> _message =
            new NetworkVariable<FixedString128Bytes>();

        LiarsDice _game;              // только на хосте
        int[] _myDice = Array.Empty<int>();
        bool _pendingNextRound;

        // выбор в интерфейсе
        int _uiQuantity = 1;
        int _uiFace = 1;

        DiceState State => (DiceState)_state.Value;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                _game = new LiarsDice();
                NetworkManager.OnClientDisconnectCallback += OnClientLeft;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
            if (Instance == this) Instance = null;
        }

        void OnClientLeft(ulong clientId)
        {
            if (!IsServer || _game == null) return;
            _game.RemovePlayer(clientId);
            PushPublicState();
        }

        // ------------------------------------------------------------ вход за стол

        public bool IsSeated(ulong clientId)
        {
            for (int i = 0; i < _seats.Count; i++)
                if (_seats[i].clientId == clientId) return true;
            return false;
        }

        /// <summary>Игрок сел за стол. Только на хосте.</summary>
        public void ServerJoin(ulong clientId)
        {
            if (!IsServer) return;
            if (_game.IndexOf(clientId) >= 0) return;

            _game.AddPlayer(clientId, "Игрок " + clientId);
            _message.Value = "Игрок " + clientId + " сел за стол";

            // партия стартует, как только за столом двое
            if (State == DiceState.Idle && _game.CanStart)
            {
                _game.StartMatch();
                _message.Value = "Партия началась! Кости брошены.";
            }
            PushPublicState();
            SendPrivateDice();
        }

        // ------------------------------------------------------------ ходы

        [ServerRpc(RequireOwnership = false)]
        public void PlaceBidServerRpc(int quantity, int face, ServerRpcParams p = default)
        {
            ulong who = p.Receive.SenderClientId;
            if (_game == null || !_game.PlaceBid(who, quantity, face)) return;

            _message.Value = "Игрок " + who + ": " + quantity + " x " + face;
            PushPublicState();
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChallengeServerRpc(ServerRpcParams p = default)
        {
            ulong who = p.Receive.SenderClientId;
            if (_game == null || !_game.Challenge(who)) return;

            var loser = _game.players[_game.lastLoserIndex];
            _message.Value = string.Format(
                "ВРЁШЬ! Грани {0} выпало {1} при ставке {2}. {3} теряет кость.",
                _game.bidFace, _game.lastActual, _game.bidQuantity, loser.name);

            PushPublicState();
            RevealAllDiceClientRpc(PackAllDice());

            if (State == DiceState.MatchOver)
            {
                int w = _game.WinnerIndex;
                _message.Value = w >= 0
                    ? "ТУРНИР ОКОНЧЕН. Победил " + _game.players[w].name
                    : "ТУРНИР ОКОНЧЕН.";
                if (MatchScore.Instance != null && w >= 0)
                    MatchScore.Instance.Add(_game.players[w].clientId, 10, "Победа за столом! +10");
            }
            else if (!_pendingNextRound)
            {
                StartCoroutine(NextRoundAfterPause());
            }
        }

        IEnumerator NextRoundAfterPause()
        {
            _pendingNextRound = true;
            yield return new WaitForSeconds(revealPause);
            if (_game != null && (DiceState)_state.Value == DiceState.Revealed)
            {
                _game.NextRound();
                _message.Value = "Раунд " + _game.roundNumber + ". Кости брошены.";
                PushPublicState();
                SendPrivateDice();
            }
            _pendingNextRound = false;
        }

        // ------------------------------------------------------------ синхронизация

        /// <summary>Разослать публичное состояние: без чужих костей.</summary>
        void PushPublicState()
        {
            _seats.Clear();
            foreach (var p in _game.players)
                _seats.Add(new SeatInfo
                {
                    clientId = p.clientId,
                    diceCount = p.diceCount,
                    eliminated = p.eliminated
                });

            _state.Value = (int)_game.state;
            _currentSeat.Value = _game.state == DiceState.Bidding ? _game.currentIndex : -1;
            _bidQuantity.Value = _game.bidQuantity;
            _bidFace.Value = _game.bidFace;
            _round.Value = _game.roundNumber;
        }

        /// <summary>Каждому - только его кости, персональным адресом.</summary>
        void SendPrivateDice()
        {
            foreach (var p in _game.players)
            {
                if (p.eliminated) continue;
                var target = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { p.clientId } }
                };
                MyDiceClientRpc(p.dice.ToArray(), target);
            }
        }

        [ClientRpc]
        void MyDiceClientRpc(int[] dice, ClientRpcParams p = default) => _myDice = dice;

        int[] PackAllDice()
        {
            // формат: clientId уложить не можем компактно, шлём парами
            // (номер места, грань) - для показа вскрытия этого достаточно
            var list = new List<int>();
            for (int i = 0; i < _game.players.Count; i++)
                foreach (int d in _game.players[i].dice) { list.Add(i); list.Add(d); }
            return list.ToArray();
        }

        readonly List<(int seat, int face)> _revealed = new List<(int, int)>();

        [ClientRpc]
        void RevealAllDiceClientRpc(int[] packed)
        {
            _revealed.Clear();
            for (int i = 0; i + 1 < packed.Length; i += 2)
                _revealed.Add((packed[i], packed[i + 1]));
        }

        // ------------------------------------------------------------ интерфейс

        void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient) return;

            ulong me = nm.LocalClientId;
            if (!IsSeated(me)) return;              // не за столом - интерфейс не нужен

            float w = Screen.width, h = Screen.height;

            // --- шапка: раунд и ставка
            string bid = _bidQuantity.Value > 0
                ? string.Format("ставка: {0} x грань {1}", _bidQuantity.Value, _bidFace.Value)
                : "ставки нет";
            PlayerInteraction.Label(new Rect(w * 0.5f - 400f, 14f, 800f, 34f),
                                    "Раунд " + _round.Value + "   ·   " + bid);

            string msg = _message.Value.ToString();
            if (!string.IsNullOrEmpty(msg))
                PlayerInteraction.Label(new Rect(w * 0.5f - 450f, 48f, 900f, 30f), msg);

            // --- список мест
            var box = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft };
            GUILayout.BeginArea(new Rect(20f, 120f, 220f, 40f + _seats.Count * 20f),
                                GUIContent.none, box);
            GUILayout.Label("За столом:");
            for (int i = 0; i < _seats.Count; i++)
            {
                var s = _seats[i];
                string tag = s.eliminated ? "выбыл" : new string('•', Mathf.Max(0, s.diceCount));
                string turn = i == _currentSeat.Value ? "  <— ход" : "";
                string who = s.clientId == me ? "Ты" : "Игрок " + s.clientId;
                GUILayout.Label(who + ": " + tag + turn);
            }
            GUILayout.EndArea();

            // --- свои кости
            if (_myDice.Length > 0 && State != DiceState.MatchOver)
            {
                float dx = w * 0.5f - _myDice.Length * 33f;
                var dieStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                for (int i = 0; i < _myDice.Length; i++)
                    GUI.Box(new Rect(dx + i * 66f, h - 108f, 56f, 56f),
                            _myDice[i].ToString(), dieStyle);
            }

            if (State == DiceState.MatchOver) return;

            // --- вскрытие: показываем всё, ходов пока нет
            if (State == DiceState.Revealed)
            {
                PlayerInteraction.Label(new Rect(w * 0.5f - 400f, h * 0.34f, 800f, 34f),
                                        "Вскрытие — следующий раунд вот-вот");
                return;
            }

            // --- твой ход
            int mySeat = -1;
            for (int i = 0; i < _seats.Count; i++) if (_seats[i].clientId == me) mySeat = i;
            if (mySeat != _currentSeat.Value) return;

            GUILayout.BeginArea(new Rect(w * 0.5f - 250f, h - 190f, 500f, 74f),
                                GUIContent.none, box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Костей:", GUILayout.Width(58));
            if (GUILayout.Button("−", GUILayout.Width(30))) _uiQuantity = Mathf.Max(1, _uiQuantity - 1);
            GUILayout.Label(_uiQuantity.ToString(), GUILayout.Width(26));
            if (GUILayout.Button("+", GUILayout.Width(30))) _uiQuantity++;

            GUILayout.Space(14);
            GUILayout.Label("Грань:", GUILayout.Width(50));
            if (GUILayout.Button("−", GUILayout.Width(30))) _uiFace = Mathf.Max(1, _uiFace - 1);
            GUILayout.Label(_uiFace.ToString(), GUILayout.Width(26));
            if (GUILayout.Button("+", GUILayout.Width(30)))
                _uiFace = Mathf.Min(LiarsDice.Faces, _uiFace + 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Поставить", GUILayout.Height(24)))
                PlaceBidServerRpc(_uiQuantity, _uiFace);
            GUI.enabled = _bidQuantity.Value > 0;
            if (GUILayout.Button("ВРЁШЬ!", GUILayout.Height(24)))
                ChallengeServerRpc();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}

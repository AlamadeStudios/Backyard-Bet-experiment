using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>Публичная строка игрока за столом: карт на руках и штрафов.</summary>
    public struct SeatInfo : INetworkSerializable, IEquatable<SeatInfo>
    {
        public ulong clientId;
        public int cards;
        public int penalties;
        public bool eliminated;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref clientId);
            s.SerializeValue(ref cards);
            s.SerializeValue(ref penalties);
            s.SerializeValue(ref eliminated);
        }

        public bool Equals(SeatInfo o) =>
            clientId == o.clientId && cards == o.cards &&
            penalties == o.penalties && eliminated == o.eliminated;
    }

    /// <summary>
    /// Стол: сетевая обёртка над правилами LiarsBarGame плюс интерфейс.
    ///
    /// Правила крутятся только на хосте. Наружу уходят две разные вещи:
    ///  * публичное состояние (чей ход, ранг стола, у кого сколько карт) -
    ///    через NetworkVariable, видно всем;
    ///  * **своя рука - адресным RPC лично игроку**.
    /// Если разослать карты всем, врать станет не во что, а вся игра
    /// держится именно на вранье.
    /// </summary>
    public class BluffTable : NetworkBehaviour
    {
        public static BluffTable Instance { get; private set; }

        [Tooltip("Пауза на показ вскрытия перед новым раундом, с.")]
        public float revealPause = 5f;

        [Tooltip("Очки победителю партии.")]
        public int winScore = 10;

        [Tooltip("Сколько мест за столом - недостающие займут боты.")]
        public int tableSize = 4;

        [Tooltip("Сколько бот думает перед ходом, с.")]
        public float botThinkTime = 1.6f;

        readonly NetworkList<SeatInfo> _seats = new NetworkList<SeatInfo>();
        readonly NetworkVariable<int> _state = new NetworkVariable<int>((int)BarState.Idle);
        readonly NetworkVariable<int> _currentSeat = new NetworkVariable<int>(-1);
        readonly NetworkVariable<int> _tableRank = new NetworkVariable<int>();
        readonly NetworkVariable<int> _round = new NetworkVariable<int>();
        readonly NetworkVariable<int> _pileSeat = new NetworkVariable<int>(-1);
        readonly NetworkVariable<int> _pileCount = new NetworkVariable<int>();
        readonly NetworkVariable<FixedString128Bytes> _message =
            new NetworkVariable<FixedString128Bytes>();

        LiarsBarGame _game;                       // только на хосте
        DiceCupShaker _cup;
        CardTable3D _cards;
        bool _pendingNextRound;

        int[] _myHand = Array.Empty<int>();       // своя рука, приходит адресно
        readonly List<int> _picked = new List<int>();
        int[] _revealed = Array.Empty<int>();

        BarState State => (BarState)_state.Value;

        // ------------------------------------------------------------ жизнь

        public override void OnNetworkSpawn()
        {
            Instance = this;

            _cup = FindAnyObjectByType<DiceCupShaker>();
            _cards = gameObject.AddComponent<CardTable3D>();

            _round.OnValueChanged += (_, __) =>
            {
                _picked.Clear();
                _cards.ClearPile();                    // новый раунд - сукно чистое
                if (_cup != null) _cup.PlayShake();
            };
            _state.OnValueChanged += (_, s) =>
            {
                if (_cup != null && (BarState)s == BarState.Revealed) _cup.PlayReveal();
            };
            _pileSeat.OnValueChanged  += (_, seat) => _cards.SetPileSeat(seat);
            _pileCount.OnValueChanged += (_, n) =>
            {
                _cards.SetPileSeat(_pileSeat.Value);
                _cards.ShowPile(n, LocalEye());
            };

            if (IsServer)
            {
                _game = new LiarsBarGame();
                NetworkManager.OnClientDisconnectCallback += OnClientLeft;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
            if (Instance == this) Instance = null;
        }

        void OnClientLeft(ulong clientId) => ServerLeave(clientId);

        public bool IsSeated(ulong clientId)
        {
            for (int i = 0; i < _seats.Count; i++)
                if (_seats[i].clientId == clientId) return true;
            return false;
        }

        // ------------------------------------------------------------ вход и выход

        /// <summary>Игрок сел за стол. Только на хосте.</summary>
        public void ServerJoin(ulong clientId)
        {
            if (!IsServer || _game == null) return;
            if (_game.IndexOf(clientId) >= 0) return;

            // живой игрок занимает место бота, а не добавляется сверх стола
            if (_game.players.Count >= tableSize) _game.RemoveOneBot();

            _game.AddPlayer(clientId, "Игрок " + clientId);
            int seat = _game.IndexOf(clientId);

            // усаживаем игрока на стул: дальше он видит стол, а не двор
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var c) &&
                c.PlayerObject != null)
            {
                var seating = c.PlayerObject.GetComponent<PlayerSeating>();
                if (seating != null) seating.ServerSeat(seat);
                else Debug.LogError("[Backyard Bet] На префабе игрока нет PlayerSeating - " +
                                    "усадить за стол нечем. Нужна пересборка префаба.");
            }

            _message.Value = "Игрок " + clientId + " сел за стол";

            // Добираем ботов до полного стола, чтобы раздача начиналась сразу.
            // Иначе в одиночку за столом ничего не происходит и проверить
            // карточную часть можно только запустив второе окно.
            while (_game.players.Count < tableSize) _game.AddBot(BotName(_game.players.Count));

            if (State == BarState.Idle && _game.CanStart)
            {
                _game.StartMatch();
                _message.Value = "Партия началась. Ранг стола: " +
                                 LiarsBarGame.RankName(_game.tableRank);
            }
            PushPublicState();
            SendHands();
        }

        static string BotName(int i)
        {
            string[] names = { "Гоша", "Хвост", "Рыжий", "Батя" };
            return names[i % names.Length];
        }

        // ------------------------------------------------------------ ходы ботов

        float _botClock;

        void Update()
        {
            if (!IsServer || _game == null) return;
            if (_game.state != BarState.Playing) { _botClock = 0f; return; }

            int i = _game.currentIndex;
            if (i < 0 || i >= _game.players.Count || !_game.players[i].isBot)
            {
                _botClock = 0f;
                return;
            }

            // пауза перед ходом: мгновенные ходы ботов читаются как сбой,
            // игрок просто не успевает понять, что произошло
            _botClock += Time.deltaTime;
            if (_botClock < botThinkTime) return;
            _botClock = 0f;

            PlayBotTurn(i);
        }

        void PlayBotTurn(int index)
        {
            var bot = _game.players[index];
            var play = _game.BotDecide(index, out bool challenge);

            if (challenge)
            {
                if (_game.Challenge(bot.clientId)) AfterChallenge();
                return;
            }

            if (play == null || play.Count == 0) return;
            int seat = _game.currentIndex;            // до хода: дальше очередь сместится
            if (!_game.Play(bot.clientId, play)) return;
            _pileSeat.Value = seat;

            _message.Value = bot.name + ": " + play.Count + " x " +
                             LiarsBarGame.RankName(_game.tableRank);
            PushPublicState();
            SendHands();

            if (_game.EveryoneOutOfCards() && !_pendingNextRound)
                StartCoroutine(RedealAfterPause());
        }

        /// <summary>Игрок встал или отключился.</summary>
        public void ServerLeave(ulong clientId)
        {
            if (!IsServer || _game == null) return;
            if (_game.IndexOf(clientId) < 0) return;

            _game.RemovePlayer(clientId);
            if (_game.players.Count < 2) _game.state = BarState.Idle;
            PushPublicState();
        }

        // ------------------------------------------------------------ ходы

        [ServerRpc(RequireOwnership = false)]
        public void PlayCardsServerRpc(int[] handIndices, ServerRpcParams p = default)
        {
            ulong who = p.Receive.SenderClientId;
            if (_game == null) return;

            // место запоминаем до хода: внутри Play очередь уже сместится,
            // а сброс должен лечь перед тем, кто выкладывал
            int seat = _game.currentIndex;
            if (!_game.Play(who, new List<int>(handIndices))) return;
            _pileSeat.Value = seat;

            _message.Value = "Игрок " + who + ": " + handIndices.Length + " x " +
                             LiarsBarGame.RankName(_game.tableRank);
            PushPublicState();
            SendHands();

            // никто не может ходить - раздаём заново, иначе раунд повиснет
            if (_game.EveryoneOutOfCards() && !_pendingNextRound)
                StartCoroutine(RedealAfterPause());
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChallengeServerRpc(ServerRpcParams p = default)
        {
            ulong who = p.Receive.SenderClientId;
            if (_game == null || !_game.Challenge(who)) return;
            AfterChallenge();
        }

        /// <summary>
        /// Разбор вскрытия - общий для игрока и бота. Вызов у них приходит
        /// разными путями, а последствия обязаны быть одинаковыми.
        /// </summary>
        void AfterChallenge()
        {
            var loser = _game.players[_game.loserIndex];
            _message.Value = string.Format(
                "ЛЖЁШЬ! Вскрытие: {0}. Штраф получает {1} ({2}/{3})",
                _game.lastClaimWasTrue ? "заявка честная" : "блеф раскрыт",
                loser.name, loser.penalties, _game.penaltyLimit);

            PushPublicState();
            RevealClientRpc(ToInts(_game.lastPlayed));

            if (State == BarState.MatchOver)
            {
                int w = _game.WinnerIndex;
                if (w >= 0)
                {
                    _message.Value = "ПАРТИЯ ОКОНЧЕНА. Победил " + _game.players[w].name;
                    if (MatchScore.Instance != null)
                        MatchScore.Instance.Add(_game.players[w].clientId, winScore,
                                                "Победа за столом!  +" + winScore);
                }
            }
            else if (!_pendingNextRound) StartCoroutine(NextRoundAfterPause());
        }

        IEnumerator NextRoundAfterPause()
        {
            _pendingNextRound = true;
            yield return new WaitForSeconds(revealPause);
            if (_game != null && (BarState)_state.Value == BarState.Revealed)
            {
                _game.NextRound();
                AnnounceRound();
            }
            _pendingNextRound = false;
        }

        IEnumerator RedealAfterPause()
        {
            _pendingNextRound = true;
            yield return new WaitForSeconds(2f);
            if (_game != null && _game.state == BarState.Playing)
            {
                _game.StartRound(_game.currentIndex);
                AnnounceRound();
            }
            _pendingNextRound = false;
        }

        void AnnounceRound()
        {
            _message.Value = "Раунд " + _game.roundNumber + ". Ранг стола: " +
                             LiarsBarGame.RankName(_game.tableRank);
            PushPublicState();
            SendHands();
        }

        // ------------------------------------------------------------ синхронизация

        /// <summary>Публичное состояние: без чужих карт.</summary>
        void PushPublicState()
        {
            _seats.Clear();
            foreach (var p in _game.players)
                _seats.Add(new SeatInfo
                {
                    clientId = p.clientId,
                    cards = p.hand.Count,
                    penalties = p.penalties,
                    eliminated = p.eliminated
                });

            _state.Value = (int)_game.state;
            _currentSeat.Value = _game.state == BarState.Playing ? _game.currentIndex : -1;
            _tableRank.Value = (int)_game.tableRank;
            _round.Value = _game.roundNumber;
            _pileCount.Value = _game.lastPlayed.Count;
        }

        /// <summary>Каждому - только его карты, персональным адресом.</summary>
        void SendHands()
        {
            foreach (var p in _game.players)
            {
                // ботам слать некуда: их идентификаторы выдуманные, и адресный
                // RPC на несуществующего клиента - это ошибка в консоли
                if (p.eliminated || p.isBot) continue;
                HandClientRpc(ToInts(p.hand), new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { p.clientId } }
                });
            }
        }

        static int[] ToInts(List<CardRank> cards)
        {
            var a = new int[cards.Count];
            for (int i = 0; i < cards.Count; i++) a[i] = (int)cards[i];
            return a;
        }

        [ClientRpc]
        void HandClientRpc(int[] hand, ClientRpcParams p = default)
        {
            _myHand = hand;
            _picked.Clear();
        }

        [ClientRpc]
        void RevealClientRpc(int[] pile)
        {
            _revealed = pile;
            if (_cards != null) _cards.RevealPile(pile);
        }

        /// <summary>Камера местного игрока - от неё летят карты и к ней цепляется рука.</summary>
        Camera LocalEye()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null) return null;
            var obj = nm.LocalClient.PlayerObject;
            if (obj == null) return null;

            var seat = obj.GetComponent<PlayerSeating>();
            if (seat == null || !seat.Seated) return null;      // не за столом - рук не видно

            return obj.GetComponentInChildren<Camera>(true);
        }

        /// <summary>
        /// Веер в руках и выбор карт щелчком.
        ///
        /// Держим в Update, а не в OnGUI: карты живут в мире, их положение
        /// должно обновляться вместе с камерой, а не при отрисовке интерфейса.
        /// </summary>
        void LateUpdate()
        {
            if (_cards == null) return;

            var eye = LocalEye();
            if (eye == null) { _cards.ClearHand(); return; }

            _cards.ShowHand(_myHand, eye, _picked, _round.Value);

            if (!Input.GetMouseButtonDown(0)) return;
            if (_cards.Hovered >= 0) TogglePick(_cards.Hovered);
        }

        // ------------------------------------------------------------ интерфейс

        void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient) return;

            ulong me = nm.LocalClientId;
            if (!IsSeated(me)) return;             // не за столом - интерфейс не нужен

            float w = Screen.width, h = Screen.height;
            var box = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft };

            // --- шапка
            PlayerInteraction.Label(new Rect(w * 0.5f - 420f, 14f, 840f, 34f),
                "Раунд " + _round.Value + "   ·   Ранг стола: " +
                LiarsBarGame.RankName((CardRank)_tableRank.Value) +
                (_pileCount.Value > 0 ? "   ·   в стопке: " + _pileCount.Value : ""));

            string msg = _message.Value.ToString();
            if (!string.IsNullOrEmpty(msg))
                PlayerInteraction.Label(new Rect(w * 0.5f - 460f, 48f, 920f, 30f), msg);

            // --- места
            GUILayout.BeginArea(new Rect(20f, 110f, 250f, 46f + _seats.Count * 20f),
                                GUIContent.none, box);
            GUILayout.Label("За столом:");
            for (int i = 0; i < _seats.Count; i++)
            {
                var s = _seats[i];
                string who = s.clientId == me ? "Ты" : "Игрок " + s.clientId;
                string tag = s.eliminated ? "выбыл"
                                          : s.cards + " карт · штраф " + s.penalties;
                GUILayout.Label(who + ": " + tag + (i == _currentSeat.Value ? "  <— ход" : ""));
            }
            GUILayout.EndArea();

            PlayerInteraction.Label(new Rect(20f, h - 40f, 420f, 24f), "Esc — встать из-за стола");

            if (State == BarState.MatchOver) return;

            // Стопка и вскрытие показываются картами на сукне, а не в
            // интерфейсе - см. CardTable3D.
            if (State == BarState.Revealed)
            {
                PlayerInteraction.Label(new Rect(w * 0.5f - 420f, h * 0.30f, 840f, 30f),
                                        "Вскрытие — смотри на стол");
                return;
            }

            DrawHand(w, h, me);
        }

        /// <summary>Своя рука: карты выбираются щелчком, до трёх за ход.</summary>
        void DrawHand(float w, float h, ulong me)
        {
            // Сами карты живут в мире - их держат в руках, а не рисуют внизу
            // экрана. Здесь остаются только кнопки хода и подсказка.
            PlayerInteraction.Label(new Rect(w * 0.5f - 300f, h - 96f, 600f, 24f),
                                    "Щелчок по карте — выбрать, до " +
                                    LiarsBarGame.MaxPlay + " за ход");

            int mySeat = -1;
            for (int i = 0; i < _seats.Count; i++) if (_seats[i].clientId == me) mySeat = i;
            if (mySeat != _currentSeat.Value) return;

            // --- кнопки хода
            float bx = w * 0.5f - 190f;
            GUI.enabled = _picked.Count > 0 && _picked.Count <= LiarsBarGame.MaxPlay;
            if (GUI.Button(new Rect(bx, h - 56f, 180f, 34f),
                           "Выложить " + _picked.Count))
            {
                PlayCardsServerRpc(_picked.ToArray());
                _picked.Clear();
            }
            GUI.enabled = _pileCount.Value > 0;
            if (GUI.Button(new Rect(bx + 200f, h - 56f, 180f, 34f), "ЛЖЁШЬ!"))
                ChallengeServerRpc();
            GUI.enabled = true;
        }

        void TogglePick(int i)
        {
            if (_picked.Contains(i)) { _picked.Remove(i); return; }
            if (_picked.Count >= LiarsBarGame.MaxPlay) return;
            _picked.Add(i);
        }

    }
}

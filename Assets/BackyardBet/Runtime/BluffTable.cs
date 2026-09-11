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

        readonly NetworkList<SeatInfo> _seats = new NetworkList<SeatInfo>();
        readonly NetworkVariable<int> _state = new NetworkVariable<int>((int)BarState.Idle);
        readonly NetworkVariable<int> _currentSeat = new NetworkVariable<int>(-1);
        readonly NetworkVariable<int> _tableRank = new NetworkVariable<int>();
        readonly NetworkVariable<int> _round = new NetworkVariable<int>();
        readonly NetworkVariable<int> _pileCount = new NetworkVariable<int>();
        readonly NetworkVariable<FixedString128Bytes> _message =
            new NetworkVariable<FixedString128Bytes>();

        LiarsBarGame _game;                       // только на хосте
        DiceCupShaker _cup;
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
            _round.OnValueChanged += (_, __) => { _picked.Clear(); if (_cup != null) _cup.PlayShake(); };
            _state.OnValueChanged += (_, s) =>
            {
                if (_cup != null && (BarState)s == BarState.Revealed) _cup.PlayReveal();
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
            if (State == BarState.Idle && _game.CanStart)
            {
                _game.StartMatch();
                _message.Value = "Партия началась. Ранг стола: " +
                                 LiarsBarGame.RankName(_game.tableRank);
            }
            PushPublicState();
            SendHands();
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
            if (!_game.Play(who, new List<int>(handIndices))) return;

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
                if (p.eliminated) continue;
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
        void RevealClientRpc(int[] pile) => _revealed = pile;

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

            // --- вскрытие: показываем стопку
            if (State == BarState.Revealed)
            {
                DrawPile(w, h);
                return;
            }

            DrawHand(w, h, me);
        }

        /// <summary>Вскрытая стопка - что там было на самом деле.</summary>
        void DrawPile(float w, float h)
        {
            PlayerInteraction.Label(new Rect(w * 0.5f - 420f, h * 0.32f, 840f, 30f),
                                    "Вскрытие:");
            float x = w * 0.5f - _revealed.Length * 45f;
            for (int i = 0; i < _revealed.Length; i++)
                DrawCard(new Rect(x + i * 90f, h * 0.38f, 80f, 112f), (CardRank)_revealed[i], false);
        }

        /// <summary>Своя рука: карты выбираются щелчком, до трёх за ход.</summary>
        void DrawHand(float w, float h, ulong me)
        {
            if (_myHand.Length > 0)
            {
                float x = w * 0.5f - _myHand.Length * 48f;
                for (int i = 0; i < _myHand.Length; i++)
                {
                    var r = new Rect(x + i * 96f, h - 190f - (_picked.Contains(i) ? 24f : 0f),
                                     86f, 120f);
                    if (GUI.Button(r, GUIContent.none)) TogglePick(i);
                    DrawCard(r, (CardRank)_myHand[i], _picked.Contains(i));
                }
            }

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

        static void DrawCard(Rect r, CardRank rank, bool picked)
        {
            var face = new GUIStyle(GUI.skin.box)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            face.normal.textColor = rank == CardRank.Joker
                ? new Color(0.95f, 0.55f, 0.15f)
                : new Color(0.12f, 0.12f, 0.14f);

            var old = GUI.color;
            GUI.color = picked ? new Color(1f, 0.92f, 0.6f) : Color.white;
            GUI.Box(r, LiarsBarGame.RankName(rank), face);
            GUI.color = old;
        }
    }
}

using System;
using System.Collections.Generic;

namespace BackyardBet
{
    /// <summary>Достоинство карты. Джокер годится за любой ранг стола.</summary>
    public enum CardRank { Queen = 0, King = 1, Ace = 2, Joker = 3 }

    public enum BarState { Idle, Playing, Revealed, MatchOver }

    /// <summary>
    /// Правила карточной игры в духе Liar's Bar. Ни одной ссылки на Unity:
    /// всё состояние живёт здесь, снаружи только показ и сеть. Крутится
    /// только на хосте - он и есть источник правды.
    ///
    /// Колода: по шесть дам, королей и тузов плюс два джокера - 20 карт.
    /// В начале раунда объявляется ранг стола. В свой ход игрок кладёт
    /// от одной до трёх карт рубашкой вверх и заявляет, что все они этого
    /// ранга. Следующий либо кладёт свои, либо кричит "Лжёшь!".
    ///
    /// На вызове вскрывается последняя стопка: если там были только карты
    /// ранга стола и джокеры - ошибся крикнувший, иначе - тот, кто клал.
    /// Проигравший получает штраф.
    /// </summary>
    public class LiarsBarGame
    {
        public const int HandSize = 5;
        public const int MaxPlay = 3;

        public class Player
        {
            public ulong clientId;
            public string name;
            public bool isBot;
            public bool eliminated;
            public int penalties;
            public readonly List<CardRank> hand = new List<CardRank>();
        }

        public readonly List<Player> players = new List<Player>();
        public BarState state = BarState.Idle;

        /// <summary>Ранг, которым игроки обязаны называть свои карты.</summary>
        public CardRank tableRank;
        public int roundNumber;
        public int currentIndex;

        /// <summary>Сколько штрафов до вылета.</summary>
        public int penaltyLimit = 3;

        // последняя стопка - её и вскрывают на вызове
        public readonly List<CardRank> lastPlayed = new List<CardRank>();
        public int lastPlayerIndex = -1;

        // итоги вскрытия, нужны показу
        public int challengerIndex = -1;
        public int loserIndex = -1;
        public bool lastClaimWasTrue;

        readonly List<CardRank> _deck = new List<CardRank>();
        readonly Random _rng = new Random();

        public bool CanStart => players.Count >= 2;
        public int AliveCount => players.FindAll(p => !p.eliminated).Count;
        public int IndexOf(ulong clientId) => players.FindIndex(p => p.clientId == clientId);

        /// <summary>Есть ли стопка, которую можно оспорить.</summary>
        public bool HasPlayToChallenge => lastPlayerIndex >= 0 && lastPlayed.Count > 0;

        // ---------------------------------------------------------------- стол

        public void AddPlayer(ulong clientId, string name)
        {
            if (IndexOf(clientId) >= 0) return;
            players.Add(new Player { clientId = clientId, name = name });
        }

        /// <summary>
        /// Посадить бота. Нужен, чтобы игра шла и с одним живым игроком:
        /// иначе проверить карточную часть можно только запустив второе окно.
        /// Идентификаторы берём заведомо большие - с настоящими клиентами
        /// они не столкнутся.
        /// </summary>
        public void AddBot(string name)
        {
            ulong id = 1000UL + (ulong)players.Count;
            players.Add(new Player { clientId = id, name = name, isBot = true });
        }

        public int BotCount => players.FindAll(p => p.isBot).Count;

        /// <summary>Убрать одного бота - его место занимает пришедший игрок.</summary>
        public bool RemoveOneBot()
        {
            int i = players.FindIndex(p => p.isBot);
            if (i < 0) return false;
            players.RemoveAt(i);
            if (currentIndex >= players.Count) currentIndex = 0;
            return true;
        }

        /// <summary>
        /// Решение бота: врать, говорить правду или ловить на лжи.
        /// Возвращает позиции карт для хода; пустой список - значит вызов.
        /// </summary>
        public List<int> BotDecide(int index, out bool challenge)
        {
            var p = players[index];
            challenge = false;

            var honest = new List<int>();
            for (int i = 0; i < p.hand.Count; i++)
                if (p.hand[i] == tableRank || p.hand[i] == CardRank.Joker) honest.Add(i);

            // Чем больше карт заявили разом, тем вероятнее блеф - на этом и
            // ловим. Совсем без карт в руке ловить приходится всегда.
            if (HasPlayToChallenge)
            {
                double suspicion = 0.12 + 0.22 * (lastPlayed.Count - 1);
                if (p.hand.Count == 0) suspicion = 1.0;
                if (_rng.NextDouble() < suspicion) { challenge = true; return null; }
            }

            if (p.hand.Count == 0) { challenge = true; return null; }

            var play = new List<int>();
            if (honest.Count > 0)
            {
                // иногда придерживаем честные карты, чтобы не читались насквозь
                int take = Math.Min(honest.Count, _rng.Next(1, MaxPlay + 1));
                for (int i = 0; i < take; i++) play.Add(honest[i]);
                return play;
            }

            // честных нет - блефуем одной случайной
            play.Add(_rng.Next(p.hand.Count));
            return play;
        }

        public void RemovePlayer(ulong clientId)
        {
            int i = IndexOf(clientId);
            if (i < 0) return;
            players.RemoveAt(i);
            if (currentIndex >= players.Count) currentIndex = 0;
        }

        public void StartMatch()
        {
            foreach (var p in players) { p.eliminated = false; p.penalties = 0; }
            roundNumber = 0;
            StartRound(0);
        }

        public void StartRound(int firstPlayer)
        {
            roundNumber++;
            tableRank = (CardRank)_rng.Next(0, 3);       // джокер рангом стола не бывает
            lastPlayed.Clear();
            lastPlayerIndex = -1;
            challengerIndex = -1;
            loserIndex = -1;

            BuildDeck();
            foreach (var p in players)
            {
                p.hand.Clear();
                if (p.eliminated) continue;
                for (int i = 0; i < HandSize && _deck.Count > 0; i++)
                {
                    int k = _rng.Next(_deck.Count);
                    p.hand.Add(_deck[k]);
                    _deck.RemoveAt(k);
                }
            }

            currentIndex = NextAlive(firstPlayer - 1);
            state = BarState.Playing;
        }

        void BuildDeck()
        {
            _deck.Clear();
            for (int i = 0; i < 6; i++)
            {
                _deck.Add(CardRank.Queen);
                _deck.Add(CardRank.King);
                _deck.Add(CardRank.Ace);
            }
            _deck.Add(CardRank.Joker);
            _deck.Add(CardRank.Joker);
        }

        // ---------------------------------------------------------------- ходы

        /// <summary>
        /// Выложить карты по их позициям в руке. Заявка всегда одна и та же:
        /// "это ранг стола" - врать можно только содержимым стопки.
        /// </summary>
        public bool Play(ulong clientId, List<int> handIndices)
        {
            if (state != BarState.Playing) return false;
            if (IndexOf(clientId) != currentIndex) return false;
            if (handIndices == null || handIndices.Count == 0) return false;
            if (handIndices.Count > MaxPlay) return false;

            var p = players[currentIndex];

            // проверяем позиции: без этого клиент мог бы выложить одну карту
            // несколько раз или карту, которой у него нет
            var seen = new HashSet<int>();
            foreach (int i in handIndices)
            {
                if (i < 0 || i >= p.hand.Count) return false;
                if (!seen.Add(i)) return false;
            }

            lastPlayed.Clear();
            foreach (int i in handIndices) lastPlayed.Add(p.hand[i]);

            // удаляем с конца, иначе позиции съезжают на ходу
            var sorted = new List<int>(handIndices);
            sorted.Sort();
            for (int i = sorted.Count - 1; i >= 0; i--) p.hand.RemoveAt(sorted[i]);

            lastPlayerIndex = currentIndex;
            currentIndex = NextAlive(currentIndex);
            return true;
        }

        /// <summary>"Лжёшь!" - вскрываем последнюю стопку.</summary>
        public bool Challenge(ulong clientId)
        {
            if (state != BarState.Playing || !HasPlayToChallenge) return false;
            int who = IndexOf(clientId);
            if (who != currentIndex) return false;
            if (who == lastPlayerIndex) return false;         // сам себя не ловят

            challengerIndex = who;
            lastClaimWasTrue = true;
            foreach (var c in lastPlayed)
                if (c != tableRank && c != CardRank.Joker) { lastClaimWasTrue = false; break; }

            // заявка честная - ошибся крикнувший, иначе отвечает тот, кто клал
            loserIndex = lastClaimWasTrue ? challengerIndex : lastPlayerIndex;
            Punish(loserIndex);

            state = AliveCount <= 1 ? BarState.MatchOver : BarState.Revealed;
            return true;
        }

        void Punish(int index)
        {
            var p = players[index];
            p.penalties++;
            if (p.penalties >= penaltyLimit) p.eliminated = true;
        }

        /// <summary>Следующий раунд - вызывать после паузы на показ вскрытия.</summary>
        public void NextRound()
        {
            if (state != BarState.Revealed) return;
            int starter = players[loserIndex].eliminated ? NextAlive(loserIndex) : loserIndex;
            StartRound(starter);
        }

        /// <summary>
        /// У всех кончились карты, а вызова так и не было - раздаём заново,
        /// иначе раунд повис бы навсегда.
        /// </summary>
        public bool EveryoneOutOfCards()
        {
            foreach (var p in players)
                if (!p.eliminated && p.hand.Count > 0) return false;
            return true;
        }

        public int WinnerIndex => players.FindIndex(p => !p.eliminated);

        int NextAlive(int from)
        {
            if (players.Count == 0) return 0;
            for (int k = 1; k <= players.Count; k++)
            {
                int i = ((from + k) % players.Count + players.Count) % players.Count;
                if (!players[i].eliminated) return i;
            }
            return 0;
        }

        public static string RankName(CardRank r)
        {
            switch (r)
            {
                case CardRank.Queen: return "Дама";
                case CardRank.King:  return "Король";
                case CardRank.Ace:   return "Туз";
                default:             return "Джокер";
            }
        }
    }
}

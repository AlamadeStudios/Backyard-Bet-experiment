using System;
using System.Collections.Generic;

namespace BackyardBet
{
    public enum DiceState { Idle, Bidding, Revealed, MatchOver }

    /// <summary>
    /// Правила блефа на костях. Ни одной ссылки на Unity: всё состояние
    /// живёт здесь, снаружи только отображение и сеть. Класс работает
    /// только на хосте - он и есть источник правды.
    ///
    /// Правила: у каждого 5 костей, все кидают втайне. По кругу игроки
    /// повышают ставку "всего на столе N костей грани X" либо кричат
    /// "врёшь!". На вызове вскрываются все: если костей хватает - кость
    /// теряет усомнившийся, если нет - теряет тот, кто ставил.
    /// Остался без костей - выбыл.
    /// </summary>
    public class LiarsDice
    {
        public const int StartingDice = 5;
        public const int Faces = 6;

        public class Player
        {
            public ulong clientId;
            public string name;
            public bool eliminated;
            public int diceCount = StartingDice;
            public readonly List<int> dice = new List<int>();

            public int CountOf(int face) => dice.FindAll(d => d == face).Count;
        }

        public readonly List<Player> players = new List<Player>();
        public DiceState state = DiceState.Idle;

        public int bidQuantity;      // 0 = ставки ещё нет
        public int bidFace;
        public int bidderIndex = -1;
        public int currentIndex;
        public int roundNumber;

        // итоги последнего вскрытия - нужны отображению
        public int lastActual;
        public int lastLoserIndex = -1;
        public bool lastBidWasTrue;

        readonly Random _rng = new Random();

        public bool HasBid => bidQuantity > 0;
        public int AliveCount => players.FindAll(p => !p.eliminated).Count;
        public Player Current => currentIndex >= 0 && currentIndex < players.Count
                                 ? players[currentIndex] : null;

        public int TotalDice
        {
            get
            {
                int n = 0;
                foreach (var p in players) if (!p.eliminated) n += p.diceCount;
                return n;
            }
        }

        public int IndexOf(ulong clientId) =>
            players.FindIndex(p => p.clientId == clientId);

        // ---------------------------------------------------------------- матч

        public void AddPlayer(ulong clientId, string name)
        {
            if (IndexOf(clientId) >= 0) return;
            players.Add(new Player { clientId = clientId, name = name });
        }

        public void RemovePlayer(ulong clientId)
        {
            int i = IndexOf(clientId);
            if (i < 0) return;
            players.RemoveAt(i);
            if (currentIndex >= players.Count) currentIndex = 0;
        }

        public bool CanStart => players.Count >= 2;

        public void StartMatch()
        {
            foreach (var p in players)
            {
                p.eliminated = false;
                p.diceCount = StartingDice;
            }
            roundNumber = 0;
            StartRound(0);
        }

        public void StartRound(int firstPlayer)
        {
            roundNumber++;
            bidQuantity = 0;
            bidFace = 0;
            bidderIndex = -1;
            lastLoserIndex = -1;
            lastActual = 0;

            foreach (var p in players)
            {
                p.dice.Clear();
                if (p.eliminated) continue;
                for (int i = 0; i < p.diceCount; i++)
                    p.dice.Add(_rng.Next(1, Faces + 1));
            }

            currentIndex = NextAlive(firstPlayer - 1);
            state = DiceState.Bidding;
        }

        // ---------------------------------------------------------------- ходы

        /// <summary>Ставка должна расти: больше костей, либо та же куча но старше грань.</summary>
        public bool IsLegalBid(int quantity, int face)
        {
            if (quantity < 1 || face < 1 || face > Faces) return false;
            if (quantity > TotalDice) return false;
            if (!HasBid) return true;
            return quantity > bidQuantity || (quantity == bidQuantity && face > bidFace);
        }

        public bool PlaceBid(ulong clientId, int quantity, int face)
        {
            if (state != DiceState.Bidding) return false;
            if (IndexOf(clientId) != currentIndex) return false;
            if (!IsLegalBid(quantity, face)) return false;

            bidQuantity = quantity;
            bidFace = face;
            bidderIndex = currentIndex;
            currentIndex = NextAlive(currentIndex);
            return true;
        }

        /// <summary>"Врёшь!" - вскрываем всех. Возвращает false, если ход не его.</summary>
        public bool Challenge(ulong clientId)
        {
            if (state != DiceState.Bidding || !HasBid) return false;
            if (IndexOf(clientId) != currentIndex) return false;

            lastActual = 0;
            foreach (var p in players)
                if (!p.eliminated) lastActual += p.CountOf(bidFace);

            lastBidWasTrue = lastActual >= bidQuantity;
            // ставка честная - кость теряет усомнившийся, иначе - тот, кто ставил
            lastLoserIndex = lastBidWasTrue ? currentIndex : bidderIndex;

            var loser = players[lastLoserIndex];
            loser.diceCount = Math.Max(0, loser.diceCount - 1);
            if (loser.diceCount == 0) loser.eliminated = true;

            state = AliveCount <= 1 ? DiceState.MatchOver : DiceState.Revealed;
            return true;
        }

        /// <summary>Следующий раунд - вызывать после паузы на показ вскрытия.</summary>
        public void NextRound()
        {
            if (state != DiceState.Revealed) return;
            int starter = players[lastLoserIndex].eliminated
                ? NextAlive(lastLoserIndex) : lastLoserIndex;
            StartRound(starter);
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
    }
}

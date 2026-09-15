using System;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>Очки одного игрока. Структура едет по сети, поэтому сериализуемая.</summary>
    public struct PlayerScore : INetworkSerializable, IEquatable<PlayerScore>
    {
        public ulong clientId;
        public int score;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref clientId);
            s.SerializeValue(ref score);
        }

        public bool Equals(PlayerScore other) =>
            clientId == other.clientId && score == other.score;
    }

    /// <summary>
    /// Общий счёт турнира. Считает только хост, всем остальным список
    /// приезжает готовым - клиент не может дописать себе очки.
    ///
    /// Одна таблица на все мини-игры: очки за топоры, банки и понг ложатся
    /// в общий зачёт, из которого потом выбывают проигравшие.
    /// </summary>
    public class MatchScore : NetworkBehaviour
    {
        public static MatchScore Instance { get; private set; }

        readonly NetworkList<PlayerScore> _scores = new NetworkList<PlayerScore>();

        /// <summary>Что показать в углу экрана последним событием.</summary>
        readonly NetworkVariable<Unity.Collections.FixedString128Bytes> _lastEvent =
            new NetworkVariable<Unity.Collections.FixedString128Bytes>();

        [Tooltip("Сколько секунд висит сообщение о начислении, с.")]
        public float eventSeconds = 4f;

        float _eventAt = -99f;         // когда пришло последнее событие

        void Awake() => Instance = this;

        public override void OnNetworkSpawn()
        {
            // Сообщение живёт несколько секунд и гаснет. Раньше оно висело до
            // следующего события, то есть почти всегда, и закрывало собой двор.
            _lastEvent.OnValueChanged += (_, __) => _eventAt = Time.time;

            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback += EnsureRow;
            foreach (var id in NetworkManager.ConnectedClientsIds) EnsureRow(id);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) NetworkManager.OnClientConnectedCallback -= EnsureRow;
        }

        void EnsureRow(ulong clientId)
        {
            if (IndexOf(clientId) >= 0) return;
            _scores.Add(new PlayerScore { clientId = clientId, score = 0 });
        }

        int IndexOf(ulong clientId)
        {
            for (int i = 0; i < _scores.Count; i++)
                if (_scores[i].clientId == clientId) return i;
            return -1;
        }

        /// <summary>Сколько игроков в зачёте - для табло во дворе.</summary>
        public int RowCount => _scores.Count;

        /// <summary>Строка зачёта по порядку. Для табло во дворе.</summary>
        public PlayerScore Row(int i) => _scores[i];

        /// <summary>Последнее событие - его же показывает табло.</summary>
        public string LastEvent => _lastEvent.Value.ToString();

        public int ScoreOf(ulong clientId)
        {
            int i = IndexOf(clientId);
            return i < 0 ? 0 : _scores[i].score;
        }

        /// <summary>Начислить очки. Только на хосте.</summary>
        public void Add(ulong clientId, int points, string message = null)
        {
            if (!IsServer) return;
            EnsureRow(clientId);
            int i = IndexOf(clientId);
            _scores[i] = new PlayerScore
            {
                clientId = clientId,
                score = _scores[i].score + points
            };
            if (!string.IsNullOrEmpty(message)) _lastEvent.Value = message;
        }

        /// <summary>
        /// Умножить счёт игрока. Нужно колесу фортуны: ноль обнуляет всё
        /// накопленное, 2x и 3x умножают. Только на хосте.
        /// </summary>
        public void Multiply(ulong clientId, int factor)
        {
            if (!IsServer) return;
            EnsureRow(clientId);
            int i = IndexOf(clientId);
            _scores[i] = new PlayerScore
            {
                clientId = clientId,
                score = _scores[i].score * factor
            };
        }

        /// <summary>Показать событие всем, не трогая счёт.</summary>
        public void Announce(string message)
        {
            if (!IsServer || string.IsNullOrEmpty(message)) return;
            _lastEvent.Value = message;
        }

        // ------------------------------------------------------------ интерфейс

        void OnGUI()
        {
            if (_scores.Count == 0) return;

            var box = new GUIStyle(GUI.skin.box)
            { fontSize = 14, alignment = TextAnchor.UpperLeft };

            GUILayout.BeginArea(new Rect(Screen.width - 210f, 20f, 190f, 30f + _scores.Count * 20f),
                                GUIContent.none, box);
            GUILayout.Label("Счёт:");
            for (int i = 0; i < _scores.Count; i++)
            {
                var s = _scores[i];
                bool me = s.clientId == NetworkManager.LocalClientId;
                GUILayout.Label((me ? "Ты" : "Игрок " + s.clientId) + ": " + s.score);
            }
            GUILayout.EndArea();

            string ev = _lastEvent.Value.ToString();
            if (string.IsNullOrEmpty(ev)) return;

            float age = Time.time - _eventAt;
            if (age > eventSeconds) return;

            // последнюю секунду гасим: резкое исчезновение читается как сбой
            float fade = Mathf.Clamp01(eventSeconds - age);

            var big = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            big.normal.textColor = new Color(1f, 0.92f, 0.7f, fade);
            GUI.Label(new Rect(Screen.width * 0.5f - 300f, 60f, 600f, 40f), ev, big);
        }
    }
}

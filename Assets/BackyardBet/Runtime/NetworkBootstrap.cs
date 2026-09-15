using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace BackyardBet
{
    /// <summary>
    /// Вход в сеть: хост создаёт лобби и выделяет Relay-канал, остальные
    /// подключаются по коду комнаты. Дальше игровая логика про Relay/Lobby
    /// ничего не знает - для неё это обычный NetworkManager.
    ///
    /// Требует привязанного Unity Cloud Project ID
    /// (Edit > Project Settings > Services), иначе UnityServices не поднимется.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkBootstrap : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        string _codeField = "";
        string _portField = BasePort.ToString();
        ushort _localPort;
        string _status = "Подключение к Unity Services...";
        bool _busy = true;
        bool _ready;
        Lobby _lobby;

        async void Start()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                _ready = true;
                _status = "Готово. Создай комнату или войди по коду.";
            }
            catch (Exception e)
            {
                _status = "Unity Services недоступны: " + e.Message +
                          "\nПроверь привязку Cloud Project ID в Project Settings > Services.";
            }
            _busy = false;
        }

        async void HostGame()
        {
            _busy = true; _status = "Создаю комнату...";
            try
            {
                // у Relay считаются только гости, хост в лимит не входит
                Allocation alloc = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
                string relayCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

                GetComponent<UnityTransport>().SetHostRelayData(
                    alloc.RelayServer.IpV4, (ushort)alloc.RelayServer.Port,
                    alloc.AllocationIdBytes, alloc.Key, alloc.ConnectionData);

                var options = new CreateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { "relay", new DataObject(DataObject.VisibilityOptions.Member, relayCode) }
                    }
                };
                _lobby = await LobbyService.Instance.CreateLobbyAsync("Backyard Bet", MaxPlayers, options);

                if (!TryStartHost()) return;
                _status = "Комната создана. Код: " + _lobby.LobbyCode;
                StartCoroutine(Heartbeat());
            }
            catch (Exception e)
            {
                _status = "Не удалось создать комнату: " + e.Message;
            }
            _busy = false;
        }

        async void JoinGame(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) { _status = "Введи код комнаты."; return; }
            _busy = true; _status = "Подключаюсь...";
            try
            {
                _lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code);
                JoinAllocation alloc = await RelayService.Instance
                    .JoinAllocationAsync(_lobby.Data["relay"].Value);

                GetComponent<UnityTransport>().SetClientRelayData(
                    alloc.RelayServer.IpV4, (ushort)alloc.RelayServer.Port,
                    alloc.AllocationIdBytes, alloc.Key, alloc.ConnectionData,
                    alloc.HostConnectionData);

                if (!TryStartClient()) return;
                _status = "Подключено к комнате " + code;
            }
            catch (Exception e)
            {
                _status = "Не удалось подключиться: " + e.Message;
            }
            _busy = false;
        }

        // ------------------------------------------------------------ запуск сети

        /// <summary>
        /// StartHost возвращает false, если сеть не поднялась - например,
        /// занят порт или в сцене оказались сломанные сетевые объекты.
        /// Раньше результат не проверялся, и игра рапортовала "хост запущен",
        /// когда на деле не запускалось ничего.
        /// </summary>
        bool TryStartHost()
        {
            if (NetworkManager.Singleton.StartHost()) return true;
            _status = "Не удалось запустить хост. Смотри Console: обычно это " +
                      "занятый порт или ошибка в сетевых объектах сцены.";
            Debug.LogError("[Backyard Bet] StartHost() вернул false.");
            return false;
        }

        bool TryStartClient()
        {
            if (NetworkManager.Singleton.StartClient()) return true;
            _status = "Не удалось запустить клиента. Смотри Console.";
            Debug.LogError("[Backyard Bet] StartClient() вернул false.");
            return false;
        }

        // ------------------------------------------------------------ локальная игра

        public const ushort BasePort = 7777;

        public const string LocalAddress = "127.0.0.1";

        /// <summary>
        /// Ищем свободный UDP-порт. Редактор Unity удерживает 127.0.0.1:7777
        /// после игровой сессии и отпускает только при перезапуске.
        ///
        /// Проверять нужно ровно тот адрес, на который сядет транспорт:
        /// проба на 0.0.0.0 Windows пропускает, даже когда 127.0.0.1 занят,
        /// и подбор портов ошибочно считал 7777 свободным.
        /// </summary>
        static ushort FindFreePort(ushort start, int tries = 16)
        {
            var loopback = System.Net.IPAddress.Parse(LocalAddress);
            for (int i = 0; i < tries; i++)
            {
                ushort port = (ushort)(start + i);
                try
                {
                    var endpoint = new System.Net.IPEndPoint(loopback, port);
                    using (var probe = new System.Net.Sockets.UdpClient(endpoint)) { }
                    return port;
                }
                catch (System.Net.Sockets.SocketException) { /* занят - пробуем дальше */ }
            }
            return 0;
        }

        void StartLocalHost()
        {
            ushort port = FindFreePort(BasePort);
            if (port == 0)
            {
                _status = "Все порты " + BasePort + "-" + (BasePort + 15) + " заняты. " +
                          "Перезапусти редактор Unity.";
                return;
            }

            var transport = GetComponent<UnityTransport>();
            transport.SetConnectionData(LocalAddress, port, LocalAddress);
            Debug.Log("[Backyard Bet] Свободный порт найден: " + port +
                      ", транспорт настроен на " + transport.ConnectionData.Address +
                      ":" + transport.ConnectionData.Port);

            if (!TryStartHost()) return;

            _localPort = port;
            _status = "Локальный хост запущен на порту " + port + ".";
        }

        void StartLocalClient(ushort port)
        {
            GetComponent<UnityTransport>().SetConnectionData(LocalAddress, port);
            if (!TryStartClient()) return;
            _status = "Подключаюсь к локальному хосту на порту " + port + "...";
        }

        // без пинга лобби закрывается примерно через 30 секунд
        IEnumerator Heartbeat()
        {
            var wait = new WaitForSecondsRealtime(15f);
            while (_lobby != null)
            {
                yield return wait;
                _ = LobbyService.Instance.SendHeartbeatPingAsync(_lobby.Id);
            }
        }

        // ------------------------------------------------------------ меню

        Texture2D _panelTex;

        Texture2D PanelTexture()
        {
            if (_panelTex != null) return _panelTex;
            _panelTex = new Texture2D(1, 1);
            _panelTex.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.88f));
            _panelTex.Apply();
            return _panelTex;
        }

        void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            bool live = nm != null && (nm.IsHost || nm.IsClient);

            // В игре на экране не должно быть служебных строк: порт и число
            // игроков нужны, когда игрок остановился и отпустил курсор, а не
            // когда он бежит по двору. Показываем их только в этот момент.
            if (live)
            {
                if (Cursor.lockState != CursorLockMode.Locked) DrawInGameBar(nm);
                return;
            }
            DrawMenu();
        }

        /// <summary>Служебная строка на паузе: порт для второго окна и счётчик игроков.</summary>
        void DrawInGameBar(NetworkManager nm)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            s.normal.textColor = new Color(1f, 0.92f, 0.72f);

            GUI.DrawTexture(new Rect(12f, 12f, 340f, 46f), PanelTexture());
            GUI.Label(new Rect(22f, 18f, 320f, 20f), _status, s);
            string port = _localPort > 0 ? "   ·   порт " + _localPort : "";
            GUI.Label(new Rect(22f, 36f, 320f, 20f),
                      "Игроков: " + nm.ConnectedClientsIds.Count + "   ·   Esc — курсор" + port, s);
        }

        static ushort ParsePort(string s) =>
            ushort.TryParse(s.Trim(), out ushort p) && p > 0 ? p : BasePort;

        static Texture2D _fade;

        /// <summary>Заливка на весь экран - фон меню и его подложки.</summary>
        static Texture2D Fill
        {
            get
            {
                if (_fade == null)
                {
                    _fade = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
                    _fade.SetPixel(0, 0, Color.white);
                    _fade.Apply();
                }
                return _fade;
            }
        }

        static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Fill);
            GUI.color = old;
        }

        static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            var was = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = was;
        }

        /// <summary>
        /// Главное меню на весь экран.
        ///
        /// Раньше это была панель на 420 пикселей посреди пустоты - вид
        /// отладочного окна, а не игры. Здесь затемнённый двор на фоне,
        /// крупное имя, кнопки в столбик и подсказка по управлению внизу:
        /// новый игрок должен понимать, что нажимать, не спрашивая.
        /// </summary>
        void DrawMenu()
        {
            float sw = Screen.width, sh = Screen.height;

            // Двор виден сквозь затемнение - он и есть лучшая заставка,
            // которая у нас уже нарисована.
            Box(new Rect(0f, 0f, sw, sh), new Color(0.04f, 0.05f, 0.09f, 0.72f));

            float w = Mathf.Min(520f, sw - 40f);
            float x = (sw - w) * 0.5f;
            float y = Mathf.Max(30f, sh * 0.5f - 250f);

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 52,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            // лёгкое дыхание: неподвижный заголовок выглядит как картинка
            float pulse = 1f + Mathf.Sin(Time.realtimeSinceStartup * 1.6f) * 0.06f;
            Shadowed(new Rect(x, y, w, 62f), "BACKYARD BET", title,
                     new Color(1f, 0.8f + 0.06f * pulse, 0.3f));

            var sub = new GUIStyle(GUI.skin.label)
            { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            sub.normal.textColor = new Color(0.82f, 0.82f, 0.86f);
            GUI.Label(new Rect(x, y + 64f, w, 24f),
                      "Пьяные дворовые игры на вылет  ·  до 4 игроков", sub);

            var btn = new GUIStyle(GUI.skin.button)
            { fontSize = 18, fontStyle = FontStyle.Bold };
            var small = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            var hint = new GUIStyle(GUI.skin.label)
            { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            hint.normal.textColor = new Color(0.62f, 0.64f, 0.70f);

            float bx = x + 60f, bw = w - 120f, by = y + 108f;

            // Локальная игра первой: она не требует ни интернета, ни Unity
            // Services, поэтому с неё игра запускается всегда.
            if (GUI.Button(new Rect(bx, by, bw, 46f), "ИГРАТЬ ВО ДВОРЕ", btn))
                StartLocalHost();
            GUI.Label(new Rect(bx, by + 48f, bw, 18f),
                      "одиночная игра с ботами, порт подбирается сам", hint);

            by += 78f;
            GUI.enabled = !_busy && _ready;
            if (GUI.Button(new Rect(bx, by, bw, 40f), "СОЗДАТЬ КОМНАТУ", btn))
                HostGame();
            by += 46f;
            _codeField = GUI.TextField(new Rect(bx, by, bw * 0.52f, 34f), _codeField);
            if (GUI.Button(new Rect(bx + bw * 0.56f, by, bw * 0.44f, 34f),
                           "Войти по коду", small))
                JoinGame(_codeField.Trim().ToUpperInvariant());
            GUI.enabled = true;

            by += 46f;
            GUI.Label(new Rect(bx, by, bw * 0.52f, 22f), "второе окно на этой машине:", hint);
            _portField = GUI.TextField(new Rect(bx + bw * 0.56f, by, bw * 0.18f, 22f),
                                       _portField);
            if (GUI.Button(new Rect(bx + bw * 0.78f, by, bw * 0.22f, 22f), "Клиент", small))
                StartLocalClient(ParsePort(_portField));

            by += 40f;
            if (GUI.Button(new Rect(bx + bw * 0.3f, by, bw * 0.4f, 30f), "Выход", small))
                Quit();

            // ---- управление и состояние
            by += 46f;
            Box(new Rect(x, by, w, 74f), new Color(0f, 0f, 0f, 0.35f));
            var keys = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.UpperCenter, wordWrap = true };
            keys.normal.textColor = new Color(0.80f, 0.82f, 0.88f);
            GUI.Label(new Rect(x + 16f, by + 8f, w - 32f, 20f),
                      "WASD — идти   ·   Space — прыжок   ·   E — взять или открыть", keys);
            GUI.Label(new Rect(x + 16f, by + 28f, w - 32f, 20f),
                      "ЛКМ удерживать — замах и бросок   ·   R — отпить из бутылки", keys);
            GUI.Label(new Rect(x + 16f, by + 48f, w - 32f, 20f),
                      "Esc — встать из-за стола и освободить курсор", keys);

            var st = new GUIStyle(GUI.skin.label)
            { fontSize = 12, wordWrap = true, alignment = TextAnchor.UpperCenter };
            st.normal.textColor = _ready ? new Color(0.72f, 0.88f, 0.74f)
                                         : new Color(0.94f, 0.76f, 0.56f);
            GUI.Label(new Rect(x, by + 80f, w, 52f), _status, st);
        }

        /// <summary>Выйти из игры. В редакторе - остановить проигрывание.</summary>
        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

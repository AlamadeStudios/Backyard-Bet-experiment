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

            if (live) { DrawInGameBar(nm); return; }
            DrawMenu();
        }

        /// <summary>В игре нужна одна строка в углу, а не панель на пол-экрана.</summary>
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

        void DrawMenu()
        {
            float w = 420f, h = 350f;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.DrawTexture(new Rect(x, y, w, h), PanelTexture());

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            title.normal.textColor = new Color(1f, 0.84f, 0.35f);
            GUI.Label(new Rect(x, y + 18f, w, 44f), "BACKYARD BET", title);

            var sub = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            sub.normal.textColor = new Color(0.78f, 0.78f, 0.80f);
            GUI.Label(new Rect(x + 24f, y + 60f, w - 48f, 20f),
                      "Пьяные игры на вылет · до 4 игроков", sub);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            float bx = x + 40f, bw = w - 80f;

            // Локальный режим не требует Unity Services и Cloud Project ID -
            // поэтому он идёт первым: с него игра запускается всегда.
            GUI.Label(new Rect(bx, y + 92f, bw, 18f),
                      "Играть на этой машине (порт подбирается сам):", sub);
            if (GUI.Button(new Rect(bx, y + 112f, bw * 0.48f, 34f), "Хост", btn))
                StartLocalHost();
            if (GUI.Button(new Rect(bx + bw * 0.52f, y + 112f, bw * 0.48f, 34f), "Клиент", btn))
                StartLocalClient(ParsePort(_portField));

            // порт нужен второму окну на этой же машине: хост показывает свой,
            // клиент вводит его сюда
            GUI.Label(new Rect(bx, y + 150f, bw * 0.44f, 20f), "Порт клиента:", sub);
            _portField = GUI.TextField(new Rect(bx + bw * 0.46f, y + 148f, bw * 0.22f, 22f),
                                       _portField);

            GUI.Label(new Rect(bx, y + 176f, bw, 18f), "Играть с друзьями по сети:", sub);
            GUI.enabled = !_busy && _ready;
            if (GUI.Button(new Rect(bx, y + 196f, bw, 30f), "Создать комнату", btn))
                HostGame();

            _codeField = GUI.TextField(new Rect(bx, y + 232f, bw * 0.46f, 28f), _codeField);
            if (GUI.Button(new Rect(bx + bw * 0.5f, y + 232f, bw * 0.5f, 28f), "Войти по коду", btn))
                JoinGame(_codeField.Trim().ToUpperInvariant());
            GUI.enabled = true;

            var st = new GUIStyle(GUI.skin.label)
            { fontSize = 12, wordWrap = true, alignment = TextAnchor.UpperCenter };
            st.normal.textColor = _ready ? new Color(0.72f, 0.85f, 0.72f)
                                         : new Color(0.92f, 0.72f, 0.55f);
            GUI.Label(new Rect(x + 24f, y + 268f, w - 48f, 66f), _status, st);
        }
    }
}

using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Готовит сеть при запуске игры: регистрирует сетевые заготовки и ставит
    /// создателя GameRoot.
    ///
    /// Раньше это делал редакторский AutoSetup перед входом в Play. В
    /// собранной игре редактора нет, и всё это не происходило: сцена
    /// поднималась без заготовок и без создателя GameRoot, то есть без счёта,
    /// стола, дверей и предметов. В редакторе всё работало, в exe - нет.
    ///
    /// Заготовки берём из Resources: только оттуда их видно в сборке, если на
    /// них не ссылается ни один объект сцены.
    /// </summary>
    public static class RuntimeNetworkSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Setup()
        {
            var nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("[Backyard Bet] В сцене нет NetworkManager - сеть не поднять.");
                return;
            }

            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();

            var player = Resources.Load<GameObject>("Player");
            var root = Resources.Load<GameObject>("GameRoot");

            if (player == null || root == null)
            {
                Debug.LogError("[Backyard Bet] В Resources нет заготовок Player/GameRoot - " +
                               "игра запустится пустой.");
                return;
            }

            if (nm.NetworkConfig.PlayerPrefab == null)
                nm.NetworkConfig.PlayerPrefab = player;

            Register(nm, player);
            Register(nm, root);

            // Создателя GameRoot вешаем на тот же объект: он ждёт запуска
            // хоста и только тогда создаёт логику партии.
            var spawner = nm.GetComponent<GameRootSpawner>();
            if (spawner == null) spawner = nm.gameObject.AddComponent<GameRootSpawner>();
            if (spawner.gameRootPrefab == null) spawner.gameRootPrefab = root;

            Debug.Log("[Backyard Bet] Сеть готова: заготовки зарегистрированы, GameRoot ждёт хоста.");
        }

        /// <summary>
        /// Добавить заготовку в список сетевых, если её там ещё нет.
        ///
        /// Повторная регистрация того же объекта - ошибка Netcode, а в
        /// редакторе список уже мог заполнить AutoSetup, поэтому сверяемся.
        /// </summary>
        static void Register(NetworkManager nm, GameObject prefab)
        {
            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError("[Backyard Bet] У заготовки " + prefab.name +
                               " нет NetworkObject - она не заспавнится.");
                return;
            }

            var list = nm.NetworkConfig.Prefabs;
            foreach (var p in list.Prefabs)
                if (p != null && p.Prefab == prefab) return;

            list.Add(new Unity.Netcode.NetworkPrefab { Prefab = prefab });
        }
    }
}

using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace BackyardBet
{
    /// <summary>
    /// Создаёт GameRoot - объект с логикой партии (счёт, состояние дверей,
    /// стол блефа) - как только поднялся хост.
    ///
    /// Почему не положить эти компоненты прямо в сцену: сетевые объекты,
    /// расставленные в сцене заранее, оживают не всегда и зависят от настроек
    /// менеджера сцен. Объект, созданный и заспавненный в рантайме, работает
    /// одинаково у хоста и у всех клиентов - этот путь уже проверен на
    /// префабе игрока.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class GameRootSpawner : MonoBehaviour
    {
        [Tooltip("Префаб с MatchScore, WorldState и BluffTable.")]
        public GameObject gameRootPrefab;

        NetworkManager _nm;

        void Awake()
        {
            _nm = GetComponent<NetworkManager>();
            _nm.OnServerStarted += SpawnGameRoot;
        }

        void OnDestroy()
        {
            if (_nm != null) _nm.OnServerStarted -= SpawnGameRoot;
        }

        void SpawnGameRoot()
        {
            if (gameRootPrefab == null)
            {
                Debug.LogError("[Backyard Bet] Не задан префаб GameRoot - логика игры не запустится.");
                return;
            }
            if (BluffTable.Instance != null) return;      // уже создан

            if (gameRootPrefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError("[Backyard Bet] У префаба GameRoot нет NetworkObject.");
                return;
            }

            var go = Instantiate(gameRootPrefab);
            go.name = "GameRoot";

            // Spawn у префаба без сетевого идентификатора не просто вернёт
            // отказ, а бросит исключение - тогда проверка ниже не отработала
            // бы и сбой снова остался незамеченным.
            try
            {
                go.GetComponent<NetworkObject>().Spawn();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Backyard Bet] Не удалось заспавнить GameRoot: " + e.Message);
            }

            // Спавн может тихо провалиться, если префаб собран на лету и
            // Netcode не присвоил ему идентификатор. Тогда вся логика игры
            // остаётся мёртвой, и раньше это никак не проявлялось - проверяем.
            StartCoroutine(VerifySpawn());
        }

        IEnumerator VerifySpawn()
        {
            yield return null;                       // даём кадр на инициализацию

            if (BluffTable.Instance != null && WorldState.Instance != null &&
                PropNetwork.Instance != null)
            {
                Debug.Log("[Backyard Bet] GameRoot запущен: счёт, двери, предметы и стол готовы.");
                yield break;
            }

            Debug.LogError("[Backyard Bet] GameRoot не заспавнился - двери и предметы работать " +
                           "не будут. Обычно помогает переимпорт префаба: выдели " +
                           "Assets/BackyardBet/Resources/GameRoot.prefab и нажми Assets > Reimport.");
        }
    }
}

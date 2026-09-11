using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using BackyardBet;

/// <summary>
/// Готовит проект и сцену к запуску без ручных шагов в редакторе.
///
/// Разнесено на два момента, и это принципиально:
///  * префаб GameRoot создаётся при перекомпиляции скриптов и сразу
///    импортируется - только тогда Netcode присваивает ему сетевой
///    идентификатор. Если создать префаб прямо перед Play, идентификатор
///    остаётся нулевым, объект молча не спавнится, и вся логика игры
///    (двери, предметы, стол) оказывается мертва;
///  * ссылки в сцене проставляются перед входом в Play - они живут только
///    в памяти сессии и не спорят с открытым редактором.
/// </summary>
[InitializeOnLoad]
public static class AutoSetup
{
    const string GameRootPath = "Assets/BackyardBet/Prefabs/GameRoot.prefab";

    static AutoSetup()
    {
        // отложенно: во время перезагрузки домена AssetDatabase трогать нельзя
        EditorApplication.delayCall += EnsureGameRootPrefab;
        EditorApplication.delayCall += EnsurePlayerPrefab;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        WireScene();
    }

    /// <summary>
    /// Пересобрать префаб игрока, если в нём не хватает свежих компонентов.
    ///
    /// Раньше это был пункт меню, и его просто не нажимали: код посадки за
    /// стол был написан, а в префабе его не было - игра молча ничего не
    /// делала. Проверяем по самому новому компоненту.
    ///
    /// Делается при перекомпиляции, а не перед Play: префабу нужно успеть
    /// пройти импорт, иначе Netcode не присвоит ему идентификатор и игрок
    /// просто не заспавнится.
    /// </summary>
    static void EnsurePlayerPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/BackyardBet/Prefabs/Player.prefab");
        if (prefab != null && prefab.GetComponent<PlayerSeating>() != null) return;

        Debug.Log("[Backyard Bet] В префабе игрока не хватает компонентов - пересобираю.");
        GameplaySetup.RebuildPlayer();
        AssetDatabase.ImportAsset("Assets/BackyardBet/Prefabs/Player.prefab",
                                  ImportAssetOptions.ForceUpdate);
    }

    // ------------------------------------------------------------ префаб

    static void EnsureGameRootPrefab()
    {
        // проверяем по самому свежему компоненту: если его нет, префаб собран
        // старой версией скрипта и его надо пересоздать
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPath);
        if (existing != null && existing.GetComponent<FortuneWheel>() != null) return;
        if (existing != null) AssetDatabase.DeleteAsset(GameRootPath);

        var go = new GameObject("GameRoot");
        go.AddComponent<NetworkObject>();
        go.AddComponent<MatchScore>();
        go.AddComponent<WorldState>();
        go.AddComponent<PropNetwork>();
        go.AddComponent<BluffTable>();
        go.AddComponent<FortuneWheel>();

        Directory.CreateDirectory(Path.GetDirectoryName(GameRootPath));
        PrefabUtility.SaveAsPrefabAsset(go, GameRootPath);
        Object.DestroyImmediate(go);

        // импорт обязателен: именно на нём Netcode проставляет
        // GlobalObjectIdHash, без которого объект не заспавнится
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(GameRootPath, ImportAssetOptions.ForceUpdate);

        Debug.Log("[Backyard Bet] GameRoot создан и импортирован: " + GameRootPath);
    }

    // ------------------------------------------------------------ сцена

    static void WireScene()
    {
        var nm = Object.FindAnyObjectByType<NetworkManager>();
        if (nm == null) return;                       // не игровая сцена

        // у менеджера, добавленного скриптом, конфиг может быть пустым
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPath);
        if (prefab == null)
        {
            Debug.LogError("[Backyard Bet] Нет префаба GameRoot - логика игры не запустится. " +
                           "Дай скриптам перекомпилироваться и попробуй снова.");
            return;
        }

        // Список префабов доступен только для чтения, удалять из него напрямую
        // нельзя - поэтому битые записи просто пропускаем при поиске.
        bool known = false;
        int broken = 0;
        foreach (var p in nm.NetworkConfig.Prefabs.Prefabs)
        {
            if (p == null || p.Prefab == null) { broken++; continue; }
            if (p.Prefab == prefab) { known = true; break; }
        }
        if (!known) nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });

        if (broken > 0)
            Debug.LogWarning("[Backyard Bet] В списке сетевых префабов пустых записей: " + broken +
                             ". Их стоит убрать в инспекторе NetworkManager.");

        var spawner = nm.GetComponent<GameRootSpawner>();
        if (spawner == null) spawner = nm.gameObject.AddComponent<GameRootSpawner>();
        spawner.gameRootPrefab = prefab;

        // сетевому компоненту нужен NetworkObject, а на менеджере его нет
        var stray = nm.GetComponent<MatchScore>();
        if (stray != null) Object.DestroyImmediate(stray, true);

        if (nm.GetComponent<PlayerSpawnPoints>() == null)
            nm.gameObject.AddComponent<PlayerSpawnPoints>();
    }
}

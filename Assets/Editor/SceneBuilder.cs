using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using BackyardBet;

/// <summary>
/// Собирает игровую сцену из импортированной карты: свет, префаб игрока,
/// NetworkManager с транспортом и скриптами подключения/спавна.
/// Запуск: меню Backyard Bet > Собрать сцену, либо -executeMethod SceneBuilder.Build
/// </summary>
public static class SceneBuilder
{
    const string MapPath = "Assets/BackyardBet/Map/BackyardBet.fbx";
    const string PrefabPath = "Assets/BackyardBet/Prefabs/Player.prefab";
    const string ScenePath = "Assets/BackyardBet/Scenes/Backyard.unity";

    [MenuItem("Backyard Bet/Собрать сцену")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var mapAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MapPath);
        if (mapAsset == null) { Debug.LogError("### НЕТ КАРТЫ: " + MapPath); return; }
        var map = (GameObject)PrefabUtility.InstantiatePrefab(mapAsset);
        map.name = "Map";

        // В FBX источников света нет - иначе сцена будет чёрной
        var sun = new GameObject("Sun");
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.color = new Color(1f, 0.95f, 0.85f);
        light.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

        var playerPrefab = BuildPlayerPrefab();

        var nmGo = new GameObject("NetworkManager");
        var nm = nmGo.AddComponent<NetworkManager>();
        var transport = nmGo.AddComponent<UnityTransport>();

        // У добавленного скриптом NetworkManager конфиг пустой: его заполняет
        // инспектор. Без этой строки следующая же строка падает с NullReference
        // и сборка сцены обрывается на полпути.
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();

        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.PlayerPrefab = playerPrefab;
        nmGo.AddComponent<NetworkBootstrap>();
        nmGo.AddComponent<PlayerSpawnPoints>();

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("### СЦЕНА СОБРАНА: " + ScenePath);
    }

    /// <summary>
    /// Сборка игрока живёт в GameplaySetup - здесь только вызываем её, чтобы
    /// пересборка сцены не вернула старую капсулу вместо модели персонажа.
    /// </summary>
    static GameObject BuildPlayerPrefab()
    {
        GameplaySetup.RebuildPlayer();
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }
}

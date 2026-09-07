using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using BackyardBet;

/// <summary>
/// Пересборка префаба игрока с моделью персонажа.
///
/// Всё остальное - двери, реквизит, мишени, стол, точки спавна - собирается
/// в рантайме компонентом WorldBuilder, а логика партии готовится
/// автоматически перед Play (см. AutoSetup). Пунктов меню для них больше нет:
/// правки сцены терялись, когда редактор держал её открытой.
/// </summary>
public static class GameplaySetup
{
    const string PrefabPath = "Assets/BackyardBet/Prefabs/Player.prefab";
    const string CharPath = "Assets/BackyardBet/Characters/CH_Bo.fbx";

    // Размеры из make_character() в backyard_bet.py: ступни на 0, глаза на
    // 2.28, макушка ~2.56. Контроллер обязан совпадать с моделью, иначе
    // персонаж тонет в земле или парит над ней.
    const float BodyHeight = 2.50f;
    const float EyeHeight = 2.28f;

    [MenuItem("Backyard Bet/Пересобрать игрока с моделью")]
    public static void RebuildPlayer()
    {
        var charAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CharPath);
        if (charAsset == null) { Debug.LogError("### НЕТ МОДЕЛИ: " + CharPath); return; }

        var go = new GameObject("Player");

        var cc = go.AddComponent<CharacterController>();
        cc.height = BodyHeight;
        cc.radius = 0.38f;
        cc.center = new Vector3(0f, BodyHeight * 0.5f, 0f);
        cc.skinWidth = 0.08f;        // толстая "кожа" - от проваливания в стыки
        cc.stepOffset = 0.45f;       // мусор и трава под ногами не должны стопорить

        // модель персонажа: начало координат у неё в ступнях, ставим как есть
        var body = (GameObject)PrefabUtility.InstantiatePrefab(charAsset);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);

        var pivot = new GameObject("CameraPivot").transform;
        pivot.SetParent(go.transform, false);
        pivot.localPosition = new Vector3(0f, EyeHeight, 0f);

        var camGo = new GameObject("PlayerCamera");
        camGo.transform.SetParent(pivot, false);
        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();

        // Точка руки висит на повороте камеры: предмет летит туда, куда
        // смотришь. Рука персонажа не анимирована, привязывать к ней нечего.
        var hand = new GameObject("HandPoint").transform;
        hand.SetParent(pivot, false);
        hand.localPosition = new Vector3(0.42f, -0.45f, 0.75f);

        go.AddComponent<NetworkObject>();
        go.AddComponent<ClientNetworkTransform>();
        var move = go.AddComponent<NetworkPlayerMovement>();
        move.cameraPivot = pivot;

        var inter = go.AddComponent<PlayerInteraction>();
        inter.aim = camGo.transform;
        inter.handPoint = hand;

        go.AddComponent<PlayerAvatar>();

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);

        var nm = Object.FindAnyObjectByType<NetworkManager>();
        if (nm != null)
        {
            nm.NetworkConfig.PlayerPrefab = prefab;
            EditorUtility.SetDirty(nm);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("### ИГРОК ПЕРЕСОБРАН С МОДЕЛЬЮ: рост " + BodyHeight +
                  " м, глаза на " + EyeHeight + " м");
    }
}

using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using BackyardBet;

/// <summary>Проверка, что сцена собрана целиком: меню Backyard Bet > Проверить сборку</summary>
public static class SetupCheck
{
    [MenuItem("Backyard Bet/Проверить сборку")]
    public static void Check()
    {
        var nm = Object.FindAnyObjectByType<NetworkManager>();
        Debug.Log("### NetworkManager: " + (nm != null));
        if (nm != null && nm.NetworkConfig == null)
            Debug.LogError("### У NetworkManager пустой NetworkConfig - пересобери сцену");
        if (nm != null && nm.NetworkConfig != null)
        {
            Debug.Log("### PlayerPrefab: " +
                      (nm.NetworkConfig.PlayerPrefab != null ? nm.NetworkConfig.PlayerPrefab.name : "НЕТ"));
            Debug.Log("### Transport: " +
                      (nm.NetworkConfig.NetworkTransport != null
                          ? nm.NetworkConfig.NetworkTransport.GetType().Name : "НЕТ"));

            var spawner = nm.GetComponent<GameRootSpawner>();
            Debug.Log("### GameRootSpawner: " + (spawner != null) +
                      ", префаб: " + (spawner != null && spawner.gameRootPrefab != null
                                      ? spawner.gameRootPrefab.name : "НЕТ"));
            Debug.Log("### Сетевых префабов: " + nm.NetworkConfig.Prefabs.Prefabs.Count);

            // сетевому компоненту нужен NetworkObject - на менеджере его нет
            if (nm.GetComponent<MatchScore>() != null)
                Debug.LogError("### ОШИБКА: MatchScore висит на NetworkManager, ему там не место");
        }

        Debug.Log("### NetworkBootstrap: " + (Object.FindAnyObjectByType<NetworkBootstrap>() != null));
        Debug.Log("### PlayerSpawnPoints: " + (Object.FindAnyObjectByType<PlayerSpawnPoints>() != null));

        var map = GameObject.Find("Map");
        Debug.Log("### Map: " + (map != null ? map.transform.childCount + " объектов" : "НЕТ"));

        int seats = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("Seat_")) seats++;
        Debug.Log("### Точек спавна Seat_*: " + seats);

        Debug.Log("### Дверей: " +
                  Object.FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None).Length);
        Debug.Log("### Предметов: " +
                  Object.FindObjectsByType<PickupInteractable>(FindObjectsSortMode.None).Length);
        Debug.Log("### Мишеней: " +
                  Object.FindObjectsByType<ThrowTarget>(FindObjectsSortMode.None).Length);
        Debug.Log("### Стол блефа: " +
                  Object.FindObjectsByType<BluffTableSeat>(FindObjectsSortMode.None).Length);

        int mannequins = 0;
        foreach (var n in new[] { "CH_Bo", "CH_Mia", "CH_Rex", "CH_Sam" })
            if (GameObject.Find(n) != null) mannequins++;
        Debug.Log("### Болванок на карте: " + mannequins + " (должно быть 0)");
    }
}

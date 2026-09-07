using UnityEditor;
using UnityEngine;

/// <summary>
/// Включает генерацию коллайдеров у карты. Без этого FBX приезжает без
/// физической оболочки вообще: земля не земля, забор не забор, игрок
/// проваливается сквозь мир при первом же кадре.
///
/// Коллайдеры пекутся в сам ассет модели, а не в сцену - сцена остаётся
/// чистой, а при переэкспорте карты из Blender ничего не надо переделывать.
///
/// Меню: Backyard Bet > Включить коллайдеры карты
/// </summary>
public static class ColliderSetup
{
    const string FbxPath = "Assets/BackyardBet/Map/BackyardBet.fbx";

    [MenuItem("Backyard Bet/Включить коллайдеры карты")]
    public static void Enable()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        if (importer == null) { Debug.LogError("### НЕТ КАРТЫ: " + FbxPath); return; }

        if (!importer.addCollider)
        {
            importer.addCollider = true;
            importer.SaveAndReimport();
            Debug.Log("### КОЛЛАЙДЕРЫ ВКЛЮЧЕНЫ, карта переимпортирована");
        }
        else
        {
            Debug.Log("### КОЛЛАЙДЕРЫ УЖЕ БЫЛИ ВКЛЮЧЕНЫ");
        }

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        int n = asset != null ? asset.GetComponentsInChildren<MeshCollider>(true).Length : 0;
        Debug.Log("### КОЛЛАЙДЕРОВ В КАРТЕ: " + n);
    }
}

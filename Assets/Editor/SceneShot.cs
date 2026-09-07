using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Рендер сцены в PNG из батч-режима - визуальная проверка импорта.</summary>
public static class SceneShot
{
    public static void Shoot()
    {
        EditorSceneManager.OpenScene("Assets/BackyardBet/Scenes/Backyard.unity");

        var camGo = new GameObject("ShotCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 55f;

        Shot(cam, new Vector3(14f, 12f, -16f), new Vector3(0f, 4f, 0f), "shot_overview.png");
        Shot(cam, new Vector3(0f, 3.2f, -7.5f), new Vector3(0f, 1.2f, 4f), "shot_table.png");

        // ---- персонаж игрока: ставим префаб рядом со столом и снимаем сбоку
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/BackyardBet/Prefabs/Player.prefab");
        GameObject player = null;
        if (prefab != null)
        {
            player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.position = new Vector3(0f, 0f, -2f);
            player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            // камера игрока в префабе выключена не будет - убираем, чтобы не мешала съёмке
            foreach (var c in player.GetComponentsInChildren<Camera>(true))
                c.gameObject.SetActive(false);
            foreach (var a in player.GetComponentsInChildren<AudioListener>(true))
                a.enabled = false;

            Shot(cam, new Vector3(2.6f, 2.2f, -4.4f), new Vector3(0f, 1.5f, -2f),
                 "shot_player.png");
        }
        else
        {
            Debug.LogError("### НЕТ ПРЕФАБА ИГРОКА");
        }

        // ---- станция с топорами: находим мишень и смотрим на неё
        var target = GameObject.Find("AxeTarget");
        if (target != null)
        {
            Vector3 t = target.transform.position;
            Shot(cam, t + new Vector3(-7f, 3.5f, 0f), t, "shot_axe_station.png");
        }
        else
        {
            Debug.LogError("### НЕ НАЙДЕНА МИШЕНЬ AxeTarget");
        }

        if (player != null) Object.DestroyImmediate(player);
        Object.DestroyImmediate(camGo);
        Debug.Log("### SHOTS DONE");
    }

    static void Shot(Camera cam, Vector3 pos, Vector3 lookAt, string file)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(lookAt);

        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), file), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Debug.Log("### SHOT " + file);
    }
}

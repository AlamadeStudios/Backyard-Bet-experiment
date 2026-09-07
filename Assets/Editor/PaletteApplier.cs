using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Красит материалы, приехавшие из FBX, по палитре из Blender.
///
/// Зачем: процедурные материалы Blender (трава, кора, листва, камень - всё,
/// что сделано через M_patchy) в FBX не переносятся и приезжают в Unity
/// белыми. backyard_palette.json несёт для них плоский цвет, посчитанный
/// на стороне Blender.
///
/// Меню: Backyard Bet > Применить палитру
/// </summary>
public static class PaletteApplier
{
    const string FbxPath = "Assets/BackyardBet/Map/BackyardBet.fbx";
    const string JsonPath = "Assets/BackyardBet/Map/backyard_palette.json";

    [Serializable]
    public class Entry
    {
        public string name;
        public float[] color;
        public float rough;
        public float metal;
        public float alpha;
        public float emit;
        public float[] emit_color;
    }

    [Serializable]
    public class Palette
    {
        public Entry[] materials;
    }

    [MenuItem("Backyard Bet/Применить палитру")]
    public static void Apply()
    {
        var json = File.ReadAllText(JsonPath);
        var palette = JsonUtility.FromJson<Palette>(json);
        var byName = new Dictionary<string, Entry>();
        foreach (var e in palette.materials) byName[e.name] = e;
        Debug.Log("### ПАЛИТРА: " + byName.Count + " материалов");

        // Материалы внутри FBX доступны только на чтение - вытаскиваем их
        // наружу отдельными ассетами, иначе перекрасить нельзя.
        var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        if (importer.materialLocation != ModelImporterMaterialLocation.External)
        {
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
            Debug.Log("### МАТЕРИАЛЫ ВЫНЕСЕНЫ ИЗ FBX");
        }

        string dir = Path.GetDirectoryName(FbxPath) + "/Materials";
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { dir });
        int painted = 0, missed = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            if (!byName.TryGetValue(mat.name, out var e)) { missed++; continue; }

            var col = new Color(e.color[0], e.color[1], e.color[2], e.alpha);
            SetColor(mat, col);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", e.metal);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 1f - e.rough);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1f - e.rough);

            if (e.emit > 0.001f && e.emit_color != null && e.emit_color.Length >= 3)
            {
                var ec = new Color(e.emit_color[0], e.emit_color[1], e.emit_color[2]) * e.emit;
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", ec);
            }

            if (e.alpha < 0.999f) MakeTransparent(mat, col);
            EditorUtility.SetDirty(mat);
            painted++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("### ПОКРАШЕНО: " + painted + ", без пары в палитре: " + missed);
    }

    static void SetColor(Material mat, Color c)
    {
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);   // URP
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);           // Built-in
    }

    static void MakeTransparent(Material mat, Color c)
    {
        // Standard shader: режим прозрачности выставляется вручную, одного
        // альфа-канала в цвете недостаточно.
        if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);      // URP
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
}

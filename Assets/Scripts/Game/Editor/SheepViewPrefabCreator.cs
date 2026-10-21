#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成羊了个羊的牌 / 槽位预制体。
/// 注意：预制体里只挂 UGUI 内置组件（RectTransform / Image / Button / Text），
/// 不挂热更脚本 —— 红线：热更程序集不能被预制体序列化。
/// 运行时由 GamePanel 经对象池 Spawn / Despawn，预制体走 YooAsset 可热更。
/// </summary>
public static class SheepViewPrefabCreator
{
    [MenuItem("Tools/Sheep/创建牌/槽位预制体")]
    public static void CreatePrefabs()
    {
        CreateTile();
        CreateSlot();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Sheep", "已生成 SheepTile.prefab / SheepSlot.prefab（Assets/Prefabs/）", "OK");
    }

    private static void CreateTile()
    {
        GameObject root = new GameObject("SheepTile");
        RectTransform rt = root.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(80f, 80f);

        Image image = root.AddComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = true;

        Button button = root.AddComponent<Button>();
        button.transition = Selectable.Transition.None;   // 不接管颜色，颜色由代码直接设 image.color
        button.targetGraphic = image;

        AddLabel(root, 30);

        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/SheepTile.prefab");
        Object.DestroyImmediate(root);
    }

    private static void CreateSlot()
    {
        GameObject root = new GameObject("SheepSlot");
        RectTransform rt = root.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(88f, 88f);

        Image image = root.AddComponent<Image>();
        image.color = new Color(0.15f, 0.17f, 0.22f, 0.85f);
        image.raycastTarget = false;

        AddLabel(root, 32);

        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/SheepSlot.prefab");
        Object.DestroyImmediate(root);
    }

    private static void AddLabel(GameObject parent, int fontSize)
    {
        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(parent.transform, false);
        RectTransform labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        Text label = labelGo.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
    }
}
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成收藏界面预制体 CollectionPanel.prefab（图鉴总入口）。
/// 结构：
///   CollectionPanel（根：RectTransform + Image 底 + CollectionPanel 脚本）
///   ├─ TitleText（标题「收藏」）
///   ├─ CloseButton（关闭）
///   ├─ SheepBtn（羊图鉴 Tab）
///   ├─ BrickBtn（砖块图鉴 Tab，暂未做）
///   └─ RefreshArea（Image + RectMask2D 刷新区域，运行时挂 ScrollRect 显示图鉴内容）
/// </summary>
public static class CollectionPanelPrefabCreator
{
    [MenuItem("Tools/Sheep/创建收藏面板预制体")]
    public static void CreatePrefab()
    {
        GameObject root = new GameObject("CollectionPanel", typeof(RectTransform));

        // 根：半透明白底
        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.95f, 0.95f, 0.97f, 1f);
        bg.raycastTarget = true;

        RectTransform rootRt = root.transform as RectTransform;
        rootRt.anchorMin = new Vector2(0f, 0f);
        rootRt.anchorMax = new Vector2(1f, 1f);
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        // 标题
        AddText(root, "TitleText", "收藏", 40, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -40f), new Vector2(600f, 60f));

        // 关闭按钮（右上角）
        AddButton(root, "CloseButton", "×", 36, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-40f, -40f), new Vector2(70f, 70f));

        // Tab：羊 / 砖块
        AddButton(root, "SheepBtn", "羊图鉴", 30, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-120f, -110f), new Vector2(200f, 56f));
        AddButton(root, "BrickBtn", "砖块图鉴", 30, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(120f, -110f), new Vector2(200f, 56f));

        // 刷新区域（图鉴内容显示在这，运行时挂 ScrollRect）
        AddArea(root, "RefreshArea", new Color(0.88f, 0.88f, 0.92f, 1f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 620f));

        // 挂收藏面板脚本（热更类，编辑器里编译成普通程序集，和 MainPanel 一样）
        CollectionPanel panel = root.AddComponent<CollectionPanel>();

        PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/CollectionPanel.prefab");
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Sheep", "已生成 CollectionPanel.prefab（Assets/Prefabs/）", "OK");
    }

    private static void AddText(GameObject parent, string name, string text, int fontSize,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.transform as RectTransform;
        rt.SetParent(parent.transform, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Text t = go.AddComponent<Text>();
        t.text = text;
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.color = Color.black;
        t.raycastTarget = false;
    }

    private static void AddButton(GameObject parent, string name, string text, int fontSize,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.transform as RectTransform;
        rt.SetParent(parent.transform, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.92f, 0.96f, 1f, 1f);
        img.raycastTarget = true;

        Button btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        btn.targetGraphic = img;
        btn.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.85f, 0.92f, 1f, 1f),
            pressedColor = new Color(0.7f, 0.8f, 0.95f, 1f),
            selectedColor = Color.white,
            disabledColor = new Color(0.7f, 0.7f, 0.7f, 1f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };

        // 按钮文字
        GameObject textGo = new GameObject("Text", typeof(RectTransform));
        RectTransform textRt = textGo.transform as RectTransform;
        textRt.SetParent(go.transform, false);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        Text t = textGo.AddComponent<Text>();
        t.text = text;
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.color = Color.black;
        t.raycastTarget = false;
    }

    private static void AddArea(GameObject parent, string name, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.transform as RectTransform;
        rt.SetParent(parent.transform, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;

        // 内容裁剪：滚动时不出区域
        go.AddComponent<RectMask2D>();
    }
}
#endif

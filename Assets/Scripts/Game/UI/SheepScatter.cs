using System.Collections.Generic;
using GameFramework.Config;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 把一组羊随机不重叠地摆进一个区域内（农场 / 星球通用）。
/// 位置随机（上下左右）、羊与羊之间留「半只羊宽」的间隔；图异步加载，加载前透明避免闪白块。
/// </summary>
public static class SheepScatter
{
    /// <summary>把 petIds 对应的羊摆到 area 里，返回创建的羊视图（调用方负责销毁）。</summary>
    public static List<GameObject> Populate(RectTransform area, List<int> petIds, float sheepSize, float gapRatio = 0.5f)
    {
        var views = new List<GameObject>();
        if (area == null || petIds == null || petIds.Count == 0) return views;

        Rect rect = area.rect;
        float size = sheepSize;
        // 区域太小就缩小羊
        if (rect.width > 0 && rect.height > 0)
            size = Mathf.Min(size, Mathf.Max(24f, Mathf.Min(rect.width, rect.height) * 0.45f));

        float gap = size * gapRatio;          // 间隔（默认半只羊宽）
        float step = size + gap;              // 两个羊中心的最小距离

        float minX = rect.xMin + size * 0.5f;
        float maxX = rect.xMax - size * 0.5f;
        float minY = rect.yMin + size * 0.5f;
        float maxY = rect.yMax - size * 0.5f;

        List<Vector2> placed = new List<Vector2>(petIds.Count);

        for (int i = 0; i < petIds.Count; i++)
        {
            Vector2 pos = PickFreePos(minX, maxX, minY, maxY, step, placed);
            placed.Add(pos);

            var controller = GameController.Instance;
            SheepPetConfig cfg = controller != null && controller.Config != null && controller.Config.Database != null
                ? controller.Config.Database.SheepPet.Get(petIds[i])
                : null;
            if (cfg == null) continue;

            GameObject go = CreateSheep(area, pos, size, cfg);
            if (go != null) views.Add(go);
        }

        return views;
    }

    /// <summary>在范围内随机找一个不与已放羊重叠的位置（最多尝试 60 次，仍失败就按网格兜底）。</summary>
    private static Vector2 PickFreePos(float minX, float maxX, float minY, float maxY, float step, List<Vector2> placed)
    {
        bool canFit = maxX >= minX && maxY >= minY;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            float x = canFit ? Random.Range(minX, maxX) : 0f;
            float y = canFit ? Random.Range(minY, maxY) : 0f;
            if (!Overlaps(x, y, step, placed))
                return new Vector2(x, y);
        }

        int fallback = placed.Count;
        float fx = Mathf.Min(minX + (fallback % 3) * step, maxX);
        float fy = Mathf.Min(minY + (fallback / 3) * step, maxY);
        return new Vector2(fx, fy);
    }

    private static bool Overlaps(float x, float y, float step, List<Vector2> placed)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            Vector2 p = placed[i];
            if (Mathf.Abs(x - p.x) < step && Mathf.Abs(y - p.y) < step)
                return true;
        }
        return false;
    }

    /// <summary>建一只羊视图（父节点 area 里，用 localPosition 定位到 area 的本地坐标）。</summary>
    private static GameObject CreateSheep(RectTransform area, Vector2 localPos, float size, SheepPetConfig cfg)
    {
        GameObject go = new GameObject("Sheep_" + cfg.Id, typeof(RectTransform), typeof(Image));
        RectTransform rt = go.transform as RectTransform;
        rt.SetParent(area, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.localPosition = new Vector3(localPos.x, localPos.y, 0f);

        Image img = go.GetComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0f);

        string spriteId = cfg.SpriteId;
        var controller = GameController.Instance;
        if (controller != null && controller.SpriteAtlases != null && !string.IsNullOrEmpty(spriteId))
        {
            string captured = spriteId;
            controller.SpriteAtlases.GetSpriteByIdAsync(captured, sprite =>
            {
                if (img != null)
                {
                    img.sprite = sprite;
                    img.color = Color.white;
                }
            });
        }

        return go;
    }
}

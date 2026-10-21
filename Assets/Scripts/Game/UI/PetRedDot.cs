using System.Collections.Generic;
using GameFramework.Business;
using GameFramework.Event;
using GameFramework.RedDot;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「获得新羊」红点逻辑（红点树：main.collection.sheep 及其子节点 1001~1004）。
///
/// 规则：
/// - 叶子节点 main.collection.sheep.&lt;羊id&gt; = 该羊**已拥有但还没看过** → 亮；
/// - 父节点 main.collection.sheep = 任一子节点亮 → 亮（收藏按钮用）；
/// - 「看过」= 打开过收藏界面（CollectionPanel 关闭时调 <see cref="MarkAllSeen"/>），
///   之后所有已拥有羊的红点消失；以后再获得新羊会重新亮。
/// - 已读记录持久化在本地（LocalData），重启 App 不会把旧羊当新羊。
///
/// 红点位置：框架的 RedDotBadge 只负责显隐，位置由 Badge 子物体自身的锚点决定；
/// <see cref="AddBadge"/> 把红点放在目标节点的**右上角**。
/// </summary>
public static class PetRedDot
{
    private const string SheepNodePrefix = "main.collection.sheep";

    /// <summary>已看过的羊 id（本地持久化）。</summary>
    private static readonly HashSet<int> seen = new HashSet<int>();

    /// <summary>当前拥有的羊 id（从 PlayerModel 取）。</summary>
    private static readonly HashSet<int> owned = new HashSet<int>();

    /// <summary>当前账号对应的已读记录 key（换号会变）。</summary>
    private static string seenKey;

    private static bool initialized;

    /// <summary>初始化：读已读记录、注册红点条件、订阅羊变化事件。幂等，面板 OnInit 调一次即可。</summary>
    public static void Init()
    {
        if (initialized) return;
        initialized = true;

        seenKey = GetSeenKey();
        LoadSeen();
        SyncOwned();

        // 给每只羊注册一个"未读"条件（红点叶子节点）
        var controller = GameController.Instance;
        if (controller != null && controller.RedDot != null && controller.Config != null
            && controller.Config.Database != null && controller.Config.Database.SheepPet != null
            && controller.Config.Database.SheepPet.Rows != null)
        {
            for (int i = 0; i < controller.Config.Database.SheepPet.Rows.Count; i++)
            {
                var row = controller.Config.Database.SheepPet.Rows[i];
                if (row == null) continue;
                int petId = row.Id;
                controller.RedDot.RegisterCondition(SheepNodePrefix + "." + petId, () => IsUnseen(petId) ? 1 : 0);
            }
        }

        // 羊数据变化 → 同步 + 刷新红点
        var bus = controller != null ? controller.Events : null;
        if (bus != null)
        {
            bus.Subscribe<PetListChangedEvent>(e => Refresh());
            bus.Subscribe<PetChangedEvent>(e => Refresh());
        }

        Refresh();
    }

    /// <summary>把当前所有已拥有羊标记为「已看过」，红点消失（收藏界面关闭时调用）。</summary>
    public static void MarkAllSeen()
    {
        bool changed = false;
        foreach (int id in owned)
        {
            if (!seen.Contains(id)) { seen.Add(id); changed = true; }
        }
        if (changed) SaveSeen();

        Refresh();
    }

    /// <summary>在目标节点右上角加一个红点（挂 RedDotBadge）。返回组件（一般不直接用）。</summary>
    public static RedDotBadge AddBadge(RectTransform parent, string nodeId, float size = 24f)
    {
        if (parent == null) return null;

        // 宿主：只挂 RedDotBadge，保持常活监听事件（不被 SetVisible 禁用）
        GameObject go = new GameObject("RedDot", typeof(RectTransform));
        RectTransform rt = go.transform as RectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);   // 右上角
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = Vector2.zero;                  // 不做偏移
        rt.sizeDelta = new Vector2(size, size);

        // 子物体 Badge：红点图，RedDotBadge 开关它（组件本身保持激活）
        GameObject badgeGo = new GameObject("Badge", typeof(RectTransform), typeof(Image));
        RectTransform brt = badgeGo.transform as RectTransform;
        brt.SetParent(rt, false);
        brt.anchorMin = Vector2.zero;    // 锚点铺满宿主
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;

        Image img = badgeGo.GetComponent<Image>();
        img.raycastTarget = false;

        // 红点图（Button 图集 / Red）
        var controller = GameController.Instance;
        if (controller != null && controller.SpriteAtlases != null)
        {
            controller.SpriteAtlases.GetSpriteByIdAsync("ui_red_dot", sprite =>
            {
                if (img != null) img.sprite = sprite;
            });
        }

        RedDotBadge badge = go.AddComponent<RedDotBadge>();
        badge.NodeId = nodeId;
        return badge;
    }

    // ---------------- 内部 ----------------

    private static bool IsUnseen(int petId)
    {
        return owned.Contains(petId) && !seen.Contains(petId);
    }

    private static void SyncOwned()
    {
        owned.Clear();
        var controller = GameController.Instance;
        if (controller != null && controller.Business != null && controller.Business.Player != null)
        {
            foreach (int id in controller.Business.Player.Pets)
                owned.Add(id);
        }
    }

    private static void Refresh()
    {
        SyncOwned();

        // 换账号了 → 换 key 重读已读记录（已读不能跨账号共享）
        string key = GetSeenKey();
        if (key != seenKey)
        {
            seenKey = key;
            LoadSeen();
        }

        var controller = GameController.Instance;
        if (controller != null && controller.RedDot != null)
            controller.RedDot.NotifyChanged(SheepNodePrefix);   // 框架已支持通知父节点刷新整棵子树
    }

    /// <summary>已读记录的存储 key：按玩家隔离，避免 A 号看过、B 号的新羊不亮。</summary>
    private static string GetSeenKey()
    {
        var controller = GameController.Instance;
        string playerId = controller != null && controller.Business != null && controller.Business.Player != null
            ? controller.Business.Player.PlayerId
            : null;
        return string.IsNullOrEmpty(playerId) ? "pet_seen" : "pet_seen_" + playerId;
    }

    private static void LoadSeen()
    {
        seen.Clear();
        var controller = GameController.Instance;
        if (controller == null || controller.Storage == null) return;
        string raw = controller.Storage.GetPref(seenKey, "");
        if (string.IsNullOrEmpty(raw)) return;
        string[] parts = raw.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            int id;
            if (int.TryParse(parts[i].Trim(), out id) && id > 0) seen.Add(id);
        }
    }

    private static void SaveSeen()
    {
        var controller = GameController.Instance;
        if (controller == null || controller.Storage == null) return;
        var list = new List<int>(seen);
        list.Sort();
        controller.Storage.SetPref(seenKey, string.Join(",", list.ConvertAll(v => v.ToString()).ToArray()));
    }
}

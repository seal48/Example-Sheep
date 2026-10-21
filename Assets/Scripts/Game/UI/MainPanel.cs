using System;
using System.Collections.Generic;
using GameFramework.Audio;
using GameFramework.Business;
using GameFramework.Config;
using GameFramework.Net.Protocol;
using GameFramework.Resource;
using GameFramework.Startup;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 大厅主界面。
///
/// 预制体：Assets/Prefabs/MainPanel.prefab（地址由 [UIPanel] 默认规则自动拼出来）。
/// 节点字段（background / titleText / startButton ...）由 MainPanel.Bindings.g.cs 生成并赋值。
/// 牧场（Pasture）与星球列表（Planet）是用户做好的布局：这里只读它们，不改布局。
///
/// 牧场：把已获得的羊随机摆进去（不重叠、半只羊间隔、上下左右随机）。
/// 星球排行：向服务端拉前 10 名（按羊数量），在 Planet 里纵向滚动展示，
///   PlanetItem 用对象池复用，滚出可视区就回收；自己的星球也包含在列表里。
/// </summary>
[UIPanel("MainPanel", UILayer.Normal)]
public partial class MainPanel : Panel
{
    /// <summary>牧场里羊的大小（边长）。</summary>
    private const float PastureSheepSize = 120f;

    /// <summary>星球上羊的大小（边长）。</summary>
    private const float PlanetSheepSize = 110f;

    /// <summary>星球排行拉取前几名。</summary>
    private const int PlanetTopN = 10;

    /// <summary>PlnetItem 预制体地址。</summary>
    private const string PlanetItemPrefabLocation = "Assets/Prefabs/PlnetItem.prefab";

    /// <summary>牧场里已创建的羊视图（刷新时清掉重建）。</summary>
    private readonly List<GameObject> pastureSheep = new List<GameObject>();

    // ============================== 星球排行 ==============================

    private ResourceAsset<GameObject> planetItemAsset;
    private GameObject planetItemPrefab;
    private IDisposable planetRankSub;

    private RectTransform planetViewport;
    private RectTransform planetContent;
    private ScrollRect planetScroll;
    private RectMask2D planetMask;

    /// <summary>当前排行数据（null = 还没拉到）。</summary>
    private List<PlanetRankEntry> planetEntries;

    /// <summary>星球项高度 / 步进（含间隔）。</summary>
    private float planetItemHeight = 770f;

    /// <summary>星球列表底部留白（让最后一项滚到底时完整显示，不被底边裁掉）。</summary>
    private const float PlanetBottomPadding = 60f;

    /// <summary>活跃的星球项（按 index 对齐）。</summary>
    private readonly List<PlanetItemView> planetItems = new List<PlanetItemView>();

    /// <summary>空闲的星球项（对象池复用）。</summary>
    private readonly List<GameObject> planetItemPool = new List<GameObject>();

    private sealed class PlanetItemView
    {
        public int Index;
        public GameObject Go;
        public RectTransform Rt;
        public Text Num;
        public RectTransform Region;
        public List<GameObject> Sheep = new List<GameObject>();
    }

    protected override void OnInit()
    {
        if (startButton != null)
            startButton.onClick.AddListener(StartGame);
        if (collectionBtn != null)
            collectionBtn.onClick.AddListener(OpenCollection);
        if (shopBtn != null)
            shopBtn.onClick.AddListener(OpenShop);
        if (menuBtn != null)
            menuBtn.onClick.AddListener(OpenMenu);

        // 新羊红点：初始化 + 给收藏按钮右上角挂红点（有未读新羊时亮）
        PetRedDot.Init();
        if (collectionBtn != null)
        {
            RectTransform btnRt = collectionBtn.transform as RectTransform;
            if (btnRt != null)
                PetRedDot.AddBadge(btnRt, "main.collection.sheep");
        }

        // 羊获得 / 移除 → 刷新牧场 + 重拉星球排行（面板缓存期间监听一直有效）
        Listen<PetListChangedEvent>(e => { RefreshPasture(); RequestPlanetRank(); });
        Listen<PetChangedEvent>(e => { RefreshPasture(); RequestPlanetRank(); });

        // 收星球排行回包
        var business = GameController.Instance != null ? GameController.Instance.Business : null;
        if (business != null)
            planetRankSub = business.Hub.Register<PlanetRankResponse>(ProtocolId.PlanetRankResponse, OnPlanetRankResponse, "MainPanel");

        LoadPlanetItemPrefab();
    }

    protected override void OnClose()
    {
        if (startButton != null)
            startButton.onClick.RemoveListener(StartGame);
        if (collectionBtn != null)
            collectionBtn.onClick.RemoveListener(OpenCollection);
        if (shopBtn != null)
            shopBtn.onClick.RemoveListener(OpenShop);
        if (menuBtn != null)
            menuBtn.onClick.RemoveListener(OpenMenu);

        if (planetRankSub != null) { planetRankSub.Dispose(); planetRankSub = null; }

        if (planetItemAsset != null) { planetItemAsset.Dispose(); planetItemAsset = null; }
        planetItemPrefab = null;

        ClearPastureSheep();
        ClearPlanetItems();
    }

    protected override void OnShow(object userData)
    {
        // 主界面背景音乐（循环；同名重复调用不会重播，换曲会自动交叉淡入淡出）
        AudioKit.PlayBgm("Assets/Audio/Bgm/MainBgm.mp3");

        RefreshPasture();
        RequestPlanetRank();
    }

    /// <summary>点击「开始游戏」：切到进游戏流程，由流程负责加载 GameScene。</summary>
    private void StartGame()
    {
        var controller = GameController.Instance;
        if (controller == null || controller.Procedures == null)
            return;

        Anim.Pop("StartButton");
        controller.Procedures.ChangeState<ProcedureEnterGame>();
    }

    /// <summary>点击「收藏」：打开收藏界面（羊/砖块图鉴）。</summary>
    private void OpenCollection()
    {
        var controller = GameController.Instance;
        if (controller == null || controller.UI == null)
            return;

        controller.UI.Open<CollectionPanel>();
    }

    /// <summary>点击「商店」：打开商店面板。</summary>
    private void OpenShop()
    {
        var controller = GameController.Instance;
        if (controller == null || controller.UI == null)
            return;

        controller.UI.Open<ShopPanel>();
    }

    /// <summary>点击「菜单」：打开菜单面板（里面有退出到登录）。</summary>
    private void OpenMenu()
    {
        var controller = GameController.Instance;
        if (controller == null || controller.UI == null)
            return;

        controller.UI.Open<MenuPanel>();
    }

    // ============================== 牧场 ==============================

    /// <summary>把已获得的羊随机摆进牧场（复用 SheepScatter：不重叠、半只羊间隔、随机）。</summary>
    private void RefreshPasture()
    {
        ClearPastureSheep();

        RectTransform pasture = Get<RectTransform>("Pasture");
        var controller = GameController.Instance;
        if (pasture == null || controller == null || controller.Business == null
            || controller.Business.Player == null)
            return;

        List<int> owned = new List<int>(controller.Business.Player.Pets);
        owned.Sort();
        if (owned.Count == 0) return;

        pastureSheep.AddRange(SheepScatter.Populate(pasture, owned, PastureSheepSize));
    }

    private void ClearPastureSheep()
    {
        for (int i = 0; i < pastureSheep.Count; i++)
            if (pastureSheep[i] != null) Destroy(pastureSheep[i]);
        pastureSheep.Clear();
    }

    // ============================== 星球排行 ==============================

    private void LoadPlanetItemPrefab()
    {
        var controller = GameController.Instance;
        if (controller == null || controller.Resource == null) return;

        controller.Resource.LoadAssetAsync<GameObject>(PlanetItemPrefabLocation, asset =>
        {
            if (asset == null || this == null) return;
            planetItemAsset = asset;
            planetItemPrefab = asset.Asset;
            ApplyPlanetEntries();
        });
    }

    private void RequestPlanetRank()
    {
        var client = GameController.Instance != null ? GameController.Instance.gameClient : null;
        if (client == null || client.Client == null) return;

        client.Send(ProtocolId.PlanetRankRequest, new PlanetRankRequest { TopN = PlanetTopN });
    }

    private void OnPlanetRankResponse(PlanetRankResponse response)
    {
        if (response == null || !response.Success) return;
        planetEntries = response.Entries != null ? response.Entries : new List<PlanetRankEntry>();
        ApplyPlanetEntries();
    }

    /// <summary>排行数据到位后：建好滚动结构 + 按窗口摆放/回收星球项。</summary>
    private void ApplyPlanetEntries()
    {
        if (planetItemPrefab == null) return;

        EnsurePlanetScroll();
        ClearPlanetItems();
        UpdatePlanetWindow();
    }

    /// <summary>在 Planet 节点里运行时建一个纵向 ScrollRect（viewport=Planet，content 撑高度）。</summary>
    private void EnsurePlanetScroll()
    {
        if (planetScroll != null) return;

        RectTransform planet = Get<RectTransform>("Planet");
        if (planet == null) return;

        planetViewport = planet;

        // 给 Planet 加裁剪（Image 没有也不影响，RectMask2D 不依赖 Image）
        planetMask = planet.gameObject.GetComponent<RectMask2D>();
        if (planetMask == null) planetMask = planet.gameObject.AddComponent<RectMask2D>();

        // 滚动容器：顶部水平拉伸（宽度自动撑满视口，避免宽度为 0 导致 ScrollRect 边界计算退化）
        GameObject contentGo = new GameObject("PlanetContent", typeof(RectTransform));
        planetContent = contentGo.transform as RectTransform;
        planetContent.SetParent(planet, false);
        planetContent.anchorMin = new Vector2(0f, 1f);
        planetContent.anchorMax = new Vector2(1f, 1f);
        planetContent.pivot = new Vector2(0.5f, 1f);
        planetContent.anchoredPosition = Vector2.zero;
        planetContent.sizeDelta = new Vector2(0f, 0f);

        planetScroll = planet.gameObject.GetComponent<ScrollRect>();
        if (planetScroll == null) planetScroll = planet.gameObject.AddComponent<ScrollRect>();
        planetScroll.viewport = planet;
        planetScroll.content = planetContent;
        planetScroll.horizontal = false;
        planetScroll.vertical = true;
        planetScroll.movementType = ScrollRect.MovementType.Clamped;
        planetScroll.scrollSensitivity = 60f;
        planetScroll.onValueChanged.AddListener(v => UpdatePlanetWindow());
    }

    /// <summary>根据滚动位置决定哪些星球项可见：可见的从池里拿/复用，滚出去的回收。</summary>
    private void UpdatePlanetWindow()
    {
        if (planetContent == null || planetEntries == null || planetEntries.Count == 0)
        {
            ClearPlanetItems();
            return;
        }

        int count = planetEntries.Count;
        float contentH = count * planetItemHeight + PlanetBottomPadding;

        // 先定 content 高度（宽度靠水平拉伸自动撑满视口）
        planetContent.sizeDelta = new Vector2(0f, contentH);

        float viewH = planetViewport != null ? planetViewport.rect.height : 0f;
        float scrollable = contentH - viewH;
        if (scrollable < 0f) scrollable = 0f;

        // 用 ScrollRect 的归一化位置算滚动距离（1=顶部，0=底部）；未初始化/超小时可能是 NaN，兜底 1
        float normY = planetScroll != null ? planetScroll.verticalNormalizedPosition : 1f;
        if (float.IsNaN(normY)) normY = 1f;
        float topY = (1f - Mathf.Clamp01(normY)) * scrollable;

        int first = (int)Mathf.Floor(topY / planetItemHeight);
        int last = (int)Mathf.Ceil((topY + viewH) / planetItemHeight);
        if (first < 0) first = 0;
        if (last > count) last = count;
        if (first >= last) first = last = 0;

        // 回收超出窗口的
        for (int i = planetItems.Count - 1; i >= 0; i--)
        {
            int idx = planetItems[i].Index;
            if (idx < first || idx >= last)
                RecyclePlanetItem(planetItems[i]);
        }

        // 补齐窗口内的
        for (int idx = first; idx < last; idx++)
        {
            if (FindPlanetItem(idx) != null) continue;
            PlanetItemView view = AcquirePlanetItem();
            if (view == null) continue;
            view.Index = idx;
            FillPlanetItem(view, planetEntries[idx]);
            PlacePlanetItem(view, idx);
        }
    }

    private PlanetItemView FindPlanetItem(int index)
    {
        for (int i = 0; i < planetItems.Count; i++)
            if (planetItems[i].Index == index) return planetItems[i];
        return null;
    }

    /// <summary>从池里拿一个 PlanetItem（没有就实例化预制体）。</summary>
    private PlanetItemView AcquirePlanetItem()
    {
        if (planetItemPrefab == null) return null;

        GameObject go = null;
        if (planetItemPool.Count > 0)
        {
            go = planetItemPool[planetItemPool.Count - 1];
            planetItemPool.RemoveAt(planetItemPool.Count - 1);
            go.SetActive(true);
        }
        else
        {
            go = Instantiate(planetItemPrefab, planetContent);
            go.name = "PlanetItem";
        }
        if (go == null) return null;

        RectTransform rt = go.transform as RectTransform;
        Text num = null;
        RectTransform region = null;
        // 预制体结构：根/PlanetBg/Num、根/PlanetBg/Region（Num/Region 不是根的直接子节点）
        Transform t = go.transform.Find("PlanetBg/Num");
        if (t != null) num = t.GetComponent<Text>();
        t = go.transform.Find("PlanetBg/Region");
        if (t != null) region = t as RectTransform;

        PlanetItemView view = new PlanetItemView { Go = go, Rt = rt, Num = num, Region = region };
        planetItems.Add(view);
        return view;
    }

    /// <summary>回收一个星球项到池里（清掉上面的羊）。</summary>
    private void RecyclePlanetItem(PlanetItemView view)
    {
        if (view == null) return;
        planetItems.Remove(view);

        for (int i = 0; i < view.Sheep.Count; i++)
            if (view.Sheep[i] != null) Destroy(view.Sheep[i]);
        view.Sheep.Clear();

        if (view.Go != null)
        {
            view.Go.SetActive(false);
            planetItemPool.Add(view.Go);
        }
    }

    private void PlacePlanetItem(PlanetItemView view, int index)
    {
        RectTransform rt = view.Rt;
        rt.SetParent(planetContent, false);
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -index * planetItemHeight);
        // 根节点尺寸撑满行星可视宽度，高度 = 项高
        float w = planetViewport != null ? planetViewport.rect.width : 0f;
        rt.sizeDelta = new Vector2(w, planetItemHeight);
    }

    /// <summary>填一个星球项：名次 + 羊（羊的位置参考农场：随机不重叠、半只羊间隔）。</summary>
    private void FillPlanetItem(PlanetItemView view, PlanetRankEntry entry)
    {
        if (view.Num != null)
            view.Num.text = "第" + entry.Rank + "名";

        if (view.Region != null && entry.Pets != null && entry.Pets.Count > 0)
        {
            List<int> pets = new List<int>(entry.Pets);
            pets.Sort();
            view.Sheep.AddRange(SheepScatter.Populate(view.Region, pets, PlanetSheepSize));
        }
    }

    private void ClearPlanetItems()
    {
        for (int i = planetItems.Count - 1; i >= 0; i--)
            RecyclePlanetItem(planetItems[i]);
    }
}

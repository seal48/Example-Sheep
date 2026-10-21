using System.Collections.Generic;
using GameFramework.Business;
using GameFramework.Config;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 收藏界面（图鉴总入口）。
/// 两个 Tab：羊图鉴（SheepBtn，显示 SheepPet 表的 4 只羊）和砖块图鉴（BrickBtn，显示 UISprite 表里
/// Atlas == "Brick" 的全部砖块，所有人默认拥有所有砖块）。
/// 图鉴内容渲染在下方 RefreshArea：每行 4 个、可上下滚动；数量不多，不用对象池。
/// 已获得的羊显示正常颜色，未获得的置黑（图标变黑）；砖块全部原色显示。图鉴不显示名字文字。
/// 节点字段（closeButton / sheepBtn / brickBtn / titleText / refreshArea）由绑定生成代码提供。
/// </summary>
[UIPanel("CollectionPanel", UILayer.Popup)]
public partial class CollectionPanel : Panel
{
    /// <summary>羊图鉴格子每行几个。</summary>
    private const int PerRow = 4;

    /// <summary>格子最大尺寸（区域够宽时用这个）。</summary>
    private static readonly Vector2 CellMaxSize = new Vector2(180f, 210f);

    /// <summary>格子间距。</summary>
    private const float CellGap = 16f;

    /// <summary>当前实际格子尺寸（按 RefreshArea 宽度自适应，见 UpdateCellMetrics）。</summary>
    private Vector2 cellSize = CellMaxSize;

    /// <summary>图鉴网格整体宽度（= 每行格子总宽）。</summary>
    private float GridWidth { get { return PerRow * cellSize.x + (PerRow - 1) * CellGap; } }

    /// <summary>当前渲染的是羊图鉴还是砖块图鉴。</summary>
    private bool sheepTab = true;

    /// <summary>滚动内容节点（运行时创建在 RefreshArea 下）。</summary>
    private RectTransform content;

    /// <summary>羊图鉴的格子视图（全部创建出来，刷新时只改状态不重建）。</summary>
    private readonly List<SheepCell> sheepCells = new List<SheepCell>();

    /// <summary>砖块图鉴的格子视图（同样全部创建、刷新只改状态）。</summary>
    private readonly List<BrickCell> brickCells = new List<BrickCell>();

    private sealed class SheepCell
    {
        public SheepPetConfig Config;
        public GameObject Root;
        public Image Icon;
        /// <summary>是否已获得（决定图标是否染黑）。</summary>
        public bool Owned;
    }

    private sealed class BrickCell
    {
        /// <summary>UISprite 表的行（Id / Atlas / Sprite）。</summary>
        public UISpriteConfig Row;
        public GameObject Root;
        public Image Icon;
    }

    protected override void OnInit()
    {
        // 节点字段（closeButton / sheepBtn / brickBtn / titleText / refreshArea）由生成代码在 BindNodes 里赋值
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (sheepBtn != null) sheepBtn.onClick.AddListener(OnSheepTab);
        if (brickBtn != null) brickBtn.onClick.AddListener(OnBrickTab);

        // 宠物数据变了 → 图鉴实时刷新（登录同步 / 后端发宠物都走这里）
        Listen<PetListChangedEvent>(e => { if (sheepTab) RefreshSheepGrid(); });
        Listen<PetChangedEvent>(e => { if (sheepTab) RefreshSheepGrid(); });
    }

    protected override void OnClose()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (sheepBtn != null) sheepBtn.onClick.RemoveListener(OnSheepTab);
        if (brickBtn != null) brickBtn.onClick.RemoveListener(OnBrickTab);
    }

    protected override void OnHide()
    {
        // 看过一次收藏 → 已读，红点全部消失（含主界面收藏按钮）。
        // 注意放在 OnHide 而不是 OnClose：本面板是缓存面板，关闭=隐藏，只触发 OnHide。
        PetRedDot.MarkAllSeen();
    }

    protected override void OnShow(object userData)
    {
        // 每次打开默认进羊图鉴
        sheepTab = true;
        RefreshSheepGrid();
    }

    private void OnSheepTab()
    {
        sheepTab = true;
        RefreshSheepGrid();
    }

    private void OnBrickTab()
    {
        sheepTab = false;
        RefreshBrickGrid();
    }

    // ============================== 羊图鉴 ==============================

    private void RefreshSheepGrid()
    {
        SetTabState();

        // 砖块图鉴的格子切走时清掉（避免叠在羊格子后面）
        ClearCells(brickCells);

        var controller = GameController.Instance;
        if (controller == null || controller.Config == null || controller.Config.Database == null)
            return;

        var table = controller.Config.Database.SheepPet;
        if (table == null) return;

        EnsureContent();
        // 先按刷新区域宽度算好格子尺寸（保证每行 4 个放得下）
        UpdateCellMetrics();

        // 取全部羊并按 Id 排序（数量不多，直接排）
        var all = new List<SheepPetConfig>();
        if (table.Rows != null) all.AddRange(table.Rows);
        all.Sort((a, b) => a.Id.CompareTo(b.Id));

        int need = all.Count;
        while (sheepCells.Count < need)
        {
            SheepCell cell = new SheepCell();
            BuildCell(cell);
            sheepCells.Add(cell);
        }
        while (sheepCells.Count > need)
        {
            SheepCell cell = sheepCells[sheepCells.Count - 1];
            sheepCells.RemoveAt(sheepCells.Count - 1);
            if (cell != null && cell.Root != null)
                Destroy(cell.Root);
        }

        for (int i = 0; i < sheepCells.Count; i++)
        {
            SheepPetConfig cfg = all[i];
            SheepCell cell = sheepCells[i];
            bool owned = controller.Business != null && controller.Business.Player != null
                && controller.Business.Player.HasPet(cfg.Id);

            ApplyCell(cell, cfg, owned);
            PositionCell(cell.Root.transform as RectTransform, i);
        }

        LayoutContentHeight(need);
    }

    private void ApplyCell(SheepCell cell, SheepPetConfig cfg, bool owned)
    {
        cell.Config = cfg;
        cell.Owned = owned;

        if (cell.Icon != null)
        {
            cell.Icon.sprite = null;
            ApplyIconColor(cell);
            LoadIconAsync(cell, cfg);

            // 新羊红点：每个羊图标右上角挂红点（只挂一次），未读的新羊亮
            if (cell.Icon.transform.Find("RedDot") == null)
                PetRedDot.AddBadge(cell.Icon.rectTransform, "main.collection.sheep." + cfg.Id);
        }
    }

    /// <summary>
    /// 图标颜色：已获得 = 原色（白，即不改色），未获得 = 黑色。
    /// 图还没加载到时整体透明，避免显示成一个方块。
    /// </summary>
    private void ApplyIconColor(SheepCell cell)
    {
        if (cell == null || cell.Icon == null) return;

        Color color = cell.Owned ? Color.white : Color.black;
        if (cell.Icon.sprite == null) color.a = 0f;
        cell.Icon.color = color;
    }

    private void LoadIconAsync(SheepCell cell, SheepPetConfig cfg)
    {
        var atlases = GameController.Instance != null ? GameController.Instance.SpriteAtlases : null;
        if (atlases == null) return;

        string spriteId = cfg.SpriteId;
        atlases.GetSpriteByIdAsync(spriteId, sprite =>
        {
            if (cell == null || cell.Icon == null) return;
            if (cell.Config != cfg) return;   // 异步回来时可能已切到别的格子 / 面板已关
            cell.Icon.sprite = sprite;
            ApplyIconColor(cell);
        });
    }

    /// <summary>建一个格子，并把 Icon / Name 组件回填到 cell 上（否则 ApplyCell 拿不到它们）。</summary>
    private void BuildCell(SheepCell cell)
    {
        GameObject root = new GameObject("SheepCell", typeof(RectTransform));
        RectTransform rt = root.transform as RectTransform;
        rt.SetParent(content, false);
        // 关键：格子锚点必须放在父节点的左上角。
        // anchoredPosition 是相对"自己的 anchor 点"算的；anchor 留在默认的 (0.5,0.5) 时，
        // x 就是相对父节点中心，格子会从中间开始排（右边还会被裁掉）。
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = cellSize;

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform iconRt = iconGo.transform as RectTransform;
        iconRt.SetParent(rt, false);
        iconRt.anchorMin = new Vector2(0.5f, 1f);
        iconRt.anchorMax = new Vector2(0.5f, 1f);
        iconRt.pivot = new Vector2(0.5f, 1f);
        iconRt.anchoredPosition = new Vector2(0f, 0f);
        float iconSide = Mathf.Max(32f, cellSize.x - 20f);
        iconRt.sizeDelta = new Vector2(iconSide, iconSide);
        Image icon = iconGo.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        // 回填，供 ApplyCell / ApplyIconColor 使用
        cell.Root = root;
        cell.Icon = icon;
    }

    private void PositionCell(RectTransform rt, int index)
    {
        int row = index / PerRow;
        int col = index % PerRow;
        float x = cellSize.x * 0.5f + col * (cellSize.x + CellGap);
        float y = -(cellSize.y * 0.5f + row * (cellSize.y + CellGap));
        rt.anchoredPosition = new Vector2(x, y);
    }

    // ============================== 砖块图鉴 ==============================

    /// <summary>
    /// 砖块图鉴：所有人默认拥有所有砖块（Brick 图集里的全部图片，配在 UISprite 表）。
    /// 从 UISprite 表取 Atlas == "Brick" 的全部行，每行一块砖，按 Id 排序显示。
    /// </summary>
    private void RefreshBrickGrid()
    {
        SetTabState();

        var controller = GameController.Instance;
        if (controller == null || controller.Config == null || controller.Config.Database == null)
            return;

        var table = controller.Config.Database.UISprite;
        if (table == null || table.Rows == null) return;

        EnsureContent();
        // 先按刷新区域宽度算好格子尺寸（保证每行 4 个放得下）
        UpdateCellMetrics();

        // 取 UISprite 表里 Atlas == "Brick" 的全部行，按 Id 排序
        var all = new List<UISpriteConfig>();
        for (int i = 0; i < table.Rows.Count; i++)
        {
            UISpriteConfig row = table.Rows[i];
            if (row != null && row.Atlas == "Brick")
                all.Add(row);
        }
        all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        // 羊图鉴的格子切走时清掉（避免叠在砖块格子后面）
        ClearCells(sheepCells);

        int need = all.Count;
        while (brickCells.Count < need)
        {
            BrickCell cell = new BrickCell();
            BuildBrickCell(cell);
            brickCells.Add(cell);
        }
        while (brickCells.Count > need)
        {
            BrickCell cell = brickCells[brickCells.Count - 1];
            brickCells.RemoveAt(brickCells.Count - 1);
            if (cell != null && cell.Root != null)
                Destroy(cell.Root);
        }

        for (int i = 0; i < brickCells.Count; i++)
        {
            ApplyBrickCell(brickCells[i], all[i]);
            PositionCell(brickCells[i].Root.transform as RectTransform, i);
        }

        LayoutContentHeight(need);
    }

    private void ApplyBrickCell(BrickCell cell, UISpriteConfig row)
    {
        cell.Row = row;

        if (cell.Icon != null)
        {
            cell.Icon.sprite = null;
            cell.Icon.color = Color.white;   // 默认全拥有：原色显示
            LoadBrickIconAsync(cell, row);
        }
    }

    private void LoadBrickIconAsync(BrickCell cell, UISpriteConfig row)
    {
        var atlases = GameController.Instance != null ? GameController.Instance.SpriteAtlases : null;
        if (atlases == null) return;

        atlases.GetSpriteAsync(row.Atlas, row.Sprite, sprite =>
        {
            if (cell == null || cell.Icon == null) return;
            if (cell.Row != row) return;   // 异步回来时可能已切到别的格子 / 面板已关
            cell.Icon.sprite = sprite;
        });
    }

    /// <summary>建一个砖块格子（和羊格子同样的布局，根节点名用 BrickCell）。</summary>
    private void BuildBrickCell(BrickCell cell)
    {
        GameObject root = new GameObject("BrickCell", typeof(RectTransform));
        RectTransform rt = root.transform as RectTransform;
        rt.SetParent(content, false);
        // 锚点放父节点左上角（跟羊格子一致，保证从左往右从上往下排）
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = cellSize;

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform iconRt = iconGo.transform as RectTransform;
        iconRt.SetParent(rt, false);
        iconRt.anchorMin = new Vector2(0.5f, 1f);
        iconRt.anchorMax = new Vector2(0.5f, 1f);
        iconRt.pivot = new Vector2(0.5f, 1f);
        iconRt.anchoredPosition = new Vector2(0f, 0f);
        float iconSide = Mathf.Max(32f, cellSize.x - 20f);
        iconRt.sizeDelta = new Vector2(iconSide, iconSide);
        Image icon = iconGo.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        // 回填，供 ApplyBrickCell / LoadBrickIconAsync 使用
        cell.Root = root;
        cell.Icon = icon;
    }

    /// <summary>销毁并清空某组格子（切 Tab 时用）。</summary>
    private void ClearCells<T>(List<T> cells) where T : class
    {
        if (cells == null) return;
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] == null) continue;
            var cell = cells[i];
            GameObject root = null;
            if (cell is SheepCell) root = (cell as SheepCell).Root;
            else if (cell is BrickCell) root = (cell as BrickCell).Root;
            if (root != null) Destroy(root);
        }
        cells.Clear();
    }

    private void SetTabState()
    {
        if (sheepBtn != null) sheepBtn.interactable = !sheepTab;
        if (brickBtn != null) brickBtn.interactable = sheepTab;
    }

    // ============================== 滚动区域 ==============================

    /// <summary>
    /// 按 RefreshArea 的实际宽度算格子大小：保证「每行 4 个」一定放得下。
    /// 区域够宽就用上限（180x210），不够宽就等比缩小。
    /// </summary>
    private void UpdateCellMetrics()
    {
        float areaWidth = refreshArea != null ? refreshArea.rectTransform.rect.width : 0f;
        if (areaWidth <= 1f) areaWidth = CellMaxSize.x * PerRow + CellGap * (PerRow - 1);

        // 每行 4 个 + 3 个间隔，剩下的宽度平分给格子
        float fitWidth = (areaWidth - CellGap * (PerRow - 1)) / PerRow;
        float width = Mathf.Min(CellMaxSize.x, Mathf.Max(24f, fitWidth));
        float ratio = CellMaxSize.y / CellMaxSize.x;
        cellSize = new Vector2(width, width * ratio);
    }

    /// <summary>懒建 RefreshArea 下的滚动内容（ScrollRect.Content），并把 RefreshArea 变成 ScrollRect。</summary>
    private void EnsureContent()
    {
        if (content != null || refreshArea == null) return;

        // 给 RefreshArea 挂 ScrollRect（运行时建，面板预制体只放刷新区域框）
        ScrollRect scroll = refreshArea.gameObject.GetComponent<ScrollRect>();
        if (scroll == null) scroll = refreshArea.gameObject.AddComponent<ScrollRect>();

        GameObject contentGo = new GameObject("Content", typeof(RectTransform));
        content = contentGo.transform as RectTransform;
        content.SetParent(refreshArea.rectTransform, false);

        // 关键：轴心放左上角。子节点的 anchoredPosition 是相对父节点 pivot 算的，
        // 如果 pivot 在横向中心，x=0 就变成"屏幕中间"，格子会从中间开始排（右边还会被裁掉）。
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(GridWidth, 0f);

        scroll.viewport = refreshArea.rectTransform;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
    }

    private void LayoutContentHeight(int cellCount)
    {
        if (content == null) return;

        int rows = (cellCount + PerRow - 1) / PerRow;
        float height = Mathf.Max(1f, rows * cellSize.y + (rows - 1) * CellGap);
        content.sizeDelta = new Vector2(GridWidth, height);

        // 网格在区域里水平居中（区域比网格宽时左右留一样的边）
        float areaWidth = refreshArea != null ? refreshArea.rectTransform.rect.width : 0f;
        float offsetX = areaWidth > GridWidth ? (areaWidth - GridWidth) * 0.5f : 0f;
        content.anchoredPosition = new Vector2(offsetX, 0f);

        // 内容不足一屏时回到顶部
        if (refreshArea != null)
        {
            ScrollRect scroll = refreshArea.gameObject.GetComponent<ScrollRect>();
            if (scroll != null) scroll.normalizedPosition = new Vector2(0f, 1f);
        }
    }
}

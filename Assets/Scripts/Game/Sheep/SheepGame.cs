using System;
using System.Collections.Generic;

namespace GameFramework.Gameplay
{
    /// <summary>
    /// 羊了个羊核心玩法（纯逻辑，不依赖 Unity）：
    ///   · 金字塔式多层牌阵，只有没被盖住的牌能点；
    ///   · 点牌进槽，同类自动聚堆，3 张相同自动消除；
    ///   · 清空牌面 + 槽位 = 通关；槽满且新牌放不进去 = 失败；
    ///   · 道具：撤回 / 移出（把槽里最前面的 3 张挪到槽位上方的暂存区，还能再点回来）/ 洗牌（重排剩余牌图标）。
    ///
    /// 图标按「解锁顺序」分配：每连续 3 张（按解锁步数排序）同一种图标，
    /// 保证玩家按从浅到深清牌时每 3 张自动消除、卡槽不会卡死——必定可通关；
    /// 每种图标数量是 3 的倍数，移出只是把牌"暂存"起来（不销毁），所以这个关系不会被破坏。
    /// </summary>
    public sealed class SheepGame
    {
        public const int DefaultSlotSize = 7;

        /// <summary>一次「移出」挪几张（固定值：玩家永远知道会移出多少张）。</summary>
        public const int RemoveBatch = 3;

        /// <summary>堆叠样式的层视觉偏移比例（相对一格；1/4 格显示厚度但不误导行列数）。</summary>
        public const float Style2OffsetRatio = 0.25f;

        /// <summary>牌占一格的宽度比例（留出间距，用于重叠判定）。</summary>
        private const float TileSizeRatio = 0.9f;

        /// <summary>
        /// 每层视觉偏移（相对一格）：
        /// Style=2 对角 1/4 格；Style=3 用配表 OffsetPercent；其余不偏移。
        /// </summary>
        public float LayerOffsetRatio
        {
            get
            {
                if (Level == null) return 0f;
                if (Level.Style == 2) return Style2OffsetRatio;
                if (Level.Style == 3 || Level.Style == 4) return Level.OffsetPercent * 0.01f;
                return 0f;
            }
        }

        private readonly System.Random rng;

        /// <summary>全部牌（含已消除的），按生成顺序。</summary>
        public readonly List<SheepTile> Tiles = new List<SheepTile>();

        /// <summary>卡槽里的牌（同类已聚堆）。</summary>
        public readonly List<SheepTile> Slot = new List<SheepTile>();

        /// <summary>
        /// 「移出」暂存区：从卡槽挪出来的牌暂存在卡槽上方，排成一行。
        /// 这些牌**还在牌局里**（状态 Parked），点一下就能再飞回卡槽。
        /// </summary>
        public readonly List<SheepTile> Holding = new List<SheepTile>();

        /// <summary>关卡参数。</summary>
        public SheepLevelData Level { get; private set; }

        /// <summary>卡槽容量。</summary>
        public int SlotSize { get { return Level != null ? Level.SlotSize : DefaultSlotSize; } }

        /// <summary>暂存区最多能放几张（= 移出次数 × 每次 3 张）；0 = 这关没有移出道具。</summary>
        public int HoldingCapacity
        {
            get { return Level != null && Level.RemoveLimit > 0 ? Level.RemoveLimit * RemoveBatch : 0; }
        }

        /// <summary>剩余道具次数。</summary>
        public int UndoCount { get; private set; }
        public int RemoveCount { get; private set; }
        public int ShuffleCount { get; private set; }

        /// <summary>是否已经结束（胜或负）。</summary>
        public bool IsOver { get; private set; }

        /// <summary>是否通关（IsOver 为 true 时有效）。</summary>
        public bool IsWon { get; private set; }

        /// <summary>上一次放入槽且仍在槽里的牌（撤回用）。被消除后置空。</summary>
        private SheepTile lastPlaced;

        /// <summary>上一次放进槽的那张牌是不是从暂存区点进来的（撤回时要放回暂存区，不是牌面）。</summary>
        private bool lastPlacedFromHolding;

        /// <summary>牌面 / 槽位 / 道具数量发生变化时触发，UI 刷一下。</summary>
        public event Action StateChanged;

        /// <summary>游戏结束（胜 / 负）时触发。</summary>
        public event Action<bool> GameOver;

        /// <summary>最近一次生成布局的错误信息（空 = 无错）。纯逻辑层不依赖 Unity，表现层可读它打日志。</summary>
        public string LastGenerateError { get; private set; }

        /// <summary>用固定种子建一局（同一关卡同一种子 = 同一牌面，方便复现）。</summary>
        public SheepGame(SheepLevelData level, int seed)
        {
            Level = level;
            rng = new System.Random(seed);
            GenerateBoard();
            UndoCount = level.UndoLimit;
            RemoveCount = level.RemoveLimit;
            ShuffleCount = level.ShuffleLimit;
        }

        /// <summary>随机种子建一局。</summary>
        public SheepGame(SheepLevelData level)
            : this(level, Environment.TickCount)
        {
        }

        /// <summary>牌面上还有多少张牌（没被消除 / 没进槽 / 没被移出暂存）。</summary>
        public int BoardCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Tiles.Count; i++)
                    if (Tiles[i].State == SheepTileState.OnBoard) count++;
                return count;
            }
        }

        /// <summary>牌是否被上层牌盖住（不可点）：只要更上层的牌与本牌矩形有任意一点重叠，就算被覆盖。</summary>
        public bool IsCovered(SheepTile tile)
        {
            if (tile == null || tile.State != SheepTileState.OnBoard)
                return false;

            float off = LayerOffsetRatio;
            float size = TileSizeRatio;

            // 本牌的矩形（相对一格为单位，含层偏移 + 每格自己的堆叠方向）
            float tx0 = tile.X + tile.SubX + tile.Layer * off * tile.StackX;
            float ty0 = tile.Y + tile.SubY + tile.Layer * off * tile.StackY;
            float tx1 = tx0 + size;
            float ty1 = ty0 + size;

            for (int i = 0; i < Tiles.Count; i++)
            {
                SheepTile other = Tiles[i];
                if (other.State != SheepTileState.OnBoard) continue;
                if (other.Layer <= tile.Layer) continue;   // 只看更高层

                float ox0 = other.X + other.SubX + other.Layer * off * other.StackX;
                float oy0 = other.Y + other.SubY + other.Layer * off * other.StackY;
                float ox1 = ox0 + size;
                float oy1 = oy0 + size;

                // 矩形相交 = 有一点被遮住
                if (tx0 < ox1 && ox0 < tx1 && ty0 < oy1 && oy0 < ty1)
                    return true;
            }
            return false;
        }

        // ============================== 牌面生成 ==============================

        private void GenerateBoard()
        {
            Tiles.Clear();
            Slot.Clear();
            Holding.Clear();
            IsOver = false;
            IsWon = false;
            lastPlaced = null;
            lastPlacedFromHolding = false;

            if (Level.Style == 1)
            {
                // 方格样式：每层满铺（宽×高全填）、层与层对齐不偏移 → 肉眼看到干净的行×列
                int nextId = 1;
                for (int l = 0; l < Level.LayerCount; l++)
                    for (int y = 0; y < Level.GridHeight; y++)
                        for (int x = 0; x < Level.GridWidth; x++)
                            Tiles.Add(new SheepTile(nextId++, 0, l, x, y));
            }
            else if (Level.Style == 3)
            {
                // 牌山样式：不规则轮廓 + 高度场 + 每格横/竖偏移（贴近原游戏的"牌山"）
                GenerateMountain();
            }
            else if (Level.Style == 4)
            {
                // 人工布局：按配表 Layout 字符串摆牌（每格层数 + 可选方向）
                GenerateFromLayout();
            }
            else
            {
                // 堆叠样式：每层在网格里随机填一部分（位置/数量随机，不死板），
                // 层与层完全重叠（同位置才算堆叠），不偏移；只有最上层（露出）的牌亮色可点，被盖住的灰暗隐藏。
                int cw = Level.GridWidth;
                int ch = Level.GridHeight;
                int depth = Level.LayerCount;
                int nextId = 1;
                for (int l = 0; l < depth; l++)
                {
                    int totalCells = cw * ch;
                    int minFill = System.Math.Max(4, totalCells * 50 / 100);
                    int fill = rng.Next(minFill, totalCells + 1);

                    var cells = new List<(int x, int y)>(totalCells);
                    for (int x = 0; x < cw; x++)
                        for (int y = 0; y < ch; y++)
                            cells.Add((x, y));
                    ShuffleList(cells);

                    for (int i = 0; i < fill; i++)
                    {
                        int x = cells[i].x;
                        int y = cells[i].y;
                        Tiles.Add(new SheepTile(nextId++, 0, l, x, y, 1, 1));   // 对角偏移
                    }
                }
            }

            // 图标分配：按解锁顺序保证可通关（见 AssignIconsByUnlockOrder）
            AssignIconsByUnlockOrder();

            // 原游戏做法：没有盲盒牌（配表 BlindCount 恒为 0，忽略）
        }

        /// <summary>
        /// 牌山样式（Style=3）：受约束的随机 —— 网格对齐 + 连通轮廓 + 高度场 + 每格横/竖偏移。
        /// 只在这些约束内随机，保证"不规则但看起来不乱"。
        /// </summary>
        private void GenerateMountain()
        {
            int w = System.Math.Max(1, Level.GridWidth);
            int hgt = System.Math.Max(1, Level.GridHeight);
            int maxLayers = System.Math.Max(1, Level.LayerCount);            // 高叠格层数上限
            int minLayers = System.Math.Max(1, Level.MinLayers);             // 普通格最低层数
            int normalMax = System.Math.Min(maxLayers, minLayers + 1);       // 普通格最高层数
            int tallPercent = Clamp01To100(Level.TallPercent);
            int horizPercent = Clamp01To100(Level.HorizPercent);

            // 1) 基础轮廓
            bool[,] inShape = new bool[w, hgt];
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                    inShape[x, y] = InsideShape(x, y, w, hgt);

            // 2) 边缘受控噪声：只动边界格，内部保持实心 → 不规则但不碎
            int noise = Clamp01To100(Level.EdgeNoise);
            if (noise > 0)
            {
                bool[,] snapshot = (bool[,])inShape.Clone();
                for (int y = 0; y < hgt; y++)
                    for (int x = 0; x < w; x++)
                        if (snapshot[x, y] && IsBoundary(x, y, w, hgt, snapshot) && rng.Next(100) < noise)
                            inShape[x, y] = false;
            }

            // 3) 连通性：只保留最大的一块连通区域 → 保证是一整座"山"
            bool[,] keep = KeepLargestComponent(inShape, w, hgt);

            // 4) 逐格生成：普通格 1~2 层；TallPercent 的格子叠到高叠层数（形成厚度）
            int cx = w - 1, cy = hgt - 1;
            int nextId = 1;
            for (int y = 0; y < hgt; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!keep[x, y]) continue;

                    int layers = rng.Next(minLayers, normalMax + 1);
                    if (tallPercent > 0 && rng.Next(100) < tallPercent)
                        layers = rng.Next(normalMax + 1, maxLayers + 1);
                    if (layers < 1) layers = 1;

                    // 方向：横向 (dx,0) 或纵向 (0,dy)；符号朝牌山中心（贴合参考图：亮牌在里、灰边在外）
                    int dirX = 0, dirY = 0;
                    if (rng.Next(100) < horizPercent) dirX = (x * 2 >= cx) ? -1 : 1;
                    else dirY = (y * 2 >= cy) ? -1 : 1;

                    for (int l = 0; l < layers; l++)
                        Tiles.Add(new SheepTile(nextId++, 0, l, x, y, dirX, dirY));
                }
            }
        }

        /// <summary>
        /// 人工布局（Style=4）：解析配表 Layout 字符串摆牌。
        /// 格式：一行一串、`|` 分隔行；`.`/`0`/空格 = 空位；`1`-`9` = 层数；`A`-`Z` = 10~35。
        /// **字符串第一行 = 屏幕最上面**（和编辑器网格一致；Unity UI 的 +Y 向上，所以内部要翻一下 Y）。
        /// 方向：LayoutDir 同格式，`1`=横右 `2`=横左 `3`=纵上 `4`=纵下；`.`/空 = **原地重叠（不偏移）**。
        /// 半格层：LayoutHalf（尺寸 (GridWidth-1)×(GridHeight-1)）摆"中心牌"，位置固定 +0.5/+0.5，
        ///         层次**自动算**（压在它下面的牌里最高的层 + 1），所以天然叠在格点牌上面。
        /// 布局为空 / 解析不到任何牌时，退回牌山生成，保证关卡仍可玩。
        /// </summary>
        private void GenerateFromLayout()
        {
            LastGenerateError = null;

            string layout = Level.Layout;
            if (string.IsNullOrEmpty(layout))
            {
                LastGenerateError = "Style=4 但 Layout 为空，已退回牌山生成";
                GenerateMountain();
                return;
            }

            string[] rows = layout.Split('|');
            string[] dirRows = string.IsNullOrEmpty(Level.LayoutDir) ? null : Level.LayoutDir.Split('|');

            int hgt = rows.Length;
            int w = 0;
            for (int y = 0; y < hgt; y++)
                if (rows[y].Length > w) w = rows[y].Length;

            int nextId = 1;

            for (int y = 0; y < hgt; y++)
            {
                string row = rows[y];
                string dirRow = (dirRows != null && y < dirRows.Length) ? dirRows[y] : null;

                // 字符串第一行 = 屏幕最上面：Unity UI 的 +Y 向上，所以这里把行号翻过来
                int ty = (hgt - 1) - y;

                for (int x = 0; x < row.Length; x++)
                {
                    int layers = CharToLayers(row[x]);
                    if (layers <= 0) continue;

                    int dirX, dirY;
                    if (!CharToDir(dirRow, x, out dirX, out dirY))
                    {
                        // 没指定方向 → 原地重叠（不偏移）：同格各层完全重合，只看到最上面一张
                        dirX = 0;
                        dirY = 0;
                    }

                    // 格点牌不加亚格偏移（要"中心牌"请用 LayoutHalf 半格层）
                    for (int l = 0; l < layers; l++)
                        Tiles.Add(new SheepTile(nextId++, 0, l, x, ty, dirX, dirY, 0f, 0f));
                }
            }

            // 半格层（中心牌）：位置固定在四格交界 (+0.5, +0.5)，层次自动叠在压住的牌之上
            nextId = GenerateHalfLayer(nextId);

            if (Tiles.Count == 0)
            {
                LastGenerateError = "Style=4 的 Layout 解析不到任何牌，已退回牌山生成";
                GenerateMountain();
                return;
            }

            // 行数/列数跟配表对不上时给出提示（不阻断）
            if (hgt != Level.GridHeight || w != Level.GridWidth)
                LastGenerateError = "Layout 尺寸(" + w + "x" + hgt + ")与配表 GridWidth/GridHeight(" + Level.GridWidth + "x" + Level.GridHeight + ")不一致";
        }

        /// <summary>布局字符 → 层数：`.`/`0`/空格 = 0；`1`-`9` = 1~9；`A`-`Z`/`a`-`z` = 10~35。</summary>
        private static int CharToLayers(char c)
        {
            if (c >= '1' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'Z') return 10 + (c - 'A');
            if (c >= 'a' && c <= 'z') return 10 + (c - 'a');
            return 0;
        }

        /// <summary>方向字符 → (dirX, dirY)：1=横右 2=横左 3=纵上 4=纵下。取不到返回 false。</summary>
        private static bool CharToDir(string dirRow, int x, out int dirX, out int dirY)
        {
            dirX = 0; dirY = 0;
            if (dirRow == null || x >= dirRow.Length) return false;

            switch (dirRow[x])
            {
                case '1': dirX = 1; return true;
                case '2': dirX = -1; return true;
                case '3': dirY = 1; return true;
                case '4': dirY = -1; return true;
                default: return false;
            }
        }

        /// <summary>
        /// 半格牌（中心牌）的自动起始层：和已有牌几何重叠的那些里最高的层 + 1；
        /// 一张都没压到就是 0。这样中心牌天然"叠在格点牌上面"，不需要手填参数。
        /// </summary>
        private int ComputeHalfBaseLayer(float px, float py)
        {
            float off = LayerOffsetRatio;
            int baseLayer = 0;

            for (int i = 0; i < Tiles.Count; i++)
            {
                SheepTile t = Tiles[i];
                float tx = t.X + t.SubX + t.Layer * off * t.StackX;
                float ty = t.Y + t.SubY + t.Layer * off * t.StackY;

                if (px < tx + TileSizeRatio && tx < px + TileSizeRatio &&
                    py < ty + TileSizeRatio && ty < py + TileSizeRatio)
                {
                    if (t.Layer + 1 > baseLayer) baseLayer = t.Layer + 1;
                }
            }

            return baseLayer;
        }

        /// <summary>
        /// 半格层（中心牌）：解析 LayoutHalf，把牌摆到四格交界处。
        /// 每格固定 +0.5/+0.5（所以一张能压住周围四张），层次自动叠在压住的牌之上。
        /// 和主网格一样：字符串第一行 = 屏幕最上面。
        /// </summary>
        private int GenerateHalfLayer(int nextId)
        {
            if (string.IsNullOrEmpty(Level.LayoutHalf)) return nextId;

            string[] hrows = Level.LayoutHalf.Split('|');
            string[] hdirRows = string.IsNullOrEmpty(Level.LayoutHalfDir) ? null : Level.LayoutHalfDir.Split('|');

            int hh = hrows.Length;

            for (int y = 0; y < hh; y++)
            {
                string row = hrows[y];
                string dirRow = (hdirRows != null && y < hdirRows.Length) ? hdirRows[y] : null;

                // 第一行 = 屏幕最上面（和主网格同一套规则）
                int ty = (hh - 1) - y;

                for (int x = 0; x < row.Length; x++)
                {
                    int layers = CharToLayers(row[x]);
                    if (layers <= 0) continue;

                    int dirX, dirY;
                    if (!CharToDir(dirRow, x, out dirX, out dirY))
                    {
                        dirX = 0;
                        dirY = 0;
                    }

                    // 自动起始层：压住的牌里最高的层 + 1 —— 中心牌天然叠在格点牌上面，不用手填参数
                    int baseLayer = ComputeHalfBaseLayer(x + 0.5f, ty + 0.5f);

                    for (int l = 0; l < layers; l++)
                        Tiles.Add(new SheepTile(nextId++, 0, baseLayer + l, x, ty, dirX, dirY, 0.5f, 0.5f));
                }
            }

            return nextId;
        }


        private static int Clamp01To100(int v)
        {
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }

        /// <summary>轮廓判定（归一化到 [-1,1]）：1=椭圆 2=菱形 3=圆角矩形。</summary>
        private bool InsideShape(int x, int y, int w, int h)
        {
            float nx = (w <= 1) ? 0f : (x / (float)(w - 1)) * 2f - 1f;
            float ny = (h <= 1) ? 0f : (y / (float)(h - 1)) * 2f - 1f;

            switch (Level.Shape)
            {
                case 2:   // 菱形
                    return System.Math.Abs(nx) + System.Math.Abs(ny) <= 1f;

                case 3:   // 圆角矩形
                    {
                        float ax = System.Math.Abs(nx);
                        float ay = System.Math.Abs(ny);
                        const float r = 0.55f;
                        if (ax <= r || ay <= r) return ax <= 1f && ay <= 1f;
                        float dx = ax - r, dy = ay - r;
                        return dx * dx + dy * dy <= (1f - r) * (1f - r);
                    }

                default:  // 椭圆
                    return nx * nx + ny * ny <= 1f;
            }
        }

        /// <summary>是不是轮廓边界格（实心且四邻有非实心）。</summary>
        private static bool IsBoundary(int x, int y, int w, int h, bool[,] mask)
        {
            if (!mask[x, y]) return false;
            if (x == 0 || y == 0 || x == w - 1 || y == h - 1) return true;
            return !mask[x - 1, y] || !mask[x + 1, y] || !mask[x, y - 1] || !mask[x, y + 1];
        }

        /// <summary>只保留最大的一块四邻连通区域。</summary>
        private static bool[,] KeepLargestComponent(bool[,] mask, int w, int h)
        {
            bool[,] best = new bool[w, h];
            bool[,] seen = new bool[w, h];
            int bestSize = 0;
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            for (int sy = 0; sy < h; sy++)
            {
                for (int sx = 0; sx < w; sx++)
                {
                    if (!mask[sx, sy] || seen[sx, sy]) continue;

                    List<int> comp = new List<int>();
                    Queue<int> queue = new Queue<int>();
                    queue.Enqueue(sy * w + sx);
                    seen[sx, sy] = true;

                    while (queue.Count > 0)
                    {
                        int cur = queue.Dequeue();
                        int cxx = cur % w;
                        int cyy = cur / w;
                        comp.Add(cur);

                        for (int k = 0; k < 4; k++)
                        {
                            int nx2 = cxx + dx[k];
                            int ny2 = cyy + dy[k];
                            if (nx2 < 0 || ny2 < 0 || nx2 >= w || ny2 >= h) continue;
                            if (!mask[nx2, ny2] || seen[nx2, ny2]) continue;
                            seen[nx2, ny2] = true;
                            queue.Enqueue(ny2 * w + nx2);
                        }
                    }

                    if (comp.Count > bestSize)
                    {
                        bestSize = comp.Count;
                        best = new bool[w, h];
                        for (int i = 0; i < comp.Count; i++)
                            best[comp[i] % w, comp[i] / w] = true;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// 按解锁顺序给牌分配图标，保证一定能凑出三个相同牌：
        /// 先把总数裁成 3 的倍数，再按「解锁步数」排序，每连续 3 张同一种图标。
        /// 玩家按从浅到深清牌时，每 3 张自动消除，卡槽最多积 2 张，永远不会卡死。
        /// </summary>
        private void AssignIconsByUnlockOrder()
        {
            int total = Tiles.Count / 3 * 3;
            if (Tiles.Count > total)
                Tiles.RemoveRange(total, Tiles.Count - total);

            int n = Tiles.Count;
            if (n <= 0) return;

            // 1) 覆盖关系：coverers[i] = 盖住第 i 张的牌下标（更高层且有重叠）
            List<List<int>> coverers = new List<List<int>>(n);
            for (int i = 0; i < n; i++) coverers.Add(new List<int>());
            for (int i = 0; i < n; i++)
            {
                SheepTile t = Tiles[i];
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    SheepTile o = Tiles[j];
                    if (o.Layer <= t.Layer) continue;
                    if (Overlaps(o, t)) coverers[i].Add(j);
                }
            }

            // 2) 解锁步数：step = 0（一开始就能点），否则 1 + max(盖住它的牌的 step)。
            //    盖住它的牌 layer 一定更高，所以按 layer 从高到低算即可保证 coverer 先算好。
            int[] step = new int[n];
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => Tiles[b].Layer.CompareTo(Tiles[a].Layer));

            for (int idx = 0; idx < n; idx++)
            {
                int i = order[idx];
                int best = -1;
                for (int k = 0; k < coverers[i].Count; k++)
                {
                    int c = coverers[i][k];
                    if (step[c] > best) best = step[c];
                }
                step[i] = coverers[i].Count == 0 ? 0 : best + 1;
            }

            // 3) 按 (step, id) 排序，每连续 3 张分配同一种图标
            int[] sorted = new int[n];
            for (int i = 0; i < n; i++) sorted[i] = i;
            System.Array.Sort(sorted, (a, b) =>
            {
                int c = step[a].CompareTo(step[b]);
                if (c != 0) return c;
                return Tiles[a].Id.CompareTo(Tiles[b].Id);
            });

            int kinds = System.Math.Max(1, System.Math.Min(Level.IconKinds, n / 3));
            int triple = 0;
            for (int i = 0; i < n; i++)
            {
                int kind = (triple % kinds) + 1;
                Tiles[sorted[i]].IconId = kind;
                if ((i + 1) % 3 == 0) triple++;
            }
        }

        /// <summary>两张牌的矩形是否重叠（与覆盖判定同口径）。</summary>
        private bool Overlaps(SheepTile a, SheepTile b)
        {
            float off = LayerOffsetRatio;
            float size = TileSizeRatio;
            float ax0 = a.X + a.SubX + a.Layer * off * a.StackX, ay0 = a.Y + a.SubY + a.Layer * off * a.StackY;
            float ax1 = ax0 + size, ay1 = ay0 + size;
            float bx0 = b.X + b.SubX + b.Layer * off * b.StackX, by0 = b.Y + b.SubY + b.Layer * off * b.StackY;
            float bx1 = bx0 + size, by1 = by0 + size;
            return ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1;
        }

        private void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        // ============================== 点牌 ==============================

        /// <summary>
        /// 点一张牌。返回是否接受这次点击（被盖住的牌 / 结束后的点击不接受）。
        /// 牌面上（OnBoard）和移出暂存区（Parked）的牌都能点：后者相当于"从槽位移回了游戏内"，再点一下又进槽。
        /// </summary>
        public bool TryTap(SheepTile tile)
        {
            if (IsOver) return false;
            if (tile == null) return false;

            bool fromHolding = tile.State == SheepTileState.Parked;
            if (!fromHolding)
            {
                if (tile.State != SheepTileState.OnBoard) return false;
                if (IsCovered(tile)) return false;
            }

            // 盲盒点开：显示图标
            if (tile.IsBlind)
                tile.Revealed = true;

            // 槽已满：不能再放牌（凑三消也不行，不存在"第 8 张"），直接判负
            if (Slot.Count >= SlotSize)
            {
                Lose();
                return true;
            }

            if (fromHolding)
                Holding.Remove(tile);

            // 同类聚堆：插到槽里最后一个同类的后面
            int insertAt = FindInsertIndex(tile.IconId);
            tile.State = SheepTileState.InSlot;
            Slot.Insert(insertAt, tile);
            lastPlaced = tile;
            lastPlacedFromHolding = fromHolding;

            // 延迟消除：插入后若形成 >=3 连消，先不消（数据暂留这组牌），
            // 由表现层在飞牌动画结束后调用 CompletePendingClear() 真正消除，并播消除动画。
            // 只有"没形成待消组"时才现在就判定胜负。
            bool pendingMatch = HasRunOfThreeAt(insertAt);
            if (!IsOver && !pendingMatch)
                CheckWinOrLoseAfterClear();

            // 槽刚好被填满、又没形成三消 → 立刻结算：
            // 还有能腾槽位的道具就先不判负（等玩家用），没有就直接判负（不用再点一次才失败）
            if (!IsOver && !pendingMatch)
                CheckSlotFullDeadlock();

            if (!IsOver)
                RaiseStateChanged();

            return true;
        }

        /// <summary>插入点（含前后同类）是否已形成 >=3 连消。</summary>
        private bool HasRunOfThreeAt(int insertAt)
        {
            if (Slot.Count == 0 || insertAt < 0 || insertAt >= Slot.Count) return false;

            int icon = Slot[insertAt].IconId;
            int start = insertAt;
            while (start > 0 && Slot[start - 1].IconId == icon) start--;
            int end = insertAt;
            while (end < Slot.Count - 1 && Slot[end + 1].IconId == icon) end++;
            return (end - start + 1) >= 3;
        }

        /// <summary>
        /// 表现层在"消除动画"播完后调用：真正消除槽里第一组 >=3 的连消，
        /// 判定胜负并刷新。返回是否发生了消除。
        /// </summary>
        public bool CompletePendingClear()
        {
            if (IsOver) return false;

            bool cleared = RemoveFirstRunOfThree();

            if (cleared)
                lastPlaced = null;

            CheckWinOrLoseAfterClear();

            if (!IsOver)
                RaiseStateChanged();

            return cleared;
        }

        /// <summary>移除槽里第一组 >=3 的连消（进槽时同类必然聚堆，每次最多一组）。</summary>
        private bool RemoveFirstRunOfThree()
        {
            for (int i = 0; i < Slot.Count; i++)
            {
                int icon = Slot[i].IconId;
                int start = i;
                while (start > 0 && Slot[start - 1].IconId == icon) start--;
                int end = i;
                while (end < Slot.Count - 1 && Slot[end + 1].IconId == icon) end++;

                int run = end - start + 1;
                if (run < 3)
                {
                    i = end;   // 跳过这一组
                    continue;
                }

                for (int k = end; k >= start; k--)
                {
                    Slot[k].State = SheepTileState.Removed;
                    Slot.RemoveAt(k);
                }
                return true;
            }
            return false;
        }

        /// <summary>找同类聚堆的插入位置（最后一个同类的后面；没有同类就放最后）。</summary>
        private int FindInsertIndex(int icon)
        {
            for (int i = Slot.Count - 1; i >= 0; i--)
            {
                if (Slot[i].IconId == icon) return i + 1;
            }
            return Slot.Count;
        }

        /// <summary>
        /// 槽满了就立刻判负（在"把牌放进槽之后"调用）。
        /// 规则：**槽满即失败**，不看还有没有道具可用。
        /// （道具要在槽满之前用掉；槽满后即使剩移出/撤回次数也算输。）
        /// </summary>
        private void CheckSlotFullDeadlock()
        {
            if (Slot.Count < SlotSize) return;

            Lose();
        }

        /// <summary>尝试消除插入点（含前后同类）形成的一整组（>=3 就消）。</summary>
        private void TryClearAt(int insertAt)
        {
            if (Slot.Count == 0) return;

            int icon = Slot[insertAt].IconId;
            int start = insertAt;
            while (start > 0 && Slot[start - 1].IconId == icon) start--;
            int end = insertAt;
            while (end < Slot.Count - 1 && Slot[end + 1].IconId == icon) end++;

            int run = end - start + 1;
            if (run < 3) return;

            // 消除整组：进槽时同类必然聚堆，所以 run 一定正好是 3（第三张放进来才触发）
            for (int i = end; i >= start; i--)
            {
                Slot[i].State = SheepTileState.Removed;
                Slot.RemoveAt(i);
            }

            lastPlaced = null;   // 触发消除的那张已不在槽里，撤回失效

            CheckWinOrLoseAfterClear();
        }

        /// <summary>
        /// 消完一组后判断胜负：牌面清空**且移出暂存区也清空**（暂存区的牌还能点回槽，所以不算通关），
        /// 再看槽位：槽也清空 = 赢；凑不齐 3 的倍数 = 输。
        /// </summary>
        private void CheckWinOrLoseAfterClear()
        {
            if (BoardCount > 0 || Holding.Count > 0) return;

            // 牌面清空了：槽里能全部凑成 3 的倍数才算赢
            if (Slot.Count == 0)
            {
                Win();
                return;
            }

            // 槽里每种图标数量都是 3 的倍数 → 自动消除（视为赢）
            if (CanClearSlot())
            {
                Win();
                return;
            }

            // 牌面没了但槽里剩的凑不齐 → 失败
            Lose();
        }

        private bool CanClearSlot()
        {
            if (Slot.Count % 3 != 0) return false;

            Dictionary<int, int> counts = new Dictionary<int, int>();
            for (int i = 0; i < Slot.Count; i++)
            {
                int icon = Slot[i].IconId;
                int c;
                counts.TryGetValue(icon, out c);
                counts[icon] = c + 1;
            }

            foreach (KeyValuePair<int, int> pair in counts)
            {
                if (pair.Value % 3 != 0) return false;
            }
            return true;
        }

        // ============================== 道具 ==============================

        /// <summary>撤回：把上一次放进槽（且还在槽里）的牌放回原处（牌面，或移出暂存区）。</summary>
        public bool TryUndo()
        {
            if (IsOver || UndoCount <= 0) return false;
            if (lastPlaced == null || lastPlaced.State != SheepTileState.InSlot) return false;

            Slot.Remove(lastPlaced);
            if (lastPlacedFromHolding)
            {
                // 这张是从暂存区点进来的：放回暂存区（不是牌面）
                lastPlaced.State = SheepTileState.Parked;
                Holding.Add(lastPlaced);
            }
            else
            {
                lastPlaced.State = SheepTileState.OnBoard;
            }
            // 盲盒保持已点开的样子（玩家已经看到过，没必要再藏起来）
            lastPlaced = null;
            lastPlacedFromHolding = false;
            UndoCount--;
            RaiseStateChanged();
            return true;
        }

        /// <summary>
        /// 移出：把卡槽最前面的 3 张挪到卡槽上方的**暂存区**（不是销毁）。
        /// 暂存区的牌还能点回卡槽，所以"每种图标数量是 3 的倍数"不会被破坏，不会因此卡关。
        /// 卡槽里不足 3 张时不能用 —— 保证"移出几张"永远是确定的 3 张。
        /// </summary>
        public bool TryRemove()
        {
            if (IsOver || RemoveCount <= 0) return false;
            if (Slot.Count < RemoveBatch) return false;

            for (int i = 0; i < RemoveBatch; i++)
            {
                SheepTile tile = Slot[0];
                Slot.RemoveAt(0);
                tile.State = SheepTileState.Parked;
                Holding.Add(tile);
            }

            lastPlaced = null;
            lastPlacedFromHolding = false;
            RemoveCount--;
            RaiseStateChanged();
            return true;
        }

        /// <summary>洗牌：重排牌面上剩余牌（含移出暂存区）的图标（位置 / 层 / 盲盒状态不变）。</summary>
        public bool TryShuffle()
        {
            if (IsOver || ShuffleCount <= 0) return false;

            List<SheepTile> boardTiles = new List<SheepTile>();
            for (int i = 0; i < Tiles.Count; i++)
                if (Tiles[i].State == SheepTileState.OnBoard || Tiles[i].State == SheepTileState.Parked)
                    boardTiles.Add(Tiles[i]);

            if (boardTiles.Count < 2)
                return false;

            List<int> icons = new List<int>(boardTiles.Count);
            for (int i = 0; i < boardTiles.Count; i++)
                icons.Add(boardTiles[i].IconId);

            ShuffleList(icons);

            for (int i = 0; i < boardTiles.Count; i++)
                boardTiles[i].IconId = icons[i];

            ShuffleCount--;
            RaiseStateChanged();
            return true;
        }

        // ============================== 胜负 ==============================

        private void Win()
        {
            Finish(true);
        }

        private void Lose()
        {
            Finish(false);
        }

        private void Finish(bool won)
        {
            if (IsOver) return;   // 幂等：槽满判负和后续点击可能都走到这里，只结算一次

            IsOver = true;
            IsWon = won;
            RaiseStateChanged();

            Action<bool> handler = GameOver;
            if (handler != null) handler(won);
        }

        private void RaiseStateChanged()
        {
            Action handler = StateChanged;
            if (handler != null) handler();
        }
    }
}

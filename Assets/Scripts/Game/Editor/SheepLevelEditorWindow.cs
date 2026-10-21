#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using GameFramework.Gameplay;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 羊了个羊关卡编辑器：**合并视图**摆牌 + 实时预览。
///
/// 一张网格同时显示两层（不会出现"看不到另一层"的问题）：
///   · 交叉点（●）= 格点牌：正好占一格；
///   · 格子中间（○）= 中心牌：落在四格交界，一张能压住周围四张，
///     层次自动算（压住的牌里最高的层 + 1），不用手填参数。
///
/// 用法：
///   1. Tools/Sheep/关卡编辑器 打开，设网格尺寸
///   2. 选方向笔刷 → 左键点位置 = +1 层（右键 -1 层，Shift+左键 只切方向）
///   3. 看「预览」确认（亮=可点、暗=被盖），再点各「复制」按钮粘进 SheepLevel.xlsx
///   4. 配表那关的 Style 改成 4
/// </summary>
public sealed class SheepLevelEditorWindow : EditorWindow
{
    private const int CellSize = 32;
    private const int MaxLayers = 35;

    private static readonly string[] DirNames = { "默认", "→ 横右", "← 横左", "↑ 纵上", "↓ 纵下" };

    private int gridW = 7;
    private int gridH = 9;

    private int[,] layers = new int[9, 7];        // 格点层 [y, x]
    private int[,] dirs = new int[9, 7];
    private int[,] halfLayers = new int[8, 6];    // 半格层 [(gridH-1), (gridW-1)]
    private int[,] halfDirs = new int[8, 6];

    private int brushDir;
    private int offsetPercent = 15;
    private int previewCell = 26;
    private Vector2 scroll;

    private SheepGame previewGame;
    private bool previewDirty = true;

    [MenuItem("Tools/Sheep/关卡编辑器")]
    public static void Open()
    {
        SheepLevelEditorWindow w = GetWindow<SheepLevelEditorWindow>("关卡编辑器");
        w.minSize = new Vector2(470f, 700f);
        w.Show();
    }

    private int HalfW { get { return Mathf.Max(1, gridW - 1); } }
    private int HalfH { get { return Mathf.Max(1, gridH - 1); } }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.BeginHorizontal();
        gridW = EditorGUILayout.IntField("宽 GridWidth", gridW);
        gridH = EditorGUILayout.IntField("高 GridHeight", gridH);
        EditorGUILayout.EndHorizontal();
        gridW = Mathf.Clamp(gridW, 2, 30);
        gridH = Mathf.Clamp(gridH, 2, 30);
        EnsureSize();

        EditorGUILayout.LabelField("方向笔刷（这一叠往哪边扇开）：");
        brushDir = GUILayout.Toolbar(brushDir, DirNames);

        EditorGUILayout.HelpBox(
            "● 交叉点 = 格点牌（正好占一格）\n" +
            "○ 格子中间 = 中心牌（落在四格交界，一张压住周围四张；层次自动叠在上面）\n" +
            "左键 +1 层　右键 -1 层　Shift+左键 只切方向\n" +
            "格子显示 \"6→\" = 6 层朝右扇开；无箭头 = 原地重叠",
            MessageType.Info);

        // ===== 合并网格 =====
        int dispW = 2 * gridW - 1;
        int dispH = 2 * gridH - 1;

        for (int dy = 0; dy < dispH; dy++)
        {
            EditorGUILayout.BeginHorizontal();
            for (int dx = 0; dx < dispW; dx++)
            {
                bool evenX = (dx & 1) == 0;
                bool evenY = (dy & 1) == 0;
                Rect r = GUILayoutUtility.GetRect(CellSize, CellSize);

                if (evenX && evenY)
                {
                    int gx = dx >> 1, gy = dy >> 1;
                    HandleClick(r, layers, dirs, gy, gx);
                    DrawCell(r, layers[gy, gx], dirs[gy, gx], false);
                }
                else if (!evenX && !evenY)
                {
                    int hx = (dx - 1) >> 1, hy = (dy - 1) >> 1;
                    if (hx < HalfW && hy < HalfH)
                    {
                        HandleClick(r, halfLayers, halfDirs, hy, hx);
                        DrawCell(r, halfLayers[hy, hx], halfDirs[hy, hx], true);
                    }
                    else
                    {
                        DrawDead(r);
                    }
                }
                else
                {
                    DrawDead(r);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("把方向刷到所有非空格")) ApplyBrushToAll();
        if (GUILayout.Button("清空")) ClearAll();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("复制 Layout")) EditorGUIUtility.systemCopyBuffer = BuildGrid(layers, gridW, gridH);
        if (GUILayout.Button("复制 LayoutDir")) EditorGUIUtility.systemCopyBuffer = BuildGridDir(dirs, gridW, gridH);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("复制 LayoutHalf")) EditorGUIUtility.systemCopyBuffer = BuildGrid(halfLayers, HalfW, HalfH);
        if (GUILayout.Button("复制 LayoutHalfDir")) EditorGUIUtility.systemCopyBuffer = BuildGridDir(halfDirs, HalfW, HalfH);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("从剪贴板载入 Layout")) LoadFromClipboard(false);
        if (GUILayout.Button("从剪贴板载入 LayoutHalf")) LoadFromClipboard(true);
        EditorGUILayout.EndHorizontal();

        // ===== 预览 =====
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("预览（用真实牌局算，亮=可点、暗=被盖）：");
        EditorGUILayout.BeginHorizontal();
        int newOff = EditorGUILayout.IntSlider("每层偏移 %", offsetPercent, 0, 30);
        if (newOff != offsetPercent) { offsetPercent = newOff; previewDirty = true; }
        previewCell = EditorGUILayout.IntSlider("格子像素", previewCell, 14, 40);
        EditorGUILayout.EndHorizontal();
        DrawPreview();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Layout（格点层，粘进 SheepLevel.xlsx）：");
        EditorGUILayout.LabelField(BuildGrid(layers, gridW, gridH), EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("LayoutDir（格点层方向）：");
        EditorGUILayout.LabelField(BuildGridDir(dirs, gridW, gridH), EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("LayoutHalf（半格层，尺寸 (W-1)x(H-1)）：");
        EditorGUILayout.LabelField(BuildGrid(halfLayers, HalfW, HalfH), EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("LayoutHalfDir（半格层方向）：");
        EditorGUILayout.LabelField(BuildGridDir(halfDirs, HalfW, HalfH), EditorStyles.wordWrappedLabel);

        EditorGUILayout.EndScrollView();
    }

    private void HandleClick(Rect r, int[,] cl, int[,] cd, int y, int x)
    {
        if (Event.current.type != EventType.MouseDown || !r.Contains(Event.current.mousePosition)) return;

        if (Event.current.button == 1)
            cl[y, x] = Mathf.Max(0, cl[y, x] - 1);
        else if (Event.current.shift)
            cd[y, x] = (cd[y, x] + 1) % 5;
        else
        {
            cl[y, x] = Mathf.Min(MaxLayers, cl[y, x] + 1);
            cd[y, x] = brushDir;
        }

        previewDirty = true;
        Event.current.Use();
        Repaint();
    }

    private void DrawCell(Rect r, int n, int dir, bool half)
    {
        Color prev = GUI.color;
        GUI.color = CellColor(n);
        GUI.Box(r, CellLabel(n, dir));
        GUI.color = prev;

        if (half)
        {
            // 中心牌加个小圈，和格点牌区分
            Color p2 = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.Box(new Rect(r.x + r.width * 0.5f - 3f, r.y + r.height * 0.5f - 3f, 6f, 6f), "");
            GUI.color = p2;
        }
    }

    private static void DrawDead(Rect r)
    {
        Color prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.10f);
        GUI.Box(r, "·");
        GUI.color = prev;
    }

    private static string CellLabel(int n, int dir)
    {
        if (n <= 0) return "";
        string s = n.ToString();
        switch (dir)
        {
            case 1: s += "→"; break;
            case 2: s += "←"; break;
            case 3: s += "↑"; break;
            case 4: s += "↓"; break;
        }
        return s;
    }

    private static Color CellColor(int n)
    {
        if (n <= 0) return new Color(0.32f, 0.32f, 0.32f);
        if (n == 1) return new Color(0.55f, 0.78f, 0.55f);
        if (n <= 3) return new Color(0.85f, 0.85f, 0.55f);
        if (n <= 5) return new Color(0.92f, 0.66f, 0.45f);
        return new Color(0.92f, 0.45f, 0.45f);
    }

    private void ApplyBrushToAll()
    {
        for (int y = 0; y < gridH; y++)
            for (int x = 0; x < gridW; x++)
                if (layers[y, x] > 0) dirs[y, x] = brushDir;

        for (int y = 0; y < HalfH; y++)
            for (int x = 0; x < HalfW; x++)
                if (halfLayers[y, x] > 0) halfDirs[y, x] = brushDir;

        previewDirty = true;
    }

    private void ClearAll()
    {
        layers = new int[gridH, gridW];
        dirs = new int[gridH, gridW];
        halfLayers = new int[HalfH, HalfW];
        halfDirs = new int[HalfH, HalfW];
        previewDirty = true;
    }

    private void EnsureSize()
    {
        int hw = HalfW, hh = HalfH;

        if (layers.GetLength(0) != gridH || layers.GetLength(1) != gridW)
        {
            int[,] nl = new int[gridH, gridW];
            int[,] nd = new int[gridH, gridW];
            int ch = Mathf.Min(gridH, layers.GetLength(0));
            int cw = Mathf.Min(gridW, layers.GetLength(1));
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++) { nl[y, x] = layers[y, x]; nd[y, x] = dirs[y, x]; }
            layers = nl; dirs = nd;
            previewDirty = true;
        }

        if (halfLayers.GetLength(0) != hh || halfLayers.GetLength(1) != hw)
        {
            int[,] hl = new int[hh, hw];
            int[,] hd = new int[hh, hw];
            int ch = Mathf.Min(hh, halfLayers.GetLength(0));
            int cw = Mathf.Min(hw, halfLayers.GetLength(1));
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++) { hl[y, x] = halfLayers[y, x]; hd[y, x] = halfDirs[y, x]; }
            halfLayers = hl; halfDirs = hd;
            previewDirty = true;
        }
    }

    // ===================== 预览 =====================

    private void EnsurePreviewGame()
    {
        if (!previewDirty && previewGame != null) return;

        previewDirty = false;
        try
        {
            previewGame = new SheepGame(BuildLevelData());
        }
        catch (System.Exception ex)
        {
            previewGame = null;
            UnityEngine.Debug.LogWarning("[关卡编辑器] 预览生成失败：" + ex.Message);
        }
    }

    private SheepLevelData BuildLevelData()
    {
        SheepLevelData data = new SheepLevelData();
        data.Id = 0;
        data.GridWidth = gridW;
        data.GridHeight = gridH;
        data.LayerCount = MaxLayers;
        data.Style = 4;
        data.IconKinds = 10;
        data.BlindCount = 0;
        data.SlotSize = 7;
        data.UndoLimit = 1;
        data.RemoveLimit = 1;
        data.ShuffleLimit = 1;
        data.Shape = 1;
        data.MinLayers = 1;
        data.EdgeNoise = 0;
        data.OffsetPercent = offsetPercent;
        data.HorizPercent = 50;
        data.TallPercent = 0;
        data.Layout = BuildGrid(layers, gridW, gridH);
        data.LayoutDir = BuildGridDir(dirs, gridW, gridH);
        data.LayoutHalf = BuildGrid(halfLayers, HalfW, HalfH);
        data.LayoutHalfDir = BuildGridDir(halfDirs, HalfW, HalfH);
        return data;
    }

    private void DrawPreview()
    {
        EnsurePreviewGame();

        float cell = previewCell;
        float w = (gridW + 1) * cell;
        float h = (gridH + 1) * cell;
        Rect area = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));

        Color prev = GUI.color;
        GUI.color = new Color(0.10f, 0.11f, 0.16f, 1f);
        GUI.DrawTexture(area, Texture2D.whiteTexture);

        int total = 0;
        int coveredCount = 0;

        if (previewGame != null && previewGame.Tiles.Count > 0)
        {
            total = previewGame.Tiles.Count;

            List<SheepTile> list = new List<SheepTile>(previewGame.Tiles);
            list.Sort(CompareLayer);

            float tile = cell * 0.9f;
            float off = cell * (offsetPercent / 100f);

            for (int i = 0; i < list.Count; i++)
            {
                SheepTile t = list[i];
                bool covered = previewGame.IsCovered(t);
                if (covered) coveredCount++;

                float tileX = t.X + t.SubX;
                float tileY = t.Y + t.SubY;
                float px = area.x + cell + tileX * cell + t.Layer * off * t.StackX;
                float py = area.y + cell + (gridH - 1 - tileY) * cell - t.Layer * off * t.StackY;

                Color baseCol = Color.HSVToRGB(((t.X * 7 + t.Y * 13) % 10 + 10) % 10 / 10f, 0.45f, 1f);
                GUI.color = covered
                    ? new Color(baseCol.r * 0.45f, baseCol.g * 0.45f, baseCol.b * 0.45f, 1f)
                    : baseCol;
                GUI.DrawTexture(new Rect(px, py, tile, tile), Texture2D.whiteTexture);
            }
        }

        GUI.color = prev;

        if (previewGame == null)
        {
            EditorGUILayout.HelpBox("预览生成失败，请检查布局字符串。", MessageType.Error);
            return;
        }

        int rawTotal = 0;
        for (int y = 0; y < gridH; y++)
            for (int x = 0; x < gridW; x++) rawTotal += layers[y, x];
        for (int y = 0; y < HalfH; y++)
            for (int x = 0; x < HalfW; x++) rawTotal += halfLayers[y, x];

        EditorGUILayout.LabelField(
            "总牌数 " + total + "　初始可点 " + (total - coveredCount) + "　被盖 " + coveredCount + "　原始 " + rawTotal);

        if (rawTotal != total)
        {
            EditorGUILayout.HelpBox(
                "原始 " + rawTotal + " 张不是 3 的倍数，实际只用 " + total + " 张（末尾 " + (rawTotal - total) + " 张会被裁掉）",
                MessageType.Warning);
        }
        if (total > 0 && total - coveredCount <= 0)
        {
            EditorGUILayout.HelpBox("初始一张都点不了，检查层次是否都压在一起了。", MessageType.Error);
        }
    }

    private static int CompareLayer(SheepTile a, SheepTile b)
    {
        return a.Layer.CompareTo(b.Layer);
    }

    // ===================== 导入导出 =====================

    private static string BuildGrid(int[,] data, int w, int h)
    {
        StringBuilder sb = new StringBuilder();
        for (int y = 0; y < h; y++)
        {
            if (y > 0) sb.Append('|');
            for (int x = 0; x < w; x++) sb.Append(LayerChar(data[y, x]));
        }
        return sb.ToString();
    }

    private static string BuildGridDir(int[,] data, int w, int h)
    {
        StringBuilder sb = new StringBuilder();
        for (int y = 0; y < h; y++)
        {
            if (y > 0) sb.Append('|');
            for (int x = 0; x < w; x++)
            {
                int d = data[y, x];
                sb.Append((d >= 1 && d <= 4) ? (char)('0' + d) : '.');
            }
        }
        return sb.ToString();
    }

    /// <summary>把剪贴板里的层数字符串载入：half=false 载入格点层，half=true 载入半格层。</summary>
    private void LoadFromClipboard(bool half)
    {
        string s = EditorGUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(s)) return;

        string[] rows = s.Trim().Split('|');
        int h = rows.Length;
        int w = 0;
        for (int i = 0; i < h; i++)
            if (rows[i].Length > w) w = rows[i].Length;

        if (half) { gridW = Mathf.Clamp(w + 1, 2, 30); gridH = Mathf.Clamp(h + 1, 2, 30); }
        else { gridW = Mathf.Clamp(w, 2, 30); gridH = Mathf.Clamp(h, 2, 30); }
        EnsureSize();

        if (half)
        {
            halfLayers = new int[HalfH, HalfW];
            halfDirs = new int[HalfH, HalfW];
            for (int y = 0; y < HalfH && y < rows.Length; y++)
                for (int x = 0; x < HalfW && x < rows[y].Length; x++)
                    halfLayers[y, x] = CharToLayers(rows[y][x]);
        }
        else
        {
            layers = new int[gridH, gridW];
            dirs = new int[gridH, gridW];
            for (int y = 0; y < gridH && y < rows.Length; y++)
                for (int x = 0; x < gridW && x < rows[y].Length; x++)
                    layers[y, x] = CharToLayers(rows[y][x]);
        }

        previewDirty = true;
        Repaint();
    }

    private static char LayerChar(int n)
    {
        if (n <= 0) return '.';
        if (n <= 9) return (char)('0' + n);
        return (char)('A' + (n - 10));
    }

    private static int CharToLayers(char c)
    {
        if (c >= '1' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'Z') return 10 + (c - 'A');
        if (c >= 'a' && c <= 'z') return 10 + (c - 'a');
        return 0;
    }
}
#endif

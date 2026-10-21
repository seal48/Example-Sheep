using UnityEngine;

namespace GameFramework.Gameplay
{
    /// <summary>
    /// 一关的玩法参数（不依赖配置表生成代码，方便测试 / 单独构造）。
    /// 正式数据从 SheepLevel 配置表读出，由 SheepLevelLoader 转成这个类。
    /// </summary>
    public sealed class SheepLevelData
    {
        public int Id;

        /// <summary>包围网格横向格数。</summary>
        public int GridWidth;

        /// <summary>包围网格纵向格数。</summary>
        public int GridHeight;

        /// <summary>层数：Style=1/2 是总层数；Style=3 是"高叠格"的层数上限。</summary>
        public int LayerCount;

        /// <summary>牌面样式：1=方格（满铺对齐）；2=堆叠（对齐+每层对角偏移）；3=牌山（不规则轮廓+横竖偏移）。</summary>
        public int Style;

        /// <summary>图标种类数。</summary>
        public int IconKinds;

        /// <summary>盲盒牌数量（原游戏玩法为 0）。</summary>
        public int BlindCount;

        /// <summary>卡槽容量（默认 7）。</summary>
        public int SlotSize;

        /// <summary>撤回道具次数。</summary>
        public int UndoLimit;

        /// <summary>移出道具次数。</summary>
        public int RemoveLimit;

        /// <summary>洗牌道具次数。</summary>
        public int ShuffleLimit;

        /// <summary>Style=3 轮廓：1=椭圆 2=菱形 3=圆角矩形。</summary>
        public int Shape;

        /// <summary>Style=3 普通格最低层数。</summary>
        public int MinLayers;

        /// <summary>Style=3 边缘噪声百分比（越高越不规则）。</summary>
        public int EdgeNoise;

        /// <summary>Style=3 每层视觉偏移（占一格百分比）。</summary>
        public int OffsetPercent;

        /// <summary>Style=3 横向堆叠的格子占比百分比（其余纵向）。</summary>
        public int HorizPercent;

        /// <summary>Style=3 高叠格占比百分比（这些格子叠到 LayerCount 层，形成"厚度"）。</summary>
        public int TallPercent;

        /// <summary>
        /// Style=4 人工布局：每格层数。一行一串、`|` 分隔行；`.`=空、`1`-`9`=层数、`A`-`Z`=10~35。
        /// 例："..333..|.311113|3111111"（行数应=GridHeight，每行长度应=GridWidth）。
        /// </summary>
        public string Layout;

        /// <summary>
        /// Style=4 人工布局：每格堆叠方向（可留空）。同 Layout 格式：
        /// `1`=横右 `2`=横左 `3`=纵上 `4`=纵下；`.`/空 = 原地重叠（不偏移）。
        /// </summary>
        public string LayoutDir;


        /// <summary>
        /// Style=4 人工布局：**半格层**（中心牌），尺寸 `(GridWidth-1) × (GridHeight-1)`，格式同 Layout。
        /// 半格层的每格固定落在四格交界（+0.5, +0.5），所以一张牌能压住周围四张。
        /// 层次**自动算**：压在它下面的牌里最高的层 + 1（没压到任何牌就是第 0 层）。
        /// </summary>
        public string LayoutHalf;

        /// <summary>Style=4 半格层的方向（可留空），格式同 LayoutDir。</summary>
        public string LayoutHalfDir;

    }
}

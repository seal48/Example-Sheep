using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.RedDot
{
    /// <summary>
    /// 红点 UI 组件：挂在任意 UI 元素上，填一个红点节点 Id，自动显示 / 隐藏 / 计数。
    ///
    /// 挂法：给入口按钮加一个子节点（或本身）挂 RedDotBadge，设置：
    /// - NodeId：红点节点 Id（配表 RedDot.xlsx 里定义）
    /// - ShowCount：true 时显示数字（0 隐藏；99+ 显示 "99+"）
    /// - Badge / CountText：不填则自动找子物体（Badge 默认就是本物体；CountText 找子物体 "RedDotText"）
    ///
    /// 组件在 OnEnable 订阅 RedDotManager.NodeChanged，OnDisable 退订，不轮询。
    /// 需要 GameController.Instance.RedDot 已初始化（UI 初始化之后生效）。
    /// </summary>
    public sealed class RedDotBadge : MonoBehaviour
    {
        [Tooltip("红点节点 Id（配表 RedDot.xlsx 定义）")]
        [SerializeField] private string nodeId;

        [Tooltip("显示数字（true 显示计数，false 只显示圆点）")]
        [SerializeField] private bool showCount;

        [Tooltip("红点底图（不填自动找子物体名为 Badge 的 Image，再没有就用本物体）")]
        [SerializeField] private Image badge;

        [Tooltip("数字文本（不填自动找子物体名为 RedDotText 的 Text）")]
        [SerializeField] private Text countText;

        private string lastNodeId;

        public string NodeId
        {
            get { return nodeId; }
            set
            {
                if (nodeId == value) return;
                nodeId = value;
                if (isActiveAndEnabled) Refresh();
            }
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            var mgr = GetManager();
            if (mgr != null) mgr.NodeChanged += OnNodeChanged;
            Refresh();
        }

        private void OnDisable()
        {
            var mgr = GetManager();
            if (mgr != null) mgr.NodeChanged -= OnNodeChanged;
        }

        private RedDotManager GetManager()
        {
            // GameControllerBridge.Instance 已经是 RedDotManager（内部反射拿 GameController.Instance.RedDot）
            return GameControllerBridge.Instance;
        }

        private void OnNodeChanged(string changedNodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(changedNodeId)) return;
            // 只关心自己节点或它的祖先 / 子孙链：节点 Id 用 '.' 分层，前缀匹配即可
            if (changedNodeId == nodeId
                || changedNodeId.StartsWith(nodeId + ".")
                || nodeId.StartsWith(changedNodeId + "."))
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            var mgr = GetManager();
            if (mgr == null || string.IsNullOrEmpty(nodeId) || !mgr.HasNode(nodeId))
            {
                SetVisible(false);
                return;
            }

            int count = mgr.GetCount(nodeId);
            bool on = mgr.IsOn(nodeId);
            SetVisible(on);

            if (on && showCount && countText != null)
            {
                countText.text = count > 99 ? "99+" : count.ToString();
            }
        }

        private void SetVisible(bool visible)
        {
            if (badge != null) badge.gameObject.SetActive(visible);
            else gameObject.SetActive(visible);   // 没有单独底图，就用自身显隐
            if (countText != null) countText.gameObject.SetActive(visible && showCount);
        }

        private void ResolveReferences()
        {
            if (badge == null)
            {
                var child = transform.Find("Badge");
                badge = child != null ? child.GetComponent<Image>() : null;
            }
            if (badge == null) badge = GetComponent<Image>();
            if (countText == null)
            {
                var child = transform.Find("RedDotText");
                countText = child != null ? child.GetComponent<Text>() : null;
            }
        }
    }
}

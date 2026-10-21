using System;
using System.Collections.Generic;
using GameFramework.Config;
using GameFramework.Core;
using GameFramework.Log;
using UnityEngine;

namespace GameFramework.RedDot
{
    /// <summary>
    /// 红点系统：树状红点 + 双模式点亮（推 / 拉）+ 事件驱动刷新。
    ///
    /// 树结构来自配置表 <c>RedDot.xlsx</c>（Id / ParentId），策划改表就能调整红点层级，不用改代码。
    ///
    /// 核心规则：
    /// - 父节点「亮」= 任一子节点亮（有子节点时）；叶子节点由业务设置
    /// - 父节点计数 = 子节点计数之和
    /// - 点亮两种模式：
    ///   1. 推模式：业务直接 SetState / SetCount（收到新邮件时 SetState("main.mail.unread", true)）
    ///   2. 拉模式：RegisterCondition 注册查询函数，数据变了调 NotifyChanged，系统自己去查
    ///
    /// 刷新靠事件驱动，不每帧轮询：业务改数据 → NotifyChanged(nodeId) → 该节点 + 祖先链重算 →
    /// 触发 <see cref="NodeChanged"/>，UI 的 <see cref="RedDotBadge"/> 订阅后自动显隐 / 计数。
    ///
    /// 用法（Game 层 / 热更）：
    ///   GameController.Instance.RedDot.RegisterCondition("main.mail.unread", () => mailSvc.UnreadCount);
    ///   GameController.Instance.RedDot.NotifyChanged("main.mail");          // 邮件数据变了
    ///   bool on = GameController.Instance.RedDot.IsOn("main.mail");         // UI 查询
    /// </summary>
    public sealed class RedDotManager : IGameModule
    {
        /// <summary>某个节点状态变化时触发，参数是节点 Id。UI 组件订阅它。</summary>
        public event Action<string> NodeChanged;

        /// <summary>红点节点运行时状态。</summary>
        private sealed class Node
        {
            public string Id;
            public Node Parent;
            public readonly List<Node> Children = new List<Node>();

            // 推模式：显式状态
            public bool ExplicitOn;
            public int ExplicitCount;

            // 拉模式：查询函数（返回 0=灭，>0=计数）
            public Func<int> Condition;

            // 缓存
            public bool CachedOn;
            public int CachedCount;
            public bool Dirty = true;
        }

        private ConfigManager config;
        private readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        private bool shutdown;

        /// <summary>是否已初始化（配置表可用且未关闭）。</summary>
        public bool IsInitialized { get { return config != null && !shutdown; } }

        /// <summary>节点总数（调试用）。</summary>
        public int NodeCount { get { EnsureTree(); return nodes.Count; } }

        /// <summary>初始化：读 RedDot 表建树。配置表就绪后调用（ProcedureInitUI 里，和 UI 一起）。</summary>
        public void Init(ConfigManager configManager)
        {
            config = configManager;
            // 注意：InitUI 可能在配置表加载完成前被调用（加载界面阶段），
            // 所以树不在这里强建，由 EnsureTree 在首次真正使用时懒建。
            EnsureTree();
        }

        /// <summary>确保树已构建（配置表就绪后懒建）。</summary>
        private void EnsureTree()
        {
            if (nodes.Count > 0) return;
            if (config == null || config.Database == null || config.Database.RedDot == null)
                return;   // 配置表还没就绪，等下次调用再建
            BuildTree();
        }

        /// <summary>重新从配置表建树（改表热更后调用）。</summary>
        public void BuildTree()
        {
            nodes.Clear();
            if (config == null || config.Database == null || config.Database.RedDot == null)
            {
                GameLog.Warn(LogTag.RedDot, "[RedDot] 配置表 RedDot 不可用，红点树为空。");
                return;
            }

            // 先建所有节点
            var rows = config.Database.RedDot.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                RedDotConfig row = rows[i];
                if (string.IsNullOrEmpty(row.Id) || nodes.ContainsKey(row.Id)) continue;
                nodes[row.Id] = new Node { Id = row.Id };
            }

            // 再挂父子关系（父在子前定义）
            for (int i = 0; i < rows.Count; i++)
            {
                RedDotConfig row = rows[i];
                if (string.IsNullOrEmpty(row.Id)) continue;
                Node child;
                if (!nodes.TryGetValue(row.Id, out child)) continue;
                if (string.IsNullOrEmpty(row.ParentId)) continue;   // 根节点
                Node parent;
                if (nodes.TryGetValue(row.ParentId, out parent))
                {
                    child.Parent = parent;
                    parent.Children.Add(child);
                }
                else
                {
                    GameLog.Warn(LogTag.RedDot, "[RedDot] 节点 " + row.Id + " 的父节点 " + row.ParentId + " 不存在。");
                }
            }

            GameLog.Info(LogTag.RedDot, "[RedDot] 红点树构建完成，共 " + nodes.Count + " 个节点。");
        }

        /// <summary>节点是否存在（配置表里有）。</summary>
        public bool HasNode(string nodeId)
        {
        EnsureTree();
            return !string.IsNullOrEmpty(nodeId) && nodes.ContainsKey(nodeId);
        }

        /// <summary>
        /// 查询节点是否亮（含子节点聚合）。未配置的节点返回 false。
        /// </summary>
        public bool IsOn(string nodeId)
        {
        EnsureTree();
            Node node;
            if (!nodes.TryGetValue(nodeId, out node)) return false;
            RefreshIfDirty(node);
            return node.CachedOn;
        }

        /// <summary>
        /// 查询节点计数（含子节点聚合，求和）。
        /// </summary>
        public int GetCount(string nodeId)
        {
        EnsureTree();
            Node node;
            if (!nodes.TryGetValue(nodeId, out node)) return 0;
            RefreshIfDirty(node);
            return node.CachedCount;
        }

        /// <summary>推模式：直接设置节点的亮 / 灭（叶子节点常用）。</summary>
        public void SetState(string nodeId, bool on)
        {
        EnsureTree();
            Node node;
            if (!nodes.TryGetValue(nodeId, out node)) return;
            if (node.ExplicitOn == on && !node.Dirty) return;
            node.ExplicitOn = on;
            MarkDirty(node);
        }

        /// <summary>推模式：直接设置节点的计数。</summary>
        public void SetCount(string nodeId, int count)
        {
        EnsureTree();
            Node node;
            if (!nodes.TryGetValue(nodeId, out node)) return;
            if (count < 0) count = 0;
            if (node.ExplicitCount == count && node.ExplicitOn == (count > 0) && !node.Dirty) return;
            node.ExplicitCount = count;
            node.ExplicitOn = count > 0;
            MarkDirty(node);
        }

        /// <summary>
        /// 拉模式：注册查询函数（返回 0=灭，&gt;0=计数）。数据变化时调 <see cref="NotifyChanged"/>。
        /// 返回一个用于注销的 Action（或调用 UnregisterCondition）。
        /// </summary>
        public void RegisterCondition(string nodeId, Func<int> condition)
        {
        EnsureTree();
            Node node;
            if (!nodes.TryGetValue(nodeId, out node)) return;
            node.Condition = condition;
            MarkDirty(node);
        }

        /// <summary>注销拉模式查询函数。</summary>
        public void UnregisterCondition(string nodeId)
        {
        EnsureTree();
            Node node;
            if (!nodes.TryGetValue(nodeId, out node)) return;
            node.Condition = null;
            MarkDirty(node);
        }

        /// <summary>
        /// 通知某个节点相关数据变了，刷新该节点 + 祖先链并广播 NodeChanged。
        /// 业务改完数据后调用（也可在事件回调里调）。
        /// </summary>
        public void NotifyChanged(string nodeId)
        {
        EnsureTree();
            Node node;
            if (!nodes.TryGetValue(nodeId, out node)) return;
            MarkDirty(node);
        }

        /// <summary>全量刷新所有节点（登录后 / 场景切换后调用）。</summary>
        public void RefreshAll()
        {
        EnsureTree();
            foreach (KeyValuePair<string, Node> kv in nodes)
                kv.Value.Dirty = true;
            foreach (KeyValuePair<string, Node> kv in nodes)
                RefreshNode(kv.Value);
        }

        /// <summary>清空所有红点（登出 / 切换账号时调用）。</summary>
        public void ClearAll()
        {
        EnsureTree();
            foreach (KeyValuePair<string, Node> kv in nodes)
            {
                kv.Value.ExplicitOn = false;
                kv.Value.ExplicitCount = 0;
                kv.Value.Condition = null;
                kv.Value.Dirty = true;
            }
            RefreshAll();
        }

        // ---------------- 内部 ----------------

        private void MarkDirty(Node node)
        {
            // 整个子树标脏：叶子条件才能重跑（否则父节点聚合用的是叶子的旧缓存）
            MarkSubtreeDirty(node);

            // 沿祖先链标记并刷新
            for (Node n = node; n != null; n = n.Parent)
            {
                if (n.Dirty) break;
                n.Dirty = true;
            }
            RefreshNode(node);
            if (NodeChanged != null) NodeChanged(node.Id);
        }

        /// <summary>把节点及其所有子孙节点标脏（叶子条件重算的前提）。</summary>
        private void MarkSubtreeDirty(Node node)
        {
            if (node == null) return;
            node.Dirty = true;
            for (int i = 0; i < node.Children.Count; i++)
                MarkSubtreeDirty(node.Children[i]);
        }

        private void RefreshIfDirty(Node node)
        {
            if (node.Dirty) RefreshNode(node);
        }

        private void RefreshNode(Node node)
        {
            if (!node.Dirty) return;
            node.Dirty = false;

            int count = node.ExplicitCount;
            bool on = node.ExplicitOn;
            if (node.Condition != null)
            {
                int c = 0;
                try { c = node.Condition(); }
                catch (Exception e)
                {
                    GameLog.Warn(LogTag.RedDot, "[RedDot] 节点 " + node.Id + " 的条件查询抛异常：" + e.Message);
                }
                if (c > 0) { on = true; count += c; }
            }

            // 子节点聚合（亮 = 任一子亮；计数 = 子计数和）
            for (int i = 0; i < node.Children.Count; i++)
            {
                Node child = node.Children[i];
                RefreshIfDirty(child);
                if (child.CachedOn) on = true;
                count += child.CachedCount;
            }

            node.CachedOn = on;
            node.CachedCount = count;

            // 父节点也标记需要重算
            if (node.Parent != null) node.Parent.Dirty = true;
        }

        /// <summary>Shutdown：清空订阅与节点。幂等。</summary>
        public void Shutdown()
        {
            if (shutdown) return;
            shutdown = true;
            NodeChanged = null;
            nodes.Clear();
            config = null;
        }
    }
}

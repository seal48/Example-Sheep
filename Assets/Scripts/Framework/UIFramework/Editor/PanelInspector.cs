using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameFramework.UI.Editor
{
    /// <summary>
    /// Panel 的 Inspector：默认面板 + 底部画出所有标了 <see cref="ButtonAttribute"/> 的方法按钮。
    ///
    /// [CustomEditor(typeof(Panel), true)] 的第二个参数 true 让它成为所有 Panel 子类
    /// （LoginPanel / MainPanel / ...）的兜底编辑器 —— 除非某个子类有自己的编辑器。
    ///
    /// 「刷新 UI 绑定」按钮除了重新解析节点，还会**重新生成 Xxx.Bindings.g.cs**：
    /// 预制体里删掉的节点 / 组件，对应的字段会从生成代码里消失，不会留下失效引用。
    /// </summary>
    [CustomEditor(typeof(Panel), true)]
    public class PanelInspector : UnityEditor.Editor
    {
        /// <summary>「刷新 UI 绑定」的方法名（Panel.Rebind）。</summary>
        private const string RefreshBindingsMethod = "Rebind";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            Panel panel = target as Panel;
            if (panel == null) return;

            // 收集该类（含基类、热更子类）所有标了 [Button] 的方法
            MethodInfo[] methods = panel.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (methods == null || methods.Length == 0) return;

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ButtonAttribute attr = method.GetCustomAttribute<ButtonAttribute>();
                if (attr == null) continue;

                string label = string.IsNullOrEmpty(attr.Label) ? method.Name : attr.Label;
                if (GUILayout.Button(label))
                {
                    method.Invoke(panel, null);

                    // 刷新绑定之后，按当前预制体重新生成绑定代码：
                    // 删掉的 UI 组件 / 节点 → 生成文件里的字段被清掉（这正是「刷新即清理」）
                    if (string.Equals(method.Name, RefreshBindingsMethod, System.StringComparison.Ordinal))
                        RegenerateBindings(panel);
                }
            }
        }

        /// <summary>右键组件菜单里的「刷新 UI 绑定」：同样重新解析 + 清掉过期字段。</summary>
        [MenuItem("CONTEXT/Panel/刷新 UI 绑定（并清理过期字段）", false, 1000)]
        private static void RefreshBindingsFromContext(MenuCommand command)
        {
            Panel panel = command.context as Panel;
            if (panel == null) return;

            panel.Rebind();
            RegenerateBindings(panel);
        }

        /// <summary>按当前预制体重新生成该面板的绑定代码（删掉不存在的节点/组件对应的字段）。</summary>
        private static void RegenerateBindings(Panel panel)
        {
            string prefabPath = GetPrefabAssetPath(panel);
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogWarning("[UIBind] 找不到 " + panel.GetType().Name + " 的预制体路径，跳过重新生成绑定。" +
                                 "（请在预制体 / 预制体实例上操作）");
                return;
            }

            if (UIBindGenerator.GeneratePanel(prefabPath))
                Debug.Log("[UIBind] 已刷新 " + panel.GetType().Name + " 的绑定代码（过期的字段已清理）：" + prefabPath);
        }

        /// <summary>取面板所在的预制体资产路径（支持预制体编辑模式 / 场景里的预制体实例）。</summary>
        private static string GetPrefabAssetPath(Panel panel)
        {
            if (panel == null) return null;

            // 预制体编辑模式（Prefab Stage）
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.IsPartOfPrefabContents(panel.gameObject))
                return stage.prefabAssetPath;

            // 场景里的预制体实例
            return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(panel.gameObject);
        }
    }
}

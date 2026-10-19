using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameFramework.UI.Editor
{
    /// <summary>
    /// Panel 的 Inspector：默认面板 + 底部画出所有标了 <see cref="ButtonAttribute"/> 的方法按钮。
    ///
    /// [CustomEditor(typeof(Panel), true)] 的第二个参数 true 让它成为所有 Panel 子类
    /// （LoginPanel / MainPanel / ...）的兜底编辑器 —— 除非某个子类有自己的编辑器。
    /// </summary>
    [CustomEditor(typeof(Panel), true)]
    public class PanelInspector : UnityEditor.Editor
    {
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
                }
            }
        }
    }
}

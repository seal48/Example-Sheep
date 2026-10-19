using System;

namespace GameFramework.UI
{
    /// <summary>
    /// 标记一个方法，让它在 Inspector 里显示成一个可点击的按钮（配合 <c>PanelInspector</c>）。
    ///
    /// 用法：标在 Panel 子类（或 Panel 本身）的任何方法上：
    ///   [Button("刷新 UI 绑定")]
    ///   public void Rebind() { ... }
    /// 然后选中该面板预制体 / 场景对象，Inspector 底部会直接出现按钮，点一下就调用。
    ///
    /// 比 [ContextMenu]（右键菜单）更直观，适合「经常要手动点一下」的操作。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ButtonAttribute : Attribute
    {
        /// <summary>按钮显示文字；不填用方法名。</summary>
        public string Label;

        public ButtonAttribute(string label = null)
        {
            Label = label;
        }
    }
}

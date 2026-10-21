using GameFramework.Audio;
using GameFramework.Scenes;
using GameFramework.Startup;
using GameFramework.UI;
using UnityEngine.UI;

/// <summary>
/// 菜单面板（主界面 MenuBtn 打开）：ExitBtn 退出账号并回到登录界面。
///
/// 退出流程：断开连接 → 关掉所有面板 → 加载 Start 场景（带进度条当缓冲）→ 切回登录流程。
/// 登录界面本身是 LoginPanel（同一 UI Root 常驻），ProcedureLogin 会把它打开。
/// </summary>
[UIPanel("MenuPanel", UILayer.Popup)]
public partial class MenuPanel : Panel
{
    /// <summary>登录界面所在场景（短名，真实地址 = SceneInitOptions.SceneFolder + 它 + .unity）。</summary>
    private const string LoginSceneName = "Start";

    private Button exitBtn;
    private Button closeBtn;

    protected override void OnInit()
    {
        exitBtn = Get<Button>("ExitBtn");
        closeBtn = Get<Button>("CloseBtn");

        if (exitBtn != null) exitBtn.onClick.AddListener(OnExitClicked);
        if (closeBtn != null) closeBtn.onClick.AddListener(Close);
    }

    protected override void OnClose()
    {
        if (exitBtn != null) exitBtn.onClick.RemoveListener(OnExitClicked);
        if (closeBtn != null) closeBtn.onClick.RemoveListener(Close);
    }

    /// <summary>退出：断线 + 关所有面板 + 回登录场景。</summary>
    private void OnExitClicked()
    {
        var controller = GameController.Instance;
        if (controller == null) return;

        // 0) 停掉背景音乐（回登录界面不放 BGM）
        AudioKit.StopBgm();

        // 1) 断开连接（等于退出登录）
        if (controller.gameClient != null && controller.gameClient.IsConnected)
            controller.gameClient.Disconnect();

        // 2) 关掉所有面板（大厅 / 商店 / 收藏…）
        if (controller.UI != null) controller.UI.CloseAll();

        // 3) 加载登录场景（SceneFramework 会显示 LoadingPanel 当缓冲）
        if (controller.Scene == null || controller.Procedures == null)
        {
            if (controller.Procedures != null) controller.Procedures.ChangeState<ProcedureLogin>();
            return;
        }

        var options = new SceneLoadOptions { Tip = "正在返回登录界面…" };
        controller.Scene.LoadAsync(LoginSceneName, options, OnLoginSceneLoaded);
    }

    private void OnLoginSceneLoaded(bool success, string error)
    {
        if (!success) return;

        var controller = GameController.Instance;
        if (controller != null && controller.Procedures != null)
            controller.Procedures.ChangeState<ProcedureLogin>();
    }
}

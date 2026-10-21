using System;
using GameFramework.Net.Protocol;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GM 测试面板：一个输入框 + 一个按钮。输入 GM 命令（不带账号，服务端用当前登录账号执行），
/// 点「执行」发给后端，结果回显在下方，命令说明列在 GmHelp 文本框里。
///
/// 节点字段（title / gmInput / gmExecBtn / gmResult / gmHelp）由 GmPanel.Bindings.g.cs 生成并绑定，
/// 这里只引用、不重复声明（避免 CS0102 重复定义）。
/// </summary>
[UIPanel("GmPanel", UILayer.Popup)]
public partial class GmPanel : Panel
{
    private IDisposable responseSub;

    /// <summary>命令说明（显示在 GmHelp 文本框里，方便照着用）。</summary>
    private const string HelpText =
        "additem 1001 5    增加道具（道具id 数量）\n" +
        "delitem 1001 2    扣除道具（道具id 数量）\n" +
        "addpet 1001       增加羊（1001~1004）\n" +
        "delpet 1001       移除羊\n" +
        "gold 1000         加金币（写负数=扣）\n" +
        "setlevel 3        设置等级\n" +
        "setattr exp 100   设置属性（属性名 值）\n" +
        "push              重新推送玩家数据";

    protected override void OnInit()
    {
        // 标题固定
        if (title != null) title.text = "GM 面板";

        // 命令说明
        if (gmHelp != null) gmHelp.text = HelpText;

        if (gmExecBtn != null)
            gmExecBtn.onClick.AddListener(OnExec);

        // 收 gm.response 回显结果（框架 ProtocolHub：强类型订阅）
        var business = GameController.Instance != null ? GameController.Instance.Business : null;
        if (business != null)
            responseSub = business.Hub.Register<GmResponse>(ProtocolId.GmResponse, OnGmResponse, "GmPanel");
    }

    protected override void OnClose()
    {
        if (gmExecBtn != null) gmExecBtn.onClick.RemoveListener(OnExec);
        if (responseSub != null) { responseSub.Dispose(); responseSub = null; }
    }

    private void OnExec()
    {
        string command = gmInput != null ? gmInput.text : string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            SetResult("请输入命令，例如：additem 1001 5");
            return;
        }

        var client = GameController.Instance != null ? GameController.Instance.gameClient : null;
        if (client == null || client.Client == null)
        {
            SetResult("未连接服务器");
            return;
        }

        client.Send(ProtocolId.GmRequest, new GmRequest { Command = command.Trim() });
        SetResult("已发送：" + command.Trim());
    }

    private void OnGmResponse(GmResponse response)
    {
        if (response == null) return;
        SetResult((response.Success ? "✔ " : "✘ ") + response.Message);
    }

    private void SetResult(string text)
    {
        if (gmResult != null) gmResult.text = text;
    }
}

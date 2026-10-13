# RedDotFramework — 红点系统

树状红点：策划在表里定义层级，业务注册条件或直接设置，UI 挂组件自动显隐。

## 核心概念

```
树（配表 RedDot.xlsx 定义）：
  main
  ├─ main.mail ── main.mail.unread
  ├─ main.activity ── main.activity.limited / main.activity.checkin
  ├─ main.friend ── main.friend.request
  └─ main.bag

规则：
- 父节点「亮」= 任一子节点亮；父计数 = 子计数求和
- 叶子节点由业务点亮（推 or 拉）
```

## 配表（RedDot.xlsx）

| 列 | 说明 |
|---|---|
| `Id` | 红点节点 ID（全局唯一，`.` 分层） |
| `ParentId` | 父节点 ID（空 = 根） |
| `SortOrder` | 同级排序 |
| `Description` | 描述 |

改树结构 = 改表 + 导出 + 重建资源，**不用改代码**（可热更）。

## 用法

```csharp
var redDot = GameController.Instance.RedDot;

// 拉模式（推荐）：条件函数，数据变了调 NotifyChanged
redDot.RegisterCondition("main.mail.unread", () => mailSvc.UnreadCount);
redDot.NotifyChanged("main.mail");            // 邮件数据变化后刷新

// 推模式：直接设置
redDot.SetState("main.activity.checkin", true);
redDot.SetCount("main.friend.request", 3);

// UI 查询
bool on = redDot.IsOn("main.mail");           // 节点或子节点亮
int count = redDot.GetCount("main.mail");     // 计数（子求和）

// 生命周期
redDot.RefreshAll();                          // 登录后全量刷
redDot.ClearAll();                            // 登出清空
```

## UI 组件（RedDotBadge）

给入口按钮（或其子物体）挂 `RedDotBadge`，设置：
- `NodeId`：红点节点 ID
- `ShowCount`：true 显示数字（99+ 显示 "99+"）
- 底图/文本：不填自动找子物体 `Badge` / `RedDotText`

组件订阅 `NodeChanged` 事件自动显隐，不轮询。放在 `Assets/Scripts/Framework/RedDotFramework/`（AOT）。

## 事件驱动

业务改数据 → `NotifyChanged(nodeId)` → 节点 + 祖先链重算 → 广播 `NodeChanged` → Badge 刷新。
不依赖每帧轮询。

## 热更友好

- 框架（AOT）：`RedDotManager` / `RedDotBadge` / 配表读取
- 业务（热更）：`RegisterCondition` / `SetState` / `NotifyChanged` —— 规则全在业务层
- 演示：`Assets/Scripts/Game/RedDotDemo.cs`（启动时注册，模拟新邮件亮灭）

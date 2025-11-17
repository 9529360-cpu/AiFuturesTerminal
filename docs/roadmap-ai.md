# AiFuturesTerminal Roadmap & Implementation Tasks

目标：把当前仓库进展整理为可执行的任务清单，驱动后续自动/手动开发。清单按照优先级列出，每项说明涉及模块、对 Agent/Execution 的影响、以及对应的 UI 入口。

---

## 优先级：P0（必须尽快完成）

1. 历史持仓 / 历史订单系统（后端 + UI）
   - 目标：提供一致且可控的数据流，Testnet 模式以 Binance 线上数据为准，DryRun/Backtest 以本地 history.db 为准。
   - 涉及模块：`Core/Exchanges/History/*`（`BinanceTradeHistoryService`, `BinanceOrderHistoryService`, `BinancePositionHistoryService`）、`Core/History/SqliteHistoryStore`、`Core/Analytics/BinanceTradeViewService`。
   - Agent/Execution 影响：Agent 在 Testnet/Live 不应写入本地 trade DB；Trade persistence 策略需与 `ProtectedTradeBook` 保持一致。
   - UI 入口：`UI/Views/MainWindow.xaml`（右侧“历史订单/历史成交” tab）、`UI/ViewModels/TodayHistoryViewModel`、`UI/ViewModels/TradeBookViewModel`。
   - 任务要点：
     - 确认并硬化 Testnet 分支：Read-only 本地，线上优先；禁止在 Testnet/Live 将线上成交写回本地（已实现但需验证并覆盖边界情况）。
     - 增加日志/指标，能够区分记录来源（local vs remote）。
     - 为 large-result 场景实现分页与按-symbol 拉取（避免一次性拉取所有 symbol）。
     - 在 UI 层显示数据来源提示（例如：Source: Binance Testnet / Local tradebook）。

2. 回测与实盘逻辑一致性保证（策略行为与 execution）
   - 目标：确保回测（Backtest/DryRun）与 Testnet/Live 的策略逻辑和订单路由保持一致，避免逻辑漂移。
   - 涉及模块：`Core/Backtest/*`、`Core/Strategy/*`、`Core/Execution/ExecutionEngine`、`Core/Execution/MockOrderRouter`、`Core/Execution/BinanceOrderRouter`、`Core/Analytics/ProtectedTradeBook`。
   - Agent/Execution 影响：高。Agent 的决策路径必须在回测和实盘下走同一套策略实现和风控检查。
   - UI 入口：Backtest 界面 `BacktestWindow` / `BacktestViewModel`，主界面的 ExecutionMode 切换。
   - 任务要点：
     - 对 `IStrategy` 接口与策略实现（ScalpingMomentum/TrendFollowing/RangeMeanReversion）进行审计，确认有无分支依赖于运行时环境。
     - 抽象与共享 OrderRouter 行为，确保 MockOrderRouter 的盈亏/填写逻辑模拟与 BinanceOrderRouter 的核心相同（fees/slippage/filled price calculation）。
     - 在回测与 DryRun 中，尽量复用 `ExecutionEngine` 的风控与成交仿真逻辑。
     - 增加整合测试：对同一历史 K 线数据分别在 Backtest 与 DryRun/Mock 环境跑策略，输出差异报告。

3. 风控（可视化与开/关控制）
   - 目标：把 `Core/Risk` 的实时状态暴露到 UI，允许观察并在必要时由 UI 发出暂停（KillSwitch）命令。
   - 涉及模块：`Core/Risk/*`（`GlobalRiskCoordinator`, `IRiskStatusService`, `GlobalRiskGuard`）、`Core/Analytics/ProtectedTradeBook`。
   - Agent/Execution 影响：高。风控决策必须能阻断 Agent 下单与 ExecutionEngine 执行。
   - UI 入口：`MainWindow.xaml` 风控面板（已有部分 VM `GlobalRiskStatusViewModel`），TradeBook/Analytics 窗口。
   - 任务要点：
     - 确保 `ExecutionEngine` 在下单前调用风控检查并尊重 `IsOpenNewPositionsFrozen` 状态。
     - 在主界面/风控窗展示：今日已实现盈亏、最大回撤、连续亏损笔数、KillSwitch 状态，并提供手动切换（仅限具有权限的开发环境/账号）。

---

## 优先级：P1（重要，次序可安排）

4. Today / 历史 Trades 的去重与冲突解决策略
   - 目标：合并本地与远端数据时采用确定性规则（远端优先覆盖本地/以 tradeId 为主键替换），避免时间窗口重叠导致重复/错位。
   - 涉及模块：`Core/Exchanges/History/BinanceTradeHistoryService`、`Core/History/SqliteHistoryStore`、`Core/Analytics/BinanceTradeViewService`。
   - UI 入口：历史 Trades tab、TradeBook 窗口。
   - 任务要点：
     - 修改合并逻辑：若远端条目具有 TradeId 则应覆盖本地同 TradeId 的记录；当 TradeId 不存在时再基于时间+symbol+price 做去重或打标记。
     - 提供 "来源" 字段用于调试及 UI 筛选。

5. 本地历史库维护与迁移工具（DEV only）
   - 目标：提供安全的 debug-only 清库、备份和恢复工具，支持迁移或重建本地 history.db。
   - 涉及模块：`Core/History/SqliteHistoryStore`、`UI/ViewModels/MainWindowViewModel`（清库按钮已添加）。
   - Agent/Execution 影响：低（仅开发辅助）。
   - 任务要点：
     - 保证 `ClearDatabaseForDevelopment` 仅在 DEBUG 编译可用（已实现）；为其添加确认日志与导出备份选项（可选）。
     - 提供 CLI/脚本用于导出 Trades/Orders 到 CSV 供离线分析。

6. Binance API 错误/重试与速率限制处理
   - 目标：在 `_client.GetUserTradesAsync` 等调用中统一处理 HTTP 错误、速率限制、重试与退避，避免 UI 卡顿或数据不一致。
   - 涉及模块：`Core/Exchanges/BinanceAdapter`、`Core/Exchanges/History/*`。
   - 任务要点：
     - 在 `BinanceAdapter` 层封装统一的重试策略（幂等 GET 请求），并在失败时返回清晰错误码给上层服务。
     - 在 History 服务中记录远端补丁失败的次数并限流补丁频率。

7. UI：TodayHistoryViewModel 的分页与按 symbol 查询
   - 目标：支持大数据量场景的分页和按 watched symbols 分批查询（已部分实现）；优化 UI 响应。
   - 涉及模块：`UI/ViewModels/TodayHistoryViewModel`、`Core/History/ITradeHistoryService`。
   - 任务要点：
     - 确保 TodayHistoryViewModel 的 RefreshTodayHistoryAsync 支持并发安全、分页加载与逐 symbol 拉取（已有逻辑）；增加进度与错误反馈给 UI。

8. 权限与配置管理（API key 的安全）
   - 目标：避免在源码或仓库中存储敏感凭据（现在 App.xaml.cs 包含示例 key，需要迁移到安全存储）。
   - 涉及模块：`App.xaml.cs`、配置管理服务、CI/CD 机密存储。
   - 任务要点：
     - 移除 hard-coded API keys，改为读取系统环境变量或用户配置文件（并在 README 指示如何设置）。
     - 在 CI/CD 把密钥设为 secret，不要提交到仓库。

---

## 优先级：P2（改进 / 长期）

9. 自动化测试与回归套件
   - 目标：为 Backtest、ExecutionEngine、Risk 和 History 增加单元与集成测试，确保行为一致。
   - 涉及模块：`Core/Backtest`、`Core/Execution`、`Core/Risk`、`Core/History`。
   - 任务要点：
     - 编写针对策略的回测一致性测试：同一策略在相同 K 线数据下，Backtest 与 DryRun/Mock 执行结果应一致（在合理误差范围内）。
     - 增加 CI 任务在 PR 时运行单元测试。

10. 性能与内存监控（长线）
    - 目标：监控 Trade 数据和 UI 大表格加载时的内存与响应，优化必要的数据结构与分页。
    - 涉及模块：UI 层、History store、Trade aggregation。

11. 文档与运行手册
    - 目标：完善 README、开发指南、配置说明（Testnet keys、history.db 管理、DEBUG 功能说明）。
    - 涉及模块：`docs/*`、`.mcp.json`（已存在）。

---

## 交付与里程碑建议（短期 2 周）
- Milestone 1（第 1 周）：完成 P0 的三项（History UI+Backend 一致性、策略回测/实盘一致性审计、风控 UI 可视化），并在本地完成手动回归验证。
- Milestone 2（第 2 周）：完成 P1 的 items（合并去重策略、清库与导出工具、Binance 重试策略、TodayHistory 分页提升）。
- Milestone 3（第 3-6 周）：补全 P2：自动化测试、性能监控与文档。

---

如果你确认该清单无遗漏，我将把这些条目拆解为具体的 GitHub issues/Task，并为每个 issue 生成实现步骤和估时（可选：自动创建并推送到你的远程仓库）。

请指示接下来的动作：
- A）把上述 roadmap 转为 Issues 并推送到 GitHub（需要确认授权与分配默认标签）；
- B）先从 P0 的某一项开始（请指定哪一项）并生成详细实现计划与代码补丁草案；
- C）只保存 roadmap，本地/远程不创建 issue。
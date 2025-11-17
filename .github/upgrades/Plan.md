# Plan for Agent loop and risk visualization integration

## Execution

1. PR 1 — Core: Introduce Agent run models and orchestrator logging
   - Add new types under `Core\Orchestration`:
     - `AgentRunContext`, `AgentRunResult`, `AgentRunLog`, `IAgentRunLogSink`, `InMemoryAgentRunLogSink`.
   - Update `AgentOrchestrator` to:
     - Build `AgentRunContext` from existing data (MarketSnapshot, AccountSnapshot, Now, StrategyId, RunId).
     - Invoke `IAgentService` to get planned orders (reuse existing `AgentDecision` -> map to `PlannedOrder`).
     - Invoke risk evaluation to split planned orders into allowed and blocked lists.
     - Submit allowed orders to `ExecutionEngine` to obtain `ExecutionInfo` list.
     - Compose `AgentRunResult` and `AgentRunLog`, append to `IAgentRunLogSink`.
   - Add constructor injection for `IAgentRunLogSink` into `AgentOrchestrator`.
   - Unit test / smoke test: call `RunOnceAsync` and verify sink has one log.

2. PR 2 — Core: Risk status service
   - Add `GlobalRiskStatus` and `IRiskStatusService` in `Core\Risk` namespace.
   - Implement `RiskStatusService` that queries TradeBook / Risk engine (or aggregates trade history) to compute:
     - `TodayRealizedPnl`, `TodayMaxDrawdown`, `ConsecutiveLosingTrades`, `IsOpenNewPositionsFrozen`.
   - Make it thread-safe and callable repeatedly.
   - Wire `AgentOrchestrator` to update risk state where appropriate (when executions occur) or let `RiskStatusService` poll TradeBook.
   - Unit test: simulate trades and validate computed status.

3. PR 3 — UI: MainWindow integration and view models
   - Extend `MainWindowViewModel`:
     - Add commands: `RunAgentOnceCommand`, `StartAgentLoopCommand`, `StopAgentLoopCommand`.
     - Add `AgentStatusText`, `RiskStatusViewModel` (or property), and `ObservableCollection<AgentRunLogViewModel>`.
     - Inject `IAgentOrchestrator`, `IAgentRunLogSink`, `IRiskStatusService`.
     - Implement timer or event subscription to refresh `RiskStatus` and `AgentRunLogs` periodically (e.g., every 2-5s) or on sink append event.
   - Modify `MainWindow.xaml`:
     - Reuse top buttons area to bind to the new commands and show `AgentStatusText` nearby.
     - Bind the existing right-top risk text to `RiskStatusViewModel` properties.
     - Change bottom log area to a `TabControl` with two tabs: "System Logs" (existing) and "Agent 运行日志" (DataGrid bound to `AgentRunLogs`).
   - Implement `AgentRunLogViewModel` mapper from `AgentRunLog` (format: time, symbol summary, planned count, blocked count, executed count, risk-blocked flag).
   - Manual test: run single-step agent, confirm new log appears, risk panel updates, and AgentStatusText shows running/stopped.

## Validation

1. Build: ensure solution builds after each PR.
2. Smoke tests:
   - PR1: run `AgentOrchestrator.RunOnceAsync` (or UI RunAgentOnce) — check `IAgentRunLogSink.Snapshot(1)` shows latest log.
   - PR2: simulate trades or use existing tradebook to verify `IRiskStatusService.GetCurrentStatus()` returns consistent values.
   - PR3: run app, press "Run Agent Once", observe:
     - AgentStatusText briefly shows activity and returns to "已停止" when finished (or shows "运行中" while loop runs).
     - Right-top risk text displays current `TodayRealizedPnl` and `IsOpenNewPositionsFrozen` appropriately.
     - Bottom Agent 运行日志 tab contains new entry with counts and risk-blocked status.
3. Thread-safety: run the loop and UI refresh concurrently to ensure no crashes.
4. Logs: ensure `InMemoryAgentRunLogSink` keeps recent N records (default 500) and Snapshot returns newest-first.

## Notes

- Keep changes minimal and non-invasive to existing public APIs where possible.
- Use existing types (e.g., `MarketSnapshot`, `AccountSnapshot`, `ExecutionInfo`, `PlannedOrder`) from current codebase; if types are missing, add minimal compatible definitions under `Core\Orchestration`.
- Avoid modifying existing UI layout semantics; only bind existing elements to new ViewModel properties and add the Agent logs tab into the bottom logs area.
- Each PR should include a short manual test checklist in the PR description.

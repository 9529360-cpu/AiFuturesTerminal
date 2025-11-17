# Manual Test: History Data Source Verification

This document describes manual steps to verify that history data (Trades / Orders / Positions) come from the correct source depending on ExecutionMode.

Files touched by this change:
- `Core/History/DelegatingTradeHistoryService.cs`
- `Core/History/DelegatingOrderHistoryService.cs`
- `Core/History/LocalTradeHistoryService.cs`
- `Core/History/LocalOrderHistoryService.cs`
- `UI/ViewModels/TodayHistoryViewModel.cs`

## Prerequisites
- Build the solution and run the app in Debug mode (so the Clear DB helper is available).
- Ensure your AppEnvironmentOptions in the app is configured with Testnet keys if you plan to validate Testnet calls.

## Steps

### 1) Switch to Testnet (verify online source)
1. Start the application.
2. In the main window, find the `Execution Mode` dropdown and select `Testnet`.
3. Wait for the Binance snapshot/state service to start (watch logs in the Logs panel).
4. Click the `刷新` (Refresh) button in the history area (or the `刷新` button near Trades/Orders) to load Today's history.
5. Expected behavior:
   - Trades / Orders / Positions shown in Today tab should reflect your Binance Testnet account data.
   - The UI should show recent trades even if history.db is empty.
6. Verification tips:
   - On disk, open `data/history.db` (if present) and confirm the newly shown trades were NOT inserted after refresh (sqlite timestamps/rows unchanged).
   - Alternatively, check the logs for messages stating data was fetched from Binance (services log warnings or debug messages).

### 2) Switch to DryRun (verify local history source)
1. Select `DryRun` from the `Execution Mode` dropdown.
2. Prepare a quick simulated trade: use the app's DryRun order entry or run a small agent simulation to generate trades (or manually insert into history.db using a SQLite client for quick test).
3. Click the `刷新` (Refresh) button in the history area.
4. Expected behavior:
   - Trades / Orders / Positions should now reflect entries from local `history.db`.
   - If you created simulated trades, they should appear in Today tab.
5. Verification tips:
   - Open `data/history.db` and confirm the rows exist in Trades/Orders tables.
   - Logs may show that the local store was queried.

### 3) Using Clear (DEBUG) helper to reset local DB
1. In Debug build only, click the `清空历史库（DEBUG）` button in the main window.
2. Confirm the dialog.
3. The app will clear `Trades`, `Orders` and `Meta` tables and VACUUM the file.
4. After clearing, switch to Testnet and refresh — you should still see online trades. Switch back to DryRun and confirm Today tab is empty until you generate local simulated trades.

## Notes
- The delegating services choose data source at call time based on the `AppEnvironmentOptions.ExecutionMode` instance. Changing the mode in the UI updates this option, and subsequent refresh calls will use the new source.
- For large watched-symbol lists, the UI may perform many per-symbol API calls in Testnet; consider using Binance state snapshot or limiting watched symbols during validation.

Path: `docs/manual-test-history.md`

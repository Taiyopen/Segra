# Sync log: upstream v1.7.0

- **Date**: 2026-08-06
- **Target**: `v1.7.0` (`d4f71ec`)
- **Base (pre-merge HEAD)**: WIP checkpoint `d61e415` (dual slots / browse / external library)
- **Strategy**: merge (not rebase); Windows TFM build verified (`net10.0-windows10.0.19041.0`)

---

## 1. 已從上游對齊

以下功能已合入本 fork，並通過 Windows 建置：

| 功能 | 狀態 | 備註 |
|------|------|------|
| ObsKit.NET **1.5.4** | 已對齊 | `Segra.csproj`；OBS init 反斜線 path 修復 |
| Frontend port **8000 → 44040** | 已對齊 | `Program.CreateStaticFileServer(startPort: 44040)` |
| Frontend URL version-stamp | 已對齊 | `index.html?v={appVersion}` 避免更新後舊 UI |
| Discord 改預設瀏覽器登入 | 已對齊 | `Backend/Auth/DiscordLoginService.cs` + OAuth callback |
| UI thread marshal（登入後視窗置頂修復） | 已對齊 | Photino / Program 路徑 |
| OBS **32.2.0** bundle | 已對齊 | `Obs/OBS 32.2.0.zip` |
| 刪除前確認設定 | 已對齊 | `ConfirmBeforeDeleting` + `useDeleteConfirmation` |
| 錄影磁碟空間顯示 | 已對齊 | `RecordingDriveUsedGb` / `RecordingDriveFreeGb` |
| Per-game recording overrides | 已對齊 | `GameSettingsService` / Settings `Games` + GameDetection UI |
| 關閉按鈕行為 / 啟動視窗模式 | 已對齊 | `CloseButtonAction` / `StartupWindowMode` / `LastWindowState` |
| 停用 Windows Game Mode | 已對齊 | `DisableWindowsGameMode` + `GameModeService` |
| 輸入降噪（RNNoise / InputNoiseSuppression） | 已對齊 | Settings + Audio UI |
| 服務路徑重整 | 已對齊 | Settings/Presets→`Core`；GameDetection/Integration→`Games`；Ai→`Media`；Preview/Recovery→`Recorder` 等 |
| Platform 抽象 + Linux/Flatpak packaging | 部分合入 | 檔案已進樹；本輪僅驗證 Windows TFM |
| Steam 遊戲庫擴充 / launcher→real exe | 已對齊 | `SteamUtils`、GameDetection 上游改進 |
| GTA / Rocket League 整合 | 已對齊 | 上游新增遊戲整合檔 |
| Separate clips / clip export 改進 | 已對齊 | ClipService 併入上游 `createSeparateClips` 等 |
| NotifyIcon / Migration / Diagnostics | 已對齊 | 上游 App 服務 |

---

## 2. 保留的 Fork 功能

| 功能 | 狀態 | 關鍵位置 |
|------|------|----------|
| 待剪輯（`待剪輯` / PendingEdit） | 保留 | FolderNames、ContentService、pending-edit、menu |
| 瀏覽影片 | 保留 | `BrowseService`、`browse-videos.tsx`、ContentServer roots |
| 外部影片庫 | 保留 | `ContentType.External`、FolderNames |
| 雙 slot 錄影 | 保留 | `RecordingSlots`、OBSService、AppState、GameIntegrationService |
| 獨立 PiP 監控窗 | 保留 | Program MonitoringWindow、MonitoringCompactShell |
| VRChat VVMW | 保留 | `VrChatVvmwIntegration`、OBS replay-tail clip |
| 剪輯設定強化 | 保留 | clip bitrate/rate control、clear segments、upload 後開瀏覽器、多音軌 |
| 監控／錄影 UX | 保留 | RecordingCard 雙槽、preview meters、playlist 流程 |

---

## 3. 刻意延後到下一輪（`v1.7.0` → `upstream/main`）

這些在 `v1.7.0` **之後**（約至 v1.7.1-beta.1），本輪**未**合入：

| 功能 | Upstream commits（摘要） |
|------|--------------------------|
| Content ID 鍵入 metadata / thumbnail / waveform | `eeb6df1` |
| FE→BE 訊息只傳 content id | `c15d3b0` |
| 壓縮標記改 metadata flag（非檔名 suffix） | `c695647` |
| 右鍵 context menu | `0813546` / `#194` |
| OBSService 進一步改用 ObsKit built-ins（main 上後續 refactor） | `c0476c4`（部分 API 已在本輪 ObsKit 1.5.4 合併時移植） |
| 壓縮保留所有音軌等後續修復 | `c331c54` 等 |

---

## 4. 合併過程備註

- 衝突約 **98** 檔（大量 add/add，歷史高度分歧）。
- 為避免雙份服務，刪除舊 `Backend/Services/*` 複本（**保留** `BrowseService.cs`），雙 slot／VrChat 邏輯已移植到上游新路徑。
- `OBSService` 以 fork 雙槽結構為基底，再移植 ObsKit 1.5 / v1.7.0 API。
- 本輪驗證：`dotnet build Segra.csproj -f net10.0-windows10.0.19041.0` → **0 errors**。
- Linux TFM / Flatpak 執行時未在本輪驗證。

## 5. 建議後續

1. 手動 smoke：雙槽錄影、待剪輯、瀏覽影片、Discord 登入、刪除確認、磁碟空間。
2. 前端 `npm install` + rebuild wwwroot（若 UI 與 bundle 不一致）。
3. 下一輪再 merge `upstream/main` 的 content-id 與右鍵選單。

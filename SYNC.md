# 上游與 Fork 功能追蹤

- **Fork HEAD 基準**：已合入 upstream `v1.7.0`（`d4f71ec`，2026-08-06）
- **現況目標 tag**：`v1.7.4`（不要追 `upstream/main`／`v1.7.5-beta.*`）
- **對齊方式**：挑功能移植，禁止整包 merge（見 `.cursor/rules/upstream-sync.mdc`）
- 上次 fetch 後，相對 `v1.7.4` 約超前 773 commit、落後 48 commit

狀態：`已追上` / `未合` / `跳過` / `進行中`

星等（越多越建議合進來）：

| 星 | 意思 |
|---|---|
| ★★★ | 優先：使用者立刻有感，且多半可搬（或衝突可控） |
| ★★ | 值得要：實用，但要手移，或會碰到熱檔 |
| ★ | 低優先或預設跳過：跟 fork 核心纏太深、或本 fork 用不到 |

熱檔：`OBSService.cs`、`video.tsx`、`MessageService.cs`、`ContentService.cs`、`ContentCard.tsx`、`Program.cs`

---

## Fork 獨立功能（不要當上游 diff 清掉）

這些是本 fork 有、上游沒有（或上游後來走向不同）的東西。對齊時必須保留。

| 功能 | 關鍵位置 | 備註 |
|---|---|---|
| 雙 slot 錄影 | `RecordingSlots`、`OBSService`、`AppState`、`RecordingCard` | 熱檔核心 |
| 待剪輯 | `FolderNames`（`待剪輯`）、`ContentService`、`pending-edit.tsx` | UI 在 ContentCard／ContentPage／video.tsx 有重複 |
| 瀏覽影片 | `BrowseService`、`browse-videos.tsx` | 仍放在 `Backend/Services/` |
| 外部影片庫 | `ContentType.External`、`FolderNames` | 不計入儲存上限 |
| 獨立 PiP 監控窗 | `Program.cs` MonitoringWindow、`MonitoringApp.tsx` | 第二 Photino |
| VRChat VVMW | `VrChatVvmwIntegration.cs` | replay-tail clip |
| 剪輯設定強化 | clip bitrate／rate control、clear segments、上傳後開瀏覽器、多音軌 | 與上游 clip 改進有重疊，合時要對過 |
| 監控／錄影 UX | 雙槽卡、preview meters、playlist | |

---

## 已追上（合入 v1.7.0 那輪）

來源：`SYNC_v1.7.0.md`。星等是「當時值得合」的回顧，不是待辦。

| 功能 | 星 | 狀態 | 備註 |
|---|---|---|---|
| ObsKit.NET 1.5.4 | ★★★ | 已追上 | OBS 反斜線 path |
| Frontend port 8000→44040 | ★★★ | 已追上 | |
| Frontend URL version-stamp | ★★★ | 已追上 | |
| Discord 改預設瀏覽器登入 | ★★★ | 已追上 | |
| UI thread marshal（登入後置頂） | ★★★ | 已追上 | |
| OBS 32.2.0 bundle | ★★ | 已追上 | 上游已升到 32.2.2，見下方未合 |
| 刪除前確認 | ★★★ | 已追上 | |
| 錄影磁碟空間顯示 | ★★★ | 已追上 | 上游後來改了 meter UI |
| Per-game recording overrides | ★★★ | 已追上 | `Settings.Games` |
| 關閉按鈕／啟動視窗模式 | ★★★ | 已追上 | |
| 停用 Windows Game Mode | ★★ | 已追上 | 上游 1.7.2 起刪除此設定；本 fork **保留** |
| 輸入降噪 RNNoise | ★★★ | 已追上 | |
| 服務路徑重整 | ★★ | 已追上 | Core／Games／Media／Recorder |
| Steam 庫／launcher→exe | ★★★ | 已追上 | |
| GTA／Rocket League 整合 | ★★★ | 已追上 | |
| Separate clips／clip export | ★★★ | 已追上 | |
| NotifyIcon／Migration／Diagnostics | ★★ | 已追上 | |
| Platform 抽象＋Linux／Flatpak | ★ | 已追上（部分） | 檔案在樹裡；Windows 為準，執行時未驗證 Linux |
| 右鍵選單＋篩選清空＋框選 | ★★★ | 已追上 | 2026-09-03；待剪輯選單保留 |
| 關閉自動錄影遊戲 | ★★★ | 已追上 | 2026-09-03；`autoRecordGames` |
| 播放路徑外／庫內檔 | ★★★ | 已追上 | 2026-09-03；Browse 授權仍在 |

---

## 未合：v1.7.0 → v1.7.4

建議順序：先 ★★★，再 ★★；★ 預設不動。同一列的 commit 要當**一組**合，不要拆開。

### ★★★ 優先

| 功能 | 上游 | 狀態 | 為什麼推薦 | 碰撞 |
|---|---|---|---|---|
| 右鍵選單 | `0813546` `b17056f` `#194` | 已追上 | 2026-09-03 手移；保留待剪輯／外部庫選項 | `ContentCard` |
| 篩選刪光後清空 | `a1e166a` `#198` | 已追上 | 2026-09-03 手移 | `ContentPage` |
| 框選多個內容卡 | `623d43f` | 已追上 | 2026-09-03 手移；選取鍵仍用 fileName | `ContentPage`／`ContentCard` |
| 關閉自動錄影的遊戲 | `db66f94` | 已追上 | 2026-09-03 手移 | GameDetection UI |
| 播放錄影路徑外的檔 | `b1c22a9` | 已追上 | 2026-09-03 手移；保留 BrowseService 授權 | `ContentServer` |
| 快取資料夾打不開則回退 | `370924a` | 未合 | 小、能少崩潰 | `Program.cs` |
| 內容資料夾消失的處理 | `2e1f813` | 未合 | 同上 | `Program.cs` |
| Copy 壓縮到 Discord 上限 | `ba359a6` `496718e` | 未合 | 實用（預設 20MB） | `video.tsx`、Compression |
| Discord Canary／PTB | `08a3041` `ab39547` `#211` | 未合 | 抓遊戲 Discord 音訊更齊 | `OBSService`（要手移，不要整檔） |
| 記住視窗大小＋托盤重設 | `1ef10de` | 未合 | 跟現有 `LastWindowState` 同方向 | `Program.cs` |
| 設定裡 Discord／docs 連結 | `d4d2db3` | 未合 | 純 UI、幾乎無衝突 | AdvancedSection |

### ★★ 值得手移

| 功能 | 上游 | 狀態 | 為什麼 | 碰撞 |
|---|---|---|---|---|
| 時間軸：區段縮放／playhead／無區段提示 | `beedd5c` `9173f6f` `bb80cd4` `28a2ba5` | 已追上 | 2026-09-03 手移；保留待剪輯／playlist／多音軌 | `video.tsx` |
| 波形繪製 | `550783d` | 已追上 | 2026-09-03 手移；duration 用 metadata，換片鍵用 filePath | `video.tsx` |
| 音訊 in／out 軌圖示 | `c49f592` `#206` | 未合 | 多音軌 UX；fork 已有多軌 | OBSService＋video.tsx |
| 剪輯／上傳背景封面 | `d36320c` | 未合 | 上傳外觀 | video.tsx、Upload |
| 卡片虛擬化／placeholder | `b2f3e31` `2e4f084` | 未合 | 列表效能 | `ContentCard` |
| 儲存空間 meter UI | `49a500f` | 未合 | 你們已有 GB 數字，這是 UI | StorageSettings |
| OBS 32.2.1→32.2.2（含 ffmpeg 9） | `c21af58` `b2ec7e1` `d82a694` `ae5ead8` | 未合 | 最終只要 **32.2.2**，別逐版合 zip | bundle＋OBS 路徑 |
| 錄影前先 hook game capture | `39541b4` | 未合 | 少黑屏；必須手移進雙槽 `StartRecording` | `OBSService` |
| 語音 mute race | `6773ffc` | 未合 | 穩定性；同上 | `OBSService` |
| 高光自動建立／音訊重複 | `260b538` | 未合 | 修高光 | HighlightService＋OBSService |
| Clip 硬解加速 | `d805c08` | 未合 | 大檔剪輯 | ClipService／FFmpegService |
| 啟動變快／啟動體驗 | `0b07165` `4078e26` | 未合 | 體感；Program 有 PiP 窗 | `Program.cs`、`main.tsx` |
| 壓縮改 metadata flag（非檔名 suffix） | `c695647` | 未合 | 與下一列當一組 | ContentService |
| 壓縮保留所有音軌 | `c331c54` | 未合 | 跟上一列一組 | CompressionService |
| 上傳 metadata 帶 exe 檔名 | `5f4966f` | 未合 | 小欄位 | 多個 Media 檔 |
| content id 鍵 metadata／縮圖／波形 | `eeb6df1` | 未合 | 架構正確，但改很廣 | **當專案**，不要拆 |
| FE→BE 只傳 content id | `c15d3b0` | 未合 | 必跟上一列同一組 | MessageService、video.tsx、ContentCard |

### ★ 低優先或跳過

| 功能 | 上游 | 狀態 | 原因 |
|---|---|---|---|
| OBSService 改用 ObsKit built-ins | `c0476c4` | 跳過 | 雙槽就長在 `OBSService`；再合等於重寫錄影 |
| 刪除 Windows Game Mode 設定 | `18e0938` | 跳過 | 本 fork 要留這個設定 |
| 只保留 beta、拿掉 RC 文案 | `f1d33f2` | 未合 | 發版流程，與功能無關 |
| 依賴與雜項 cleanup | `0c8cb51` `2fbfae9` | 跳過 | 不要為清理而合；容易誤傷 fork |

---

## 暫不追：v1.7.4 之後（main／v1.7.5-beta）

等 `v1.7.5` 穩定 tag 再評估。現在不要合。

| 功能 | 上游 | 星 | 狀態 |
|---|---|---|---|
| 設定 UI 整理＋full-fps 錄影預覽 | `2ce536b` | ★★ | 未合（等 tag） |
| video settings layout | `ee8fc31` | ★ | 未合（等 tag） |
| Wayland 無 X 時 recorder init | `2cf4d04` | ★ | 跳過（Linux） |
| Linux OBS 32.2.2 | `4b97458` | ★ | 跳過（Linux） |

---

## 維護

對齊完一個功能後：把該列狀態改成 `已追上`，必要時加一行備註（哪個 commit／哪天）。不要另開第三份追蹤表。

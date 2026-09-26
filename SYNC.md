# 上游與 Fork 功能追蹤

- **Fork HEAD 基準**：已合入 upstream `v1.7.0`（`d4f71ec`，2026-08-06）
- **現況目標 tag**：`v1.8.0`（`36f42ad`，2026-09-17；不要追 `upstream/main`／beta）
- **對齊方式**：挑功能移植，禁止整包 merge（見 `.cursor/rules/upstream-sync.mdc`）
- 上次 fetch（2026-09-20）後，相對 `v1.8.0` 約超前 776 commit、落後 93 commit（`upstream/main` 目前與 `v1.8.0` 同一點）

狀態：`已追上` / `未合` / `跳過` / `進行中`

星等（越多越建議合進來）：

| 星 | 意思 |
|---|---|
| ★★★ | 優先：使用者立刻有感，且多半可搬（或衝突可控） |
| ★★ | 值得要：實用，但要手移，或會碰到熱檔 |
| ★ | 低優先或預設跳過：跟 fork 核心纏太深、或本 fork 用不到 |

熱檔：`OBSService.cs`、`video.tsx`、`MessageService.cs`、`ContentService.cs`、`ContentCard.tsx`、`Program.cs`

**Release 欄**：對應 GitHub 穩定 Release 頁的描述（繁中，保留官方分類）。沒出現在穩定 Release 的寫 `Release 未列`，不要用 commit 主旨冒充。

---

## Fork 獨立功能（不要當上游 diff 清掉）

這些是本 fork 有、上游沒有（或上游後來走向不同）的東西。對齊時必須保留。

| 功能 | 關鍵位置 | 備註 |
|---|---|---|
| 雙 slot 錄影 | `RecordingSlots`、`OBSService`、`AppState`、`RecordingCard` | 熱檔核心 |
| 待剪輯 | `FolderNames`（`待剪輯`）、`ContentService`、`pending-edit.tsx` | UI 在 ContentCard／ContentPage／video.tsx 有重複 |
| 瀏覽影片 | `BrowseService`、`browse-videos.tsx` | 仍放在 `Backend/Services/` |
| 外部影片庫 | `ContentType.External`、`FolderNames` | 不計入儲存上限 |
| 獨立 PiP 監控窗 | `Program.cs` MonitoringWindow、`MonitoringApp.tsx` | 第二 Photino；上游已遷 PhotinoX，合時必須手移 |
| VRChat VVMW | `VrChatVvmwIntegration.cs` | replay-tail clip |
| 剪輯設定強化 | clip bitrate／rate control、clear segments、上傳後開瀏覽器、多音軌 | 與上游 clip 改進有重疊，合時要對過 |
| 監控／錄影 UX | 雙槽卡、preview meters、playlist | |

---

## 已追上（合入 v1.7.0 那輪）

來源：`SYNC_v1.7.0.md`。星等是「當時值得合」的回顧，不是待辦。v1.7.1 起的項目以下方購物清單的 Release 欄為準。

| 功能 | 星 | 狀態 | 備註 |
|---|---|---|---|
| ObsKit.NET 1.5.4 | ★★★ | 已追上 | OBS 反斜線 path |
| Frontend port 8000→44040 | ★★★ | 已追上 | |
| Frontend URL version-stamp | ★★★ | 已追上 | |
| Discord 改預設瀏覽器登入 | ★★★ | 已追上 | |
| UI thread marshal（登入後置頂） | ★★★ | 已追上 | |
| OBS 32.2.0 bundle | ★★ | 已追上 | 上游已升到 32.2.2／32.2.2-segra，見下方未合 |
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

| 功能 | 上游 | 狀態 | Release | 為什麼推薦 | 碰撞 |
|---|---|---|---|---|---|
| 右鍵選單 | `0813546` `b17056f` `#194` | 已追上 | v1.7.1：**UI**：影片卡新增右鍵選單。 | 2026-09-03 手移；保留待剪輯／外部庫選項 | `ContentCard` |
| 篩選刪光後清空 | `a1e166a` `#198` | 已追上 | v1.7.1：**UI**：篩選某遊戲的最後一支影片刪掉後，遊戲篩選不再顯示空清單。 | 2026-09-03 手移 | `ContentPage` |
| 框選多個內容卡 | `623d43f` | 已追上 | v1.7.1：**UI**：影片庫可拖曳框選；並修 Ctrl+Click 無反應。 | 2026-09-03 手移；選取鍵仍用 fileName | `ContentPage`／`ContentCard` |
| 關閉自動錄影的遊戲 | `db66f94` | 已追上 | v1.7.3：**Settings**：偵測到遊戲時可關閉自動錄影（#219）。 | 2026-09-03 手移 | GameDetection UI |
| 播放錄影路徑外的檔 | `b1c22a9` | 已追上 | v1.7.1：**Playback**：可播放存放在「目前錄影資料夾」之外的錄影。 | 2026-09-03 手移；保留 BrowseService 授權 | `ContentServer` |
| 快取／錄影資料夾打不開則回退 | `370924a` `2e1f813` | 未合 | v1.7.3：**Startup**：設定的錄影或快取資料夾無法使用（例如磁碟未掛載）時，改回預設資料夾並通知，避免啟動失敗。 | 兩 commit 當一組 | `Program.cs` |
| Copy 壓縮到 Discord 上限 | `ba359a6` `496718e` | 未合 | v1.7.2：**Sharing**：Copy as 10/50/100/500 MB，先壓到選定大小再複製（#140）。v1.7.3：**Upload**：預設「copy as compressed」從 10 MB 改 20 MB，對齊 Discord 新上傳上限。 | 兩條 Release 當一組 | `video.tsx`、Compression |
| Discord Canary／PTB | `08a3041` `ab39547` `#211` | 未合 | v1.7.3：**Recording**：可偵測 Discord Canary、Discord PTB 為語音軟體。 | 抓遊戲 Discord 音訊更齊 | `OBSService`（要手移，不要整檔） |
| 記住視窗大小＋托盤重設 | `1ef10de` | 未合 | v1.7.2：**Window**：記住視窗大小與最大化，重開回復原狀（#182）。**Tray**：新增 Reset window size 恢復預設大小；Exit 改名 Quit，視窗已顯示時隱藏 Open。 | 跟現有 `LastWindowState` 同方向；與 `e6bab8c` 當一組 | `Program.cs` |
| 設定裡 Discord／docs 連結 | `d4d2db3` | 未合 | v1.7.2：**Settings**：新增 Discord 與文件連結。 | 純 UI、幾乎無衝突 | AdvancedSection |

### ★★ 值得手移

| 功能 | 上游 | 狀態 | Release | 為什麼 | 碰撞 |
|---|---|---|---|---|---|
| 時間軸：區段縮放／playhead／無區段提示 | `beedd5c` `9173f6f` `bb80cd4` `28a2ba5` | 已追上 | v1.7.2：**Clip Editor**：拉區段時 playhead 不動、影片預覽被拉的那一端；拉區段時暫停播放、放開從正確位置續播；拉起點後 playhead 移到新起點。**Timeline**：最大縮放 500x→1000x；新區段最短 6 秒→1 秒；playhead 在區段邊緣時仍可拉；修 1px「移動」游標其實在縮放。**UI**：沒選區段就按 Add Segment 會閃黃。 | 2026-09-03 手移；保留待剪輯／playlist／多音軌 | `video.tsx` |
| 波形繪製 | `550783d` | 已追上 | v1.7.2：**Timeline**：改善音訊波形繪製。 | 2026-09-03 手移；duration 用 metadata，換片鍵用 filePath | `video.tsx` |
| 音訊 in／out 軌圖示 | `c49f592` `#206` | 未合 | v1.7.2：**Audio Tracks**：用麥克風／耳機圖示區分輸入與輸出軌。 | fork 已有多軌 | OBSService＋video.tsx |
| 剪輯／上傳背景封面 | `d36320c` | 未合 | v1.7.2：**UI**：剪輯與上傳卡片用原始錄影縮圖當背景封面。 | 上傳外觀 | video.tsx、Upload |
| 卡片虛擬化／placeholder | `b2f3e31` `2e4f084` | 未合 | v1.7.1：**Performance**：影片庫略過畫面外卡片的繪製以提升 FPS。 | 列表效能 | `ContentCard` |
| 儲存空間 meter UI | `49a500f` | 未合 | v1.7.1：**Storage**：更新儲存用量 meter。 | 你們已有 GB 數字，這是 UI | StorageSettings |
| OBS 32.2.1→32.2.2（含 ffmpeg 9） | `c21af58` `b2ec7e1` `d82a694` `ae5ead8` | 未合 | Release 未列（Windows bundle）。Linux 那條在 v1.7.5。v1.8.0 另有 `32.2.2-segra` 改版 zip。 | 最終只要 **32.2.2**，別逐版合 zip；合 OBS 時對 v1.8.0 那一列 | bundle＋OBS 路徑 |
| 錄影前先 hook game capture | `39541b4` | 未合 | v1.7.1：**Recording**：改為錄影開始前先 hook game capture。 | 少黑屏；必須手移進雙槽 `StartRecording` | `OBSService` |
| 語音 mute race | `6773ffc` | 未合 | v1.7.4：**Voice Chat**：修 race，遊戲在錄影 setup 期間 hook 時語音軟體可能一直靜音。 | 穩定性；手移進雙槽 | `OBSService` |
| 高光自動建立／音訊重複 | `260b538` | 未合 | v1.7.4：**Highlights**：修錄影結束後高光沒自動建立。**Highlights**：修高光裡遊戲與語音音訊重複。 | 修高光 | HighlightService＋OBSService |
| Clip 硬解加速 | `d805c08` | 未合 | v1.7.2：**Clips**：剪輯改 GPU 解碼加速，不支援的硬體自動退回軟解。 | 大檔剪輯 | ClipService／FFmpegService |
| 啟動變快／啟動體驗 | `0b07165` `4078e26` | 未合 | v1.7.3：**Performance**：改善啟動時間。 | Program 有 PiP 窗 | `Program.cs`、`main.tsx` |
| 壓縮改 metadata flag＋保留所有音軌 | `c695647` `c331c54` | 未合 | v1.7.1：**Compression**：拿掉檔名 `_compressed` 後綴，壓縮檔維持原名。**Compression**：修壓縮只留第一條音軌。 | 兩 commit 當一組 | ContentService、CompressionService |
| 上傳 metadata 帶 exe 檔名 | `5f4966f` | 未合 | v1.7.3：**Upload**：上傳 metadata 帶上遊戲 exe 檔名。 | 小欄位 | 多個 Media 檔 |
| content id 鍵 metadata／縮圖／波形＋FE 只傳 id | `eeb6df1` `c15d3b0` | 未合 | v1.7.1：**Performance**：前端到後端改傳 content id，請求少帶重複資料。 | 兩 commit 當一組、改很廣，**當專案**不要拆 | MessageService、video.tsx、ContentCard、ContentService |

### ★ 低優先或跳過

| 功能 | 上游 | 狀態 | Release | 原因 |
|---|---|---|---|---|
| OBSService 改用 ObsKit built-ins | `c0476c4` | 跳過 | Release 未列 | 雙槽就長在 `OBSService`；再合等於重寫錄影 |
| 刪除 Windows Game Mode 設定 | `18e0938` | 跳過 | v1.7.1：**Settings**：移除 Windows Game Mode 設定。 | 本 fork 要留這個設定 |
| 只保留 beta、拿掉 RC 文案 | `f1d33f2` | 未合 | Release 未列 | 發版流程，與功能無關 |
| 依賴與雜項 cleanup | `0c8cb51` `2fbfae9` | 跳過 | Release 未列 | 不要為清理而合；容易誤傷 fork |

---

## 未合：v1.7.4 → v1.8.0

2026-09-20 fetch 後納入。含穩定 `v1.7.5` 與 `v1.8.0`。建議順序：先 ★★★，再 ★★；★ 預設不動。同一列的 commit 要當**一組**合，不要拆開。

### ★★★ 優先

| 功能 | 上游 | 狀態 | Release | 為什麼推薦 | 碰撞 |
|---|---|---|---|---|---|
| 錄影卡切螢幕＋顯示器列舉 | `7b073dc` `9785b4e` | 已追上 | v1.7.5：**Recording**：錄影卡新增螢幕切換下拉，可改擷取顯示器。**Displays**：修顯示器名稱錯誤或重複，比較好挑對的螢幕。 | 2026-09-20 手移；雙槽／停止／PiP 保留；顯示擷取時切螢幕仍走全域 `selectedDisplay` | `RecordingCard`、`OBSService` |
| Discord 分享聽得到 App 音訊 | `0e53ec5` | 已追上 | v1.7.5：**Audio**：修在 Discord 分享 Segra 時沒聲音。 | 2026-09-20 手移；單軌走 video element、多軌走 useAudioTracks；PiP 未接 | `video.tsx`、`Program.cs` |
| 過期 FE 設定覆寫視窗位置 | `e6bab8c` | 未合 | v1.7.5：**Window State**：修過期的前端設定在啟動時覆寫已存的視窗大小與位置。 | 跟 `1ef10de`（記住視窗大小）當一組 | `Program.cs`、SettingsService |
| Idle 記憶體 -24% | `797582e` | 已追上 | v1.8.0：**Performance**：閒置記憶體降低 24%。 | 2026-09-20 手移；波形仍先 ffmpeg mix 多軌再串流 PCM | `ContentService`、UploadService |
| 播放記憶體 -28% | `2d5fa6e` | 已追上 | v1.8.0：**Performance**：影片播放記憶體降低 28%。 | 2026-09-20 手移；保留 Discord native tap | `useAudioTracks.ts` |
| 縮到托盤降低記憶體 | `7dad99e` | 已追上 | v1.8.0：**Performance**：縮到托盤後降低記憶體用量。 | 2026-09-20 Photino.NET 近似：關主窗卸 WebView，先 detach PiP；不引入 PhotinoX | `Program.cs` |
| 錄影預覽 full-fps | `2ce536b`（預覽） | 未合 | v1.7.5：**Recording Preview**：改善錄影中的預覽幀率。 | 同 commit 的設定排版見下方 ★★ | RecordingPreviewService |
| 管理員遊戲熱鍵仍可用 | `56e947e` `970b3de` `18bd693` `66de0df` `456ee27` | 未合 | v1.8.0：**Hotkeys**：以系統管理員執行的遊戲裡熱鍵仍可用。 | 含 broker 關機／更新／deadlock | 新 `HotkeyBroker` 專案＋`Program.cs` |
| hook 失敗改 window capture | `d111c2a` | 未合 | v1.8.0：**Recording**：遊戲 hook 失敗時改用 window capture。**Audio**：遊戲音訊改從遊戲行程擷取，不再靠 hook。**Audio**：Discord／TeamSpeak 擷取改與錄影同時開始，不再等 hook。**Recording**：顯示擷取改跟隨遊戲所在螢幕，螢幕設定只影響手動錄影。**Settings**：重新命名音訊模式。**Settings**：移除顯示擷取方式選項。 | 一 commit 對多條 Release，不要拆 | **熱檔** `OBSService`、`RecordingCard` |
| 顯示卡重設後重建 GPU | `d82d1fa` | 未合 | v1.8.0：**Stability**：顯示卡／驅動重設後，自動從遺失的 graphics device 恢復。 | | `OBSService`、`Program.cs` |
| 遊戲重開 PID 立刻停錄 | `7f1c024` `d06b3b1` | 未合 | Release 未列（穩定 v1.8.0 頁沒寫；beta 有「錄影開始後幾秒就停」與「process watcher 被堵住」）。 | 重開遊戲不再秒停；watcher 不堵 | `OBSService`、`GameDetectionService` |
| Rainbow Six 整合 | `492a24a` `1cdf993` `0b92915` `3470d2c` `aa981e6` `36f42ad` | 未合 | v1.8.0：**Game Integration**：新增 Rainbow Six Siege 遊戲整合。 | 後段改內建 parser、修書籤時間；穩定 Release 只寫到「有整合」 | 新檔為主 |

### ★★ 值得手移

| 功能 | 上游 | 狀態 | Release | 為什麼 | 碰撞 |
|---|---|---|---|---|---|
| Photino.NET → PhotinoX | `ed614ac` `12d98ac` | 未合 | v1.7.5：**Window**：桌面視窗宿主從 Photino.NET 遷到 PhotinoX。**UI**：拿掉「Starting OBS」轉圈，OBS 載入時不再禁用錄影按鈕。 | 本 fork 有獨立 PiP 第二窗；spinner 在 `12d98ac` 的 `menu.tsx` | **熱檔** `Program.cs`；必須手移，不要整檔 |
| 設定 UI polish | `2ce536b`（設定） `ee8fc31` | 未合 | v1.7.5：**Settings**：整理設定 UI，並改善 video settings 版面。 | 預覽幀率那半已拆到 ★★★ | Settings、VideoSettingsSection |
| 高光音軌名稱／進度 | `8165cf5` `883ab25` | 未合 | v1.7.5：**Highlights**：修高光在外部編輯器／播放器裡丟失獨立音軌與名稱。 | 進度列恢復那半 Release 未列 | HighlightService |
| 更新卡可關閉 | `d4fe43a` `57ba294` | 未合 | v1.7.5：**UI**：更新卡可關閉。 | | UpdateCard |
| 閒置自動安裝更新 | `6c966b6` | 未合 | v1.8.0：**Updates**：下載完的更新在閒置時自動靜默安裝；預設開啟，進階設定可關。 | | `Program.cs`、UpdateService |
| 拿掉 6 軌上限（改版 OBS zip） | `3e48919` `0c95ab6` | 未合 | v1.8.0：**Audio Tracks**：拿掉六軌音訊上限。 | 改 `32.2.2-segra` bundle；fork 已有多軌 | `OBSService`；與 OBS 32.2.2 列對過 |
| CS2／Dota 開錄不補舊擊殺書籤 | `dabccc8` | 未合 | v1.8.0：**Counter-Strike 2 & Dota 2**：修錄影開始前就已發生的擊殺／死亡仍被打成書籤。 | | 遊戲整合 |
| 熱鍵可帶額外修飾鍵 | `5f0e897` `8462d3f` | 未合 | v1.8.0：**Hotkeys**：綁定組合之外多按其他鍵仍會觸發。 | | `OBSService` |
| 視窗關閉時 FE send 卡住 | `59c0a7d` | 未合 | Release 未列 | 關主窗後訊息不再 stall | `MessageService` |
| 遊戲音訊 setup race | `4277bf5` | 未合 | v1.7.4：**Recording**：遊戲在音訊 setup 完成前就 hook 時，桌面音不再漏進 full mix。（v1.8.0 後續修） | 與 ★★★ window capture／音訊改行程擷取對過 | `OBSService` |

### ★ 低優先或跳過

| 功能 | 上游 | 狀態 | Release | 原因 |
|---|---|---|---|---|
| Wayland 無 X 時 recorder init | `2cf4d04` | 跳過 | v1.7.5：**Linux**：Wayland 且無 X 顯示時（例如 KDE／GNOME 的 Flatpak）recorder 初始化失敗。 | Linux |
| Linux OBS 32.2.2 | `4b97458` | 跳過 | v1.7.5：**Linux**：bundled OBS runtime 更新到 32.2.2。 | Linux |
| OBS log 整理 | `afa8c3a` | 跳過 | v1.8.0：**Diagnostics**：整理 OBS log 等級，隱藏吵雜的內部警告。 | chore；容易跟雙槽 log 纏在一起 |

---

## 暫不追：v1.8.0 之後

現況 `upstream/main` 等於 `v1.8.0`，沒有更新的穩定 tag。不要追 beta／未 tag 的 main。

---

## 維護

對齊完一個功能後：把該列狀態改成 `已追上`，必要時加一行備註（哪個 commit／哪天）。不要另開第三份追蹤表。

寫入新 tag 的購物清單時：每個功能列都要填 **Release** 欄（穩定 Release 頁原文的繁中）。同一 Release 一條 bullet 對一列；一列對多條就全寫。穩定頁沒有的寫 `Release 未列`。不要把 Release 收成內部簡稱而把數字／分類藏起來。

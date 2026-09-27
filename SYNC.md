# 上游與 Fork 功能追蹤

- **Fork HEAD 基準**：已合入 upstream `v1.7.0`（`d4f71ec`，2026-08-06）
- **現況目標**：穩定版 `upstream/v1.8.1`（`1679d3d`，2026-09-22 打 tag，Release 頁 2026-09-26 發佈）；使用者指定加看 beta `upstream/v1.9.0-beta.2`（`1f85493`，2026-09-26），見最後一段
- **Tag 命名**：上游 tag 在本機一律抓成 `upstream/<tag>`（`remote.upstream.fetch` 加了 `+refs/tags/*:refs/tags/upstream/*`、`tagOpt --no-tags`）。沒有前綴的 `v1.9.x` 是 **fork 自己**的 release。本表寫的版本號（v1.7.1、v1.8.0…）都指上游
- **對齊方式**：挑功能移植，禁止整包 merge（見 `.cursor/rules/upstream-sync.mdc`）
- 2026-09-27 fetch：相對 `upstream/v1.8.1` 超前 789、落後 100 commit；`upstream/main` 停在 `v1.9.0-beta.2`（比 v1.8.1 多 7 commit）

狀態：`已追上` / `未合` / `跳過` / `進行中`

星等（越多越建議合進來）：

| 星 | 意思 |
|---|---|
| ★★★ | 優先：使用者立刻有感，且多半可搬（或衝突可控） |
| ★★ | 值得要：實用，但要手移，或會碰到熱檔 |
| ★ | 低優先或預設跳過：跟 fork 核心纏太深、或本 fork 用不到 |

熱檔：`OBSService.cs`、`video.tsx`、`MessageService.cs`、`ContentService.cs`、`ContentCard.tsx`、`Program.cs`（fork 拆出去的部分對照見 `.cursor/rules/upstream-sync.mdc`）

**Release 欄**：對應 GitHub 穩定 Release 頁的描述（繁中，保留官方分類）。沒出現在穩定 Release 的寫 `Release 未列`，不要用 commit 主旨冒充。beta 段落用 beta Release 頁，並標（beta）。

**建議欄**：要不要改、怎麼改（對過 fork 現有程式後寫的）。「可直接 pick」＝fork 對應檔跟上游改之前一樣或只差幾行；「手移」＝要照 fork 結構重寫。

---

## Fork 獨立功能（不要當上游 diff 清掉）

這些是本 fork 有、上游沒有（或上游後來走向不同）的東西。對齊時必須保留。

| 功能 | 關鍵位置 | 備註 |
|---|---|---|
| 雙 slot 錄影 | `RecordingSlots`、`OBSService`、`AppState`、`RecordingCard` | 熱檔核心 |
| 待剪輯 | `FolderNames`（`待剪輯`）、`ContentService`、`pending-edit.tsx` | UI 在 ContentCard／ContentPage／video.tsx 有重複 |
| 瀏覽影片 | `BrowseService`、`browse-videos.tsx` | 仍放在 `Backend/Services/` |
| 外部影片庫 | `ContentType.External`、`FolderNames` | 不計入儲存上限 |
| 獨立 PiP 監控窗 | `Program.MonitoringWindow.cs`、`MonitoringApp.tsx` | 第二 Photino；上游已遷 PhotinoX，合時必須手移 |
| VRChat VVMW | `VrChatVvmwIntegration.cs` | replay-tail clip |
| 剪輯設定強化 | clip bitrate／rate control、clear segments、上傳後開瀏覽器、多音軌 | 與上游 clip 改進有重疊，合時要對過 |
| 監控／錄影 UX | 雙槽卡、preview meters、playlist | |

---

## 相依與前置（先看這段再挑）

幾組上游改動互相牽連，分開合會白做或做兩次：

| 組 | 內容 | 建議 |
|---|---|---|
| ObsKit.NET 升級 | fork 還在 `1.5.4`，上游 v1.8.x 是 `1.6.1`（`2cf4d04`→`9785b4e`→`d82d1fa` 逐步升） | 顯卡重建（`d82d1fa`）一定要 1.6.1；其他大改（window capture、常駐回放）也建議先升。**當成獨立一步**：只升套件＋編譯＋smoke 雙槽／PiP／VVMW，確認沒事再搬功能 |
| 「hook 失敗改 window capture」家族 | `d111c2a`（v1.8.0 ★★★）改掉遊戲音訊來源與語音靜音切換 | 做了它之後，`6773ffc`（語音 mute race）、`4277bf5`（遊戲音訊 setup race）就不用搬；beta 的「OBS Studio 讓出 hook」也要它才有意義 |
| OBS 內建包家族 | 32.2.1→32.2.2、`32.2.2-segra`（拿掉 6 軌上限＋熱鍵不嚴格比對修飾鍵）、v1.8.1 的 FFmpeg 9.0.2 | 一次換成 **v1.8.1 的 `OBS 32.2.2-segra.zip`**，不要逐版合 zip |
| 前端舊設定蓋回後端 | fork `f4c7870` 起，前端送來的每個設定都會套用 | `lastWindowState` 要列為後端專屬（`e6bab8c`）、遊戲整合開關不要再送整份設定（v1.8.1 小修）。這兩個不先修，資料夾回退、編碼器回退這類「後端自己改設定」的功能都可能被前端舊值蓋回。**另外：** fork 的 `updateSettings`（`SettingsContext.tsx:70`～`:91`）每次都送**整份設定**，只要有一個視窗（例如 PiP 窗）手上的設定是舊的，改任何一項都可能把別的設定蓋回舊值。短期：PiP 上的操作一律走專用訊息，不用 `updateSettings`；長期：把 `updateSettings` 改成只送有變的欄位（可另開一項） |
| content id 專案 | `eeb6df1` `c15d3b0` | 上游之後很多媒體改動都建在它上面（例如 `260b538` 的自動高光）。fork 還用檔名，搬那些改動時要換回檔名寫法 |

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

| 功能 | 上游 | 狀態 | Release | 為什麼推薦 | 建議（要不要改／怎麼改） | 碰撞 |
|---|---|---|---|---|---|---|
| 右鍵選單 | `0813546` `b17056f` `#194` | 已追上 | v1.7.1：**UI**：影片卡新增右鍵選單。 | 2026-09-03 手移；保留待剪輯／外部庫選項 | — | `ContentCard` |
| 篩選刪光後清空 | `a1e166a` `#198` | 已追上 | v1.7.1：**UI**：篩選某遊戲的最後一支影片刪掉後，遊戲篩選不再顯示空清單。 | 2026-09-03 手移 | — | `ContentPage` |
| 框選多個內容卡 | `623d43f` | 已追上 | v1.7.1：**UI**：影片庫可拖曳框選；並修 Ctrl+Click 無反應。 | 2026-09-03 手移；選取鍵仍用 fileName | — | `ContentPage`／`ContentCard` |
| 關閉自動錄影的遊戲 | `db66f94` | 已追上 | v1.7.3：**Settings**：偵測到遊戲時可關閉自動錄影（#219）。 | 2026-09-03 手移 | — | GameDetection UI |
| 播放錄影路徑外的檔 | `b1c22a9` | 已追上 | v1.7.1：**Playback**：可播放存放在「目前錄影資料夾」之外的錄影。 | 2026-09-03 手移；保留 BrowseService 授權 | — | `ContentServer` |
| 快取／錄影資料夾打不開則回退 | `370924a` `2e1f813` | 未合 | v1.7.3：**Startup**：設定的錄影或快取資料夾無法使用（例如磁碟未掛載）時，改回預設資料夾並通知，避免啟動失敗。 | 兩 commit 當一組 | **要，手移。** fork `Program.cs:334` 只有 `CreateDirectory`，外接碟沒插會直接啟動失敗；快取資料夾完全沒檢查。兩段照抄到那裡。先修「前端舊設定蓋回後端」（見相依），不然回退後的新路徑可能被前端舊值蓋回去 | `Program.cs` |
| Copy 壓縮到 Discord 上限 | `ba359a6` `496718e` | 未合 | v1.7.2：**Sharing**：Copy as 10/50/100/500 MB，先壓到選定大小再複製（#140）。v1.7.3：**Upload**：預設「copy as compressed」從 10 MB 改 20 MB，對齊 Discord 新上傳上限。 | 兩條 Release 當一組 | **要，但工作量偏大（實際像 ★★）。** 後端 `CopyCompressedToClipboard` 是新方法，可幾乎照抄進 fork `CompressionService`。前端 fork 有三個 Copy 按鈕（`video.tsx:2312`、`:2398`、`ContentCard.tsx:407`），上游只改 video.tsx，要決定卡片上的要不要一起有下拉。先合「壓縮保留所有音軌」；Discord 只播第一軌，要確認壓出來的第 1 軌是總混音。`496718e` 的 migration 編號別跟 fork 的撞 | `video.tsx`、Compression |
| Discord Canary／PTB | `08a3041` `ab39547` `#211` | 未合 | v1.7.3：**Recording**：可偵測 Discord Canary、Discord PTB 為語音軟體。 | 抓遊戲 Discord 音訊更齊 | **要，最省事。** 就是在 `OBSService.cs:447` 的 `VoiceChatApps` 陣列加兩行；先試 cherry-pick，衝突就手加 | `OBSService`（要手移，不要整檔） |
| 記住視窗大小＋托盤重設 | `1ef10de` | 未合 | v1.7.2：**Window**：記住視窗大小與最大化，重開回復原狀（#182）。**Tray**：新增 Reset window size 恢復預設大小；Exit 改名 Quit，視窗已顯示時隱藏 Open。 | 跟現有 `LastWindowState` 同方向；與 `e6bab8c` 當一組 | **要，和 `e6bab8c` 一起手移。** fork 的 `WindowState` 只有 X／Y，要加寬、高、是否最大化；`Program.cs:831`／`:853` 的讀寫照上游擴充，不要碰 PiP 窗自己的位置記憶。托盤選單在 `WindowsPlatform.cs:35` 加「Reset window size」；Exit 改 Quit 可有可無 | `Program.cs` |
| 設定裡 Discord／docs 連結 | `d4d2db3` | 未合 | v1.7.2：**Settings**：新增 Discord 與文件連結。 | 純 UI、幾乎無衝突 | **可以不做（建議降 ★）。** 檔案跟上游一樣，可直接 pick，但連結指向上游 Discord 和文件，對 fork 使用者可能誤導；要做就改成 fork 的 GitHub | AdvancedSection |

### ★★ 值得手移

| 功能 | 上游 | 狀態 | Release | 為什麼 | 建議（要不要改／怎麼改） | 碰撞 |
|---|---|---|---|---|---|---|
| 時間軸：區段縮放／playhead／無區段提示 | `beedd5c` `9173f6f` `bb80cd4` `28a2ba5` | 已追上 | v1.7.2：**Clip Editor**：拉區段時 playhead 不動、影片預覽被拉的那一端；拉區段時暫停播放、放開從正確位置續播；拉起點後 playhead 移到新起點。**Timeline**：最大縮放 500x→1000x；新區段最短 6 秒→1 秒；playhead 在區段邊緣時仍可拉；修 1px「移動」游標其實在縮放。**UI**：沒選區段就按 Add Segment 會閃黃。 | 2026-09-03 手移；保留待剪輯／playlist／多音軌 | — | `video.tsx` |
| 波形繪製 | `550783d` | 已追上 | v1.7.2：**Timeline**：改善音訊波形繪製。 | 2026-09-03 手移；duration 用 metadata，換片鍵用 filePath | — | `video.tsx` |
| 音訊 in／out 軌圖示 | `c49f592` `#206` | 未合 | v1.7.2：**Audio Tracks**：用麥克風／耳機圖示區分輸入與輸出軌。 | fork 已有多軌 | **可做，但不能照抄（建議降 ★）。** fork 的多軌是「每個裝置自己勾進哪幾軌」，一軌可能同時有麥克風和喇叭，上游的輸入／輸出二分法套不上。做法：在 `AudioTrackLayout` 加一個函式，依每軌收到的裝置判斷 input／output／mix，寫進 metadata `audioTrackTypes`；前端 `AudioTrackIcon.tsx` 照抄 | OBSService＋video.tsx |
| 剪輯／上傳背景封面 | `d36320c` | 未合 | v1.7.2：**UI**：剪輯與上傳卡片用原始錄影縮圖當背景封面。 | 上傳外觀 | **可做，不急（建議降 ★）。** 純 UI；`FileUtils` 新函式照抄，`ClippingCard`／`UploadCard` 手移。要確認待剪輯、外部庫影片拿得到縮圖 | video.tsx、Upload |
| 卡片虛擬化／placeholder | `b2f3e31` `2e4f084` | 未合 | v1.7.1：**Performance**：影片庫略過畫面外卡片的繪製以提升 FPS。 | 列表效能 | **要。** 只搬第二個 commit 的做法（IntersectionObserver 佔位）；第一個的 `content-visibility` 上游自己已經放棄。`ContentCard` 是熱檔，fork 卡片有待剪輯按鈕與框選，手移；`ContentPage` 只加 2 行 | `ContentCard` |
| 儲存空間 meter UI | `49a500f` | 未合 | v1.7.1：**Storage**：更新儲存用量 meter。 | 你們已有 GB 數字，這是 UI | **可做（建議降 ★）。** `StorageUsageMeter.tsx` 新檔照抄；fork 的 `StorageSettingsSection` 多了自己的欄位，要手接。跟 v1.8.1 的「資料夾輸入框」修正是同一檔，一起做 | StorageSettings |
| OBS 32.2.1→32.2.2（含 ffmpeg 9） | `c21af58` `b2ec7e1` `d82a694` `ae5ead8` | 未合 | Release 未列（Windows bundle）。Linux 那條在 v1.7.5。v1.8.0 另有 `32.2.2-segra` 改版 zip。 | 最終只要 **32.2.2**，別逐版合 zip；合 OBS 時對 v1.8.0 那一列 | **要，但跳過這幾個 commit，直接用 v1.8.1 的 `OBS 32.2.2-segra.zip`**（見相依「OBS 內建包家族」）。要確認 fork 的 OBS 版本挑選（`SelectedOBSVersion`）認得 `-segra` 後綴 | bundle＋OBS 路徑 |
| 錄影前先 hook game capture | `39541b4` | 未合 | v1.7.1：**Recording**：改為錄影開始前先 hook game capture。 | 少黑屏；必須手移進雙槽 `StartRecording` | **要。** fork 的 ObsKit 1.5.4 已經有 `SetHookRate`，不用等升級。手移到 `OBSService.StartRecording.cs` 的 `AddCaptureSources` 之後；要用該槽自己的 `Pipeline(slot).GameCapture`，不要用全域。順便帶 beta `2aa9c75` 的小修：背景恢復 hook 速度時要確認還是同一個來源 | `OBSService` |
| 語音 mute race | `6773ffc` | 未合 | v1.7.4：**Voice Chat**：修 race，遊戲在錄影 setup 期間 hook 時語音軟體可能一直靜音。 | 穩定性；手移進雙槽 | **看 `d111c2a` 做不做。** 要做 window capture 那個大項 → 這列跳過（上游之後整個拿掉靜音切換）。短期不做 → 值得先搬，小改：`OBSService.cs:2022` 附近改成「建立時先靜音，再依目前是否已 hook 決定」 | `OBSService` |
| 高光自動建立／音訊重複 | `260b538` | 未合 | v1.7.4：**Highlights**：修錄影結束後高光沒自動建立。**Highlights**：修高光裡遊戲與語音音訊重複。 | 修高光 | **只要一半。**「沒自動建立」是上游改用 content id 之後才出的 bug，fork 還用檔名（`AiService.CreateHighlight(fileName)`），不需要。「音訊重複」要：`HighlightService` 加 `-map 0`（跟 `8165cf5` 一起做）；desktop 音源若遊戲已經 hook 就一開始靜音（fork 還有同樣的靜音切換） | HighlightService＋OBSService |
| Clip 硬解加速 | `d805c08` | 未合 | v1.7.2：**Clips**：剪輯改 GPU 解碼加速，不支援的硬體自動退回軟解。 | 大檔剪輯 | **可做，小心手移。** fork 的 `ClipService.cs` 已經跟上游差 400 多行（剪輯 bitrate／rate control），不能 pick。`FFmpegService` 新增的硬解偵測照抄，再到 fork 組剪輯 ffmpeg 參數的地方加 `-hwaccel`；「失敗自動退軟解」那段一定要帶 | ClipService／FFmpegService |
| 啟動變快／啟動體驗 | `0b07165` `4078e26` | 未合 | v1.7.3：**Performance**：改善啟動時間。 | Program 有 PiP 窗 | **要後端那半。** 沒設 proxy 時加 `--no-proxy-server`（省約 1 秒），`Program.cs:781` 和 `Program.MonitoringWindow.cs:17` 兩個視窗都要加。前端開場畫面（`index.html`／`main.tsx`）可選；PiP 窗也吃 `main.tsx`，要確認不影響 `MonitoringApp` | `Program.cs`、`main.tsx` |
| 壓縮改 metadata flag＋保留所有音軌 | `c695647` `c331c54` | 未合 | v1.7.1：**Compression**：拿掉檔名 `_compressed` 後綴，壓縮檔維持原名。**Compression**：修壓縮只留第一條音軌。 | 兩 commit 當一組 | **拆開看，保留音軌那半建議升 ★★★。** fork 壓縮（`CompressionService.cs:16`）沒有 `-map`，多音軌錄影壓完只剩第一軌。`c331c54` 跟 fork 只差約 20 行，照改。拿掉 `_compressed` 檔名要 migration 改舊檔，而且要把待剪輯、外部庫資料夾算進去 → ★，可晚點 | ContentService、CompressionService |
| 上傳 metadata 帶 exe 檔名 | `5f4966f` | 未合 | v1.7.3：**Upload**：上傳 metadata 帶上遊戲 exe 檔名。 | 小欄位 | **可做，不急（建議降 ★）。** 欄位小，但要改 OBSService 存 metadata 的呼叫（雙槽兩處），跟「Copy 壓縮」一起做比較順 | 多個 Media 檔 |
| content id 鍵 metadata／縮圖／波形＋FE 只傳 id | `eeb6df1` `c15d3b0` | 未合 | v1.7.1：**Performance**：前端到後端改傳 content id，請求少帶重複資料。 | 兩 commit 當一組、改很廣，**當專案**不要拆 | **暫緩（建議降 ★）。** 改動面最大，fork 的待剪輯、外部庫、瀏覽影片都用檔名或路徑當鍵，改成 id 要全部重想。等到要大量搬上游媒體功能時再做；做的話開獨立 branch，先寫 migration 測試 | MessageService、video.tsx、ContentCard、ContentService |

### ★ 低優先或跳過

| 功能 | 上游 | 狀態 | Release | 原因 | 建議（要不要改／怎麼改） |
|---|---|---|---|---|---|
| OBSService 改用 ObsKit built-ins | `c0476c4` | 跳過 | Release 未列 | 雙槽就長在 `OBSService`；再合等於重寫錄影 | 同意跳過 |
| 刪除 Windows Game Mode 設定 | `18e0938` | 跳過 | v1.7.1：**Settings**：移除 Windows Game Mode 設定。 | 本 fork 要留這個設定 | 同意跳過 |
| 只保留 beta、拿掉 RC 文案 | `f1d33f2` | 未合 | Release 未列 | 發版流程，與功能無關 | **建議改成跳過。** fork 自己的發版 workflow 自己決定版本規則，沒必要跟 |
| 依賴與雜項 cleanup | `0c8cb51` `2fbfae9` | 跳過 | Release 未列 | 不要為清理而合；容易誤傷 fork | 同意跳過 |

---

## 未合：v1.7.4 → v1.8.0

2026-09-20 fetch 後納入。含穩定 `v1.7.5` 與 `v1.8.0`。建議順序：先 ★★★，再 ★★；★ 預設不動。同一列的 commit 要當**一組**合，不要拆開。

### ★★★ 優先

| 功能 | 上游 | 狀態 | Release | 為什麼推薦 | 建議（要不要改／怎麼改） | 碰撞 |
|---|---|---|---|---|---|---|
| 錄影卡切螢幕＋顯示器列舉 | `7b073dc` `9785b4e` | 已追上 | v1.7.5：**Recording**：錄影卡新增螢幕切換下拉，可改擷取顯示器。**Displays**：修顯示器名稱錯誤或重複，比較好挑對的螢幕。 | 2026-09-20 手移；雙槽／停止／PiP 保留；顯示擷取時切螢幕仍走全域 `selectedDisplay` | — | `RecordingCard`、`OBSService` |
| Discord 分享聽得到 App 音訊 | `0e53ec5` | 已追上 | v1.7.5：**Audio**：修在 Discord 分享 Segra 時沒聲音。 | 2026-09-20 手移；單軌走 video element、多軌走 useAudioTracks；PiP 未接 | — | `video.tsx`、`Program.cs` |
| 過期 FE 設定覆寫視窗位置 | `e6bab8c` | 未合 | v1.7.5：**Window State**：修過期的前端設定在啟動時覆寫已存的視窗大小與位置。 | 跟 `1ef10de`（記住視窗大小）當一組 | **要，而且 fork 比上游更需要。** fork 從 `f4c7870` 起會自動套用前端送來的每個設定，`lastWindowState` 不在 `SettingsService.cs:214` 的 `BackendOwnedSettings` 裡，前端舊值會蓋掉後端剛存的位置。最小改法：`BackendOwnedSettings` 加 `nameof(Settings.LastWindowState)`（一行），並在 `Segra.Tests/SettingsUpdateTests.cs` 補一個測試。防抖存檔那半跟 `1ef10de` 一起做 | `Program.cs`、SettingsService |
| Idle 記憶體 -24% | `797582e` | 已追上 | v1.8.0：**Performance**：閒置記憶體降低 24%。 | 2026-09-20 手移；波形仍先 ffmpeg mix 多軌再串流 PCM | — | `ContentService`、UploadService |
| 播放記憶體 -28% | `2d5fa6e` | 已追上 | v1.8.0：**Performance**：影片播放記憶體降低 28%。 | 2026-09-20 手移；保留 Discord native tap | — | `useAudioTracks.ts` |
| 縮到托盤降低記憶體 | `7dad99e` | 已追上 | v1.8.0：**Performance**：縮到托盤後降低記憶體用量。 | 2026-09-20 Photino.NET 近似：關主窗卸 WebView，先 detach PiP；不引入 PhotinoX | — | `Program.cs` |
| 錄影預覽 full-fps | `2ce536b`（預覽） | 未合 | v1.7.5：**Recording Preview**：改善錄影中的預覽幀率。 | 同 commit 的設定排版見下方 ★★ | **要。** 只拿 `RecordingPreviewService.cs` 那 10 行手移；fork 有雙槽 preview 和 PiP 預覽，兩個槽都要套 | RecordingPreviewService |
| 管理員遊戲熱鍵仍可用 | `56e947e` `970b3de` `18bd693` `66de0df` `456ee27` | 未合 | v1.8.0：**Hotkeys**：以系統管理員執行的遊戲裡熱鍵仍可用。 | 含 broker 關機／更新／deadlock | **要，但算專案。** 新的 `HotkeyBroker/` 專案、安裝要 UAC，發佈 workflow 和 `.signpath` 也要改（fork 自己發 Setup.exe，要把 broker 一起打包）。連同 v1.8.1 的 `HotkeyBrokerSetup` 修正、beta 的 keybind→hotkey 改名一起看。你常玩「以系統管理員執行」的遊戲才值得先做 | 新 `HotkeyBroker` 專案＋`Program.cs` |
| hook 失敗改 window capture | `d111c2a` | 未合 | v1.8.0：**Recording**：遊戲 hook 失敗時改用 window capture。**Audio**：遊戲音訊改從遊戲行程擷取，不再靠 hook。**Audio**：Discord／TeamSpeak 擷取改與錄影同時開始，不再等 hook。**Recording**：顯示擷取改跟隨遊戲所在螢幕，螢幕設定只影響手動錄影。**Settings**：重新命名音訊模式。**Settings**：移除顯示擷取方式選項。 | 一 commit 對多條 Release，不要拆 | **要，但這是整份清單最大的一塊，當專案做。** 全部要改寫進雙槽的 `SessionPipeline`（每槽一份 window capture、一份行程音訊），`AudioTrackLayout` 的遊戲音軌遮罩要重接。「螢幕設定只影響手動錄影」跟 fork 已合的錄影卡切螢幕行為要對過。做完它，`6773ffc`、`4277bf5` 標跳過；beta 的 OBS Studio 讓出 hook 也要它。建議先升 ObsKit、先做完 v1.8.1 的小修再開 | **熱檔** `OBSService`、`RecordingCard` |
| 顯示卡重設後重建 GPU | `d82d1fa` | 未合 | v1.8.0：**Stability**：顯示卡／驅動重設後，自動從遺失的 graphics device 恢復。 | | **可做（建議降 ★★）。** 需要 ObsKit 1.6.1 和新的 `RecorderHealthService`；`Program.cs` 的改動要避開 PiP 窗。好處是驅動重設後不必重開 Segra。跟 ObsKit 升級一起做 | `OBSService`、`Program.cs` |
| 遊戲重開 PID 立刻停錄 | `7f1c024` `d06b3b1` | 未合 | Release 未列（穩定 v1.8.0 頁沒寫；beta 有「錄影開始後幾秒就停」與「process watcher 被堵住」）。 | 重開遊戲不再秒停；watcher 不堵 | **要。** fork 用 `StopRecordingSlot(slot)`，要把「預期 PID 已經換了就不要停」的檢查放進每槽的停止路徑，開錄時用 PreRecording 修正過的 PID。`d06b3b1` 的 `WindowUtils` 改動可照抄 | `OBSService`、`GameDetectionService` |
| Rainbow Six 整合 | `492a24a` `1cdf993` `0b92915` `3470d2c` `aa981e6` `36f42ad` | 未合 | v1.8.0：**Game Integration**：新增 Rainbow Six Siege 遊戲整合。 | 後段改內建 parser、修書籤時間；穩定 Release 只寫到「有整合」 | **看你玩不玩 R6（不玩就降 ★）。** 大多是新檔，直接取 v1.8.0 的最終版複製；Settings、`GameIntegrationService`、前端整合開關各加幾行 | 新檔為主 |

### ★★ 值得手移

| 功能 | 上游 | 狀態 | Release | 為什麼 | 建議（要不要改／怎麼改） | 碰撞 |
|---|---|---|---|---|---|---|
| Photino.NET → PhotinoX | `ed614ac` `12d98ac` | 未合 | v1.7.5：**Window**：桌面視窗宿主從 Photino.NET 遷到 PhotinoX。**UI**：拿掉「Starting OBS」轉圈，OBS 載入時不再禁用錄影按鈕。 | 本 fork 有獨立 PiP 第二窗；spinner 在 `12d98ac` 的 `menu.tsx` | **暫緩（建議降 ★）。** PiP 第二窗、縮到托盤卸 WebView 都綁 Photino.NET，換掉要重測所有視窗行為，好處主要是之後好搬依賴 PhotinoX 的功能。拿掉轉圈那半（`menu.tsx`）可以單獨先做 | **熱檔** `Program.cs`；必須手移，不要整檔 |
| 設定 UI polish | `2ce536b`（設定） `ee8fc31` | 未合 | v1.7.5：**Settings**：整理設定 UI，並改善 video settings 版面。 | 預覽幀率那半已拆到 ★★★ | **可做（建議降 ★）。** 純前端，但 fork 設定頁加了很多自己的欄位。建議跟 beta 的「keybind 改名、說明文字」一次做完，不要改兩次 | Settings、VideoSettingsSection |
| 高光音軌名稱／進度 | `8165cf5` `883ab25` | 未合 | v1.7.5：**Highlights**：修高光在外部編輯器／播放器裡丟失獨立音軌與名稱。 | 進度列恢復那半 Release 未列 | **要。** fork 的 `HighlightService` 跟上游只差約 20 行，可以先試 cherry-pick；跟 `260b538` 的 `-map 0` 一起做 | HighlightService |
| 更新卡可關閉 | `d4fe43a` `57ba294` | 未合 | v1.7.5：**UI**：更新卡可關閉。 | | **要，可直接 pick。** fork 這兩個檔跟上游改之前一模一樣 | UpdateCard |
| 閒置自動安裝更新 | `6c966b6` | 未合 | v1.8.0：**Updates**：下載完的更新在閒置時自動靜默安裝；預設開啟，進階設定可關。 | | **可做。** fork 自己發 release（`Taiyopen/Segra`），對你的使用者有用。「閒置」判斷要加上 fork 狀態：任一槽在錄、VVMW 正在剪、PiP 開著都不算閒置 | `Program.cs`、UpdateService |
| 拿掉 6 軌上限（改版 OBS zip） | `3e48919` `0c95ab6` | 未合 | v1.8.0：**Audio Tracks**：拿掉六軌音訊上限。 | 改 `32.2.2-segra` bundle；fork 已有多軌 | **6 軌夠用就跳過。** 要做：換 OBS 包（見相依），`AudioTrackLayout.MaxTracks = 6` 和遮罩 `0x3F` 寫死，要改成讀 `ObsAudioTrackLimit.Value`（新檔照抄），前端軌道勾選 UI 也寫死 6 | `OBSService`；與 OBS 32.2.2 列對過 |
| CS2／Dota 開錄不補舊擊殺書籤 | `dabccc8` | 未合 | v1.8.0：**Counter-Strike 2 & Dota 2**：修錄影開始前就已發生的擊殺／死亡仍被打成書籤。 | | **要，可試 cherry-pick。** fork 兩個檔只差 15 行 | 遊戲整合 |
| 熱鍵可帶額外修飾鍵 | `5f0e897` `8462d3f` | 未合 | v1.8.0：**Hotkeys**：綁定組合之外多按其他鍵仍會觸發。 | | **不用搬程式。** 上游先加 `ObsStrictModifiers.cs` 又刪掉，實際效果做進了改版的 `OBS 32.2.2-segra` 包，換 OBS 包就有 → 併入「OBS 內建包家族」 | `OBSService` |
| 視窗關閉時 FE send 卡住 | `59c0a7d` | 未合 | Release 未列 | 關主窗後訊息不再 stall | **大多不需要（建議降 ★）。** fork 的 `SendFrontendMessage`（`MessageService.cs:817`）沒連線就略過，不會卡住。上游另外做了「視窗關著時跳的提示先排隊、開窗再顯示」，fork 目前這些提示會直接丟掉；想要再做 | `MessageService` |
| 遊戲音訊 setup race | `4277bf5` | 未合 | v1.7.4：**Recording**：遊戲在音訊 setup 完成前就 hook 時，桌面音不再漏進 full mix。（v1.8.0 後續修） | 與 ★★★ window capture／音訊改行程擷取對過 | **看 `d111c2a` 做不做**（同 `6773ffc`）。不做的話這是一行修正，可以先搬 | `OBSService` |

### ★ 低優先或跳過

| 功能 | 上游 | 狀態 | Release | 原因 | 建議（要不要改／怎麼改） |
|---|---|---|---|---|---|
| Wayland 無 X 時 recorder init | `2cf4d04` | 跳過 | v1.7.5：**Linux**：Wayland 且無 X 顯示時（例如 KDE／GNOME 的 Flatpak）recorder 初始化失敗。 | Linux | 同意跳過（但它順便把 ObsKit 升到 1.5.7，見相依） |
| Linux OBS 32.2.2 | `4b97458` | 跳過 | v1.7.5：**Linux**：bundled OBS runtime 更新到 32.2.2。 | Linux | 同意跳過 |
| OBS log 整理 | `afa8c3a` | 跳過 | v1.8.0：**Diagnostics**：整理 OBS log 等級，隱藏吵雜的內部警告。 | chore；容易跟雙槽 log 纏在一起 | 同意跳過 |

---

## 未合：v1.8.0 → v1.8.1

2026-09-27 fetch 後納入。穩定 `v1.8.1`。

| 功能 | 上游 | 星 | 狀態 | Release | 建議（要不要改／怎麼改） | 碰撞 |
|---|---|---|---|---|---|---|
| 編碼器消失時自動換一個 | `0dce1c4` `3436526`＋`3dd1b85` 的 PresetsService 段 | ★★★ | 未合 | v1.8.1：**Recording**：已存的編碼器不存在時（例如換了顯卡）不再錄不起來，改用可用的編碼器並跳提示。 | **要，手移。** fork `OBSService.cs:2926` 只在設定是空的時候選預設值，換顯卡後會錄不起來。改成「是空的，或不在 `AppState.Instance.Codecs` 裡」都重選，然後跳提示、存檔、`SendSettingsToFrontend`。fork 的 `SettingsService` 寫法不同，要在 `CanApplySetting` 的 `case nameof(Settings.Codec)` 加「不在清單就拒絕」，並確認被拒後前端下拉會跳回原值。`PresetsService.cs:110` 換編碼器時要一起重選 codec | `OBSService`、SettingsService、PresetsService |
| 縮圖不再全黑 | `c60d472` | ★★★ | 未合 | v1.8.1：**Thumbnails**：遊戲切到背景時縮圖不再全黑；第一張是黑的就改挑別的時間點。 | **要，可直接 pick。** fork 的 `CreateThumbnailFile` 跟上游改之前一樣，`RunAndCaptureOutput` 也在，不是熱檔。瀏覽影片、外部庫一起受惠 | FFmpegService |
| 安裝時一併裝 VC++ runtime | `05ece84` | ★★★ | 已追上 | v1.8.1：**Installer**：安裝時一併安裝 Visual C++ runtime，修正全新 Windows 上錄影元件默默載入失敗。 | **要，可直接 pick。** fork 自己用 CI 發 `Segra-win-Setup.exe`，這條直接影響新裝的使用者；只改兩個 workflow 的 `--framework webview2` → `webview2,vcredist143-x64`。2026-09-27 已追上：cherry-pick 並同步改 `pack-local.md`（fork 實際用本機打包） | `.github/workflows` |
| OBS 包升到 FFmpeg 9.0.2 | `9597aff` | ★★ | 未合 | v1.8.1：**OBS**：OBS 32.2.2 build 更新到 FFmpeg 9.0.2，修 #214（剪輯建立問題）。 | **要，但不單獨合。** 併入「OBS 內建包家族」，直接用這一版的 `OBS 32.2.2-segra.zip` | bundle |
| 各處小 bug 修正 | `3dd1b85` `1679d3d` | ★★★ | 未合 | v1.8.1：**Stability**：修正設定、上傳、影片播放器、遊戲偵測裡幾個少見情況的 bug。 | **不要整包 pick。** Release 頁只寫一條，但裡面有二十幾個小修正，兩個 commit 都碰熱檔（`MessageService`、`video.tsx`），fork 的設定頁也拆成不同檔。拆項見下表 | 見下表 |

### 「Stability」拆項

| 小項 | 星 | 建議（要不要改／怎麼改） |
|---|---|---|
| 儲存設定的資料夾輸入框改成離開欄位才送出 | ★★★ | **要。** fork `StorageSettingsSection.tsx:187`／`:212` 每打一個字就送一次 `ContentFolder`，每次都跑搬資料夾檢查（`StorageWarningService`）。照上游改成先存本地、離開欄位或按 Enter 才送 |
| 遊戲整合開關改用 `updateSettings` | ★★★ | **要。** fork `GameIntegrationsSection.tsx:186` 還把整份舊設定送出去；`f4c7870` 之後後端會照單全收，等於把其他設定蓋回舊值 |
| 高光優先用 `content.FilePath` 找原始檔 | ★★★ | **要，對 fork 更重要。** 待剪輯、外部庫的檔不在「內容資料夾／類型／遊戲」底下，`HighlightService.cs:63` 會找不到來源。順便看 `ClipService.cs:113`／`:931` 有沒有同樣問題 |
| 本機檔案伺服器只對本機網頁開放讀取（CORS），且只收完整路徑 | ★★★ | **要（安全性）。** WebSocket 那邊 fork 已做。`ContentServer.cs` 還有 4 處 `Access-Control-Allow-Origin: *`（上游只有 2 處），全換掉，沿用 fork 現有的 `MessageService.IsLocalOrigin`，不要再寫一份。`ValidateUserPath` 加「必須完整路徑」前，先確認瀏覽影片、外部庫送的都是完整路徑 |
| 設定檔先寫暫存再改名、加鎖；讀檔容許註解與尾逗號 | ★★★ | **要，照抄。** `SettingsService.cs:44`／`:76`。雙槽時寫設定更頻繁，寫到一半斷電整份設定就壞 |
| 偵測音訊裝置／螢幕的計時器加 try/catch | ★★★ | **要，照抄。** `AppState.cs:554`／`:565`；計時器裡的錯誤沒人接，會讓整個程式閃退 |
| 複製到剪貼簿的執行緒加 try/catch | ★★ | **要，照抄。** `WindowsPlatform.cs:165`，理由同上 |
| 上傳不再 10 分鐘逾時 | ★★ | **要，一行。** `UploadService.cs:19`；大檔傳超過 10 分鐘會被中斷 |
| 算資料夾大小時跳過沒權限的子資料夾 | ★★ | **要。** `StorageService.cs:267`（同檔 `:226` 一起）；碰到沒權限的資料夾會整個出錯 |
| 遊戲記錄檔讀到半行時等讀完整才處理 | ★★ | **要，照抄。** Minecraft、Rust、Dragonwilds 用這個。VRChat VVMW 讀記錄檔是自己寫的（`VrChatVvmwIntegration.cs:204`），可能有同樣問題，另外處理 |
| 數字輸入加上下限（新增 `NumberUtils.clampInt`） | ★★ | **要，手移。** fork 拆成 `CaptureModeSection`、`ClipSettingsSection`（含 fork 自己的 `:397`）、`VideoSettingsSection`、`GameDetectionSection`、`StorageSettingsSection`，逐一換 |
| 容量警告視窗被關掉時也回覆後端（`ModalContext` 加 `onDismiss`） | ★★ | **要。** 現在按 Esc 關掉，後端會一直等回應 |
| video.tsx：方向鍵音量讀 state；目前區段只找同一支影片；新區段沿用同影片上一段的靜音軌 | ★★ | **要，手移（熱檔）。** fork 有播放清單與多檔區段，比上游更容易碰到。`skipTime` 更新時間那條 fork 已自己修（`video.tsx:1005`），不用動 |
| 登入 token 更新遇伺服器錯誤時重試、不登出（`useAuth`、`api.ts`） | ★★ | **要。** 上游伺服器掛掉時不會把你登出 |
| 一行小修：換篩選清選取、沒登入不能按上傳、壓縮進度舊計時器、復原視窗、錄影卡封面、選 exe 取消時回覆前端 | ★ | **順手做。** 錄影卡那條要確認雙槽兩張卡都改到 |
| 預設換編碼器時同步換 codec（PresetsService） | — | 併入上面「編碼器消失時自動換一個」 |
| WebSocket 只收本機來源 | — | **fork 已有**（`MessageService.cs:655`、`AudioStreamServer.cs:49`） |
| War Thunder 一直重試找暱稱 | ★ | 可合，照抄 |
| HotkeyBroker 安裝改到背景執行 | — | 等「管理員遊戲熱鍵」那列一起帶；fork 目前沒有 HotkeyBroker |
| Steam/Proton 安裝路徑、Linux 磁碟根目錄 | — | 跳過（Linux） |

---

## 未合：v1.9.0-beta（使用者指定加看）

2026-09-27 納入。**beta，上游之後可能還會改**；Release 欄用 beta Release 頁。

| 功能 | 上游 | 星 | 狀態 | Release | 建議（要不要改／怎麼改） | 碰撞 |
|---|---|---|---|---|---|---|
| 常駐回放暫存 | `2aa9c75` | ★★★ | 已追上 | v1.9.0-beta.1（beta）：**Replay Buffer**：新增可選的常駐回放暫存，沒有遊戲或手動錄影時持續錄顯示器。 | **值得，但要為雙槽重新設計。** 上游只有一條錄影管線：開錄前先停常駐暫存、錄完再開回來，設定一改就重啟。fork 建議：沿用「有錄影就停」，但條件改成「任一槽在錄」；常駐暫存固定用 slot 0，存檔熱鍵在沒有槽錄影時存它（`SaveReplayBuffer(slot)` 要認得）；呼叫點（設定存檔、螢幕變化、停錄後）照上游 `SyncAlwaysOnBuffer`。要確認：跟 VVMW 的 replay-tail 會不會搶同一個 buffer。已決定：不顯示錄影卡；PiP 手動開時顯示「桌面重播緩衝中」。它不走磁碟空間檢查（暫存在記憶體），這點照上游。**使用者指定要做，見下方實作計畫 B**。2026-09-27 已追上（`b3cd1c5`，fork v1.10.0）；上游的 OBS Studio 讓出 hook 沒做 | **熱檔** `OBSService`、SettingsService、`menu.tsx` |
| 錄影中可改設定、下次生效 | `d44f902` | ★★★ | 已追上 | v1.9.0-beta.1（beta）：**Settings**：錄影中不再鎖住錄影設定，改動從下一次錄影開始生效。 | **要，fork 比上游更需要**：雙槽常常有一槽在錄，設定幾乎一直鎖著。前端拿掉 `disabled={isRecording}`（CaptureMode／Video／GameIntegrations／AudioDevices），加 `PendingRecordingSettingsBanner`＋`usePendingRecordingSettings`（照抄）。後端每槽開錄時存 `StartSettings`；上游用全域 `_recordingAudioOutputMode`，fork 放進 `SessionPipeline`。要先確認 fork 錄影中改音訊裝置會不會即時動到正在錄的槽。**使用者指定要做，見下方實作計畫 A**。2026-09-27 已追上（`e8b5285`，fork v1.10.0）；快照多了 fork 自己的音軌與擷取方式欄位 | `OBSService`、`MessageService`、設定頁 |
| OBS Studio 直播／錄影時讓出遊戲 hook | `e6d1a32` `1f85493` `fc3f8a0` | ★★ | 未合 | v1.9.0-beta.1（beta）：**Game Capture**：OBS Studio 在直播或錄影時略過 hook，兩邊不再衝突。**Recording**：修開錄時短暫顯示 window capture 而不是顯示器擷取。v1.9.0-beta.2（beta）：**Game Capture**：切回遊戲時也檢查 OBS Studio，遊戲中開始直播會把 hook 交給 OBS Studio。**Game Capture**：切回遊戲時若 Segra 沒 hook 就重試（OBS Studio 在直播或 hook 被別的程式佔住時除外）。**Game Capture**：修 hook 已被別的程式（例如 OBS Studio 預覽）佔住時 Segra 一直重試。 | **你會同時開 OBS Studio 直播才有用。** 依賴 `d111c2a`（讓出 hook 後要靠 window capture 撐畫面），沒做那個就只剩顯示器擷取。`ObsStudioOutput.cs` 新檔可照抄；`OBSService` 部分手移進雙槽（每槽各自讓出／重試）。`fc3f8a0` 只在 `d111c2a` 之後有意義 | **熱檔** `OBSService`、`GameDetectionService` |
| 被刪掉的版本自動降回最新版 | `f9fa064` | ★★ | 未合 | v1.9.0-beta.1（beta）：**Updates**：使用者裝的版本被刪掉時，改回最新發佈的版本。 | **要，小改。** fork 自己發 release，刪掉有問題的版本後，裝到壞版的人能自動回到最新版。`UpdateService.cs` 照抄（fork 行號不同，手移） | UpdateService |
| keybind 改名 hotkey＋設定說明 | `1137f29` | ★ | 未合 | v1.9.0-beta.1（beta）：**Settings**：Keybindings 改名為 Hotkeys，並讓幾個設定說明更清楚。 | **等 HotkeyBroker 或設定 UI polish 一起做。** 大量改名，碰到 fork 沒有的 HotkeyBroker；設定檔欄位名沒變，不用 migration | 設定頁、Settings |

### 實作計畫（使用者指定：A、B 兩項）

**順序：先 A 再 B。** A 比較小，而且 B 需要 A 先把「錄影中還在讀全域設定」的地方改掉（B 會在設定改變時自己重啟，設定一改就要生效）。開工前建議先做完 v1.8.1 小修裡的「計時器 try/catch」和「設定檔原子寫入」，因為 B 會從螢幕變化和設定存檔觸發。兩項各開一條 branch。

#### A. 錄影中可改設定、下次生效（`d44f902`）

1. **後端：開錄時把設定存進槽裡。** `SessionPipeline` 已經有 `EffectiveSettings`（`GameSettingsService.Resolve` 在開錄時算好的快照），畫質、編碼那些不用動。但 fork 有幾處在錄影中還會讀全域設定，要改成讀開錄時存的值：
   - `OBSService.cs:1728`、`:1731`（遊戲 hook 上時，決定靜不靜音 desktop 音源：`AudioOutputMode`、`EnableSeparateAudioTracks`）
   - `OBSService.cs:1793`（遊戲 unhook 時）
   - `OBSService.cs:2045`（錄影中才開的語音軟體，上游只修了這一處）
   做法：`SessionPipeline` 加 `AudioOutputMode`、`EnableSeparateAudioTracks` 兩個欄位，在 `OBSService.StartRecording.cs` 建管線時填入。
2. **存回放熱鍵改看該槽的模式。** `RecordingHotkeyActions.cs:41` 和 `KeybindCaptureService.cs:179` 用全域 `Settings.Instance.RecordingMode` 判斷能不能存。錄影中把模式改成 Session 後，正在跑的 buffer 就存不了了。改成讀 `Pipeline(slot).EffectiveSettings.RecordingMode`。
3. **把開錄時的設定交給前端比對。** `MessageService` 加 `GetRecordingStartSettings()`（照抄上游），欄位清單除了上游那些，還要加上 fork 自己的錄影設定：錄影音軌遮罩、音軌名稱、錄影音訊 bitrate（`f4c7870` 補上的那幾個）。每槽開錄時存進該槽 `Recording.StartSettings`。
4. **前端：**
   - 拿掉 `disabled={isRecording}`：`CaptureModeSection.tsx:148`／`:173`、`VideoSettingsSection.tsx`（`:142`～`:374` 共 9 處）、`GameIntegrationsSection.tsx:170`。
   - `usePendingRecordingSettings.ts` 照抄，但改成比對「所有進行中的槽」，任一槽的開錄設定和目前不同就算有待生效的變更。
   - `PendingRecordingSettingsBanner.tsx` 照抄，放進 `Pages/settings.tsx`。
5. **刻意保留的即時行為：** 錄影中改 `SelectedDisplay` 會立刻換螢幕擷取（`SettingsService.cs:350`），這是 fork 已合的「錄影卡切螢幕」要的行為，不要改成下次生效；提示條的比對清單也不要放它。
6. **驗證：**
   - 錄影中改解析度／位元率 → 目前這支不變、下一支套用、提示條出現；停錄後提示消失。
   - 錄影中把音訊模式改掉，再切出切回遊戲（觸發 hook／unhook）→ 正在錄的這支聲音不受影響。
   - 雙槽各用自己開錄時的設定。
   - 錄影中改成 Session 模式 → 正在跑的 buffer 還能存。
   - `Segra.Tests` 補 `GetRecordingStartSettings` 欄位清單的測試。

#### B. 常駐回放暫存（`2aa9c75`）

**設計（跟上游不同的地方）：**
- **固定用 slot 0。** 它不進 `AppState.Recordings`：沒有錄影卡、不算錄影。PiP 不會自己跳出來，但手動打開時會顯示常駐暫存（見步驟 9）。另外用 `AppState.AlwaysOnBufferActive` 通知前端。
- **有任何開錄就先停它。** 在 `OBSService.StartRecording`（`OBSService.StartRecording.cs:23`）選槽之前停掉常駐暫存。遊戲偵測用 `AppState.GetFreeSlot()` 選槽（`GameDetectionService.cs:405` 附近），常駐暫存不佔 AppState，所以遊戲會拿到 slot 0；停掉後管線空出來，就能正常開錄。停止時要拿 `_slotLifecycleLocks[0]`，不要只靠 `_stopRecordingSemaphore`。
- **所有槽都停了才重開。** 條件是「沒有任何槽在錄影或預錄」。在 `StopRecordingSlot` 結束後、開錄失敗後呼叫 `SyncAlwaysOnBuffer()`。

**步驟：**
1. **設定：** `Settings.AlwaysOnReplayBuffer`（預設關）。fork 的 `SettingsService` 會自動套用前端送來的欄位，不用像上游那樣手寫比對。`AppState.AlwaysOnBufferActive` 照抄。
2. **開啟方式：** `StartRecording` 加 `alwaysOn` 參數，用 `startManually: true, reservedSlot: 0` 走進來。常駐模式下：
   - 強制 Buffer 模式
   - 略過磁碟空間檢查（暫存在記憶體）
   - 不播開始音效；失敗不跳視窗
   - 不建 `Recording`
   - `AddMonitorCapture` 不跳「找不到螢幕」的視窗
3. **同步函式：** `SyncAlwaysOnBuffer()`、`StopAlwaysOnBufferCore()`、`DisposeAlwaysOnBuffer()`、`StopFailedAlwaysOnBuffer()` 照上游邏輯改寫成操作 `Pipeline(0)`。
   - 設定變了就重開：上游用設定組一把 key 來比對；fork 的 key 要另外加錄影音軌遮罩／名稱、錄影音訊 bitrate。
   - 呼叫點：`SettingsService.SaveSettings` 結尾、`AppState.UpdateDisplays` 偵測到變化時、OBS 初始化完成時、停錄後。
4. **失敗處理照上游：** 跑超過 5 分鐘才失敗（多半是顯卡重設這類暫時問題）→ 30 秒後重試一次。剛開就失敗 → 等設定改變或下一次錄影結束後再試。
5. **存檔：** `RecordingHotkeyActions.TrySaveReplayBuffer`、`KeybindCaptureService.cs:179`、`MessageService.cs:124` 在「沒有錄影、但常駐暫存在跑」時存 slot 0。`SaveReplayBuffer(slot)` 裡從 `AppState.GetRecording(slot)` 取遊戲名、音軌名的地方，改成常駐時讀常駐暫存自己的資訊；遊戲名照上游用 "Manual Recording"。
6. **VVMW：** `TrySaveReplayBufferTailAsClipAsync(slot)` 只看 `buffer.IsActive`，要加「常駐暫存不算」，不然 VRChat 被偵測前的那段可能誤剪到常駐的 buffer。
7. **關閉程式：** `Shutdown`／`TryStopRecording` 設 `_isExiting`，避免關閉途中又被 Sync 開回來；`Shutdown` 等 `_stopRecordingSemaphore` 最多 5 秒（照上游）。
8. **前端：**
   - `CaptureModeSection.tsx` 加開關（文案照上游）
   - `menu.tsx` 顯示「桌面重播緩衝中」小標
   - `AppStateContext`／`types.ts` 加 `alwaysOnBufferActive`、`alwaysOnReplayBuffer`
9. **PiP 介面（`MonitoringCompactShell.tsx`，使用者指定）：** 排版跟錄影時一樣，差別如下：
   - **出現方式：** 常駐暫存啟動時**不**自動開 PiP；使用者從主視窗或托盤手動打開時，才顯示常駐暫存的面板。遊戲開錄時照舊自動開 PiP（`OBSService.StartRecording.cs:186`），這時改成顯示錄影。
   - **標題列：** 顯示「桌面重播緩衝中」，跟錄影的紅點做出區別。
   - **畫面預覽：** 只有「常駐暫存在跑，而且 PiP 開著」時，才讓 `RecordingPreviewService` 產生 slot 0 的預覽圖；PiP 關掉就停，閒置時不會一直在背後壓 JPEG。接點：`Program.MonitoringWindow.cs` 的開窗、關窗，以及常駐暫存的啟動、停止。`useRecordingPreview` 要能在沒有 `Recording` 物件時也顯示預覽。
   - **音量表：** 目前是直接讀 Windows 裝置的音量（`/api/recording-audio-levels`，`ContentServer.cs:205`），跟 OBS 無關；前端在常駐時也去讀就好，後端不用改。
   - **存回放按鈕：** 沿用 `SaveReplayBufferFromUi`（`MessageService.cs:123`），要先做完步驟 5 才能用。
   - **開始錄影按鈕：** 常駐暫存不算錄影，照樣顯示「開始錄影」；按下去就停掉常駐暫存、改成手動錄影（slot 0）。
   - **暫停／恢復常駐的開關：** 新增後端訊息 `SetAlwaysOnReplayBuffer`（`{ enabled }`），後端改設定、存檔，再送設定給前端。**不要**走 `updateSettings`（原因見「相依與前置」的「前端舊設定蓋回後端」）。
10. **風險：** 一直開著會持續佔用 GPU 編碼器和記憶體（上限是 `ReplayBufferMaxSize`），筆電較耗電，所以預設關閉。上游還在 beta，合之前再對一次上游最新版。
11. **驗證：**
    - 打開開關 → 閒置時 menu 顯示「桌面重播緩衝中」；按存檔熱鍵得到一支影片。
    - 開啟常駐後 PiP 不會自己跳出來；手動打開 PiP，看得到「桌面重播緩衝中」、預覽、音量表、存回放按鈕；按存回放得到一支影片；關掉 PiP 後預覽停止（log 裡看得到）。
    - 從 PiP 暫停常駐 → 設定頁的開關跟著變；再恢復 → 常駐暫存回來。
    - 開遊戲 → 常駐暫存停止、遊戲在 slot 0 正常錄、PiP 切成錄影畫面；再開第二個遊戲進 slot 1。
    - 兩槽都停 → 常駐暫存自動回來，PiP（若開著）切回「桌面重播緩衝中」。
    - 改解析度或換螢幕 → 常駐暫存重開。
    - VRChat VVMW 剪輯正常；關閉程式不卡住。

---

## 暫不追：v1.9.0-beta.2 之後

`upstream/main` 目前等於 `v1.9.0-beta.2`。更新的 beta／未 tag 的 main 不追，等下一個穩定版或使用者指定。

---

## 維護

對齊完一個功能後：把該列狀態改成 `已追上`，必要時加一行備註（哪個 commit／哪天）。不要另開第三份追蹤表。

寫入新 tag 的購物清單時：每個功能列都要填 **Release** 欄（穩定 Release 頁原文的繁中）。同一 Release 一條 bullet 對一列；一列對多條就全寫。穩定頁沒有的寫 `Release 未列`。不要把 Release 收成內部簡稱而把數字／分類藏起來。每個未合列也要填 **建議** 欄（要不要改、怎麼改），要先對過 fork 程式再寫。

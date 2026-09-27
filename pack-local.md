# 本機打包

讓其他人安裝這個 fork，並在之後自動更新。更新來源是 `https://github.com/Taiyopen/Segra`，repo 必須公開。

`publish\Segra.exe` 不會檢查更新。對方要執行 `Segra-win-Setup.exe` 安裝過一次，之後開 App 才會下載新版。

## 每次發布

在 repo 根目錄執行。`<版本>` 寫成 `1.9.0` 這種三碼，不要加 `v`。

1. 建置

```powershell
.\build-local.ps1
```

2. 安裝打包工具（已安裝可略過；要跟專案的 Velopack 1.2.0 相同）

```powershell
dotnet tool install -g vpk --version 1.2.0
```

3. 清掉上次的 `output`，再打包

```powershell
if (Test-Path .\output) { Remove-Item .\output -Recurse -Force }
vpk pack -u Segra -v <版本> -p ./publish -e Segra.exe -o ./output --packTitle "Segra" --noPortable -i ./Resources/icon.ico -s ./Resources/splash.png --splashProgressColor "#fecb00" --framework webview2,vcredist143-x64
```

`--framework` 會讓 Setup 在缺少時先裝好 WebView2 與 Visual C++ 2015–2022 x64 runtime。少了 VC++ runtime，全新的 Windows 上 OBS 會默默載入失敗（上游 v1.8.1 的修正）。

4. 上傳到 GitHub Release。不要勾 Pre-release，否則預設關閉「接收測試版」的安裝不會看到這包。

```powershell
gh release create "v<版本>" --repo Taiyopen/Segra --title "Release v<版本>" --notes "這版改了什麼" `
  ./output/Segra-win-Setup.exe `
  ./output/RELEASES `
  ./output/releases.win.json `
  ./output/assets.win.json `
  ./output/Segra-<版本>-full.nupkg
```

`gh` 需要能寫 `Taiyopen/Segra` 的權限。Release 說明會出現在 App 的 What's New。

## 版本號

每一包都要比上一包高，例如 `1.9.0`、`1.9.1`。

第一包要比對方電腦上已經安裝的 Segra 高，不然安裝程式會當成降版而拒絕。到 [官方最新 Release](https://github.com/Segergren/Segra/releases/latest) 看版號；官方若是 `1.8.0`，就從 `1.9.0` 起編。

## 對方怎麼裝

第一次：從 [Releases](https://github.com/Taiyopen/Segra/releases/latest) 下載 `Segra-win-Setup.exe` 並執行。沒有程式碼簽章，SmartScreen 會擋，選「仍要執行」。

這份安裝的應用識別與官方相同，都是 `Segra`。對方若已裝官方版，這次安裝會蓋掉它。

之後：你照上面發布更高的版本號。對方開 App 時會下載並提示安裝，不必再傳檔。對方也可以自己到 Releases 下載新的 Setup 再裝一次。

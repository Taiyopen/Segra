# CLAUDE.md

本專案的規則只維護在 `.cursor/rules/`，Cursor 與 Claude Code 共用。要改規則就改那邊的 `.mdc`，不要在這裡另寫一份。

## 工具對照

規則是照 Cursor 寫的，Claude Code 對應如下：

- `block_until_ms` → Bash／PowerShell 工具的 `timeout`（`build-local.ps1` 給 ≥ 180000）
- 「寫檔」包含 Write／Edit，以及用 shell 寫入檔案

## 規則

@.cursor/rules/ask-before-modify.mdc
@.cursor/rules/karpathy-guidelines.mdc
@.cursor/rules/multi-agent.mdc
@.cursor/rules/project-architecture.mdc
@.cursor/rules/build-local.mdc
@.cursor/rules/session-changelog.mdc
@.cursor/rules/upstream-sync.mdc

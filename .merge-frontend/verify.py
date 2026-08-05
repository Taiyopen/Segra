from pathlib import Path

files = [
    "Frontend/src/App.tsx",
    "Frontend/src/Components/ContentCard.tsx",
    "Frontend/src/Components/ContentPage.tsx",
    "Frontend/src/Models/types.ts",
    "Frontend/src/menu.tsx",
    "Frontend/src/Pages/video.tsx",
    "Frontend/src/Pages/settings.tsx",
    "Frontend/src/Components/Settings/GameDetectionSection.tsx",
    "Frontend/src/Components/Settings/StorageSettingsSection.tsx",
    "Frontend/src/Components/Settings/AudioDevicesSection.tsx",
    "Frontend/src/Components/Settings/CaptureModeSection.tsx",
    "Frontend/src/Components/Settings/ClipSettingsSection.tsx",
    "Frontend/src/Components/Settings/GameIntegrationsSection.tsx",
    "Frontend/src/Components/Settings/MenuCustomizationSection.tsx",
    "Frontend/src/Components/Settings/VideoSettingsSection.tsx",
    "Frontend/src/Components/RecordingCard.tsx",
    "Frontend/src/Context/AppStateContext.tsx",
    "Frontend/src/Context/SelectedVideoContext.tsx",
    "Frontend/src/Pages/sessions.tsx",
]

for f in files:
    t = Path(f).read_text(encoding="utf-8")
    markers = t.count("<<<<<<<")
    flags = []
    for key in [
        "airplaneMode",
        "useDeleteConfirmation",
        "settings.games",
        "vrChat",
        "PendingEdit",
        "getActiveRecordings",
        "recordingDriveUsedGb",
        "confirmBeforeDeleting",
        "startupWindowMode",
        "inputNoiseSuppression",
        "normalizeMenuItems",
        "待剪輯",
        "瀏覽影片",
    ]:
        if key in t:
            flags.append(key)
    print(f"{f}: markers={markers} | {', '.join(flags)}")

# App airplane detail
app = Path("Frontend/src/App.tsx").read_text(encoding="utf-8")
print("\n--- App airplane/signOut ---")
for i, line in enumerate(app.splitlines(), 1):
    if "airplane" in line or "signOut" in line or "needsUsername" in line:
        print(f"{i}: {line}")

# ContentCard delete / airplane
card = Path("Frontend/src/Components/ContentCard.tsx").read_text(encoding="utf-8")
print("\n--- ContentCard key lines ---")
for i, line in enumerate(card.splitlines(), 1):
    if "confirmDelete" in line or "airplaneMode" in line or "useDeleteConfirmation" in line:
        print(f"{i}: {line}")

# ContentPage delete
page = Path("Frontend/src/Components/ContentPage.tsx").read_text(encoding="utf-8")
print("\n--- ContentPage delete block ---")
for i, line in enumerate(page.splitlines(), 1):
    if "confirmDelete" in line or "useDeleteConfirmation" in line or "handleDeleteSelected" in line:
        print(f"{i}: {line}")

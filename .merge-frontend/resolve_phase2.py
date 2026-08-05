import os
import re
import subprocess
from pathlib import Path

ROOT = Path(r"C:\Users\Taiyo\Desktop\Segra")
os.chdir(ROOT)


def git_show(ref: str, path: str) -> str:
    return subprocess.check_output(["git", "show", f"{ref}:{path}"], encoding="utf-8")


def write(path: str, content: str) -> None:
    content = content.replace("\r\n", "\n")
    if not content.endswith("\n"):
        content += "\n"
    (ROOT / path).write_text(content, encoding="utf-8", newline="\n")
    print(f"WROTE {path} ({len(content.splitlines())} lines)")


def checkout(side: str, path: str) -> None:
    subprocess.check_call(["git", "checkout", f"--{side}", "--", path])
    print(f"CHECKOUT --{side} {path}")


# ---- ContentCard: ours + useDeleteConfirmation + airplaneMode ----
checkout("ours", "Frontend/src/Components/ContentCard.tsx")
card = (ROOT / "Frontend/src/Components/ContentCard.tsx").read_text(encoding="utf-8")

if "useDeleteConfirmation" not in card:
    card = card.replace(
        "import Button from './Button';\n",
        "import Button from './Button';\nimport { useDeleteConfirmation } from '../Hooks/useDeleteConfirmation';\n",
    )

# settings destructure
card = card.replace(
    "const { enableAi, showNewBadgeOnVideos } = useSettings();",
    "const { enableAi, showNewBadgeOnVideos, airplaneMode } = useSettings();",
)

if "const confirmDelete = useDeleteConfirmation();" not in card:
    card = card.replace(
        "const { compressionProgress, isCompressing } = useCompression();\n",
        "const { compressionProgress, isCompressing } = useCompression();\n"
        "  const confirmDelete = useDeleteConfirmation();\n",
    )

old_delete = """  const handleDelete = () => {
    const parameters: any = {
      FileName: content!.fileName,
      ContentType: type,
    };

    sendMessageToBackend('DeleteContent', parameters);
  };"""

new_delete = """  const handleDelete = () => {
    const parameters: any = {
      FileName: content!.fileName,
      ContentType: type,
    };

    const displayName = content!.title || content!.game || content!.fileName;
    confirmDelete({
      title: `Delete ${type.toLowerCase()}?`,
      description: (
        <>
          Are you sure you want to permanently delete <strong>{displayName}</strong>?
          <br />
          <span className="text-sm text-gray-400">This action cannot be undone.</span>
        </>
      ),
      onConfirm: () => sendMessageToBackend('DeleteContent', parameters),
    });
  };"""

if old_delete not in card:
    raise SystemExit("ContentCard handleDelete block not found")
card = card.replace(old_delete, new_delete)

# Gate upload dropdown entries with airplaneMode when present
# Pattern 1: Clip/Highlight upload menu item wrapper - find lines with handleUpload
# Look for common patterns in fork
patterns_to_wrap = [
    (
        "{(type === 'Clip' || type === 'Highlight') && (",
        "{!airplaneMode && (type === 'Clip' || type === 'Highlight') && (",
    ),
    (
        "{(type === 'Clip' || type === 'Highlight' || type === 'PendingEdit' || type === 'External') && (",
        "{!airplaneMode && (type === 'Clip' || type === 'Highlight' || type === 'PendingEdit' || type === 'External') && (",
    ),
]
for old, new in patterns_to_wrap:
    if old in card and new not in card:
        card = card.replace(old, new)

# uploadId view link
card = card.replace(
    "{content!.uploadId && (",
    "{!airplaneMode && content!.uploadId && (",
)
# avoid double wrapping
card = card.replace(
    "{!airplaneMode && !airplaneMode && content!.uploadId && (",
    "{!airplaneMode && content!.uploadId && (",
)

write("Frontend/src/Components/ContentCard.tsx", card)


# ---- ContentPage: ours + useDeleteConfirmation ----
checkout("ours", "Frontend/src/Components/ContentPage.tsx")
page = (ROOT / "Frontend/src/Components/ContentPage.tsx").read_text(encoding="utf-8")

if "useDeleteConfirmation" not in page:
    # insert import after Button import if present, else after MessageUtils
    if "import Button from './Button';\n" in page:
        page = page.replace(
            "import Button from './Button';\n",
            "import Button from './Button';\nimport { useDeleteConfirmation } from '../Hooks/useDeleteConfirmation';\n",
        )
    else:
        page = page.replace(
            "import { sendMessageToBackend } from '../Utils/MessageUtils';\n",
            "import { sendMessageToBackend } from '../Utils/MessageUtils';\n"
            "import { useDeleteConfirmation } from '../Hooks/useDeleteConfirmation';\n",
        )

if "const confirmDelete = useDeleteConfirmation();" not in page:
    page = page.replace(
        "const { isModalOpen } = useModal();\n",
        "const { isModalOpen } = useModal();\n  const confirmDelete = useDeleteConfirmation();\n",
    )

old_bulk = """  const handleDeleteSelected = useCallback(() => {
    if (selectedItems.size === 0) return;

    const items = Array.from(selectedItems).map((fileName) => {
      const item = state.content.find((c) => c.fileName === fileName);
      return {
        FileName: fileName,
        ContentType: item?.type ?? contentType,
      };
    });

    sendMessageToBackend('DeleteMultipleContent', { Items: items });
    setSelectedItems(new Set());
  }, [selectedItems, contentType, state.content]);"""

new_bulk = """  const handleDeleteSelected = useCallback(() => {
    if (selectedItems.size === 0) return;

    const items = Array.from(selectedItems).map((fileName) => {
      const item = state.content.find((c) => c.fileName === fileName);
      return {
        FileName: fileName,
        ContentType: item?.type ?? contentType,
      };
    });

    const count = items.length;
    confirmDelete({
      title: `Delete ${count} ${count === 1 ? 'item' : 'items'}?`,
      description: `Are you sure you want to permanently delete the selected ${count === 1 ? 'item' : `${count} items`}?\\n\\nThis action cannot be undone.`,
      onConfirm: () => {
        sendMessageToBackend('DeleteMultipleContent', { Items: items });
        setSelectedItems(new Set());
      },
    });
  }, [selectedItems, contentType, state.content, confirmDelete]);"""

if old_bulk not in page:
    raise SystemExit("ContentPage handleDeleteSelected block not found")
page = page.replace(old_bulk, new_bulk)
# Fix escaped newlines in description to real \n for template string
page = page.replace(
    "description: `Are you sure you want to permanently delete the selected ${count === 1 ? 'item' : `${count} items`}?\\n\\nThis action cannot be undone.`,",
    "description: `Are you sure you want to permanently delete the selected ${count === 1 ? 'item' : `${count} items`}?\\n\\nThis action cannot be undone.`.replace(/\\\\n/g, '\\n'),",
)
# Actually just write proper newlines in the template string
page = page.replace(
    "description: `Are you sure you want to permanently delete the selected ${count === 1 ? 'item' : `${count} items`}?\\n\\nThis action cannot be undone.`.replace(/\\\\n/g, '\\n'),",
    "description: `Are you sure you want to permanently delete the selected ${count === 1 ? 'item' : `${count} items`}?\n\nThis action cannot be undone.`,",
)

write("Frontend/src/Components/ContentPage.tsx", page)


# ---- App.tsx: ours + airplane mode sign-out from upstream ----
app = git_show("HEAD", "Frontend/src/App.tsx")

# Ensure useAuth destructures signOut
if "signOut" not in app:
    app = app.replace(
        "const { session } = useAuth();",
        "const { session, signOut } = useAuth();",
    )

# needsUsername should respect airplane mode
app = app.replace(
    "const needsUsername = session && profile?.username?.startsWith('user_');",
    "const needsUsername = !settings.airplaneMode && session && profile?.username?.startsWith('user_');",
)

airplane_effect = """
  // Airplane mode hides all cloud features and must not keep a signed-in session.
  useEffect(() => {
    if (settings.airplaneMode && session) {
      signOut();
    }
  }, [settings.airplaneMode, session, signOut]);
"""

if "settings.airplaneMode && session" not in app:
    # insert after needsUsername line
    app = re.sub(
        r"(const needsUsername = !settings\.airplaneMode && session && profile\?\.username\?\.startsWith\('user_'\);)",
        r"\1\n" + airplane_effect,
        app,
        count=1,
    )

write("Frontend/src/App.tsx", app)

print("Done phase 2")

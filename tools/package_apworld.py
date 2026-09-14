"""Packs apworld/reus2 into dist/reus2.apworld: a zip whose entries use forward slashes, which
Archipelago requires to load it.

Usage:  python tools/package_apworld.py
"""

import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "apworld" / "reus2"
OUTPUT = ROOT / "dist" / "reus2.apworld"


def main():
    OUTPUT.parent.mkdir(exist_ok=True)
    with zipfile.ZipFile(OUTPUT, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(SOURCE.rglob("*")):
            if path.is_file() and "__pycache__" not in path.parts:
                archive.write(path, "reus2/" + path.relative_to(SOURCE).as_posix())
    print(f"wrote {OUTPUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()

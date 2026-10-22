import io
import json
import re
import sys

SPEC = r"d:/CSharpProjects/WC4AssetsManager-In-CSharp/scripts/mapgen/regions/shanghai.txt"
MAP = r"d:/CSharpProjects/WC4AssetsManager-In-CSharp/scripts/mapgen/regions/shanghai_zh_names.json"


def has_cjk(s):
    return re.search(r"[\u4e00-\u9fff]", s) is not None


def main():
    mapping = json.load(io.open(MAP, encoding="utf-8"))
    lines = io.open(SPEC, encoding="utf-8").read().splitlines()
    out = []
    section = None
    changed = 0

    for line in lines:
        stripped = line.split("#", 1)[0].strip()
        if stripped.startswith("[") and stripped.endswith("]"):
            section = stripped[1:-1].strip().lower()
            out.append(line)
            continue

        if section != "cities" or not stripped:
            out.append(line)
            continue

        m = re.match(r"^(\S+)\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(\S+)\s+(\d+)$", stripped)
        if not m:
            out.append(line)
            continue

        name, lat, lon, owner, level = m.groups()
        if not has_cjk(name):
            name = mapping.get(name, name)
            changed += 1
        out.append(f"{name} {lat} {lon} {owner} {level}")

    io.open(SPEC, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
    print("converted", changed)


if __name__ == "__main__":
    sys.exit(main())

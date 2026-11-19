"""网页基础/扩展优先，蓝岛补充；Gboard 字典不参与。只读资料，不联网或执行 JS。"""
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent
SOURCES = ROOT / "tools/emoticons"
entries = {}
names = set()

def add(text, name, source):
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    # 只保留用于排版的全角/零宽空格，过滤单个符号。
    if not text or (len(text.strip()) <= 1 and text not in ("　", "\u200b")):
        return
    # “҉( ﾟ∀。)”的实际图案为两个全角空格 + ҉ + 换行 + 表情。
    # ҉ 贴在全角空格上会被分配给不含此符号的 CJK 字体；插入零宽基底，保留原有缩进。
    text = re.sub(r"(^|\s)(?=҉)", lambda m: m[1] + "\u200b", text)
    if name.startswith("҉"):
        name = "\u200b" + name
    key = re.sub(r"\s+", "", text)
    if key in entries:
        if name and not entries[key]["Name"]:
            entries[key]["Name"] = name
        return
    normalized_name = re.sub(r"\s+|（网页）", "", name)
    # 同名多行图案以网页版为准，不额外收录仅空格不同的蓝岛版本。
    if "\n" in text and normalized_name and normalized_name in names:
        return
    entries[key] = {"Text": text, "Name": name, "Source": source}
    if normalized_name:
        names.add(normalized_name)

for filename, source in (("web-2026-10-25.json", "X岛网页"), ("web-ex-2026-10-25.json", "X岛-EX")):
    for item in json.loads((SOURCES / filename).read_text(encoding="utf-8"))["Entries"]:
        add(item["Text"], item["Name"], source)

lines = (SOURCES / "blue-island.txt").read_text(encoding="utf-8-sig").splitlines()
title = re.compile(r"^（([^）]+)）$")
first_large = next(i for i, line in enumerate(lines) if title.fullmatch(line))
for line in lines[1:first_large]:
    if not line.strip():
        continue
    line = re.sub(r"[ \t]*（显示不完全）[ \t]*$", "", line)
    alias = re.search(r"[ \t]{2,}（([^）]+)）[ \t]*$", line)
    add(line[:alias.start()] if alias else line, alias[1] if alias else "", "蓝岛资料")
i = first_large
while i < len(lines):
    match = title.fullmatch(lines[i])
    j = i + 1
    while j < len(lines) and not title.fullmatch(lines[j]):
        j += 1
    body = lines[i + 1:j]
    while body and body[-1] == "":
        body.pop()
    add("\n".join(body), match[1], "蓝岛资料")
    i = j

ordered = list(entries.values())
target = ROOT / "src/Paomian.Desktop/Assets/emoticons.json"
target.write_text(json.dumps(ordered, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"Total": len(ordered), "Multiline": sum("\n" in e["Text"] for e in ordered), "Sources": {s: sum(e["Source"] == s for e in ordered) for s in ("X岛网页", "X岛-EX", "蓝岛资料")}}, ensure_ascii=True))

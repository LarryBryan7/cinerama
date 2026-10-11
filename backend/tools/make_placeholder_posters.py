#!/usr/bin/env python3
"""Genera pósters de marcador (título sobre un degradado) para cada película de cartelera.json. No descarga imágenes.

Uso:   python3 -I backend/tools/make_placeholder_posters.py
Para usar un póster real, copia tu archivo con el MISMO nombre a backend/src/Cinerama.Api/media/posters/.
Solo crea los que faltan; con --force reemplaza todos.
"""
import colorsys, json, re, sys, unicodedata, zlib
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "src/Cinerama.Infrastructure/Persistence/Seed/cartelera.json"
OUT = ROOT / "src/Cinerama.Api/media/posters"
FONTS = ["/System/Library/Fonts/Helvetica.ttc", "/System/Library/Fonts/Supplemental/Arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"]


def slug(title: str) -> str:  # misma regla que DatabaseSeeder.Slug
    ascii_ = "".join(c for c in unicodedata.normalize("NFD", title) if unicodedata.category(c) != "Mn")
    return re.sub(r"[^a-z0-9]+", "-", ascii_.lower()).strip("-")


def font(size: int, bold: bool = False):
    for path in FONTS:
        try:
            return ImageFont.truetype(path, size, index=1 if bold and path.endswith(".ttc") else 0)
        except OSError:
            continue
    return ImageFont.load_default(size)


def wrap(draw, text, fnt, width):
    lines, line = [], ""
    for word in text.split():
        trial = f"{line} {word}".strip()
        if draw.textlength(trial, font=fnt) <= width:
            line = trial
        else:
            lines.append(line)
            line = word
    return lines + [line]


def make(title: str, path: Path) -> None:
    w, h = 600, 900
    hue = (zlib.crc32(title.encode()) % 360) / 360  # un color distinto y estable por título
    top = tuple(int(c * 255) for c in colorsys.hsv_to_rgb(hue, .55, .55))
    bottom = tuple(int(c * 255) for c in colorsys.hsv_to_rgb(hue, .70, .16))
    img = Image.new("RGB", (w, h))
    px = ImageDraw.Draw(img)
    for y in range(h):
        t = y / (h - 1)
        px.line([(0, y), (w, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    px.rectangle([30, 30, w - 30, h - 30], outline="white", width=3)
    big = font(54, bold=True)
    lines = wrap(px, title, big, w - 120)
    y = h / 2 - len(lines) * 36
    for line in lines:
        px.text((w / 2, y), line, font=big, fill="white", anchor="mm")
        y += 72
    px.text((w / 2, h - 85), "PÓSTER DE EJEMPLO · CINERAMA", font=font(24), fill="white", anchor="mm")
    img.save(path, "JPEG", quality=88)


def main() -> None:
    force = "--force" in sys.argv
    OUT.mkdir(parents=True, exist_ok=True)
    titles = [m["title"] for m in json.loads(DATA.read_text(encoding="utf-8"))["movies"]]
    for title in titles:
        path = OUT / f"{slug(title)}.jpg"
        if force or not path.exists():
            make(title, path)
            print("creado", path.name)
    keep = {f"{slug(t)}.jpg" for t in titles}
    for old in OUT.glob("*.jpg"):
        if old.name not in keep:
            print("sobra (de un catálogo anterior):", old.name)


if __name__ == "__main__":
    main()

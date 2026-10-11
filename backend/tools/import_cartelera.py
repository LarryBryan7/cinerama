#!/usr/bin/env python3
"""Importa la cartelera real de Cinerama Chimbote y la guarda como datos del seed.

Uso:   python3 backend/tools/import_cartelera.py                       # cartelera de hoy (cambia a diario)
       python3 backend/tools/import_cartelera.py --save-snapshot       # además guarda la respuesta cruda
       python3 backend/tools/import_cartelera.py --from-file backend/tools/snapshots/cinerama-chimbote-2026-10-07.json

- Lee el endpoint público que usa la propia página https://www.cinerama.com.pe/cartelera_cine/CINERAMA_CHIMBOTE
  (assets/ajax/obtener_peliculas.php). Solo texto: NO descarga imágenes.
- El texto queda en minúsculas con la primera letra en mayúscula, y "mayores de N" se muestra como "+N". Los horarios son los del día de la consulta.
- Escribe backend/src/Cinerama.Infrastructure/Persistence/Seed/cartelera.json, que el seed carga al arrancar con la base vacía.
- Cinestar (https://www.cinestar.com.pe/cinemas/chimbote) está detrás de una verificación anti-bots de Cloudflare y no se
  consulta automáticamente. Sus películas se agregan con --extra <archivo.json> (mismo formato que "movies" y "screenings").
"""
import argparse
import datetime as dt
import json
import re
import sys
import unicodedata
import urllib.parse
import urllib.request
from pathlib import Path

BASE = "https://www.cinerama.com.pe"
MEDIA = Path(__file__).resolve().parents[1] / "src/Cinerama.Api/media"
SNAPSHOTS = Path(__file__).resolve().parent / "snapshots"
OUT = Path(__file__).resolve().parents[1] / "src/Cinerama.Infrastructure/Persistence/Seed/cartelera.json"

# Ubicación real del cine (OpenStreetMap, nodo "Cinerama", Av. José Pardo, Chimbote). La dirección es la que publica Cinerama.
CINEMA = {
    "name": "Cinerama - Chimbote",
    "address": "Av. v. raúl h. de la torre, mega plaza chimbote",
    "region": "Áncash",
    "latitude": -9.102564,
    "longitude": -78.557360,
    "logoUrl": None,
    # Salas de demostración (la página no publica su distribución): nombre, filas, posiciones por fila, posición del pasillo, precio.
    # Siete: es lo mínimo para que los horarios reales de un día no se solapen en una misma sala.
    "rooms": [
        {"name": "Sala 1", "rows": 6, "positions": 11, "aisle": 6, "price": 12.0},
        {"name": "Sala 2", "rows": 10, "positions": 14, "aisle": 7, "price": 15.0},
        {"name": "Sala 3", "rows": 10, "positions": 21, "aisle": 11, "price": 18.0},
        {"name": "Sala 4", "rows": 6, "positions": 11, "aisle": 6, "price": 12.0},
        {"name": "Sala 5", "rows": 8, "positions": 12, "aisle": 6, "price": 14.0},
        {"name": "Sala 6", "rows": 10, "positions": 14, "aisle": 7, "price": 15.0},
        {"name": "Sala 7", "rows": 6, "positions": 11, "aisle": 6, "price": 12.0},
    ],
}


def slug(title: str) -> str:  # misma regla que DatabaseSeeder.Slug
    ascii_ = "".join(c for c in unicodedata.normalize("NFD", title) if unicodedata.category(c) != "Mn")
    return re.sub(r"[^a-z0-9]+", "-", ascii_.lower()).strip("-")


def fetch(path: str, **params) -> list:
    url = f"{BASE}/assets/ajax/{path}" + ("?" + urllib.parse.urlencode(params) if params else "")
    req = urllib.request.Request(url, headers={"User-Agent": "cinerama-portfolio-importer/1.0 (proyecto personal)"})
    with urllib.request.urlopen(req, timeout=30) as response:
        return json.load(response)


def cap(text: str) -> str:
    """Minúsculas con la primera letra en mayúscula ("AVENGERS: DOOMSDAY" -> "Avengers: doomsday")."""
    text = text.strip().lower()
    for i, ch in enumerate(text):
        if ch.isalpha():
            return text[:i] + ch.upper() + text[i + 1:]
    return text


def classification(raw: str) -> str:
    """"MAYORES DE 14" -> "+14"; "TODO ESPECTADOR" -> "Todo espectador"."""
    match = re.fullmatch(r"\s*mayores\s+de\s+(\d+)\s*", raw, re.I)
    return f"+{match.group(1)}" if match else cap(raw)


def clean_title(raw: str) -> tuple[str, bool]:
    """Quita la marca "(PREVENTA)" del título y avisa de que es una preventa."""
    presale = bool(re.search(r"\(\s*preventa\s*\)", raw, re.I))
    title = re.sub(r"\s*\(\s*preventa\s*\)", "", raw, flags=re.I).strip()
    return cap(title), presale


def trailer_url(code) -> str | None:
    """Cinerama guarda el código de YouTube (11 caracteres) o a veces basura ("e", "dsaf"). Solo se acepta un código válido."""
    code = (code or "").strip()
    match = re.search(r"(?:v=|youtu\.be/|embed/)([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])", code)
    video_id = match.group(1) if match else (code if re.fullmatch(r"[A-Za-z0-9_-]{11}", code) else None)
    return f"https://www.youtube.com/watch?v={video_id}" if video_id else None


def minutes(duration: str) -> int:
    h, m = duration.split(":")[:2]
    return int(h) * 60 + int(m)


def to_24h(hour: str) -> str:
    return dt.datetime.strptime(hour.strip(), "%I:%M %p").strftime("%H:%M")


def preview_video(title: str) -> str | None:
    """Si hay un video <título>.mp4 en la carpeta de pósters, la película lo usa como video corto del detalle."""
    name = slug(title) + ".mp4"
    return f"posters/{name}" if (MEDIA / "posters" / name).exists() else None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--from-file", type=Path, help="usa una respuesta guardada (backend/tools/snapshots/*.json) en lugar de consultar la página")
    parser.add_argument("--save-snapshot", action="store_true", help="guarda la respuesta de hoy en backend/tools/snapshots/")
    parser.add_argument("--extra", type=Path, help="JSON con más 'movies' y 'screenings' (por ejemplo, de Cinestar)")
    args = parser.parse_args()

    source = json.loads(args.from_file.read_text(encoding="utf-8")) if args.from_file else fetch("obtener_peliculas.php", cine="CINERAMA_CHIMBOTE")
    if args.save_snapshot and not args.from_file:
        snapshot_file = SNAPSHOTS / f"cinerama-chimbote-{source[0]['funcion']}.json"
        snapshot_file.write_text(json.dumps(source, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print("snapshot guardado:", snapshot_file)
    movies, screenings, snapshot = [], [], None
    for p in source:
        title, presale = clean_title(p["nombre"])
        snapshot = snapshot or p.get("funcion")
        movies.append({
            "title": title,
            "synopsis": cap(p["sinopsis"]),
            "minutes": minutes(p["duracion"]),
            "classification": classification(p["censura"]),
            "genre": cap(p["genero"]),
            "releaseDate": p["estreno"],
            "preSale": presale,
            "trailerUrl": trailer_url(p.get("trailer")),
            "previewVideo": preview_video(title),
            "director": p["director"].strip().lower(),
            "cast": p["actores"].strip().lower(),
            "distributor": p["distribuidora"].strip().lower(),
        })
        screenings.append({"movie": title, "times": [to_24h(h["hora"]) for h in p["horarios"]]})

    cinema = {**CINEMA, "screenings": screenings}
    data = {"source": f"{BASE}/cartelera_cine/CINERAMA_CHIMBOTE", "snapshotDate": snapshot, "movies": movies, "cinemas": [cinema]}

    if args.extra:
        extra = json.loads(args.extra.read_text(encoding="utf-8"))
        known = {m["title"] for m in data["movies"]}
        data["movies"] += [m for m in extra.get("movies", []) if m["title"] not in known]
        data["cinemas"] += extra.get("cinemas", [])

    OUT.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{len(movies)} películas, {sum(len(s['times']) for s in screenings)} horarios -> {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

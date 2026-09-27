"""Собирает src/*.js в один index.html (всё в одном модуле, модели лежат рядом в models/)."""
import pathlib

root = pathlib.Path(__file__).parent
src = root / "src"
code = "\n".join((src / f).read_text(encoding="utf-8") for f in sorted(p.name for p in src.glob("*.js")))
page = (src / "page.html").read_text(encoding="utf-8").replace("/*__GAME__*/", code)
(root / "index.html").write_text(page, encoding="utf-8")
print("index.html:", len(page) // 1024, "KB")
local = '<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body>' + page + '</body></html>'
(root / "local.html").write_text(local, encoding="utf-8")

# Сеча — симулятор битв

Личный симулятор сражений «армия на армию» с процедурными ландшафтами, тактическим рельефом и полководцами, которые сами управляют отрядами.

Проект состоит из двух частей:

| Папка | Что внутри |
|---|---|
| `web/` | Играбельная браузерная версия на three.js: рельеф, укрытия, засады, гонцы, полководцы, летопись боя. |
| `unity/Assets/BattleSim/` | Скрипты для Unity (URP) — основа будущей версии под Android. |

## Браузерная версия

Исходники лежат в `web/src/` и собираются в один `web/index.html`:

```bash
cd web
python build.py
python -m http.server 8766
```

Затем откройте `http://localhost:8766/local.html`.

- `src/1_core.js` — шум, типы войск, биомы, полководцы
- `src/2_world.js` — рельеф, овраги, ограды, анализ высот и низин, прямая видимость
- `src/3_assets.js` — загрузка моделей, перекраска армий, слияние в один меш
- `src/4_sim.js` — отряды, приказы, боевой дух, бой, болты
- `src/4b_commander.js` — «мозг» полководца
- `src/5_input.js` — камера и управление
- `src/6_main.js` — интерфейс и главный цикл
- `src/page.html` — разметка и стили

Модели упаковываются скриптом `pack_models.py`: из GLB выбрасываются неиспользуемые анимации, текстуры выносятся в PNG, а сам GLB кодируется в base64 (`pack/*.txt`), потому что хостинг демо не отдаёт файлы `.glb`.

## Unity

1. Установите Unity Hub и Unity 6 LTS с модулем **Android Build Support** (ставьте на диск с запасом места — нужно 12–15 ГБ).
2. Создайте проект по шаблону **Universal 3D**.
3. Скопируйте папку `unity/Assets/BattleSim` в `Assets/` проекта.
4. Скрипт `Editor/BattleSimSetup.cs` сам создаст сцену `BattleSim`, материалы и настроит проект. Нажмите Play.

## Модели

Все модели под лицензией CC0 (можно использовать где угодно, указывать автора не обязательно):

- [KayKit Adventurers](https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0) — солдаты и анимации
- [KayKit Medieval Hexagon](https://github.com/KayKit-Game-Assets/KayKit-Medieval-Hexagon-Pack-1.0) — замки, деревья, камни
- [Quaternius Horse](https://poly.pizza/m/qvTrSG9pZF), [White Horse](https://poly.pizza/m/bEdE4rmZy9) — кони

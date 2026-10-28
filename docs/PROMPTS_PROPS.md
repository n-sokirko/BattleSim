# Промпты для Hunyuan3D (текст → 3D): уникальные декорации карты

Общие слова для каждого запроса: `stylized fantasy game asset, painted low-poly look, chunky heroic proportions, single object, centered, plain background, no ground`.
Дальше — сам предмет. Массовые вещи (деревья, трава, камни) берём из бесплатных паков, из Hunyuan — только то, что даёт карте лицо.

Первая партия (по значимости):
1. **Юрта кочевников**: round nomad felt yurt, ochre and rust-red felt, bone-and-horn trim on the roof, leather ropes, small door flap.
2. **Частокол**: short wooden palisade section, sharpened logs, iron bands, rust-red tribal cloth, a skull on a spike.
3. **Сторожевая башня**: wooden watchtower on stilts, small platform with roof, rope ladder, nomad banner.
4. **Знамя Эрланда**: tall banner pole, teal cloth with a golden tower emblem, bronze finial, stone base.
5. **Лагерный костёр**: camp fire pit with stones, iron cooking tripod and pot, logs, a pile of bones nearby.
6. **Повозка**: old wooden supply wagon with a torn canvas cover, two wheels, spilled crates and sacks.
7. **Руины-арка**: ruined ancient stone archway overgrown with ivy, fallen blocks at the base (для места сундука «Тайник у холма»).

Дальше по запросу: логово Чёрного Клыка, камни-менгиры, мост через брод, сломанная повозка, статуя, шатёр вождя.

После генерации: скачать GLB → прогнать `tools/blender/remesh_bake.py` (чистая сетка 8–20 тыс. треугольников, запечённый цвет) → `godot/assets/props_gen/`.

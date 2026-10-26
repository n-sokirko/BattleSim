# Промпты для генерации персонажей под анимацию (ChatGPT → Hunyuan3D)

Правила, чтобы модель можно было привязать к скелету:

1. **Одно изображение — один предмет.** Персонаж отдельно, каждое оружие и щит отдельно. Иначе получаем три копии (как с листами).
2. **Персонаж в T-позе:** руки прямо в стороны, ладони вниз, ноги на ширине плеч, стопы вперёд. Руки **пустые**, без оружия и щита.
3. **Лицо видно:** открытое, нейтральное, глаза открыты. Шлем без забрала, закрывающего лицо; капюшон не на глаза.
4. **Без плаща до земли и длинных юбок:** плащ либо короткий (до лопаток), либо отдельной вещью. Длинные ткани мешают при ходьбе.
5. **Фон белый, свет ровный, без теней на полу, без текста и рамок.**

## 1. Общий стиль (отправить первым)

```
You are a concept artist for a stylized fantasy RTS (Warcraft-3-like, but with our own original designs). Create ONE character per image, in ONE consistent style:
- chunky heroic proportions: wide shoulders, thick arms and legs, big boots and gauntlets, head about 1/6 of body height
- painted stylized 3D look: crisp readable shapes, hand-painted color, slight facets on armor, NO photorealism
- strictly front view, orthographic, no perspective, full body from head to feet
- strict T-POSE: arms straight out to the sides horizontally, palms facing down, fingers together; legs straight, shoulder width apart, feet pointing forward
- hands EMPTY: no weapon, no shield, no bow, nothing held
- face clearly visible, neutral calm expression, eyes open, clean readable features
- plain pure white background, soft even light, no floor shadow, no text, no borders, a single character only
Reply "ready" and wait for the character descriptions.
```

## 2. Персонажи

**Каэл Вейн (герой Эрланда):**
```
Character: Kael Vein, human knight-commander, about 28 years old. Face: strong square jaw, short dark brown hair, light stubble, a thin scar over the left eyebrow, calm determined grey eyes. Open-faced steel helmet with a tall narrow crest fin, face fully visible. Heavy steel plate armor in dark steel with bronze trim, asymmetrical pauldrons (large spiked left, smaller right), deep teal-blue sleeves and a short teal surcoat to the knees, leather belt, steel greaves and boots. NO cape, NO weapon, NO shield. T-pose as established. Palette: dark steel, teal blue, bronze.
```

**Ополченец Эрланда:**
```
Character: Erland militia swordsman, about 35 years old. Face: round honest face, broad nose, short brown beard, tired kind eyes, no helmet visor. Simple open steel cap helmet with a short brim, face visible. Brown leather jerkin over a teal tunic, one steel pauldron on the left shoulder, leather bracers, brown trousers, sturdy boots. Hands empty, T-pose as established. Palette: leather brown, muted steel, teal.
```

**Стрелок Эрланда:**
```
Character: Erland crossbowman, about 24 years old, lean build but still chunky style. Face: narrow face, sharp green eyes, light freckles, no beard, face fully visible. Olive-green hood pushed back onto the shoulders (not covering the face) and a short green shoulder cloak ending above the waist, light leather armor, teal armband, belt with pouches, leather boots. Hands empty, T-pose. Palette: olive green, brown, teal accent.
```

**Налётчик Кочевников:**
```
Character: Dry Sea nomad raider, about 30 years old. Face: weathered tanned face, high cheekbones, thin black mustache, fierce dark eyes, face fully visible. Horned leather-and-iron helmet with an open face, fur-trimmed shoulders, brown leather chest plate with bone buckles, rust-red cloth wrap on the waist and a short split loincloth, fur-lined boots. Hands empty, T-pose. Palette: ochre, rust red, leather brown, bone.
```

**Лучник Кочевников:**
```
Character: Dry Sea nomad archer, about 22 years old. Face: young lean face, sharp amber eyes, small chin beard, a rust-red scarf pulled down around the neck (face fully visible). Pointed fur cap, light leather coat with ochre patterns, rust-red sash, quiver strap across the chest (no quiver visible on the back so the body stays clean), fur boots. Hands empty, T-pose. Palette: ochre, rust red, bone.
```

**Хан Кобек (босс):**
```
Character: Khan Kobek, nomad warlord, about 45 years old, 50% larger than normal soldiers, very broad and heavy build. Face: heavy brow, broken nose, long braided black beard with bone beads, cold pale eyes, face visible under an open horned iron helmet with a horsehair plume. Fur mantle over scale armor, dark rust red tunic, iron gauntlets and greaves, belt with bone and gold trophies. NO cape, NO weapon. T-pose as established. Palette: dark rust red, black iron, bone, a little gold.
```

## 3. Оружие и щиты (отдельные картинки; в стиле выше, но не персонажи)

Общая вставка для каждого предмета:
```
Prop concept, ONE object only, lying flat, side view (or front for shields), plain white background, no hand, no character, no shadow, no text. Same painted stylized chunky style as the characters. Keep the object straight and symmetric, orthographic.
```

Предметы:
- `Long straight knight sword: broad steel blade with a fuller, bronze crossguard, leather-wrapped grip, round pommel.`
- `Tall teal tower shield with rounded top, bronze rim, a white tower emblem with three battlements (front view).`
- `Simple militia short sword with a plain crossguard.`
- `Rectangular wooden shield with a teal stripe and iron rim (front view).`
- `Crossbow with a dark wooden stock and iron prod, string drawn.`
- `Nomad curved saber with a bronze guard and leather grip.`
- `Round wooden shield with a painted red sun emblem and an iron boss (front view).`
- `Short composite bow of dark wood with a bone-colored string.`
- `Huge two-handed bearded war axe with iron head, bone decorations and a long wooden haft.`

## 4. Что делаю я после генерации

- Персонаж из Hunyuan3D кладёте в `art/generated`, я привязываю к скелету KayKit (в T-позе веса получаются заметно лучше, чем из A-позы).
- Оружие и щит ставлю на кости рук (handslot) отдельными деталями. Их можно менять местами и поднимать в бой/в ножны.
- Размеры оружия подгоняю под руку, ориентацию хвата настраиваю в Blender.

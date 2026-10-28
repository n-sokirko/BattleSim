# Промпты для ChatGPT: интерфейс, иконки, курсоры

Делать по очереди: сначала **промпт 0** (единый стиль), потом в том же чате промпты 1–5. Каждый результат — на прозрачном фоне, PNG. Складывать в `art/generated/ui/`, дальше я режу на файлы и подключаю.

## Промпт 0 — стиль (отправить первым)

```
We are making the UI for a stylized fantasy RTS (Warcraft 3 style hero + small squad campaign, top-down camera, PC first, phone later). Hand-painted look, NOT pixel art, NOT flat vector. Chunky readable shapes that stay clear at 32 px.

Style bible, keep it for every image in this chat:
- Materials: dark walnut wood and aged bronze/brass. Panel fill is deep brown (#2a1c12 to #1a110b) with subtle worn texture. Frames are bronze (#b8864b) with a darker rim and a thin bright highlight on the top-left edge. Small rivets in corners.
- Accent colours: warm gold (#f0c860) for important things, cream parchment text (#efe3c4), red (#d6453a) for danger/attack, green (#6cc04a) for health, blue (#4aa3e0) for mana/magic.
- Factions: Erland = teal (#2f8f8a) with gold tower emblem; Nomads = rust red (#a8432a) and ochre with bone and horn trim.
- Light comes from the top-left. Soft dark outline around every icon so it reads on any background.
- No text, no letters, no numbers inside any image unless I ask.
- Every image: transparent background, centred, generous empty margin.

Confirm you understood, then wait for the next request.
```

## Промпт 1 — рамки и панели (одним листом)

```
Sheet of UI frame pieces in the style bible, on transparent background, each piece clearly separated:
1. Large bottom command panel frame (wide, 16:3), empty inside, bronze border with corner ornaments, space for a minimap on the left, a portrait square, a stats block, a 6-slot item grid, and a 4x2 button grid on the right.
2. Small panel frame (4:3) for tooltips and the objectives box.
3. Portrait frame, square, with a bronze ring and small banner clip at the bottom.
4. Empty square button, three states side by side: normal, hovered (brighter gold rim), pressed (darker, inset).
5. Empty round button, same three states.
6. Item slot, square, empty, inset look.
7. Horizontal bar frame, long and thin, empty inside (for health / experience bars), plus three matching fills: green, blue, gold, each with a soft gradient and a thin highlight on top.
8. Minimap frame, square, bronze with a small compass mark in a corner.
9. Selection ring for a friendly unit (green), an enemy unit (red), a neutral (yellow): flat ellipse seen from above at an angle, glowing thin line, transparent inside.
10. Top-left objectives panel header strip with a small scroll ornament.
Keep the same line weight and rivet design in all of them.
```

## Промпт 2 — иконки команд и способностей (сетка 4×4, по 256 px на иконку)

```
Icon sheet, 4 columns x 4 rows, each icon in its own square cell with a thin gap, in the style bible. Every icon sits on a small dark bronze-rimmed square plate with a coloured glow behind the symbol so it reads at 32 px. No text.
Row 1, commands: Move (boot with a speed trail), Stop (raised open hand on a shield), Hold position (shield planted in ground), Attack (two crossed swords).
Row 2, hero abilities: Battle cry (war horn with sound waves, gold), Ground slam (fist hitting cracked earth, orange), Taunt (roaring face of a warrior with red aura), Unbreakable (tower shield with blue glowing ward, blue).
Row 3, status: Poise / balance (a stone scale balancing), Stagger (a dizzy spiral over a helmet), Combo finisher (sword with three slash marks), Guard buff (shield with a glowing blue edge).
Row 4, stats: Damage (sword), Speed (winged boot), Health (red heart), Level (laurel wreath with a star).
```

## Промпт 3 — иконки интерфейса и предметов (сетка 4×4)

```
Same style and layout as the previous icon sheet.
Row 1, objectives: Boss skull (horned nomad skull with a red banner), Enemy camp (tent with a red flag), Treasure chest (open chest with gold glow), Quest scroll.
Row 2, items, rarity colours as the glow: Health potion (red flask), Mana potion (blue flask), Iron sword, Round wooden shield.
Row 3, items: Bow with quiver, Leather boots, Amulet on a chain, Key.
Row 4, UI: Pause (two bars), Menu (three lines), Camera focus on hero (crosshair with a small helmet), Group select (three small figures).
```

## Промпт 4 — курсоры (сетка 4×3, каждый 128 px, остриё указано)

```
Mouse cursor set for the same game, drawn large (128 px each) so I can shrink them to 32 px. Each cursor has a dark outline and a soft shadow, reads on both green grass and dark fog. Hand-painted bronze and gold, as in the style bible. In each cell mark the hotspot (the exact tip that points at the target) with a tiny red dot I will remove later.
Row 1: Default arrow (bronze gauntlet pointer), Hover over a friendly unit (green-tinted arrow with a small shield), Hover over an enemy (red sword pointing down-left, aggressive), Attack-move target (red crossed swords with a crosshair).
Row 2: Move command (green boot print and arrow), Pick up / open chest (golden open hand over a chest), Cannot go here / blocked (red ring with a slash across), Hold / stop (open palm).
Row 3: Ability target (blue crosshair circle with runic ticks), Ability target area (dashed blue circle with centre dot), Select box corner (thin gold bracket), Busy / waiting (small hourglass in bronze).
```

## Промпт 5 — туман, метки, всплывающие значки (необязательно)

```
Small effect sprites in the style bible, transparent background, each 256 px:
1. Move-order marker: green ring with an arrow tip, seen from above at an angle.
2. Attack-order marker: red ring with two crossed blades.
3. Pickup sparkle: golden glints around a point.
4. Floating damage badge shapes (no numbers): a small white-gold starburst and a red starburst for a critical hit.
5. Level-up burst: golden ring of light with rising sparks.
6. Stagger flash: pale yellow ring with a short shockwave arc.
7. Unexplored fog edge: soft dark-blue smoky border strip that tiles horizontally, fading to transparent.
8. Alert exclamation mark in a red circle (an enemy camp woke up).
```

## Что мне прислать назад
Листы целиком (PNG) без правок и по порядку 1–5. Если иконка вышла нечитаемой в 32 px, скажу, какую перегенерировать. Курсоры уменьшаю и режу сам, остриё беру по красной точке.

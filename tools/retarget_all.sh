#!/bin/sh
# Перенос клипов Mixamo на всех персонажей (запуск из корня репозитория, нужны Godot и собранные godot/assets/mx/*.glb)
cd godot
G=/o/Godot/Godot_v4.7.2-stable_win64_console.exe
R="--headless --path . --script scripts/retarget_mx.gd --"
SNS="sword_and_shield_idle sword_and_shield_walk sword_and_shield_run sword_and_shield_slash sword_and_shield_slash_2 sword_and_shield_slash_3 sword_and_shield_attack_4 sword_and_shield_impact sword_and_shield_death"
for ch in erland_militia nomad_raider; do $G $R pro_sns $ch $SNS; done
$G $R pro_sns armed_kael sword_and_shield_attack sword_and_shield_attack_2 sword_and_shield_attack_3 sword_and_shield_attack_4 sword_and_shield_slash sword_and_shield_slash_2 sword_and_shield_slash_3 sword_and_shield_slash_4 sword_and_shield_slash_5 sword_and_shield_kick sword_and_shield_power_up sword_and_shield_block sword_and_shield_block_idle sword_and_shield_impact sword_and_shield_impact_2 sword_and_shield_impact_3 sword_and_shield_death sword_and_shield_death_2 sword_and_shield_casting sword_and_shield_casting_2 sword_and_shield_idle sword_and_shield_idle_2
$G $R great_sword nomad_khan great_sword_idle great_sword_walk great_sword_run great_sword_slash great_sword_slash_2 great_sword_slash_3 great_sword_attack great_sword_impact two_handed_sword_death
BOW="standing_idle_01 standing_walk_forward standing_run_forward standing_draw_arrow standing_aim_overdraw standing_aim_recoil standing_react_small_from_front standing_death_forward_01"
$G $R longbow nomad_archer $BOW
$G $R longbow erland_archer standing_react_small_from_front
$G $R rifle erland_archer idle walk_forward run_forward firing_rifle death_from_the_front idle_aiming

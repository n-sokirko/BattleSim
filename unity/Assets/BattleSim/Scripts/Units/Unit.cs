using UnityEngine;

namespace BattleSim
{
    /// <summary>Как солдат был расставлен до боя (для кнопки "Реванш").</summary>
    public struct Placement
    {
        public UnitType Type;
        public int Team;
        public Vector3 Pos;
        public float Yaw;
        public int Variant;
    }

    /// <summary>Состояние одного солдата. Логика — в Battle, картинка — в UnitView.</summary>
    public class Unit
    {
        public Placement Plan;
        public UnitType Type;
        public UnitStats S;
        public int Team;

        public Vector3 Pos;
        public float Yaw;
        public Vector3 Vel;
        public Vector3 Knock;
        public float Hp;
        public bool Alive = true;

        public Unit Target;
        public float RetargetT;
        public float Cooldown;
        public float AttackT = -1f;
        public float AttackDur = 0.5f;
        public bool AttackIsShot;
        public bool HitDone;
        public bool Aiming;
        public float ChargeT;

        public float Phase;
        public float Move;
        public float Seed;
        public float Scale = 1f;
        public float DeadT;
        public float FallSign = 1f;

        public UnitView View;

        public Vector3 Forward => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;

        public void FaceTowards(Vector3 dir, float dt, float degPerSec = 540f)
        {
            if (dir.x * dir.x + dir.z * dir.z < 1e-6f) return;
            float target = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Yaw = Mathf.MoveTowardsAngle(Yaw, target, degPerSec * dt);
        }
    }

    /// <summary>Процедурная анимация: шаг, бег, галоп, удары, стрельба и падение.</summary>
    public static class UnitAnim
    {
        static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>Замах мечом: вверх-назад, резкий удар вниз, возврат.</summary>
        static float SwordArc(float a, float idle)
        {
            if (a < 0.4f) return Mathf.Lerp(idle, -160f, Ease(a / 0.4f));
            if (a < 0.58f) return Mathf.Lerp(-160f, -5f, (a - 0.4f) / 0.18f);
            return Mathf.Lerp(-5f, idle, Ease((a - 0.58f) / 0.42f));
        }

        /// <summary>Укол копьём: отвести назад, выпад вперёд, вернуть.</summary>
        static float Thrust(float a)
        {
            if (a < 0.35f) return Mathf.Lerp(0f, -0.25f, Ease(a / 0.35f));
            if (a < 0.5f) return Mathf.Lerp(-0.25f, 0.6f, (a - 0.35f) / 0.15f);
            return Mathf.Lerp(0.6f, 0f, Ease((a - 0.5f) / 0.5f));
        }

        public static void Pose(Unit u, float time)
        {
            var v = u.View;
            if (v == null) return;
            var b = v.Bones;
            var rig = v.Rig;
            Quaternion yawRot = Quaternion.Euler(0f, u.Yaw, 0f);

            if (!u.Alive)
            {
                float f = Ease(Mathf.Clamp01(u.DeadT / 0.55f));
                float sink = Mathf.Max(0f, u.DeadT - 20f) * 0.12f;
                if (rig.Horse)
                    v.Root.SetPositionAndRotation(u.Pos + Vector3.up * (0.32f * f - sink), yawRot * Quaternion.Euler(0f, 0f, 88f * f * u.FallSign));
                else
                    v.Root.SetPositionAndRotation(u.Pos + Vector3.up * (0.13f * f - sink), yawRot * Quaternion.Euler(86f * f * u.FallSign, 0f, 0f));
                return;
            }

            v.Root.SetPositionAndRotation(u.Pos, yawRot);
            float move = u.Move, ph = u.Phase;
            float breath = Mathf.Sin(time * 2.1f + u.Seed * 10f) * 0.008f;
            float atk = u.AttackT;

            if (rig.Horse)
            {
                float amp = 38f * move;
                b[rig.Legs[0]].localRotation = Quaternion.Euler(Mathf.Sin(ph) * amp, 0f, 0f);
                b[rig.Legs[1]].localRotation = Quaternion.Euler(Mathf.Sin(ph + 0.6f) * amp, 0f, 0f);
                b[rig.Legs[2]].localRotation = Quaternion.Euler(Mathf.Sin(ph + Mathf.PI) * amp, 0f, 0f);
                b[rig.Legs[3]].localRotation = Quaternion.Euler(Mathf.Sin(ph + Mathf.PI + 0.6f) * amp, 0f, 0f);
                b[rig.Body].localPosition = rig.LocalRest(rig.Body) + Vector3.up * (Mathf.Abs(Mathf.Sin(ph)) * 0.1f * move + breath);
                b[rig.Body].localRotation = Quaternion.Euler(Mathf.Sin(ph * 2f) * 3f * move, 0f, 0f);
                float arm = atk >= 0f ? SwordArc(atk, -25f) : -25f + Mathf.Sin(ph) * 6f * move;
                b[rig.ArmR].localRotation = Quaternion.Euler(arm, 0f, 0f);
                b[rig.ArmL].localRotation = Quaternion.Euler(-15f, 0f, 0f);
                return;
            }

            float swing = Mathf.Sin(ph) * 36f * move;
            b[rig.Legs[0]].localRotation = Quaternion.Euler(swing, 0f, 0f);
            b[rig.Legs[1]].localRotation = Quaternion.Euler(-swing, 0f, 0f);

            float lean = 8f * move;
            float twist = 0f;
            float armR = -15f + swing * 0.6f, armL = -25f - swing * 0.6f;
            Vector3 armROff = Vector3.zero;

            switch (u.Type)
            {
                case UnitType.Swordsman:
                    armL = -35f;
                    if (atk >= 0f)
                    {
                        armR = SwordArc(atk, -15f);
                        lean += atk > 0.4f && atk < 0.7f ? 12f : 0f;
                        twist = atk < 0.4f ? -15f * Ease(atk / 0.4f) : 0f;
                    }
                    break;
                case UnitType.Spearman:
                    armR = -12f;
                    armL = -55f;
                    if (atk >= 0f)
                    {
                        float t = Thrust(atk);
                        armROff = new Vector3(0f, 0f, t);
                        lean += t * 18f;
                    }
                    break;
                case UnitType.Archer:
                    if (atk >= 0f && !u.AttackIsShot)
                    {
                        armR = SwordArc(atk, -15f);
                    }
                    else if (u.Aiming || (atk >= 0f && u.AttackIsShot))
                    {
                        float draw = atk >= 0f && atk < 0.8f ? Ease(atk / 0.8f) : 0f;
                        armL = -88f;
                        armR = -88f + draw * 10f;
                        armROff = new Vector3(-0.12f * draw, 0f, -0.3f * draw);
                        twist = -20f;
                        lean = 0f;
                    }
                    break;
            }

            b[rig.Body].localPosition = rig.LocalRest(rig.Body) + Vector3.up * (Mathf.Abs(Mathf.Cos(ph)) * 0.06f * move + breath);
            b[rig.Body].localRotation = Quaternion.Euler(lean, twist, 0f);
            b[rig.ArmR].localPosition = rig.LocalRest(rig.ArmR) + armROff;
            b[rig.ArmR].localRotation = Quaternion.Euler(armR, 0f, 0f);
            b[rig.ArmL].localRotation = Quaternion.Euler(armL, 0f, 0f);
        }
    }
}

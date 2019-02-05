using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Mod_PhysicalShield
{
    [DefOf]
    public static class StatDefOf
    {
        public static StatDef BlockMax;
        public static StatDef StaminaRechargeRate;
    }

    [StaticConstructorOnStartup]
    public class PhysicalShield : Apparel
    {
        //variables
        public float Stamina;
        private Material shieldMat;
        private Vector3 impactAngleVect;
        private int ticksToReset = -1;
        private int lastKeepDisplayTick = -9999;
        private int lastAbsorbDamageTick = -9999;
        private readonly float StaminaOnReset = .2f;
        private readonly int KeepDisplayingTicks = 600;
        private readonly int StartingTicksToReset = 300;
        private float MeleeSkill => Wearer.skills.GetSkill(SkillDefOf.Melee).Level;

        //Durability damage done when guard is broken
        private readonly int DurabilityDamage = 3;
        //StaminaMax affected by quality of the shield, representing a sturdier build.
        private float StaminaMax => this.GetStatValue(StatDefOf.BlockMax);
        //StaminaGainPerTick affected by melee skill, representing stamina and the ability to raise the shield quicker.
        private float StaminaGainPerTick => (this.GetStatValue(StatDefOf.StaminaRechargeRate) + (MeleeSkill / 800f)) / 60f;
        //StaminaLossPerDamage is by default higher than the shieldbelt, but a pawn skilled in melee can reduce it further. 0melee = 5x, 20melee = 3x
        private float StaminaLossPerDamage => .05f - (MeleeSkill / 1000f);      
        //value used for equipment selection by raids
        private readonly float ApparelScorePerStaminaMax = 0.25f;
        public override float GetSpecialApparelScoreOffset()
        {
            return StaminaMax * ApparelScorePerStaminaMax;
        }

        //sounds
        private static readonly SoundDef SoundAbsorbDamage = SoundDef.Named("BulletImpact_Metal");
        private static readonly SoundDef SoundBreak = SoundDef.Named("MetalHitImportant");

        //check if sheild is active
        //0 = active
        //1 = resetting
        public ShieldState ShieldState
        {
            get
            {
                if (ticksToReset > 0)
                {
                    return ShieldState.Resetting;
                }
                return ShieldState.Active;
            }
        }

        //when stamina is depleted, cannot block again for a set time. Durability takes damage once guard is broken.
        //when depleted, stamina will be set to 0 and a timer will start before refreshing stamina.

        private void TakeDurabilityDamage()
        {
            HitPoints -= DurabilityDamage;
            if (HitPoints < 0f)
            {
                Destroy();
            }
        }

    private void Break()
        {
            SoundBreak.PlayOneShot(new TargetInfo(Wearer.Position, Wearer.Map));
            Stamina = 0f;
            ticksToReset = StartingTicksToReset;
            TakeDurabilityDamage();
        }

        private void Reset()
        {
            ticksToReset = -1;
            Stamina = StaminaOnReset;
        }

        //tick calculation
        public override void Tick()
        {
            base.Tick();
            if (Wearer == null || !UsingValidWeapon)
            {
                Stamina = 0f;
            }
            else if (ShieldState == ShieldState.Resetting)
            {
                ticksToReset--;
                if (ticksToReset <= 0)
                {
                    Reset();
                }
            }
            else if (ShieldState == ShieldState.Active)
            {
                Stamina += StaminaGainPerTick;
                if (Stamina > StaminaMax)
                {
                    Stamina = StaminaMax;
                }
            }
        }

        //shield will only provide cover when using a valid weapon
        //could be optimized with a better list to reference from
        private bool UsingValidWeapon
        {
            get
            {
                if (Wearer.equipment.Primary != null)
                {
                    if (Wearer.equipment.Primary.def.weaponTags.Contains("UsableWithShield"))
                    {
                        return true;
                    }
                    if (Wearer.equipment.Primary.def.IsRangedWeapon)
                    {
                        return false;
                    }
                    if (Wearer.equipment.Primary.def.weaponTags.Contains("NotUsableWithShield"))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        //when the shield is displayed on the pawn
        private bool ShouldDisplay
        {
            get
            {
                if (UsingValidWeapon)
                {
                    Pawn wearer = Wearer;
                    if (!Wearer.Spawned || Wearer.Dead || Wearer.Downed)
                    {
                        return false;
                    }
                    if (Wearer.InAggroMentalState)
                    {  
                       return true;
                    }
                    if (Wearer.CurJob != null && Wearer.CurJob.def == JobDefOf.AttackMelee)
                    {
                        return true;
                    }
                    if (Wearer.Drafted)
                    {
                        return true;
                    }
                    if (Wearer.Faction.HostileTo(Faction.OfPlayer) && !Wearer.IsPrisoner)
                    {
                        return true;
                    }
                    if (Find.TickManager.TicksGame < lastKeepDisplayTick + KeepDisplayingTicks)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        //saved data
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Stamina, "Stamina", 0f);
            Scribe_Values.Look(ref ticksToReset, "ticksToReset", -1);
            Scribe_Values.Look(ref lastKeepDisplayTick, "lastKeepDisplayTick", 0);
        }

        public void KeepDisplaying()
        {
            lastKeepDisplayTick = Find.TickManager.TicksGame;
        }

        //check if there is enough stamina to absorb damage. if so, shield takes damage
        public override bool CheckPreAbsorbDamage(DamageInfo dinfo)
        {
            //if the shield is down, shield does not absorb damage and pawn takes damage.
            //or if attack is an EMP, the EMP does not affect the shield.
            if (ShieldState != 0 || dinfo.Def == DamageDefOf.EMP)
            {
                return false;
            }
            //if stamina is below 0. Break the shield and start recharge timer. shield does not absorb damage.
            //otherwise, stamina > 0f, and stamina will take damage
            if (ShieldState == 0 && ShouldDisplay && dinfo.Instigator != null)
            {
                Stamina -= dinfo.Amount * StaminaLossPerDamage;
                MoteMaker.ThrowText(Wearer.DrawPos, Wearer.Map, "BLOCKED".Translate(), 3.65f);
                if (Stamina < 0f)
                {
                    Break();
                }
                else
                {
                    AbsorbedDamage(dinfo);
                    TakeDurabilityDamage();
                }
                return true;
            }
            return false;
        }

        private void AbsorbedDamage(DamageInfo dinfo)
        {
            PhysicalShield.SoundAbsorbDamage.PlayOneShot(new TargetInfo(Wearer.Position, Wearer.Map));
            impactAngleVect = Vector3Utility.HorizontalVectorFromAngle(dinfo.Angle);
            Vector3 vector = Wearer.TrueCenter() + impactAngleVect.RotatedBy(180f) * 0.5f;
            lastAbsorbDamageTick = Find.TickManager.TicksGame;
            KeepDisplaying();
        }

        public override void DrawWornExtras()
        {
            if (ShieldState == ShieldState.Active && ShouldDisplay)
            {
                float angle = 0f;
                Vector3 drawPos = Wearer.Drawer.DrawPos;
                Vector3 s = new Vector3(1f, 1f, 1f);
                if (Wearer.Rotation == Rot4.North)
                {
                    drawPos.y = AltitudeLayer.Item.AltitudeFor();
                    drawPos.x -= 0.2f;
                    drawPos.z -= 0.2f;
                }
                else if (Wearer.Rotation == Rot4.South)
                {
                    drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                    drawPos.y += 1f;
                    drawPos.x += 0.2f;
                    drawPos.z -= 0.2f;
                }
                else if (Wearer.Rotation == Rot4.East)
                {
                    drawPos.y = AltitudeLayer.Item.AltitudeFor();
                    drawPos.x += 0.2f;
                    drawPos.z -= 0.2f;
                    angle = 90f;
                }
                else if (Wearer.Rotation == Rot4.West)
                {
                    drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                    drawPos.z -= 0.2f;
                    angle = 270f;
                }
                shieldMat = MaterialPool.MatFrom(def.graphicData.texPath, ShaderDatabase.Cutout, Stuff.stuffProps.color);
                Matrix4x4 matrix = default(Matrix4x4);
                matrix.SetTRS(drawPos, Quaternion.AngleAxis(angle, Vector3.up), s);
                Graphics.DrawMesh(MeshPool.plane10, matrix, shieldMat, 0);
            }
        }

    public override IEnumerable<Gizmo> GetWornGizmos()
        {
            if (Find.Selector.SingleSelectedThing == Wearer && UsingValidWeapon)
            {
                yield return (Gizmo)new Gizmo_PhysicalShieldStatus
                {
                    shield = this
                };
                ;
            }
        }
        
        [StaticConstructorOnStartup]
        public class Gizmo_PhysicalShieldStatus : Gizmo
        {
            public PhysicalShield shield;
            private static readonly Texture2D FullShieldBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0f, 0.7f, 0.7f));
            private static readonly Texture2D EmptyShieldBarTex = SolidColorMaterials.NewSolidColorTexture(Color.clear);

            //position of the gizmo
            public Gizmo_PhysicalShieldStatus()
            {
                order = -100f;
            }

            public override float GetWidth(float maxWidth)
            {
                return 140f;
            }

            public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth)
            {
                Rect overRect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
                Find.WindowStack.ImmediateWindow(984688, overRect, WindowLayer.GameUI, delegate
                {
                    Rect rect = overRect.AtZero().ContractedBy(6f);
                    Rect rect2 = rect;
                    rect2.height = overRect.height / 2f;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(rect2, shield.LabelCap);
                    Rect rect3 = rect;
                    rect3.yMin = overRect.height / 2f;
                    float fillPercent = shield.Stamina / Mathf.Max(1f, shield.GetStatValue(StatDefOf.BlockMax));
                    Widgets.FillableBar(rect3, fillPercent, FullShieldBarTex, EmptyShieldBarTex, doBorder: false);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(rect3, (shield.Stamina * 100f).ToString("F0") + " / " + (shield.GetStatValue(StatDefOf.BlockMax) * 100f).ToString("F0"));
                    Text.Anchor = TextAnchor.UpperLeft;
                });
                return new GizmoResult(GizmoState.Clear);
            }
        }
    }
}
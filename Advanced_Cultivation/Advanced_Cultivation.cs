﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace Advanced_Cultivation
{
    [DefOf]
    public static class ThingDefOf
    {
        public static ThingDef AC_RawCompost;
        public static ThingDef AC_FermentedCompost;
        public static ThingDef AC_CompostBin;
        public static ThingDef AC_CompostBinRaw;
        public static ThingDef AC_CompostBinFermented;
    }

    [DefOf]
    public static class JobDefOf
    {
        public static JobDef AC_EmptyCompostBin;
        public static JobDef AC_FillCompostBin;
    }
    
    public class Building_AC_CompostBin : Building
    {
        // These two are just to change the appearance of the building when it has items in it.
        private Thing CompostBinRaw
        {
            get
            {
                return ThingMaker.MakeThing(ThingDefOf.AC_CompostBinRaw, null);
            }
        }

        private Thing CompostBinFermented
        {
            get
            {
                return ThingMaker.MakeThing(ThingDefOf.AC_CompostBinFermented, null);
            }
        }

        private float progressInt;
        private int compostCount;
        private Material barFilledCachedMat;
        public const int Capacity = 25;
        private const int BaseFermentationDuration = 600000;
        public const float minFermentTemperature = 10f;
        public const float maxFermentTemperature = 50f;
        private static readonly Vector2 BarSize = new Vector2(0.55f, 0.1f);
        private static readonly Color BarZeroProgressColor = new Color(0.4f, 0.27f, 0.22f);
        private static readonly Color BarFermentedColor = new Color(0.9f, 0.6f, 0.2f);
        private static readonly Material BarUnfilledMat = SolidColorMaterials.SimpleSolidColorMaterial(
            new Color(0.3f, 0.3f, 0.3f), false);

        public float Progress
        {
            get
            {
                return this.progressInt;
            }
            set
            {
                if (value == this.progressInt)
                {
                    return;
                }
                this.progressInt = value;
                this.barFilledCachedMat = null;
            }
        }

        private Material BarFilledMat
        {
            get
            {
                if (this.barFilledCachedMat == null)
                {
                    this.barFilledCachedMat = SolidColorMaterials.SimpleSolidColorMaterial(Color.Lerp
                        (Building_AC_CompostBin.BarZeroProgressColor,
                        Building_AC_CompostBin.BarFermentedColor, this.Progress), false);
                }
                return this.barFilledCachedMat;
            }
        }

        public int SpaceLeftForCompost
        {
            get
            {
                if (this.Fermented)
                {
                    return 0;
                }
                return Building_AC_CompostBin.Capacity - this.compostCount;
            }
        }

        private bool Empty
        {
            get
            {
                return this.compostCount <= 0;
            }
        }

        public bool Fermented
        {
            get
            {
                return !this.Empty && this.Progress >= 1f;
            }
        }

        private float CurrentTempProgressSpeedFactor
        {
            get
            {
                CompProperties_TemperatureRuinable compProperties =
                    this.def.GetCompProperties<CompProperties_TemperatureRuinable>();
                float ambientTemperature = base.AmbientTemperature;
                if (ambientTemperature <= compProperties.minSafeTemperature)
                {
                    return 0.0f;
                }
                if (ambientTemperature <= Building_AC_CompostBin.minFermentTemperature)
                {
                    return GenMath.LerpDouble(compProperties.minSafeTemperature,
                        Building_AC_CompostBin.minFermentTemperature,
                        0.0f, 1f, ambientTemperature);
                }
                if (ambientTemperature <= Building_AC_CompostBin.maxFermentTemperature)
                {
                    return 1.0f;
                }
                if (ambientTemperature <= compProperties.maxSafeTemperature)
                {
                    return GenMath.LerpDouble(Building_AC_CompostBin.maxFermentTemperature,
                        compProperties.maxSafeTemperature,
                        1f, 0.0f, ambientTemperature);
                }
                return 0.0f;
            }
        }

        private float ProgressPerTickAtCurrentTemp
        {
            get
            {
                return 5.55555555E-6f * this.CurrentTempProgressSpeedFactor;
            }
        }

        private int EstimatedTicksLeft
        {
            get
            {
                return Mathf.Max(Mathf.RoundToInt((1f - this.Progress) /
                    this.ProgressPerTickAtCurrentTemp), 0);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref this.compostCount, "compostCount", 0, false);
            Scribe_Values.Look<float>(ref this.progressInt, "progress", 0, false);
        }

        public override void TickRare()
        {
            base.TickRare();
            if (!this.Empty)
            {
                this.Progress = Mathf.Min(this.Progress + 250f * this.ProgressPerTickAtCurrentTemp, 1f);
            }
        }

        public void AddCompost(int count)
        {
            base.GetComp<CompTemperatureRuinable>().Reset();
            if (this.Fermented)
            {
                Log.Warning("Tried to add compost to a bin with fermented compost in it. " +
                    "Colonists should harvest the bin first.");
                return;
            }
            int numToAdd = Mathf.Min(count, this.SpaceLeftForCompost);
            if (numToAdd <= 0)
            {
                return;
            }
            this.Progress = GenMath.WeightedAverage(0f, (float)numToAdd, this.Progress, (float)this.compostCount);
            this.compostCount += numToAdd;
        }

        protected override void ReceiveCompSignal(string signal)
        {
            if (signal == "RuinedByTemperature")
            {
                this.Reset();
            }
        }

        private void Reset()
        {
            this.compostCount = 0;
            this.Progress = 0f;
        }

        public void AddCompost(Thing compost)
        {
            int numToAdd = Mathf.Min(compost.stackCount, this.SpaceLeftForCompost);
            if (numToAdd > 0)
            {
                this.AddCompost(numToAdd);
                compost.SplitOff(numToAdd).Destroy(DestroyMode.Vanish);
            }
        }

        public override string GetInspectString()
        {
            CompProperties_TemperatureRuinable compProperties =
                this.def.GetCompProperties<CompProperties_TemperatureRuinable>();
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(base.GetInspectString());
            if (stringBuilder.Length != 0)
            {
                stringBuilder.AppendLine();
            }
            CompTemperatureRuinable comp = base.GetComp<CompTemperatureRuinable>();
            if (!this.Empty && !comp.Ruined)
            {
                if (this.Fermented)
                {
                    stringBuilder.AppendLine("AC.ContainsFermentedCompost".Translate(new object[]
                    {
                        this.compostCount,
                        Building_AC_CompostBin.Capacity
                    }));
                }
                else
                {
                    stringBuilder.AppendLine("AC.ContainsRawCompost".Translate(new object[]
                    {
                        this.compostCount,
                        Building_AC_CompostBin.Capacity
                    }));
                }
            }
            if (!this.Empty)
            {
                if (this.Fermented)
                {
                    stringBuilder.AppendLine("AC.Fermented".Translate());
                }
                else
                {
                    stringBuilder.AppendLine("AC.FermentationProgress".Translate(new object[]
                    {
                        this.Progress.ToStringPercent(),
                        this.EstimatedTicksLeft.ToStringTicksToPeriod(true, false, true)
                    }));
                    if (this.CurrentTempProgressSpeedFactor != 1f)
                    {
                        stringBuilder.AppendLine("AC.OutOfTemp".Translate(new object[]
                        {
                            this.CurrentTempProgressSpeedFactor.ToStringPercent()
                        }));
                    }
                }
                if (comp.Ruined)
                {

                }
            }
            stringBuilder.AppendLine("AC.Temp".Translate() + ": " +
                base.AmbientTemperature.ToStringTemperature("F0"));
            stringBuilder.AppendLine(string.Concat(new string[]
            {
                "AC.IdealTemp".Translate(),
                ": ",
                Building_AC_CompostBin.minFermentTemperature.ToStringTemperature("F0"),
                "-",
                Building_AC_CompostBin.maxFermentTemperature.ToStringTemperature("F0")
            }));
            return stringBuilder.ToString().TrimEndNewlines();
        }

        public Thing TakeOutCompost()
        {
            if (!this.Fermented)
            {
                Log.Warning("AC.NotReady".Translate());
                return null;
            }
            Thing thing = ThingMaker.MakeThing(ThingDefOf.AC_FermentedCompost, null);
            thing.stackCount = this.compostCount;
            this.Reset();
            return thing;
        }

        [DebuggerHidden]
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }
            if (Prefs.DevMode && !this.Empty)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Debug: Set progress to 1",
                    action = delegate ()
                    {
                        this.Progress = 1f;
                    }
                };
            }
            yield break;
        }

        public override void DrawAt(Vector3 drawLoc, bool flip = false )
        {
            if (this.Empty)
            {
                this.Graphic.Draw(drawLoc, this.Rotation, this, 0f);
            }
            if (this.Progress < 1f)
            {
                this.Graphic.Draw(drawLoc, this.Rotation, this.CompostBinRaw, 0f);
            }
            else
            {
                this.Graphic.Draw(drawLoc, this.Rotation, this.CompostBinFermented, 0f);
            }
        }

        public override void Draw()
        {
            base.Draw();
            if (!this.Empty)
            {
                Vector3 drawPos = this.DrawPos;
                drawPos.y += 0.05f;
                drawPos.z += 0.25f;
                GenDraw.DrawFillableBar(new GenDraw.FillableBarRequest
                {
                    center = drawPos,
                    size = Building_AC_CompostBin.BarSize,
                    fillPercent = (float)this.compostCount / 25f,
                    filledMat = this.BarFilledMat,
                    unfilledMat = Building_AC_CompostBin.BarUnfilledMat,
                    margin = 0.1f,
                    rotation = Rot4.North
                });
            }
        }
    }

    public class WorkGiver_FillCompostBin : WorkGiver_Scanner
    {
        private static string TemperatureTrans = "AC.BadTemp".Translate();
        private static string NoCompostTrans = "AC.NoRawCompost".Translate();

        public override ThingRequest PotentialWorkThingRequest
        {
            get
            {
                return ThingRequest.ForDef(ThingDefOf.AC_CompostBin);
            }
        }

        public override PathEndMode PathEndMode
        {
            get
            {
                return PathEndMode.Touch;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_AC_CompostBin CompostBin = t as Building_AC_CompostBin;
            if (CompostBin == null ||
                CompostBin.Fermented ||
                CompostBin.SpaceLeftForCompost <= 0)
            {
                return false;
            }
            float temperature = CompostBin.Position.GetTemperature(CompostBin.Map);
            CompProperties_TemperatureRuinable compProperties = CompostBin.def.GetCompProperties<CompProperties_TemperatureRuinable>();
            if (temperature < compProperties.minSafeTemperature || temperature > compProperties.maxSafeTemperature)
            {
                JobFailReason.Is(WorkGiver_FillCompostBin.TemperatureTrans);
                return false;
            }
            if (t.IsForbidden(pawn) ||
                !pawn.CanReserveAndReach(t, PathEndMode.Touch, pawn.NormalMaxDanger(), 1))
            {
                return false;
            }
            if (pawn.Map.designationManager.DesignationOn(t, DesignationDefOf.Deconstruct) != null)
            {
                return false;
            }
            if (this.FindCompost(pawn, CompostBin) == null)
            {
                JobFailReason.Is(WorkGiver_FillCompostBin.NoCompostTrans);
                return false;
            }
            return !t.IsBurning();
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_AC_CompostBin CompostBin = t as Building_AC_CompostBin;
            Thing t2 = this.FindCompost(pawn, CompostBin);
            return new Job(JobDefOf.AC_FillCompostBin, t, t2)
            {
                count = CompostBin.SpaceLeftForCompost
            };
        }

        private Thing FindCompost(Pawn pawn, Building_AC_CompostBin bin)
        {
            Predicate<Thing> predicate = (Thing x) => !x.IsForbidden(pawn) && pawn.CanReserve(x, 1);
            Predicate<Thing> validator = predicate;
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(ThingDefOf.AC_RawCompost),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false),
                9999f,
                validator,
                null,
                0,
                -1,
                false);
        }
    }

    public class WorkGiver_EmptyCompostBin : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest
        {
            get
            {
                return ThingRequest.ForDef(ThingDefOf.AC_CompostBin);
            }
        }
        public override PathEndMode PathEndMode
        {
            get
            {
                return PathEndMode.Touch;
            }
        }
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_AC_CompostBin CompostBin = t as Building_AC_CompostBin;
            return CompostBin != null &&
                CompostBin.Fermented &&
                !t.IsBurning() &&
                !t.IsForbidden(pawn) &&
                pawn.CanReserveAndReach(t, PathEndMode.Touch, pawn.NormalMaxDanger(), 1);
        }
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return new Job(JobDefOf.AC_EmptyCompostBin, t);
        }
    }

    public class JobDriver_FillCompostBin : JobDriver
    {
        protected Building_AC_CompostBin CompostBin
        {
            get
            {
                return (Building_AC_CompostBin)this.job.GetTarget(TargetIndex.A).Thing;
            }
        }
        protected Thing RawCompost
        {
            get
            {
                return this.job.GetTarget(TargetIndex.B).Thing;
            }
        }
        public override bool TryMakePreToilReservations()
        {
            return this.pawn.Reserve(this.CompostBin, this.job, 1, -1, null) &&
                this.pawn.Reserve(this.RawCompost, this.job, 1, -1, null);
        }
        [DebuggerHidden]
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            base.AddEndCondition(() => (this.CompostBin.SpaceLeftForCompost > 0) ? JobCondition.Ongoing : JobCondition.Succeeded);
            yield return Toils_General.DoAtomic(delegate {
                this.job.count = this.CompostBin.SpaceLeftForCompost;
            });
            Toil reserveRawCompost = Toils_Reserve.Reserve(TargetIndex.B, 1, -1, null);
            yield return reserveRawCompost;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TargetIndex.B).FailOnSomeonePhysicallyInteracting(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, true, false).FailOnDestroyedNullOrForbidden(TargetIndex.B);
            yield return Toils_Haul.CheckForGetOpportunityDuplicate(reserveRawCompost, TargetIndex.B, TargetIndex.None, true, null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(200).FailOnDestroyedNullOrForbidden(TargetIndex.B).FailOnDestroyedNullOrForbidden(TargetIndex.A).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch).WithProgressBarToilDelay(TargetIndex.A, false, -0.5f);
            yield return new Toil
            {
                initAction = delegate {
                    this.CompostBin.AddCompost(this.RawCompost);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public class JobDriver_EmptyCompostBin : JobDriver
    {
        protected Building_AC_CompostBin CompostBin
        {
            get
            {
                return (Building_AC_CompostBin)this.job.GetTarget(TargetIndex.A).Thing;
            }
        }
        protected Thing FermentedCompost
        {
            get
            {
                return this.job.GetTarget(TargetIndex.B).Thing;
            }
        }
        public override bool TryMakePreToilReservations()
        {
            return this.pawn.Reserve(this.CompostBin, this.job, 1, -1, null);
        }
        [DebuggerHidden]
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(200).FailOnDestroyedNullOrForbidden(TargetIndex.A).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch).FailOn(() => !this.CompostBin.Fermented).WithProgressBarToilDelay(TargetIndex.A, false, -0.5f);
            yield return new Toil
            {
                initAction = delegate {
                    Thing thing = this.CompostBin.TakeOutCompost();
                    GenPlace.TryPlaceThing(thing, this.pawn.Position, this.Map, ThingPlaceMode.Near, null);
                    StoragePriority currentPriority = HaulAIUtility.StoragePriorityAtFor(thing.Position, thing);
                    IntVec3 c;
                    if (StoreUtility.TryFindBestBetterStoreCellFor(thing, this.pawn, this.Map, currentPriority, this.pawn.Faction, out c, true))
                    {
                        this.job.SetTarget(TargetIndex.C, c);
                        this.job.SetTarget(TargetIndex.B, thing);
                        this.job.count = thing.stackCount;
                    }
                    else
                    {
                        this.EndJobWith(JobCondition.Incompletable);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return Toils_Reserve.Reserve(TargetIndex.B, 1, -1, null);
            yield return Toils_Reserve.Reserve(TargetIndex.C, 1, -1, null);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, false, false);
            Toil carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.C);
            yield return carryToCell;
            yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.C, carryToCell, true);
        }
    }
}

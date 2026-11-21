using RimWorld;
using Verse;
using Verse.AI;

namespace SmartCQB
{
    /// <summary>
    /// Botão direito numa arma corpo a corpo no chão: "Equipar [arma] como secundária".
    /// O colono vai buscar a arma, guarda no inventário e ela passa a ser a secundária dele.
    /// (O RimWorld 1.6 encontra todas as subclasses de FloatMenuOptionProvider sozinho.)
    /// </summary>
    public class FloatMenuOptionProvider_EquipSecondary : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            ThingWithComps weapon = clickedThing as ThingWithComps;
            if (weapon == null || !weapon.def.IsMeleeWeapon) return null;

            Pawn pawn = context.FirstSelectedPawn;
            if (pawn == null || !pawn.RaceProps.Humanlike || pawn.inventory == null || pawn.equipment == null) return null;

            string label = "SmartCQB_EquipSecondary".Translate(weapon.LabelShort);

            if (!EquipmentUtility.CanEquip(weapon, pawn, out string cantReason))
                return Disabled(label, cantReason);

            if (weapon.IsForbidden(pawn))
                return Disabled(label, "SmartCQB_Forbidden".Translate());

            if (!pawn.CanReach(weapon, PathEndMode.ClosestTouch, Danger.Deadly))
                return Disabled(label, "SmartCQB_NoPath".Translate());

            return new FloatMenuOption(label, () =>
            {
                Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, weapon);
                job.count = 1;
                if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                    SmartCQB_GameComponent.Instance?.Designate(pawn, weapon);
            });
        }

        private static FloatMenuOption Disabled(string label, string reason)
        {
            // Ação nula = opção desativada (acinzentada) no menu.
            return new FloatMenuOption(label + " (" + reason + ")", null);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SmartCQB
{
    /// <summary>
    /// A arma corpo a corpo que o jogador designou como secundária de um colono.
    /// A designação é persistente: sobrevive à troca de armas e ao "descarregar inventário".
    /// </summary>
    public class SecondaryRecord : IExposable
    {
        public Pawn pawn;
        public Thing secondary;     // a arma melee designada
        public Thing mainWeapon;    // arma principal guardada no inventário enquanto a melee está na mão (pode ser null)
        public bool engaged;        // true = a melee está na mão agora
        public bool everCarried;    // true = a melee já chegou ao inventário (a designação deixa de ser "pendente")
        public int calmTicks;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref secondary, "secondary");
            Scribe_References.Look(ref mainWeapon, "mainWeapon");
            Scribe_Values.Look(ref engaged, "engaged");
            Scribe_Values.Look(ref everCarried, "everCarried");
            Scribe_Values.Look(ref calmTicks, "calmTicks");
        }
    }

    public class SmartCQB_GameComponent : GameComponent
    {
        private const int CheckInterval = 15; // ticks entre verificações (~0,25 s)

        private List<SecondaryRecord> records = new List<SecondaryRecord>();

        public static SmartCQB_GameComponent Instance => Current.Game?.GetComponent<SmartCQB_GameComponent>();

        public SmartCQB_GameComponent(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null) records = new List<SecondaryRecord>();
                records.RemoveAll(r => r == null || r.pawn == null || r.secondary == null);
            }
        }

        // ------------------------------------------------------------------
        // API usada pelo menu do botão direito e pelo patch do Harmony
        // ------------------------------------------------------------------

        /// <summary>Define a arma como secundária do colono (substitui a anterior, se houver).</summary>
        public void Designate(Pawn pawn, Thing weapon)
        {
            SecondaryRecord old = GetRecord(pawn);
            if (old != null)
            {
                if (old.engaged) Disengage(pawn, old); // devolve a arma principal à mão antes de trocar a designação
                records.Remove(old);
            }

            records.Add(new SecondaryRecord { pawn = pawn, secondary = weapon });
        }

        /// <summary>True se o item não pode ser descarregado do inventário deste colono.</summary>
        public bool IsProtected(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null) return false;
            SecondaryRecord rec = GetRecord(pawn);
            if (rec == null) return false;
            return thing == rec.secondary || (rec.engaged && thing == rec.mainWeapon);
        }

        private SecondaryRecord GetRecord(Pawn pawn)
        {
            for (int i = 0; i < records.Count; i++)
                if (records[i].pawn == pawn) return records[i];
            return null;
        }

        // ------------------------------------------------------------------
        // Loop principal
        // ------------------------------------------------------------------

        public override void GameComponentTick()
        {
            if (records.Count == 0) return;
            if (Find.TickManager.TicksGame % CheckInterval != 0) return;

            var settings = SmartCQB_Mod.Settings;
            if (settings == null) return;

            var threatsByMap = new Dictionary<Map, List<Pawn>>();

            foreach (SecondaryRecord rec in records.ToList())
            {
                Pawn pawn = rec.pawn;
                if (pawn == null || pawn.Dead || pawn.Discarded || rec.secondary == null || rec.secondary.Destroyed)
                {
                    records.Remove(rec);
                    continue;
                }
                if (!pawn.Spawned || pawn.equipment == null || pawn.inventory == null) continue;

                if (!threatsByMap.TryGetValue(pawn.Map, out List<Pawn> threats))
                {
                    threats = GetThreats(pawn.Map);
                    threatsByMap[pawn.Map] = threats;
                }

                ProcessRecord(pawn, rec, threats, settings);
            }
        }

        private static List<Pawn> GetThreats(Map map)
        {
            List<Pawn> threats = null;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || p.Downed) continue;
                if (!p.HostileTo(Faction.OfPlayer)) continue;
                if (threats == null) threats = new List<Pawn>();
                threats.Add(p);
            }
            return threats;
        }

        private void ProcessRecord(Pawn pawn, SecondaryRecord rec, List<Pawn> threats, SmartCQB_Settings settings)
        {
            Thing sec = rec.secondary;
            bool inInventory = pawn.inventory.innerContainer.Contains(sec);
            bool inHand = pawn.equipment.Primary == sec;

            if (!rec.engaged)
            {
                if (inInventory || inHand)
                {
                    rec.everCarried = true;
                }
                else
                {
                    // Ainda a caminho de pegar a arma? Senão, a designação acabou (arma largada, perdida...).
                    if (rec.everCarried || !IsPickupPending(pawn, sec))
                        records.Remove(rec);
                    return;
                }

                if (inInventory)
                    TryEngage(pawn, rec, threats, settings);
                return;
            }

            // Engajado: a melee deveria estar na mão.
            if (!inHand)
            {
                // O jogador (ou outro mod) mexeu na arma: abandona a troca, mas mantém a designação.
                rec.engaged = false;
                rec.mainWeapon = null;
                rec.calmTicks = 0;
                return;
            }

            if (HasEnemyWithin(pawn, threats, settings.EffectiveRelease, requireSight: false))
            {
                rec.calmTicks = 0;
                return;
            }

            rec.calmTicks += CheckInterval;
            if (rec.calmTicks >= settings.calmSeconds * 60f)
                Disengage(pawn, rec);
        }

        private static bool IsPickupPending(Pawn pawn, Thing weapon)
        {
            Job cur = pawn.CurJob;
            if (cur != null && cur.def == JobDefOf.TakeInventory && cur.targetA.Thing == weapon)
                return true;

            if (pawn.jobs?.jobQueue != null)
            {
                foreach (QueuedJob q in pawn.jobs.jobQueue)
                    if (q.job != null && q.job.def == JobDefOf.TakeInventory && q.job.targetA.Thing == weapon)
                        return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Troca de armas
        // ------------------------------------------------------------------

        private void TryEngage(Pawn pawn, SecondaryRecord rec, List<Pawn> threats, SmartCQB_Settings settings)
        {
            if (threats == null) return;
            if (settings.onlyDrafted && !pawn.Drafted) return;
            if (pawn.Downed || pawn.InMentalState) return;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return;

            ThingWithComps melee = rec.secondary as ThingWithComps;
            if (melee == null || !EquipmentUtility.CanEquip(melee, pawn, out _)) return;

            ThingWithComps primary = pawn.equipment.Primary;
            if (primary != null && primary.def.IsMeleeWeapon) return; // já está de melee

            if (!HasEnemyWithin(pawn, threats, settings.triggerDistance, requireSight: true)) return;

            ThingOwner inv = pawn.inventory.innerContainer;

            // 1) Arma principal sai da mão e vai para o inventário (não cai no chão).
            if (primary != null && !pawn.equipment.TryTransferEquipmentToContainer(primary, inv))
                return;

            // 2) A melee secundária sai do inventário e é equipada.
            inv.Remove(melee);
            pawn.equipment.AddEquipment(melee);

            rec.mainWeapon = primary;
            rec.engaged = true;
            rec.calmTicks = 0;

            RefreshAttackJob(pawn);
        }

        private void Disengage(Pawn pawn, SecondaryRecord rec)
        {
            ThingOwner inv = pawn.inventory.innerContainer;
            ThingWithComps main = rec.mainWeapon as ThingWithComps;
            ThingWithComps melee = rec.secondary as ThingWithComps;

            // Se a arma principal sumiu do inventário, mantém a melee na mão (nunca deixa o colono desarmado).
            if (main != null && !inv.Contains(main))
            {
                rec.engaged = false;
                rec.mainWeapon = null;
                rec.calmTicks = 0;
                return;
            }

            if (melee != null && !pawn.equipment.TryTransferEquipmentToContainer(melee, inv))
                return;

            if (main != null)
            {
                inv.Remove(main);
                pawn.equipment.AddEquipment(main);
            }

            rec.engaged = false;
            rec.mainWeapon = null;
            rec.calmTicks = 0;

            RefreshAttackJob(pawn);
        }

        private static bool HasEnemyWithin(Pawn pawn, List<Pawn> threats, float distance, bool requireSight)
        {
            if (threats == null) return false;
            float maxSq = distance * distance;
            foreach (Pawn enemy in threats)
            {
                if (enemy.Dead || enemy.Downed || !enemy.Spawned || enemy.Map != pawn.Map) continue;
                if ((enemy.Position - pawn.Position).LengthHorizontalSquared > maxSq) continue;
                if (requireSight && !GenSight.LineOfSight(pawn.Position, enemy.Position, pawn.Map)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Se o colono estava no meio de um ataque, reinicia o job para usar o verbo da arma nova.</summary>
        private static void RefreshAttackJob(Pawn pawn)
        {
            Job job = pawn.CurJob;
            if (job != null && job.verbToUse != null)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true, false);
        }
    }
}

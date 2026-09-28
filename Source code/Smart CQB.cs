using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoMeleeSwitch
{
    // ==========================================
    // 1. INICIALIZAÇÃO E INJEÇÃO DO HARMONY/COMP
    // ==========================================
    [StaticConstructorOnStartup]
    public static class Patcher
    {
        static Patcher()
        {
            // Inicializa o Harmony
            var harmony = new Harmony("com.seunome.automeleeswitch");
            harmony.PatchAll();

            // Injeta o Componente de Memória em todas as raças humanas na inicialização
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race != null && def.race.Humanlike)
                {
                    def.comps.Add(new CompProperties_WeaponMemory());
                }
            }
        }
    }

    // ==========================================
    // 2. COMPONENTE DE MEMÓRIA (THINGCOMP)
    // ==========================================
    public class CompWeaponMemory : ThingComp
    {
        public ThingWithComps previousRangedWeapon;

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Salva a referência da arma de fogo original no save do jogo
            Scribe_References.Look(ref previousRangedWeapon, "previousRangedWeapon");
        }
    }

    public class CompProperties_WeaponMemory : CompProperties
    {
        public CompProperties_WeaponMemory()
        {
            this.compClass = typeof(CompWeaponMemory);
        }
    }

    // ==========================================
    // 3. PATCH DO HARMONY (LÓGICA PRINCIPAL)
    // ==========================================
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "EquipmentTrackerTick")]
    public static class AutoSwitch_Patch
    {
        public static void Postfix(Pawn_EquipmentTracker __instance)
        {
            Pawn pawn = __instance.pawn;

            // Filtros básicos de performance: apenas colonos recrutados, vivos, no mapa, a cada 30 ticks
            if (pawn == null || !pawn.IsColonist || !pawn.Drafted || !pawn.Spawned || pawn.Dead) return;
            if (!pawn.IsHashIntervalTick(30)) return;

            // Recupera a memória do colono
            var memoryComp = pawn.GetComp<CompWeaponMemory>();
            if (memoryComp == null) return;

            // Failsafe 1: Limpeza por estado de incapacidade ou perda de controle
            if (pawn.Downed || pawn.InMentalState)
            {
                memoryComp.previousRangedWeapon = null;
                return; // Interrompe a execução tática
            }

            // Failsafe 2: Validação física da arma salva na memória
            if (memoryComp.previousRangedWeapon != null)
            {
                if (memoryComp.previousRangedWeapon.Destroyed ||
                    memoryComp.previousRangedWeapon.Spawned ||
                    !pawn.inventory.innerContainer.Contains(memoryComp.previousRangedWeapon))
                {
                    memoryComp.previousRangedWeapon = null;
                }
            }

            bool enemyInMeleeRange = false;

            // Checa as 8 células adjacentes por inimigos vivos
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(pawn))
            {
                if (cell.InBounds(pawn.Map))
                {
                    var things = cell.GetThingList(pawn.Map);
                    foreach (Thing thing in things)
                    {
                        if (thing is Pawn otherPawn && otherPawn.HostileTo(pawn) && !otherPawn.Downed)
                        {
                            enemyInMeleeRange = true;
                            break;
                        }
                    }
                }
                if (enemyInMeleeRange) break;
            }

            // AÇÃO: Troca para Melee (Inimigo Próximo)
            if (enemyInMeleeRange && __instance.Primary != null && __instance.Primary.def.IsRangedWeapon)
            {
                // Grava a arma de fogo atual na memória
                memoryComp.previousRangedWeapon = __instance.Primary;

                // Executa a troca
                EquipMeleeFromInventory(pawn, __instance);
            }
            // AÇÃO: Troca para Ranged (Inimigo Afastado, Caído ou Morto)
            else if (!enemyInMeleeRange && __instance.Primary != null && __instance.Primary.def.IsMeleeWeapon)
            {
                // Verifica se há algo na memória (já validado pelos failsafes)
                if (memoryComp.previousRangedWeapon != null)
                {
                    EquipRangedFromInventory(pawn, __instance, memoryComp.previousRangedWeapon);

                    // Limpa a memória após reequipar a arma original
                    memoryComp.previousRangedWeapon = null;
                }
            }
        }

        private static void EquipMeleeFromInventory(Pawn pawn, Pawn_EquipmentTracker eqTracker)
        {
            // Busca a primeira arma melee no inventário e já garante que é um ThingWithComps
            ThingWithComps meleeWeapon = pawn.inventory.innerContainer.OfType<ThingWithComps>().FirstOrDefault(t => t.def.IsMeleeWeapon);

            if (meleeWeapon != null)
            {
                ThingWithComps currentRanged = eqTracker.Primary;

                // Remove a arma de fogo da mão e joga direto no container interno (sem dropar no chão)
                if (currentRanged != null)
                {
                    eqTracker.Remove(currentRanged);
                    pawn.inventory.innerContainer.TryAdd(currentRanged);
                }

                // Tira a arma melee do inventário e a equipa
                pawn.inventory.innerContainer.Remove(meleeWeapon);
                eqTracker.AddEquipment(meleeWeapon);
            }
        }

        private static void EquipRangedFromInventory(Pawn pawn, Pawn_EquipmentTracker eqTracker, ThingWithComps targetWeapon)
        {
            // Verifica se a arma salva está de fato no inventário
            if (pawn.inventory.innerContainer.Contains(targetWeapon))
            {
                ThingWithComps currentMelee = eqTracker.Primary;

                // Remove a arma corpo a corpo da mão e devolve direto pro container interno
                if (currentMelee != null)
                {
                    eqTracker.Remove(currentMelee);
                    pawn.inventory.innerContainer.TryAdd(currentMelee);
                }

                // Tira a arma ranged original do inventário e reequipa no colono
                pawn.inventory.innerContainer.Remove(targetWeapon);
                eqTracker.AddEquipment(targetWeapon);
            }
        }
    }
}
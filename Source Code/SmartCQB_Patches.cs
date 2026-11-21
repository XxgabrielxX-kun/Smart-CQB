using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace SmartCQB
{
    [StaticConstructorOnStartup]
    public static class SmartCQB_Patcher
    {
        static SmartCQB_Patcher()
        {
            new Harmony("seunome.smartcqb").PatchAll(typeof(SmartCQB_Patcher).Assembly);
        }
    }

    /// <summary>
    /// "Descarregar inventário" pergunta ao inventário qual é o próximo item descarregável
    /// (FirstUnloadableThing). Aqui escondemos a arma secundária (e a principal guardada durante a troca):
    /// se o item escolhido pelo jogo for um deles, o inventário é reordenado (protegidos vão para o fim)
    /// e a pergunta é refeita; se só sobrarem itens protegidos, o resultado é "nada para descarregar".
    /// </summary>
    [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.FirstUnloadableThing), MethodType.Getter)]
    public static class Patch_FirstUnloadableThing
    {
        private static bool busy;
        private static readonly FieldInfo InnerListField = AccessTools.Field(typeof(ThingOwner<Thing>), "innerList");

        public static void Postfix(Pawn_InventoryTracker __instance, ref ThingCount __result)
        {
            if (busy) return;

            Thing first = __result.Thing;
            if (first == null) return;

            SmartCQB_GameComponent comp = SmartCQB_GameComponent.Instance;
            Pawn pawn = __instance.pawn;
            if (comp == null || !comp.IsProtected(pawn, first)) return;

            // O jogo escolheu um item protegido. Tenta achar outro item descarregável.
            var list = InnerListField?.GetValue(__instance.innerContainer) as List<Thing>;
            if (list == null)
            {
                __result = default(ThingCount); // plano B: nada a descarregar, mas a arma fica segura
                return;
            }

            List<Thing> protectedItems = list.Where(t => comp.IsProtected(pawn, t)).ToList();
            list.RemoveAll(t => protectedItems.Contains(t));
            list.AddRange(protectedItems); // protegidos vão para o fim da lista

            busy = true;
            try
            {
                ThingCount again = __instance.FirstUnloadableThing;
                __result = (again.Thing != null && !comp.IsProtected(pawn, again.Thing))
                    ? again
                    : default(ThingCount);
            }
            finally
            {
                busy = false;
            }
        }
    }
}

using UnityEngine;
using Verse;

namespace SmartCQB
{
    public class SmartCQB_Settings : ModSettings
    {
        public float triggerDistance = 3f;   // casas: inimigo "muito perto"
        public float releaseDistance = 7f;   // casas: abaixo disso a luta ainda conta como ativa
        public float calmSeconds = 3f;       // segundos sem inimigo antes de voltar à arma principal
        public bool onlyDrafted = false;

        // A distância de saída nunca pode ser menor que a de entrada (evita troca em loop).
        public float EffectiveRelease => Mathf.Max(releaseDistance, triggerDistance + 1f);

        public override void ExposeData()
        {
            Scribe_Values.Look(ref triggerDistance, "triggerDistance", 3f);
            Scribe_Values.Look(ref releaseDistance, "releaseDistance", 7f);
            Scribe_Values.Look(ref calmSeconds, "calmSeconds", 3f);
            Scribe_Values.Look(ref onlyDrafted, "onlyDrafted", false);
        }
    }

    public class SmartCQB_Mod : Mod
    {
        public static SmartCQB_Settings Settings;

        public SmartCQB_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SmartCQB_Settings>();
        }

        public override string SettingsCategory() => "SmartCQB";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("SmartCQB_TriggerDistance".Translate(Settings.triggerDistance.ToString("F1")));
            Settings.triggerDistance = Mathf.Round(list.Slider(Settings.triggerDistance, 1f, 8f) * 2f) / 2f;

            list.Label("SmartCQB_ReleaseDistance".Translate(Settings.EffectiveRelease.ToString("F1")));
            Settings.releaseDistance = Mathf.Round(list.Slider(Settings.releaseDistance, 2f, 15f) * 2f) / 2f;

            list.Label("SmartCQB_CalmSeconds".Translate(Settings.calmSeconds.ToString("F0")));
            Settings.calmSeconds = Mathf.Round(list.Slider(Settings.calmSeconds, 1f, 15f));

            list.CheckboxLabeled("SmartCQB_OnlyDrafted".Translate(), ref Settings.onlyDrafted);

            list.End();
        }
    }
}

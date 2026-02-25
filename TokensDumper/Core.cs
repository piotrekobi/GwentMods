using System;
using System.IO;
using System.Text;
using MelonLoader;
using HarmonyLib;
using Il2CppGwentVisuals;
using Il2CppGwentGameplay;
using Il2CppSystem.Collections.Generic;

[assembly: MelonInfo(typeof(TokensDumper.TokensDumperMod), "TokensDumper", "1.0.0", "piotrekobi")]
[assembly: MelonGame("CDProjektRED", "Gwent")]

namespace TokensDumper
{
    public class TokensDumperMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("TokensDumper initialized. Waiting for HandleDefinitionsLoaded...");
        }
    }

    [HarmonyPatch(typeof(GwentApp), nameof(GwentApp.HandleDefinitionsLoaded))]
    public class GwentApp_HandleDefinitionsLoaded_Patch
    {
        public static void Postfix()
        {
            MelonLogger.Msg("Definitions loaded, dumping list of cards missing premium behaviours...");

            try
            {
                var sharedData = GwentApp.Instance?.SharedData;
                if (sharedData?.SharedRuntimeTemplates == null)
                {
                    MelonLogger.Error("SharedRuntimeTemplates is null.");
                    return;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("--- Cards Missing Premium ---");
                int missingCount = 0;
                int totalCount = 0;

                foreach (var entry in sharedData.SharedRuntimeTemplates)
                {
                    var template = entry.Value;
                    if (template?.Template == null) continue;
                    totalCount++;

                    int templateId = template.TemplateId;
                    bool hasPremium = PremiumBehaviourSettings.CardHasPremiumBehaviour(templateId);

                    if (!hasPremium)
                    {
                        var avail = template.Template.Availability;
                        string debugName = template.Template.DebugName ?? "Unknown";

                        // Dump missing premiums
                        sb.AppendLine($"TemplateId: {templateId} | DebugName: {debugName} | Availability: {avail}");
                        missingCount++;
                    }
                }

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string outputPath = Path.Combine(desktopPath, "MissingPremiumTokens.txt");
                File.WriteAllText(outputPath, sb.ToString());

                MelonLogger.Msg($"SUCCESS! Checked {totalCount} cards. Dumped {missingCount} missing premiums.");
                MelonLogger.Msg($"File saved to: {outputPath}");

                // Force quit since we're done
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"An error occurred:\n{ex}");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
        }
    }
}

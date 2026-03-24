using HarmonyLib;
using Verse;

namespace NewBeginnings
{
    [StaticConstructorOnStartup]
    public static class NewBeginningsMod
    {
        static NewBeginningsMod()
        {
            new Harmony("anna.newbeginnings").PatchAll();
        }
    }
}

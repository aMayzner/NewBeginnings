using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NewBeginnings
{
    public class CaravanArrivalAction_SetFree : CaravanArrivalAction
    {
        private Settlement settlement;

        public override string Label => "Set colonists free";

        public override string ReportString => "Sending colonists to start a new life at " + settlement.Label;

        public CaravanArrivalAction_SetFree()
        {
        }

        public CaravanArrivalAction_SetFree(Settlement settlement)
        {
            this.settlement = settlement;
        }

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport report = base.StillValid(caravan, destinationTile);
            if (!report)
                return report;
            if (settlement != null && settlement.Tile != destinationTile)
                return false;
            return CanSetFreeAt(caravan, settlement);
        }

        public override void Arrived(Caravan caravan)
        {
            Faction targetFaction = settlement.Faction;
            List<Pawn> pawns = caravan.PawnsListForReading.ToList();
            List<Thing> items = CaravanInventoryUtility.AllInventoryItems(caravan).ToList();

            // Calculate total wealth of items (75% counts as gift)
            float totalItemWealth = 0f;
            foreach (Thing item in items)
                totalItemWealth += item.MarketValue * item.stackCount;

            // Also count animals
            List<Pawn> animals = pawns.Where(p => p.RaceProps.Animal).ToList();
            float animalWealth = 0f;
            foreach (Pawn animal in animals)
                animalWealth += animal.MarketValue;

            float giftWealth = (totalItemWealth + animalWealth) * 0.75f;

            // Calculate goodwill from gift value (same curve as vanilla gifts roughly)
            int goodwillGain = (int)(giftWealth / 40f);
            if (goodwillGain < 1 && giftWealth > 0f) goodwillGain = 1;
            if (goodwillGain > 100) goodwillGain = 100;

            // Transfer colonists to the target faction as world pawns
            List<Pawn> colonists = pawns.Where(p => p.IsColonist && p.RaceProps.Humanlike).ToList();
            List<string> colonistNames = colonists.Select(p => p.Name.ToStringShort).ToList();

            foreach (Pawn colonist in colonists)
            {
                caravan.RemovePawn(colonist);
                colonist.SetFaction(targetFaction);
                if (!colonist.IsWorldPawn())
                    Find.WorldPawns.PassToWorld(colonist);
            }

            // Transfer animals to the faction
            foreach (Pawn animal in animals)
            {
                caravan.RemovePawn(animal);
                animal.SetFaction(targetFaction);
                if (!animal.IsWorldPawn())
                    Find.WorldPawns.PassToWorld(animal);
            }

            // Destroy items (given as gifts)
            foreach (Thing item in items)
                item.Destroy();

            // Apply goodwill
            if (goodwillGain > 0)
            {
                Faction.OfPlayer.TryAffectGoodwillWith(targetFaction, goodwillGain,
                    canSendMessage: true, canSendHostilityLetter: true,
                    HistoryEventDefOf.GaveGift);
            }

            // Goodwill bonus for sending colonists (5 per colonist)
            int colonistGoodwill = colonists.Count * 5;
            if (colonistGoodwill > 0)
            {
                Faction.OfPlayer.TryAffectGoodwillWith(targetFaction, colonistGoodwill,
                    canSendMessage: false, canSendHostilityLetter: false);
            }

            // Colony mood boost
            ThoughtDef freshStart = DefDatabase<ThoughtDef>.GetNamedSilentFail("NewBeginnings_GaveFreshStart");
            if (freshStart != null)
            {
                foreach (Map map in Find.Maps)
                {
                    foreach (Pawn colonist in map.mapPawns.FreeColonists)
                        colonist.needs?.mood?.thoughts?.memories?.TryGainMemory(freshStart);
                }
            }

            // Send letter
            string names = string.Join(", ", colonistNames);
            string letterText = names + " arrived at " + settlement.Label + " and "
                + (colonists.Count == 1 ? "has" : "have") + " been welcomed by " + targetFaction.Name + "."
                + " They will start a new life there.";

            if (giftWealth > 0f)
                letterText += "\n\nThe gifts you sent were worth " + giftWealth.ToStringMoney()
                    + ", earning you goodwill with " + targetFaction.Name + ".";

            Find.LetterStack.ReceiveLetter(
                "New Beginnings",
                letterText,
                LetterDefOf.PositiveEvent,
                settlement);

            // If caravan is empty now, remove it
            if (caravan.PawnsListForReading.Count == 0)
            {
                caravan.Destroy();
            }
        }

        public static FloatMenuAcceptanceReport CanSetFreeAt(Caravan caravan, Settlement settlement)
        {
            if (settlement == null || !settlement.Spawned || settlement.HasMap)
                return false;
            if (settlement.Faction == null || settlement.Faction == Faction.OfPlayer)
                return false;
            if (settlement.Faction.HostileTo(Faction.OfPlayer))
                return false;
            // Need at least one colonist to send
            if (!caravan.PawnsListForReading.Any(p => p.IsColonist && p.RaceProps.Humanlike))
                return false;
            return true;
        }

        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan, Settlement settlement)
        {
            return CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => CanSetFreeAt(caravan, settlement),
                () => new CaravanArrivalAction_SetFree(settlement),
                "Set colonists free at " + settlement.Label,
                caravan, settlement.Tile, settlement);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
        }
    }
}

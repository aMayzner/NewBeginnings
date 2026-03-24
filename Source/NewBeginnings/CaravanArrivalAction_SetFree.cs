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

        // 2 years in ticks
        private const float MinTimeInColonyTicks = 2f * 60f * 60000f;
        // 1 year cooldown tracked via game component
        private const int CooldownTicks = 60 * 60000;

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

            // Calculate goodwill from gift value
            int goodwillGain = (int)(giftWealth / 40f);
            if (goodwillGain < 1 && giftWealth > 0f) goodwillGain = 1;
            if (goodwillGain > 100) goodwillGain = 100;

            // Get eligible colonists (adults, free, 2+ years in colony)
            List<Pawn> colonists = pawns
                .Where(p => p.IsColonist && p.RaceProps.Humanlike && IsEligible(p))
                .ToList();

            if (colonists.Count == 0)
                return;

            List<string> colonistNames = colonists.Select(p => p.Name.ToStringShort).ToList();
            NewBeginningsCooldown tracker = Current.Game.GetComponent<NewBeginningsCooldown>();

            // Relationship pain — remaining colonists who had close bonds
            ThoughtDef friendLeft = DefDatabase<ThoughtDef>.GetNamedSilentFail("NewBeginnings_FriendLeft");
            ThoughtDef friendLeftMood = DefDatabase<ThoughtDef>.GetNamedSilentFail("NewBeginnings_FriendLeftMood");
            ThoughtDef loverLeft = DefDatabase<ThoughtDef>.GetNamedSilentFail("NewBeginnings_LoverLeft");
            ThoughtDef loverLeftMood = DefDatabase<ThoughtDef>.GetNamedSilentFail("NewBeginnings_LoverLeftMood");
            if (friendLeft != null || loverLeft != null)
            {
                foreach (Pawn leaving in colonists)
                {
                    // Check direct relations (lover, spouse, fiance, family)
                    foreach (DirectPawnRelation rel in leaving.relations.DirectRelations)
                    {
                        Pawn other = rel.otherPawn;
                        if (other == null || other.Dead || colonists.Contains(other))
                            continue;
                        if (!other.IsColonist || !other.RaceProps.Humanlike)
                            continue;

                        if (rel.def == PawnRelationDefOf.Lover
                            || rel.def == PawnRelationDefOf.Fiance
                            || rel.def == PawnRelationDefOf.Spouse)
                        {
                            if (loverLeft != null)
                                other.needs?.mood?.thoughts?.memories?.TryGainMemory(loverLeft, leaving);
                            if (loverLeftMood != null)
                                other.needs?.mood?.thoughts?.memories?.TryGainMemory(loverLeftMood);
                        }
                    }

                    // Check opinion-based friendships (opinion > 40 = close friend)
                    if (friendLeft != null)
                    {
                        foreach (Map map in Find.Maps)
                        {
                            foreach (Pawn other in map.mapPawns.FreeColonists)
                            {
                                if (other == leaving || colonists.Contains(other))
                                    continue;
                                if (leaving.relations.OpinionOf(other) > 40)
                                {
                                    if (friendLeft != null)
                                        other.needs?.mood?.thoughts?.memories?.TryGainMemory(friendLeft, leaving);
                                    if (friendLeftMood != null)
                                        other.needs?.mood?.thoughts?.memories?.TryGainMemory(friendLeftMood);
                                }
                            }
                        }
                    }
                }
            }

            // Permanent bond between colonists starting a new life together
            if (colonists.Count > 1)
            {
                ThoughtDef newLifeTogether = DefDatabase<ThoughtDef>.GetNamedSilentFail("NewBeginnings_NewLifeTogether");
                if (newLifeTogether != null)
                {
                    for (int a = 0; a < colonists.Count; a++)
                    {
                        for (int b = 0; b < colonists.Count; b++)
                        {
                            if (a != b)
                            {
                                Thought_MemorySocial thought = (Thought_MemorySocial)ThoughtMaker.MakeThought(newLifeTogether);
                                thought.permanent = true;
                                colonists[a].needs?.mood?.thoughts?.memories?.TryGainMemory(thought, colonists[b]);
                            }
                        }
                    }
                }
            }

            // Record sent colonists for return visits and faction memory
            foreach (Pawn colonist in colonists)
                tracker?.RecordSentColonist(colonist, targetFaction);

            // Transfer colonists to the target faction as world pawns
            foreach (Pawn colonist in colonists)
            {
                caravan.RemovePawn(colonist);
                colonist.SetFaction(targetFaction);
                if (!colonist.IsWorldPawn())
                    Find.WorldPawns.PassToWorld(colonist);
            }

            // Transfer animals
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

            // Apply goodwill from gifts
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
                    foreach (Pawn col in map.mapPawns.FreeColonists)
                        col.needs?.mood?.thoughts?.memories?.TryGainMemory(freshStart);
                }
            }

            // Set cooldown
            if (tracker != null)
                tracker.lastUsedTick = Find.TickManager.TicksGame;

            // Build letter with faction memory
            string names = string.Join(", ", colonistNames);
            string letterText = names + " arrived at " + settlement.Label + " and "
                + (colonists.Count == 1 ? "has" : "have") + " been welcomed by " + targetFaction.Name + "."
                + " They will start a new life there.";

            // Faction memory — mention previous colonists sent to this faction
            if (tracker != null)
            {
                List<string> previous = tracker.GetPreviouslySentTo(targetFaction);
                // Exclude the ones we just sent
                List<string> previousOnly = previous
                    .Where(n => !colonistNames.Contains(n))
                    .ToList();
                if (previousOnly.Count > 0)
                {
                    letterText += "\n\nThey join " + string.Join(", ", previousOnly)
                        + " who " + (previousOnly.Count == 1 ? "was" : "were")
                        + " sent to " + targetFaction.Name + " before them.";
                }
            }

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
                caravan.Destroy();
        }

        public static bool IsEligible(Pawn pawn)
        {
            if (!pawn.ageTracker.Adult)
                return false;
            if (pawn.IsPrisoner || pawn.IsSlave)
                return false;
            float timeInColony = pawn.records.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal);
            if (timeInColony < MinTimeInColonyTicks)
                return false;
            return true;
        }

        public static bool IsOnCooldown()
        {
            NewBeginningsCooldown cooldown = Current.Game?.GetComponent<NewBeginningsCooldown>();
            if (cooldown == null)
                return false;
            return Find.TickManager.TicksGame - cooldown.lastUsedTick < CooldownTicks;
        }

        public static FloatMenuAcceptanceReport CanSetFreeAt(Caravan caravan, Settlement settlement)
        {
            if (settlement == null || !settlement.Spawned || settlement.HasMap)
                return false;
            if (settlement.Faction == null || settlement.Faction == Faction.OfPlayer)
                return false;
            if (settlement.Faction.HostileTo(Faction.OfPlayer))
                return false;
            if (IsOnCooldown())
                return false;
            if (!caravan.PawnsListForReading.Any(p => p.IsColonist && p.RaceProps.Humanlike && IsEligible(p)))
                return false;
            int totalColonists = 0;
            foreach (Map map in Find.Maps)
                totalColonists += map.mapPawns.FreeColonistsCount;
            int eligibleInCaravan = caravan.PawnsListForReading
                .Count(p => p.IsColonist && p.RaceProps.Humanlike && IsEligible(p));
            if (totalColonists - eligibleInCaravan < 2)
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

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace NewBeginnings
{
    public class NewBeginningsCooldown : GameComponent
    {
        public int lastUsedTick = -9999999;

        // Track all sent colonists for return visits
        public List<Pawn> sentColonists = new List<Pawn>();
        public List<Faction> sentToFactions = new List<Faction>();
        public List<int> sentAtTicks = new List<int>();

        // Track per-faction history for faction memory
        public Dictionary<string, List<string>> factionHistory = new Dictionary<string, List<string>>();

        private int nextVisitCheckTick;
        private const int VisitCheckInterval = 60000 * 15; // Check every 15 days
        private const float VisitChancePerCheck = 0.08f; // 8% chance per sent colonist per check
        private const int MinDaysBeforeVisit = 60; // At least 60 days before they can visit

        public NewBeginningsCooldown(Game game)
        {
        }

        public void RecordSentColonist(Pawn pawn, Faction faction)
        {
            sentColonists.Add(pawn);
            sentToFactions.Add(faction);
            sentAtTicks.Add(Find.TickManager.TicksGame);

            string factionKey = faction.loadID.ToString();
            if (!factionHistory.ContainsKey(factionKey))
                factionHistory[factionKey] = new List<string>();
            factionHistory[factionKey].Add(pawn.Name.ToStringShort);
        }

        public List<string> GetPreviouslySentTo(Faction faction)
        {
            string factionKey = faction.loadID.ToString();
            if (factionHistory.TryGetValue(factionKey, out List<string> names))
                return names;
            return new List<string>();
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame < nextVisitCheckTick)
                return;
            nextVisitCheckTick = Find.TickManager.TicksGame + VisitCheckInterval;

            // Clean up null entries
            for (int i = sentColonists.Count - 1; i >= 0; i--)
            {
                if (sentColonists[i] == null || sentColonists[i].Destroyed)
                {
                    sentColonists.RemoveAt(i);
                    sentToFactions.RemoveAt(i);
                    sentAtTicks.RemoveAt(i);
                }
            }

            // Try to trigger a return visit
            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
                return;

            for (int i = 0; i < sentColonists.Count; i++)
            {
                int daysSinceSent = (Find.TickManager.TicksGame - sentAtTicks[i]) / 60000;
                if (daysSinceSent < MinDaysBeforeVisit)
                    continue;

                if (!Rand.Chance(VisitChancePerCheck))
                    continue;

                Pawn visitor = sentColonists[i];
                if (visitor == null || visitor.Dead)
                    continue;

                // Trigger visit
                TriggerReturnVisit(visitor, sentToFactions[i], map);
                break; // Only one visit per check
            }
        }

        private void TriggerReturnVisit(Pawn visitor, Faction faction, Map map)
        {
            // Spawn them at map edge
            IntVec3 spawnSpot;
            if (!CellFinder.TryFindRandomEdgeCellWith(
                c => c.Standable(map) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Friendly, out spawnSpot))
                return;

            GenSpawn.Spawn(visitor, spawnSpot, map);

            // Send letter
            Find.LetterStack.ReceiveLetter(
                visitor.Name.ToStringShort + " is visiting",
                visitor.Name.ToStringShort + " has returned for a visit from " + faction.Name
                    + "! They want to see how everyone is doing. They'll leave after a short stay.",
                LetterDefOf.PositiveEvent, visitor);

            // They'll leave on their own after about a day via the visitor lord
            // For simplicity, just set them to leave after a delay
            visitor.mindState.exitMapAfterTick = Find.TickManager.TicksGame + 60000;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastUsedTick, "lastUsedTick", -9999999);
            Scribe_Values.Look(ref nextVisitCheckTick, "nextVisitCheckTick", 0);
            Scribe_Collections.Look(ref sentColonists, "sentColonists", LookMode.Reference);
            Scribe_Collections.Look(ref sentToFactions, "sentToFactions", LookMode.Reference);
            Scribe_Collections.Look(ref sentAtTicks, "sentAtTicks", LookMode.Value);
            Scribe_Collections.Look(ref factionHistory, "factionHistory", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (sentColonists == null) sentColonists = new List<Pawn>();
                if (sentToFactions == null) sentToFactions = new List<Faction>();
                if (sentAtTicks == null) sentAtTicks = new List<int>();
                if (factionHistory == null) factionHistory = new Dictionary<string, List<string>>();
                sentColonists.RemoveAll(p => p == null);
            }
        }
    }
}

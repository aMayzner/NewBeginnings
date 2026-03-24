using Verse;

namespace NewBeginnings
{
    public class NewBeginningsCooldown : GameComponent
    {
        public int lastUsedTick = -9999999;

        public NewBeginningsCooldown(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastUsedTick, "lastUsedTick", -9999999);
        }
    }
}

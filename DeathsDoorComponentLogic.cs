using System.Linq;

namespace LiveSplit.DeathsDoor {
    public partial class DeathsDoorComponent {

        private readonly RemainingDictionary remainingSplits;

        public override bool Update() {
            return memory.Update();
        }

        public override bool Start() {
            return memory.HasStartedANewSave();
        }

        public override void OnStart() {
            remainingSplits.Setup(settings.Splits);
            memory.ResetData();
        }

        public override bool Split() {
            return remainingSplits.Count() != 0 && (SplitFade() || SplitBool() || SplitScene() || SplitTruthEnding());

            bool SplitFade() {
                if(!remainingSplits.ContainsKey("Fade")) {
                    return false;
                }
                if(memory.Scene.New == "lvl_HallOfDoors_BOSSFIGHT") {
                    return memory.HasStartedFading(Color.White, 2f) && remainingSplits.Split("Fade", "lod");
                } else if(memory.Scene.New.StartsWith("boss_")) {
                    return memory.HasStartedFading(Color.White, 1.5f) && remainingSplits.Split("Fade", memory.Scene.New.Substring(5));
                }
                return false;
            }

            bool SplitBool() {
                if(!remainingSplits.ContainsKey("Bool")) {
                    return false;
                }
                foreach(string name in memory.NewBoolSequence()) {
                    if(remainingSplits.Split("Bool", name)) {
                        return true;
                    }
                }
                return false;
            }

            bool SplitScene() {
                return remainingSplits.ContainsKey("Scene")
                    && memory.Scene.Changed
                    && remainingSplits.Split("Scene", memory.Scene.New);
            }

            bool SplitTruthEnding() {
                return remainingSplits.ContainsKey("TruthEnding")
                    && memory.IsInTruthTrigger()
                    && remainingSplits.Split("TruthEnding");
            }

        }

        public override bool Reset() {
            return memory.HasDeletedASave();
        }

        public override bool Loading() {
            return memory.LoadingIconShown.New || memory.IsCurrentlyLoading.New;
        }
    }
}
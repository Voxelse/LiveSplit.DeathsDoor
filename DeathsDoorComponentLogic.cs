using System.Linq;

namespace LiveSplit.DeathsDoor {
    public partial class DeathsDoorComponent {

        private readonly RemainingDictionary remainingSplits;

        public override bool Update() {
            return memory.Update();
        }

        public override bool Start() {
            return memory.SlotSelected && memory.GameTimeOfSlot(memory.SlotIndex) == 0;
        }

        public override void OnStart() {
            remainingSplits.Setup(settings.Splits);
        }

        public override bool Split() {
            return remainingSplits.Count() != 0 && (SplitBool() || SplitScene() || SplitFade());

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
        }

        public override bool Reset() {
            return memory.SaveMenu.Old == default && memory.SaveMenu.New != default;
        }

        public override bool Loading() {
            return memory.LoadingIconShown.New || memory.IsCurrentlyLoading.New;
        }
    }
}
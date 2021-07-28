using System;
using System.Collections.Generic;
using Voxif.AutoSplitter;
using Voxif.Helpers.Unity;
using Voxif.IO;
using Voxif.Memory;

namespace LiveSplit.DeathsDoor {
    public class DeathsDoorMemory : Memory {

        protected override string[] ProcessNames => new string[] { "DeathsDoor" };

        public Pointer<bool> LoadingIconShown { get; private set; }

        public Pointer<bool> IsCurrentlyLoading { get; private set; }
        public StringPointer Scene { get; private set; }

        private Pointer<IntPtr> SaveSlots { get; set; }
        private Pointer<int> SlotIndex { get; set; }
        private Pointer<IntPtr> SlotTransition { get; set; }

        private StringPointer SpawnId { get; set; }

        private Pointer<Color> FadeColor { get; set; }
        private Pointer<float> FadeTimer { get; set; }
        private Pointer<float> FadeMaxTime { get; set; }

        private Pointer<Vector3> PlayerPosition { get; set; }

        private readonly DictData<int> countKeys = new DictData<int>();
        private readonly DictData<bool> boolKeys = new DictData<bool>();

        private readonly float[] gameTimes = new float[3];
        private bool saveIsInitialized = false;

        private UnityHelperTask unityTask;

        public DeathsDoorMemory(Logger logger) : base(logger) {
            OnHook += () => {
                unityTask = new UnityHelperTask(game, logger);
                unityTask.Run(InitPointers);
            };

            OnExit += () => {
                if(unityTask != null) {
                    unityTask.Dispose();
                    unityTask = null;
                }
            };
        }

        private void InitPointers(IMonoHelper unity) {
            MonoNestedPointerFactory ptrFactory = new MonoNestedPointerFactory(game, unity);

            var gameSceneManager = ptrFactory.Make("GameSceneManager");
            IsCurrentlyLoading = ptrFactory.Make<bool>(gameSceneManager, "instance", "isCurrentlyLoading");
            Scene = ptrFactory.MakeString(gameSceneManager, "currentScene", ptrFactory.StringHeaderSize);
            Scene.StringType = EStringType.UTF16Sized;

            LoadingIconShown = ptrFactory.Make<bool>("LoadingIcon", "instance", "show");

            var saveMenu = ptrFactory.Make<IntPtr>("TitleScreen", "instance", "saveMenu");
            ptrFactory.Make("SaveMenu", out IntPtr saveMenuClass); //TODO add proper helper func to get classPtr
            SaveSlots = ptrFactory.Make<IntPtr>(saveMenu, unity.GetFieldOffset(saveMenuClass, "saveSlots"));
            SlotTransition = ptrFactory.Make<IntPtr>(saveMenu, unity.GetFieldOffset(saveMenuClass, "transitionButton"));
            SlotIndex = ptrFactory.Make<int>(saveMenu, unity.GetFieldOffset(saveMenuClass, "index"));

            var gameSave = ptrFactory.Make<IntPtr>("GameSave", "currentSave", out IntPtr gameSaveClass);
            SpawnId = ptrFactory.MakeString(gameSave, unity.GetFieldOffset(gameSaveClass, "spawnId"), ptrFactory.StringHeaderSize);
            SpawnId.StringType = EStringType.UTF16Sized;
            countKeys.pointer = ptrFactory.Make<IntPtr>(gameSave, unity.GetFieldOffset(gameSaveClass, "countKeys"));
            boolKeys.pointer = ptrFactory.Make<IntPtr>(gameSave, unity.GetFieldOffset(gameSaveClass, "boolKeys"));

            var screenFade = ptrFactory.Make<IntPtr>("ScreenFade", "instance", out IntPtr screenFadeClass);
            FadeColor = ptrFactory.Make<Color>(screenFade, unity.GetFieldOffset(screenFadeClass, "fadeColor"));
            FadeTimer = ptrFactory.Make<float>(screenFade, unity.GetFieldOffset(screenFadeClass, "timer"));
            FadeMaxTime = ptrFactory.Make<float>(screenFade, unity.GetFieldOffset(screenFadeClass, "maxTime")); 
        
            PlayerPosition = ptrFactory.Make<Vector3>("PlayerGlobal", "instance", 0x10, 0x30, 0x30, 0x8, 0x28, 0x10, 0x38, 0x180);

            logger.Log(ptrFactory.ToString());

            unityTask = null;
        }

        public override bool Update() {
            if(base.Update() && unityTask == null) {
                if(!saveIsInitialized && SpawnId.New.Equals("bus_overridespawn")) {
                    saveIsInitialized = true;
                }
                return true;
            }
            return false;
        }

        public void ResetData() {
            saveIsInitialized = false;
            boolKeys.Clear();
            countKeys.Clear();
        }

        public bool HasStartedANewSave() {
            return SlotTransition.New != default && GameTimeOfSlot(SlotIndex.New) == 0;
        }

        public bool HasDeletedASave() {
            if(SaveSlots.New == default) {
                return false;
            }
            for(int slotId = 0; slotId < gameTimes.Length; slotId++) {
                float time = GameTimeOfSlot(slotId);
                if(time != gameTimes[slotId]) {
                    float oldTime = gameTimes[slotId];
                    gameTimes[slotId] = time;
                    if(time == 0 && oldTime != 0) {
                        return true;
                    }
                }
            }
            return false;
        }

        private float GameTimeOfSlot(int index) {
            return game.Read<float>(SaveSlots.New, 0x20 + 0x8 * index, 0x18, 0x18, 0x40);
        }

        public IEnumerable<string> NewBoolSequence() {
            foreach(KeyValuePair<string, bool> kvp in UpdateDict(boolKeys)) {
                if(kvp.Value) {
                    yield return kvp.Key;
                }
            }
        }

        public IEnumerable<string> NewCountSequence() {
            foreach(KeyValuePair<string, int> kvp in UpdateDict(countKeys)) {
                yield return kvp.Key + "_" + kvp.Value;
            }
        }

        private IEnumerable<KeyValuePair<string, T>> UpdateDict<T>(DictData<T> dictData) where T : unmanaged {
            if(!saveIsInitialized) {
                yield break;
            }

            int version = game.Read<int>(dictData.pointer.New + 0x44);
            if(version == dictData.version) {
                yield break;
            }
            dictData.version = version;

            IntPtr entries = game.Read<IntPtr>(dictData.pointer.New + 0x18);
            int count = game.Read<int>(dictData.pointer.New + 0x40);
            for(int i = 0; i < count; i++) {
                IntPtr entryOffset = entries + 0x20 + 0x18 * i;
                string key = game.ReadString(game.Read(entryOffset, 0x8, 0x14), EStringType.UTF16Sized);
                T value = game.Read<T>(entryOffset + 0x10);
                if(!dictData.dict.ContainsKey(key)) {
#if DEBUG
                    Debug($"Dict add {key}: {value}");
#endif
                    dictData.dict.Add(key, value);
                    yield return new KeyValuePair<string, T>(key, value);
                } else if(!dictData.dict[key].Equals(value)) {
#if DEBUG                    
                    Debug($"Dict change {key}: {dictData.dict[key]} -> {value}");
#endif
                    dictData.dict[key] = value;
                    yield return new KeyValuePair<string, T>(key, value);
                }
            }

#if DEBUG
            void Debug(string msg) {
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " --- " + msg);
            }
#endif
        }

        public bool HasStartedFading(Color color, float fadeTime) {
            return FadeTimer.Old < FadeTimer.New && FadeMaxTime.New == fadeTime && FadeColor.New.Equals(color);
        }

        public bool IsInTruthTrigger() {
            const float x = -128.9004f;
            const float width = 6.1308f;
            const float z = 789.7526f;
            const float depth = 43.6074f;

            const float pSize = 1f;

            return Scene.New.Equals("lvlConnect_Fortress_Mountaintops")
                && PlayerPosition.New.x - pSize < x + width && PlayerPosition.New.x + pSize > x - width
                && PlayerPosition.New.z - pSize < z + depth && PlayerPosition.New.z + pSize > z - depth;
        }

        private class DictData<T> where T : unmanaged {
            public Dictionary<string, T> dict = new Dictionary<string, T>();
            public Pointer<IntPtr> pointer = null;
            public int version = default;

            public void Clear() {
                dict.Clear();
                version = 0;
            }
        }
    }

    public struct Color : IEquatable<Color> {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a) {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
        
        public bool Equals(Color other) => r == other.r && g == other.g && b == other.b && a == other.a;

        public static Color White => new Color(1f, 1f, 1f, 1f);
        public static Color Black => new Color(0f, 0f, 0f, 1f);
    }

    public struct Vector3 {
        public float x, y, z;

        public Vector3(float x, float y, float z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }
}
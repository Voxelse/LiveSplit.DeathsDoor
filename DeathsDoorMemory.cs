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

        public Pointer<IntPtr> SaveMenu { get; private set; }

        private Pointer<Color> FadeColor { get; set; }
        private Pointer<float> FadeTimer { get; set; }
        private Pointer<float> FadeMaxTime { get; set; }

        private DictData<int> CountKeys { get; set; } = new DictData<int>();
        private DictData<bool> BoolKeys { get; set; } = new DictData<bool>();

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

            SaveMenu = ptrFactory.Make<IntPtr>("TitleScreen", "instance", "saveMenu");

            var gameSave = ptrFactory.Make<IntPtr>("GameSave", "currentSave", out IntPtr gameSaveClass);
            CountKeys.pointer = ptrFactory.Make<IntPtr>(gameSave, unity.GetFieldOffset(gameSaveClass, "countKeys"));
            BoolKeys.pointer = ptrFactory.Make<IntPtr>(gameSave, unity.GetFieldOffset(gameSaveClass, "boolKeys"));

            var screenFade = ptrFactory.Make<IntPtr>("ScreenFade", "instance", out IntPtr screenFadeClass);
            FadeColor = ptrFactory.Make<Color>(screenFade, unity.GetFieldOffset(screenFadeClass, "fadeColor"));
            FadeTimer = ptrFactory.Make<float>(screenFade, unity.GetFieldOffset(screenFadeClass, "timer"));
            FadeMaxTime = ptrFactory.Make<float>(screenFade, unity.GetFieldOffset(screenFadeClass, "maxTime")); 

            logger.Log(ptrFactory.ToString());

            unityTask = null;
        }

        public override bool Update() => base.Update() && unityTask == null;

        public IEnumerable<string> NewBoolSequence() {
            foreach(KeyValuePair<string, bool> kvp in UpdateDict(BoolKeys)) {
                if(kvp.Value) {
                    yield return kvp.Key;
                }
            }
        }

        public IEnumerable<string> NewCountSequence() {
            foreach(KeyValuePair<string, int> kvp in UpdateDict(CountKeys)) {
                yield return kvp.Key + "_" + kvp.Value;
            }
        }

        private IEnumerable<KeyValuePair<string, T>> UpdateDict<T>(DictData<T> dictData) where T : unmanaged {
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

        public int SlotIndex => game.Read<int>(SaveMenu.New + 0xA0);
        public bool SlotSelected => game.Read<IntPtr>(SaveMenu.New + 0x88) != default;
        public float GameTimeOfSlot(int index) {
            return game.Read<float>(SaveMenu.New, 0x60, 0x20 + 0x8 * index, 0x18, 0x18, 0x40);
        }

        public bool HasStartedFading(Color color, float fadeTime) {
            return FadeTimer.Old < FadeTimer.New && FadeMaxTime.New == fadeTime && FadeColor.New.Equals(color);
        }

        private class DictData<T> where T : unmanaged {
            public Dictionary<string, T> dict = new Dictionary<string, T>();
            public Pointer<IntPtr> pointer = null;
            public int version = default;
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
}
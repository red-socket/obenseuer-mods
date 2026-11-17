using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace SoundSanity
{
    // Patch targets and private game members, read through Harmony's AccessTools rather than the publicized reference
    // (docs/modding-guide.md §4). A required member that is missing logs one error and stays null; Plugin.PatchGroup
    // checks the members each group needs, so only the feature using it fails. Every member here was checked against the
    // shipped 0.5.19 Assembly-CSharp.dll with Mono.Cecil (IL sizes in spec §5 and design.md §5).
    internal static class Vanilla
    {
        // ---- Routing (R1-R7) ----

        // AudioSourceMaker (SRC/AudioSourceMaker.cs:44, :56), public static.
        internal static readonly MethodInfo FromClip = Method(typeof(AudioSourceMaker), "FromClip",
            new[] { typeof(AudioClip), typeof(float), typeof(float), typeof(float), typeof(float), typeof(GameObject), typeof(bool), typeof(bool),
                typeof(AudioMixerGroup), typeof(bool), typeof(bool) });
        internal static readonly MethodInfo ResolveMixerGroups = Method(typeof(AudioSourceMaker), "ResolveMixerGroups", new[] { typeof(GameObject) });
        internal static readonly MethodInfo CreateAudioSource = Method(typeof(SoundInfo), "CreateAudioSource", new[] { typeof(GameObject) });
        // Runtime AddSound helpers that create null-group or duplicate-group sources (SRC/LightsON.cs:99, LightFlicker.cs:76,
        // Bell.cs:87, UseTriggerMoney.cs:162). Optional: a missing one only drops itself.
        internal static readonly MethodInfo LightsOnAddSound = Method(typeof(LightsON), "AddSound",
            new[] { typeof(AudioSource), typeof(AudioClip), typeof(float), typeof(float), typeof(float), typeof(bool) }, optional: true);
        internal static readonly MethodInfo LightFlickerAddSound = Method(typeof(LightFlicker), "AddSound",
            new[] { typeof(AudioClip), typeof(float), typeof(float), typeof(float) }, optional: true);
        internal static readonly MethodInfo BellAddSound = Method(typeof(Bell), "AddSound",
            new[] { typeof(AudioClip), typeof(float), typeof(float), typeof(float) }, optional: true);
        internal static readonly MethodInfo MoneyAddSound = Method(typeof(UseTriggerMoney), "AddSound",
            new[] { typeof(AudioClip), typeof(float), typeof(float), typeof(float) }, optional: true);
        // Tenant upgrade prefabs from Addressables (SRC/TenementApartmentPrefabs.cs:643 public; TenementGeneralPrefabs.cs:321 private).
        internal static readonly MethodInfo ProcessGuids = Method(typeof(TenementApartmentPrefabs), "ProcessGUIDs",
            new[] { typeof(TenementApartment), typeof(GameObject) }, optional: true);
        internal static readonly MethodInfo UpdateInstantiatedPrefab = Method(typeof(TenementGeneralPrefabs), "UpdateInstantiatedPrefab",
            new[] { typeof(GameObject), typeof(TenementUpgradeProgress) }, optional: true);
        // The (slot, bool) overload, not (int, List) (SRC/ShopBaseUI.cs:339), and its protected currentPreview (:36).
        internal static readonly MethodInfo ChangeSelection = Method(typeof(ShopBaseUI), "ChangeSelection",
            new[] { typeof(BuildingSystemShopSlot), typeof(bool) }, optional: true);
        internal static readonly AccessTools.FieldRef<ShopBaseUI, GameObject> CurrentPreview = Ref<ShopBaseUI, GameObject>("currentPreview", optional: true);
        // Residents (SRC/TenementApartmentPrefabInfo.cs:338, private npcList :129) and NPCManager.AddNpcToList (NPCManager.cs:119).
        internal static readonly MethodInfo SpawnResidents = Method(typeof(TenementApartmentPrefabInfo), "SpawnResidents", new[] { typeof(TenementResident) });
        internal static readonly AccessTools.FieldRef<TenementApartmentPrefabInfo, List<GameObject>> NpcList =
            Ref<TenementApartmentPrefabInfo, List<GameObject>>("npcList");
        internal static readonly MethodInfo AddNpcToList = Method(typeof(NPCManager), "AddNpcToList", new[] { typeof(NPCInfo) });

        // ---- Gain core (G1-G6) ----

        internal static readonly MethodInfo FromSound = Method(typeof(AudioSourceMaker), "FromSound", new[] { typeof(SoundInfo), typeof(GameObject) });
        internal static readonly MethodInfo PlaySound = Method(typeof(SoundInfo), "PlaySound", Type.EmptyTypes);
        internal static readonly MethodInfo PlayDelayed = Method(typeof(SoundInfo), "PlayDelayed", new[] { typeof(float) });
        internal static readonly MethodInfo SoundscapeAreaPlay = Method(typeof(SoundInfo), "SoundscapeAreaPlay", Type.EmptyTypes);
        internal static readonly MethodInfo SoundscapeAreaStop = Method(typeof(SoundInfo), "SoundscapeAreaStop", Type.EmptyTypes);
        internal static readonly MethodInfo AddSoundInfo = Method(typeof(SoundscapeArea), "AddSoundInfo", new[] { typeof(SoundInfo) });

        // ---- Machines (M1) ----

        // Declared on CraftingBase (SRC/CraftingBase.cs:466), protected virtual; every override calls base.Start().
        internal static readonly MethodInfo CraftingBaseStart = Method(typeof(CraftingBase), "Start", Type.EmptyTypes);

        // ---- Radio (RA1, RA2, AR1, VS1, VS2) ----

        internal static readonly MethodInfo RadioPlaySound = Method(typeof(global::Radio), "PlaySound", Type.EmptyTypes);
        // Radio.soundInfo and Radio.sounds (SRC/Radio.cs:17-23), private and [fsIgnore].
        internal static readonly AccessTools.FieldRef<global::Radio, SoundInfo> RadioSoundInfo = Ref<global::Radio, SoundInfo>("soundInfo");
        internal static readonly AccessTools.FieldRef<global::Radio, RandomAudio> RadioSounds = Ref<global::Radio, RandomAudio>("sounds");
        // The List<SoundInfo> overload, not the List<SoundscapeSoundLocation> one (SRC/SoundscapeSoundTypes.cs:6, :18).
        internal static readonly MethodInfo ChangeMixersInfos = Method(typeof(SoundscapeSoundTypes), "ChangeLocationsMixers",
            new[] { typeof(List<SoundInfo>), typeof(AudioMixerGroup), typeof(AudioMixerGroup) });
        internal static readonly MethodInfo SoundVolumePercent = Method(typeof(SoundscapeSoundLoop), "SoundVolumePercent", Type.EmptyTypes);
        internal static readonly MethodInfo SaveVolume = Method(typeof(VolumeSave), "SaveVolume", new[] { typeof(string), typeof(float), typeof(string) });
        internal static readonly MethodInfo GetVolume = Method(typeof(VolumeSave), "GetVolume", Type.EmptyTypes);

        // ---- Mixer identity ----

        // VolumeSave.audioMixer (SRC/VolumeSave.cs:17), private: the mixer the sliders write.
        internal static readonly AccessTools.FieldRef<VolumeSave, AudioMixer> VolumeSaveMixer = Ref<VolumeSave, AudioMixer>("audioMixer", optional: true);
        // AudioSourceMaker.MainMixer (SRC/AudioSourceMaker.cs:12), private static property.
        private static readonly MethodInfo AsmMainMixerGetter = Getter(typeof(AudioSourceMaker), "MainMixer");

        internal static AudioMixer AsmMainMixer()
        {
            try { return AsmMainMixerGetter != null ? (AudioMixer)AsmMainMixerGetter.Invoke(null, null) : null; }
            catch (Exception e) { Log.Exception("AudioSourceMaker.MainMixer", e); return null; }
        }

        // ---- Helpers ----

        internal static MethodInfo Method(Type t, string name, Type[] args, bool optional = false)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Missing($"{t.Name}.{name}", null, optional);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static MethodInfo Getter(Type t, string name)
        {
            try
            {
                var m = AccessTools.PropertyGetter(t, name);
                if (m == null) Missing($"{t.Name}.{name}", null, true);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, true); return null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name, bool optional = false)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Missing($"{typeof(T).Name}.{name}", e, optional); return null; }
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; the part of the feature using it is off.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }
    }
}

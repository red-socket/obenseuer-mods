using UnityEngine;

namespace SoundSanity
{
    // U-012 radio music gains (spec §4.2). Always at most 1: every setting is 0-100 %.
    internal static class RadioGain
    {
        // The Music level the player hears. Only VolumeSave ever sets the "Music" parameter (SRC/VolumeSave.cs:52, :89),
        // and a new scene's VolumeSave holds the default 0 dB until GetVolume runs, while the mixer already has the real
        // value, so the mixer is read first.
        private static float MusicDb()
        {
            var m = Mixers.Main;
            if (m != null && m.GetFloat("Music", out float v)) return v;
            var vs = VolumeSave.instance;
            return vs != null ? vs.soundSave.music : 0f;
        }

        private static float MusicFactor => Cfg.Radio.FollowMusicSlider.Value ? VolumeSave.DecibelToPercent(MusicDb()) / 100f : 1f;

        private static float Base => Cfg.Radio.RadioMusicVolume.Value / 100f;

        // Radios and record players you can switch on. A radio on the real Music group already gets the Music slider from
        // the mixer, so only RadioMusicVolume applies to it; on any other group (Soundscape, BackgroundSoundscape) the
        // Music slider is applied here. Either way the music part is Music slider x RadioMusicVolume. The rest follows the
        // group, as in vanilla: Music sits directly under Load (Resources/sounds/MainMixer.mixer), outside Sound > Sleeping
        // > Muffled, so a scene radio still on Music after a load ignores the Sound slider, sleep and earmuffs until the
        // game moves it to a soundscape group, where all three apply. Not compensated here (design D7).
        internal static float Interactive(AudioClip clip, AudioSource src)
        {
            if (!Cfg.Radio.Enabled.Value || !Groups.Radios.On || Compat.ObentunesPresent || !Clips.IsMusic(clip)) return 1f;
            if (Cfg.Radio.MuteInteractiveRadios.Value) return 0f;
            bool onMusic = src != null && src.outputAudioMixerGroup == Mixers.Music;
            return Base * (onMusic ? 1f : MusicFactor);
        }

        // Background radio and bar music loops (soundscape loops on the Soundscape groups).
        internal static float Ambient(AudioClip clip)
        {
            if (!Cfg.Radio.Enabled.Value || !Groups.AmbientRadios.On || !Clips.IsMusic(clip)) return 1f;
            if (Cfg.Radio.MuteAmbientRadios.Value) return 0f;
            return Base * MusicFactor;
        }
    }
}

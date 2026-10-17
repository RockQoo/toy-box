using System;
using Godot;
using ToyBox.Core;

namespace ToyBox.Common;

public enum Music { None, Menu, Play }

/// <summary>
/// Autoload. Plays synthesized sound effects (round-robin pool, so overlapping sounds do not cut each other off)
/// and looping background music. BGM and SFX go to their own buses so the settings sliders can set each volume.
/// </summary>
public partial class Audio : Node
{
    private const int Voices = 16;
    private const string BgmBus = "BGM";
    private const string SfxBus = "SFX";
    private const float MusicDb = -8f;

    public static Audio I { get; private set; } = null!;

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[Voices];
    private readonly Random _rng = new();
    private AudioStreamPlayer _bgm = null!;
    private Tween? _bgmTween;   // Track switch (fade out → swap → fade in)
    private Tween? _duckTween;
    private Tween? _tempoTween;
    private int _next;
    private Music _music;

    private AudioStreamWav _pop = null!, _click = null!, _bonk = null!, _tick = null!, _go = null!, _cheer = null!;
    private AudioStreamWav _timeUp = null!, _fanfare = null!, _count = null!, _whoosh = null!;
    private AudioStreamWav _catch = null!, _coin = null!, _plop = null!;
    private AudioStreamWav _menuSong = null!, _playSong = null!;

    public float BgmVolume
    {
        get => (float)App.I.GetSetting("audio", "bgm", 0.6f);
        set { App.I.SetSetting("audio", "bgm", value); ApplyBusVolume(BgmBus, value); }
    }

    public float SfxVolume
    {
        get => (float)App.I.GetSetting("audio", "sfx", 0.9f);
        set { App.I.SetSetting("audio", "sfx", value); ApplyBusVolume(SfxBus, value); }
    }

    public override void _EnterTree() => I = this;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        EnsureBus(BgmBus);
        EnsureBus(SfxBus);
        ApplyBusVolume(BgmBus, BgmVolume);
        ApplyBusVolume(SfxBus, SfxVolume);

        for (var i = 0; i < Voices; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = SfxBus };
            AddChild(_players[i]);
        }
        _bgm = new AudioStreamPlayer { Bus = BgmBus };
        AddChild(_bgm);

        _pop = ToStream(SfxSynth.Pop());
        _click = ToStream(SfxSynth.Click());
        _bonk = ToStream(SfxSynth.Bonk());
        _tick = ToStream(SfxSynth.Tick());
        _go = ToStream(SfxSynth.Go());
        _cheer = ToStream(SfxSynth.Cheer());
        _timeUp = ToStream(SfxSynth.TimeUp());
        _fanfare = ToStream(SfxSynth.Fanfare());
        _count = ToStream(SfxSynth.CountStep());
        _whoosh = ToStream(SfxSynth.Whoosh());
        _catch = ToStream(SfxSynth.Catch());
        _coin = ToStream(SfxSynth.Coin());
        _plop = ToStream(SfxSynth.Plop());
        _menuSong = ToStream(SfxSynth.MenuSong(), loop: true);
        _playSong = ToStream(SfxSynth.PlaySong(), loop: true);
    }

    // ---- Sound effects ----

    /// <summary>Balloon pop. The note climbs the scale while the player keeps a streak going.</summary>
    public void Pop(int streak) => Play(_pop, SfxSynth.StreakPitch(streak) * Jitter(0.03f), -1f);
    /// <summary>Fruit caught ("뿅"). Climbs the scale with the streak, like <see cref="Pop"/>.</summary>
    public void Catch(int streak, float db = -2f) => Play(_catch, SfxSynth.StreakPitch(streak) * Jitter(0.02f), db);
    /// <summary>Golden (bonus) fruit caught.</summary>
    public void Coin() => Play(_coin, 1f, -2f);
    /// <summary>Fruit landed on the floor: very quiet, not a failure sound.</summary>
    public void Plop() => Play(_plop, Jitter(0.08f), -14f);
    public void Click() => Play(_click, Jitter(0.04f), -4f);
    public void Bonk() => Play(_bonk, 1f, -2f);
    public void Tick(float pitch = 1f) => Play(_tick, pitch, -4f);
    public void Go() => Play(_go, 1f, -2f);
    public void Cheer() => Play(_cheer, 1f, -3f);
    public void TimeUp() => Play(_timeUp, 1f, -1f);
    public void Fanfare() => Play(_fanfare, 1f, 0f);
    public void CountStep(float pitch) => Play(_count, pitch, -6f);
    public void Whoosh() => Play(_whoosh, Jitter(0.1f), -6f);

    private void Play(AudioStream stream, float pitch, float db)
    {
        var player = _players[_next];
        _next = (_next + 1) % Voices;
        player.Stream = stream;
        player.PitchScale = pitch;
        player.VolumeDb = db;
        player.Play();
    }

    // ---- Music ----

    /// <summary>Switches the background loop with a short fade. Calling it with the current track does nothing.</summary>
    public void PlayMusic(Music music)
    {
        if (music == _music)
            return;
        _music = music;
        _duckTween?.Kill();
        _tempoTween?.Kill();
        _bgmTween?.Kill();
        _bgmTween = CreateTween();
        if (_bgm.Playing)
            _bgmTween.TweenProperty(_bgm, "volume_db", -40f, 0.3f);
        _bgmTween.TweenCallback(Callable.From(() =>
        {
            _bgm.Stop();
            _bgm.PitchScale = 1f;
            if (music == Music.None)
                return;
            _bgm.Stream = music == Music.Menu ? _menuSong : _playSong;
            _bgm.VolumeDb = -40f;
            _bgm.Play();
        }));
        if (music != Music.None)
            _bgmTween.TweenProperty(_bgm, "volume_db", MusicDb, 0.4f);
    }

    /// <summary>Speeds the music up (and raises its pitch) to make later stages feel livelier.</summary>
    public void SetMusicTempo(float scale)
    {
        _tempoTween?.Kill();
        _tempoTween = CreateTween();
        _tempoTween.TweenProperty(_bgm, "pitch_scale", scale, 0.6f);
    }

    /// <summary>Dips the music, e.g. under the result fanfare. 0 dB restores the normal level.</summary>
    public void DuckMusic(float db, float time = 0.3f)
    {
        _duckTween?.Kill();
        _duckTween = CreateTween();
        _duckTween.TweenProperty(_bgm, "volume_db", MusicDb + db, time);
    }

    /// <summary>
    /// Back to normal volume and tempo. <see cref="PlayMusic"/> keeps a track that is already playing as it is,
    /// so a game calls this when a new round starts (e.g. "다시 하기" after the result screen ducked the music).
    /// </summary>
    public void ResetMusic()
    {
        _duckTween?.Kill();
        _tempoTween?.Kill();
        if (_bgmTween?.IsRunning() == true)
            return; // A track switch is in progress; it ends at normal volume and tempo by itself
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(_bgm, "volume_db", MusicDb, 0.3f);
        tween.TweenProperty(_bgm, "pitch_scale", 1f, 0.3f);
        _duckTween = tween;
    }

    // ---- Helpers ----

    private float Jitter(float amount) => 1f + (float)(_rng.NextDouble() * 2 - 1) * amount;

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
            return;
        AudioServer.AddBus();
        var index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
    }

    private static void ApplyBusVolume(string bus, float linear)
    {
        var index = AudioServer.GetBusIndex(bus);
        AudioServer.SetBusMute(index, linear <= 0.001f);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(linear, 0.001f)));
    }

    private static AudioStreamWav ToStream(float[] samples, bool loop = false)
    {
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SfxSynth.SampleRate,
            Stereo = false,
            Data = SfxSynth.ToPcm16(samples),
        };
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = samples.Length;
        }
        return stream;
    }
}

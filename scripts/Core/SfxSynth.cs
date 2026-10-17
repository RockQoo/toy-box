using System;
using System.Collections.Generic;

namespace ToyBox.Core;

/// <summary>
/// Synthesizes sound effects and background music in code, so the game needs no audio files.
/// Outputs mono float samples in [-1, 1]. Each sound is an oscillator (with a pitch sweep) mixed with noise,
/// shaped by an envelope. Adapted from color-blast.
/// </summary>
public static class SfxSynth
{
    public const int SampleRate = 44100;

    public enum Wave { Sine, Square, Triangle }

    /// <summary>Pentatonic steps, in semitones, for each streak level. Past the end it stays at the top note.</summary>
    private static readonly int[] StreakSemitones = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24 };

    /// <summary>Pitch scale for a sound at a given streak count (1 for streak 1, rising after that).</summary>
    public static float StreakPitch(int streak)
    {
        var index = Math.Clamp(streak - 1, 0, StreakSemitones.Length - 1);
        return MathF.Pow(2f, StreakSemitones[index] / 12f);
    }

    /// <summary>Balloon pop: a sharp noise snap with a quick upward "뽁" underneath.</summary>
    public static float[] Pop() =>
        Render(0.14f, u => ExpSweep(Math.Min(u / 0.25f, 1f), 450, 1300), Wave.Sine,
            noiseMix: 0.15f, decay: 5f, gain: 0.75f, clickTime: 0.012f);

    /// <summary>Fruit caught: a round, bouncy "뿅" (a quick upward sine slide, no noise).</summary>
    public static float[] Catch() =>
        Render(0.13f, u => ExpSweep(Math.Min(u / 0.6f, 1f), 520, 1250), Wave.Sine, noiseMix: 0f, decay: 4f, gain: 0.6f);

    /// <summary>Golden fruit: a bright two-note "띠링" (B5 then E6), a step bigger than a normal catch.</summary>
    public static float[] Coin() => Arpeggio(new[] { 987.77f, 1318.5f }, 0.07f, 0.3f, 0.45f);

    /// <summary>Fruit lands on the floor: a tiny, low, soft "톡". Quiet on purpose (missing is not a failure).</summary>
    public static float[] Plop() =>
        Render(0.08f, u => ExpSweep(u, 340, 220), Wave.Sine, noiseMix: 0.05f, decay: 7f, gain: 0.4f);

    /// <summary>UI button press: a short soft blip.</summary>
    public static float[] Click() =>
        Render(0.07f, u => ExpSweep(u, 700, 1100), Wave.Sine, noiseMix: 0f, decay: 6f, gain: 0.5f);

    /// <summary>Locked or invalid tap: a low, soft "통" (not a harsh error sound).</summary>
    public static float[] Bonk() =>
        Render(0.12f, u => ExpSweep(u, 260, 160), Wave.Sine, noiseMix: 0.1f, decay: 6f, gain: 0.6f);

    /// <summary>Countdown / last-seconds tick.</summary>
    public static float[] Tick() =>
        Render(0.09f, _ => 880, Wave.Triangle, noiseMix: 0f, decay: 5f, gain: 0.5f);

    /// <summary>"시작!": a quick rising arpeggio.</summary>
    public static float[] Go() => Arpeggio(new[] { 523.25f, 783.99f, 1046.5f }, 0.07f, 0.28f, 0.5f);

    /// <summary>Milestone sparkle (every N pops): a fast high pentatonic run.</summary>
    public static float[] Cheer() =>
        Arpeggio(new[] { 1046.5f, 1174.7f, 1318.5f, 1568f, 1760f, 2093f }, 0.045f, 0.22f, 0.35f);

    /// <summary>Time up: a bright two-note "띠리링" falling to the tonic. Not sad.</summary>
    public static float[] TimeUp() => Arpeggio(new[] { 783.99f, 659.25f, 523.25f, 1046.5f }, 0.1f, 0.4f, 0.5f);

    /// <summary>New best / ranking: a short fanfare (C E G, then a held C chord).</summary>
    public static float[] Fanfare()
    {
        var intro = Arpeggio(new[] { 523.25f, 659.25f, 783.99f }, 0.1f, 0.1f, 0.45f);
        var n = (int)(0.9f * SampleRate);
        var chord = new float[n];
        foreach (var f in new[] { 523.25f, 659.25f, 783.99f, 1046.5f })
        {
            var note = Render(0.9f, _ => f, Wave.Triangle, 0f, 2.2f, 0.22f);
            for (var i = 0; i < n; i++)
                chord[i] += note[i];
        }
        return Concat(new List<float[]> { intro, chord });
    }

    /// <summary>Quiz correct answer "딩동댕!": three bell notes climbing to the octave (C5 E5 G5, held C6).</summary>
    public static float[] DingDongDang() => Bell(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.15f, 0.7f, 0.42f);

    /// <summary>Quiz question appears: a soft, high two-note "띵동" (E6 then C6), so the child looks up.</summary>
    public static float[] QuestionChime() => Bell(new[] { 1318.5f, 1046.5f }, 0.13f, 0.45f, 0.3f);

    /// <summary>Quiz wrong pick: a gentle, low "음?" that slides up like a question. Not an error buzzer.</summary>
    public static float[] Retry() =>
        Concat(new List<float[]>
        {
            Render(0.11f, u => ExpSweep(u, 330, 350), Wave.Sine, noiseMix: 0f, decay: 1.5f, gain: 0.4f),
            Render(0.2f, u => ExpSweep(u, 370, 470), Wave.Sine, noiseMix: 0f, decay: 3f, gain: 0.4f),
        });

    /// <summary>Card flip: a short papery "팔락" (a soft noise swish over a quick upward blip). Light, it is heard a lot.</summary>
    public static float[] CardFlip() =>
        Render(0.09f, u => ExpSweep(u, 700, 1400), Wave.Sine, noiseMix: 0.45f, decay: 5f, gain: 0.32f, clickTime: 0.005f);

    /// <summary>Animal match found: a bright two-bell "띵동!" climbing a fourth (G5 then C6), a step above a flip.</summary>
    public static float[] MatchChime() => Bell(new[] { 783.99f, 1046.5f, 1318.5f }, 0.09f, 0.55f, 0.4f);

    /// <summary>Maze: one step. A tiny, soft, low "톡" (heard on every move, so it stays out of the way).</summary>
    public static float[] Step() =>
        Render(0.05f, u => ExpSweep(u, 520, 380), Wave.Sine, noiseMix: 0.06f, decay: 6f, gain: 0.4f);

    /// <summary>Maze: walked into a wall. A short, round, low "통" (softer than <see cref="Bonk"/>, not an error).</summary>
    public static float[] WallBump() =>
        Render(0.1f, u => ExpSweep(u, 230, 150), Wave.Sine, noiseMix: 0.12f, decay: 7f, gain: 0.55f, clickTime: 0.004f);

    /// <summary>Maze: a star picked up. A quick sparkling bell run up to a high note ("반짝!").</summary>
    public static float[] StarPickup() => Bell(new[] { 1046.5f, 1318.5f, 1568f, 2093f }, 0.055f, 0.5f, 0.32f);

    /// <summary>Dino jump: a light, springy "보잉" (a quick upward slide with a little wobble). Heard on every jump.</summary>
    public static float[] Jump() =>
        Render(0.16f, u => ExpSweep(MathF.Min(u / 0.5f, 1f), 280, 640) * (1f + 0.05f * MathF.Sin(u * 38f)), Wave.Sine,
            noiseMix: 0f, decay: 3.5f, gain: 0.45f);

    /// <summary>Dino: an obstacle passed. A short, bright "띵" (pitch is raised by the caller within each ten).</summary>
    public static float[] PassTick() => Bell(new[] { 1174.7f }, 0.05f, 0.16f, 0.32f);

    /// <summary>
    /// Dino crash: a soft, wobbly slide down ("뿌우웅") ending in a round little thump. Cartoon-like, not an error sound.
    /// </summary>
    public static float[] Tumble() =>
        Concat(new List<float[]>
        {
            Render(0.42f, u => ExpSweep(u, 620, 210) * (1f + 0.07f * MathF.Sin(u * 55f)), Wave.Sine,
                noiseMix: 0f, decay: 1.6f, gain: 0.42f),
            Render(0.12f, u => ExpSweep(u, 200, 130), Wave.Sine, noiseMix: 0.08f, decay: 6f, gain: 0.5f, clickTime: 0.004f),
        });

    /// <summary>Result count-up step: a tiny tick (pitch is raised by the caller as the number grows).</summary>
    public static float[] CountStep() =>
        Render(0.04f, _ => 1200, Wave.Sine, noiseMix: 0f, decay: 6f, gain: 0.35f);

    /// <summary>Soft upward whoosh for a balloon drifting away or a panel sliding in.</summary>
    public static float[] Whoosh() =>
        Render(0.25f, u => ExpSweep(u, 300, 900), Wave.Sine, noiseMix: 0.6f, decay: 3f, gain: 0.25f);

    /// <summary>
    /// A looping tune. <paramref name="melody"/> and <paramref name="bass"/> are space-separated tokens "NOTE:BEATS"
    /// (e.g. "C5:1", "G4:0.5", "R:1" for a rest). Both parts should have the same total length.
    /// </summary>
    public static float[] Song(string melody, string bass, float bpm, float melodyGain = 0.22f, float bassGain = 0.13f)
    {
        var beat = 60f / bpm;
        var lead = Sequence(melody, beat, Wave.Triangle, melodyGain, 3.5f);
        var low = Sequence(bass, beat, Wave.Square, bassGain, 4f);
        var n = Math.Max(lead.Length, low.Length);
        var mix = new float[n];
        for (var i = 0; i < n; i++)
            mix[i] = Math.Clamp((i < lead.Length ? lead[i] : 0) + (i < low.Length ? low[i] : 0), -1f, 1f);
        return mix;
    }

    /// <summary>Main menu tune: relaxed, bouncy.</summary>
    public static float[] MenuSong() => Song(
        "C5:1 E5:0.5 G5:0.5 A5:1 G5:1 E5:1 D5:0.5 E5:0.5 C5:2 " +
        "D5:1 E5:0.5 D5:0.5 C5:1 A4:1 G4:1 A4:0.5 C5:0.5 D5:2 " +
        "C5:1 E5:0.5 G5:0.5 A5:1 C6:1 A5:1 G5:0.5 E5:0.5 G5:2 " +
        "A5:0.5 G5:0.5 E5:1 D5:1 E5:0.5 D5:0.5 C5:1 R:1 C5:2",
        "C3:2 C3:2 A2:2 A2:2 F2:2 F2:2 G2:2 G2:2 " +
        "C3:2 C3:2 A2:2 A2:2 F2:2 G2:2 C3:2 C3:2",
        112);

    /// <summary>In-game tune: quicker and livelier.</summary>
    public static float[] PlaySong() => Song(
        "G5:0.5 E5:0.5 G5:0.5 A5:0.5 G5:0.5 E5:0.5 C5:1 D5:0.5 E5:0.5 G5:0.5 E5:0.5 D5:2 " +
        "G5:0.5 E5:0.5 G5:0.5 A5:0.5 C6:0.5 A5:0.5 G5:1 A5:0.5 G5:0.5 E5:0.5 D5:0.5 C5:2",
        "C3:1 G2:1 C3:1 G2:1 F2:1 C3:1 G2:1 D3:1 " +
        "C3:1 G2:1 C3:1 G2:1 F2:1 G2:1 C3:1 C3:1",
        138);

    /// <summary>Converts to 16-bit little-endian PCM bytes (the format AudioStreamWav.Data expects).</summary>
    public static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var s = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xff);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        return bytes;
    }

    /// <summary>Frequency in Hz of a note name like "C5", "F#4" or "Bb3". Returns 0 for "R" (rest).</summary>
    public static float NoteFrequency(string note)
    {
        if (note == "R")
            return 0f;
        var semis = note[0] switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11,
            _ => throw new FormatException($"Bad note: {note}"),
        };
        var i = 1;
        if (note[i] == '#') { semis++; i++; }
        else if (note[i] == 'b') { semis--; i++; }
        var octave = int.Parse(note[i..]);
        var midi = (octave + 1) * 12 + semis;
        return 440f * MathF.Pow(2f, (midi - 69) / 12f);
    }

    private static float[] Sequence(string tokens, float beat, Wave wave, float gain, float decay)
    {
        var parts = new List<float[]>();
        foreach (var token in tokens.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = token.Split(':');
            var duration = float.Parse(bits[1], System.Globalization.CultureInfo.InvariantCulture) * beat;
            var f = NoteFrequency(bits[0]);
            parts.Add(f == 0 ? new float[(int)(duration * SampleRate)] : Render(duration, _ => f, wave, 0f, decay, gain));
        }
        return Concat(parts);
    }

    private static float[] Arpeggio(float[] notes, float step, float last, float gain)
    {
        var parts = new List<float[]>();
        for (var i = 0; i < notes.Length; i++)
        {
            var f = notes[i];
            var isLast = i == notes.Length - 1;
            parts.Add(Render(isLast ? last : step, _ => f, Wave.Triangle, 0f, isLast ? 3f : 2f, gain));
        }
        return Concat(parts);
    }

    /// <summary>
    /// Bell-like notes: a sine with a quieter octave overtone, each ringing on under the next (overlapping by the
    /// step), the last one held for <paramref name="last"/> seconds.
    /// </summary>
    private static float[] Bell(float[] notes, float step, float last, float gain)
    {
        var total = step * (notes.Length - 1) + last;
        var mix = new float[(int)(total * SampleRate)];
        for (var i = 0; i < notes.Length; i++)
        {
            var f = notes[i];
            var length = i == notes.Length - 1 ? last : step * 2.5f;
            var body = Render(length, _ => f, Wave.Sine, 0f, 4f, gain);
            var shine = Render(length, _ => f * 2, Wave.Sine, 0f, 7f, gain * 0.3f);
            var offset = (int)(i * step * SampleRate);
            for (var s = 0; s < body.Length && offset + s < mix.Length; s++)
                mix[offset + s] += body[s] + shine[s];
        }
        for (var s = 0; s < mix.Length; s++)
            mix[s] = Math.Clamp(mix[s], -1f, 1f);
        return mix;
    }

    /// <summary>Exponential (pitch-linear) sweep from <paramref name="from"/> to <paramref name="to"/> as u goes 0 to 1.</summary>
    private static float ExpSweep(float u, float from, float to) => from * MathF.Pow(to / from, u);

    /// <param name="freqAt">Frequency (Hz) for normalized time u (0 to 1).</param>
    /// <param name="decay">Exponential decay rate of the envelope (per normalized time).</param>
    /// <param name="clickTime">Length in seconds of the noise click at the start (0 for none).</param>
    private static float[] Render(float duration, Func<float, float> freqAt, Wave wave, float noiseMix, float decay,
        float gain, float clickTime = 0f)
    {
        const float attack = 0.003f;
        const float release = 0.008f;
        var n = (int)(duration * SampleRate);
        var buffer = new float[n];
        var rng = new Random(1234); // Fixed seed, so the same sound is produced every run
        double phase = 0;

        for (var i = 0; i < n; i++)
        {
            var t = i / (float)SampleRate;
            var u = t / duration;
            phase += freqAt(u) / SampleRate;
            var p = (float)(phase - Math.Floor(phase));

            var osc = wave switch
            {
                Wave.Square => p < 0.5f ? 1f : -1f,
                Wave.Triangle => 4f * MathF.Abs(p - 0.5f) - 1f,
                _ => MathF.Sin(MathF.Tau * p),
            };
            var noise = (float)(rng.NextDouble() * 2 - 1);
            var mix = t < clickTime ? 0.7f : noiseMix;

            var env = MathF.Min(1f, t / attack) * MathF.Exp(-decay * u) * MathF.Min(1f, (duration - t) / release);
            buffer[i] = Math.Clamp((osc * (1 - mix) + noise * mix) * env * gain, -1f, 1f);
        }
        return buffer;
    }

    private static float[] Concat(List<float[]> parts)
    {
        var total = 0;
        foreach (var p in parts) total += p.Length;
        var result = new float[total];
        var offset = 0;
        foreach (var p in parts)
        {
            Array.Copy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }
}

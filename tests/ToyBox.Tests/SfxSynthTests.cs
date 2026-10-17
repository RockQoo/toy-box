using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class SfxSynthTests
{
    [Theory]
    [InlineData("A4", 440f)]
    [InlineData("C5", 523.25f)]
    [InlineData("A5", 880f)]
    [InlineData("F#4", 369.99f)]
    [InlineData("Bb3", 233.08f)]
    [InlineData("R", 0f)]
    public void NoteFrequency(string note, float hz) => Assert.InRange(SfxSynth.NoteFrequency(note), hz - 0.05f, hz + 0.05f);

    [Fact]
    public void SongsRenderWithinRange()
    {
        foreach (var song in new[] { SfxSynth.MenuSong(), SfxSynth.PlaySong() })
        {
            Assert.True(song.Length > SfxSynth.SampleRate * 4);
            Assert.All(song, s => Assert.InRange(s, -1f, 1f));
        }
    }

    [Fact]
    public void EffectsRenderWithinRange()
    {
        foreach (var sfx in new[]
                 {
                     SfxSynth.Pop(), SfxSynth.Catch(), SfxSynth.Coin(), SfxSynth.Plop(),
                     SfxSynth.DingDongDang(), SfxSynth.QuestionChime(), SfxSynth.Retry(),
                     SfxSynth.CardFlip(), SfxSynth.MatchChime(),
                     SfxSynth.Step(), SfxSynth.WallBump(), SfxSynth.StarPickup(),
                 })
        {
            Assert.True(sfx.Length > 0);
            Assert.All(sfx, s => Assert.InRange(s, -1f, 1f));
        }
    }

    [Fact]
    public void StreakPitchRisesThenCaps()
    {
        Assert.Equal(1f, SfxSynth.StreakPitch(1), 3);
        Assert.True(SfxSynth.StreakPitch(3) > SfxSynth.StreakPitch(2));
        Assert.Equal(SfxSynth.StreakPitch(50), SfxSynth.StreakPitch(11), 3);
    }
}

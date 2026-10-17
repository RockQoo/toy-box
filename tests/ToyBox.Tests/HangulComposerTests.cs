using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class HangulComposerTests
{
    /// <summary>Types 두벌식 keys; uppercase letters mean Shift.</summary>
    private static HangulComposer Type(string keys, int max = 20)
    {
        var c = new HangulComposer(max);
        foreach (var k in keys)
            c.InputJamo(HangulComposer.JamoForKey(k, char.IsUpper(k))!.Value);
        return c;
    }

    [Theory]
    [InlineData("rbsk", "규나")]
    [InlineData("ekdhs", "다온")]
    [InlineData("tlsgP", "신혜")]
    [InlineData("dkQK", "아빠")]
    [InlineData("djaak", "엄마")]
    [InlineData("rhkdtk", "광사")]   // ㅗ+ㅏ → ㅘ
    [InlineData("dml", "의")]         // ㅡ+ㅣ → ㅢ
    [InlineData("ekfrrhk", "닭과")]   // ㄹ+ㄱ → ㄺ stays, next syllable starts fresh
    [InlineData("ekfrk", "달가")]     // composite final splits: 닭 + ㅏ → 달가
    [InlineData("ekfrdk", "닭아")]    // ㄺ + ㅇ: no composite, ㅇ starts the next syllable
    [InlineData("rkqtdl", "값이")]    // ㅂ+ㅅ → ㅄ, then ㅇ starts a new syllable
    [InlineData("Ekfl", "따리")]
    [InlineData("rkE", "가ㄸ")]       // ㄸ cannot be a final
    public void ComposesNames(string keys, string expected) => Assert.Equal(expected, Type(keys).Text);

    [Fact]
    public void BackspaceRemovesOneJamoAtATime()
    {
        var c = Type("rbs"); // 균
        c.Backspace();
        Assert.Equal("규", c.Text);
        c.Backspace();
        Assert.Equal("ㄱ", c.Text);
        c.Backspace();
        Assert.Equal("", c.Text);
    }

    [Fact]
    public void BackspaceAfterCommitRemovesWholeSyllable()
    {
        var c = Type("rbsk"); // 규 + 나 (open)
        c.Commit();
        c.Backspace();
        Assert.Equal("규", c.Text);
    }

    [Fact]
    public void MixesEnglishAndHangul()
    {
        var c = Type("rb");
        c.InputChar('A');
        Assert.Equal("규A", c.Text);
        Assert.False(c.IsComposing);
    }

    [Fact]
    public void RespectsMaxLength()
    {
        var c = Type("rkrkrk", max: 2); // 가가 → 가각 (a final fits), then ㅏ would need a third syllable
        Assert.Equal("가각", c.Text);
        Assert.False(c.InputChar('x'));
    }

    [Fact]
    public void FinalStillFitsAtMaxLength()
    {
        var c = Type("rkrks", max: 2); // 가간: adding a final keeps the length
        Assert.Equal("가간", c.Text);
    }

    [Fact]
    public void ShiftOnlyChangesDoubledKeys()
    {
        Assert.Equal('ㄲ', HangulComposer.JamoForKey('r', true));
        Assert.Equal('ㅒ', HangulComposer.JamoForKey('o', true));
        Assert.Equal('ㅏ', HangulComposer.JamoForKey('k', true));
        Assert.Null(HangulComposer.JamoForKey('1', false));
    }
}

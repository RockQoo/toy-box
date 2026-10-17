using System;
using System.Collections.Generic;
using System.Text;

namespace ToyBox.Core;

/// <summary>
/// Builds Hangul text from 두벌식 (standard 2-set) keystrokes without the OS IME. The PC name field uses it because
/// Godot's IME path delivered plain Latin letters on the user's Windows setup ("규나" typed as "rbsk").
/// Keys are kept per syllable so Backspace removes one jamo at a time, like a real IME.
/// </summary>
public sealed class HangulComposer
{
    private const string Choseong = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private const string Jungseong = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    private const string Jongseong = "\0ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

    private static readonly Dictionary<(char, char), char> CompositeVowels = new()
    {
        [('ㅗ', 'ㅏ')] = 'ㅘ', [('ㅗ', 'ㅐ')] = 'ㅙ', [('ㅗ', 'ㅣ')] = 'ㅚ',
        [('ㅜ', 'ㅓ')] = 'ㅝ', [('ㅜ', 'ㅔ')] = 'ㅞ', [('ㅜ', 'ㅣ')] = 'ㅟ', [('ㅡ', 'ㅣ')] = 'ㅢ',
    };

    private static readonly Dictionary<(char, char), char> CompositeFinals = new()
    {
        [('ㄱ', 'ㅅ')] = 'ㄳ', [('ㄴ', 'ㅈ')] = 'ㄵ', [('ㄴ', 'ㅎ')] = 'ㄶ', [('ㄹ', 'ㄱ')] = 'ㄺ',
        [('ㄹ', 'ㅁ')] = 'ㄻ', [('ㄹ', 'ㅂ')] = 'ㄼ', [('ㄹ', 'ㅅ')] = 'ㄽ', [('ㄹ', 'ㅌ')] = 'ㄾ',
        [('ㄹ', 'ㅍ')] = 'ㄿ', [('ㄹ', 'ㅎ')] = 'ㅀ', [('ㅂ', 'ㅅ')] = 'ㅄ',
    };

    /// <summary>두벌식 layout: lowercase key → jamo; Shift gives the doubled consonants / ㅒ ㅖ.</summary>
    private const string Keys = "qwertyuiopasdfghjklzxcvbnm";
    private const string Plain = "ㅂㅈㄷㄱㅅㅛㅕㅑㅐㅔㅁㄴㅇㄹㅎㅗㅓㅏㅣㅋㅌㅊㅍㅠㅜㅡ";
    private const string Shifted = "ㅃㅉㄸㄲㅆㅛㅕㅑㅒㅖㅁㄴㅇㄹㅎㅗㅓㅏㅣㅋㅌㅊㅍㅠㅜㅡ";

    private readonly StringBuilder _committed = new();
    private readonly List<char> _syllable = new(); // Jamo keys of the syllable being composed
    private readonly int _maxLength;

    public HangulComposer(int maxLength = int.MaxValue) => _maxLength = maxLength;

    /// <summary>Committed text plus the syllable being composed.</summary>
    public string Text => _committed + Compose(_syllable);

    /// <summary>True while a syllable is still open (shown underlined by the field).</summary>
    public bool IsComposing => _syllable.Count > 0;

    /// <summary>Jamo for a 두벌식 key (a–z), or null for any other key.</summary>
    public static char? JamoForKey(char key, bool shift)
    {
        var i = Keys.IndexOf(char.ToLowerInvariant(key));
        return i < 0 ? null : (shift ? Shifted : Plain)[i];
    }

    /// <summary>Adds one jamo. Returns false (and changes nothing) if the text would exceed the length limit.</summary>
    public bool InputJamo(char jamo)
    {
        var committedBefore = _committed.Length;
        var syllableBefore = new List<char>(_syllable);

        var trial = new List<char>(_syllable) { jamo };
        if (Compose(trial) is { Length: 1 })
        {
            _syllable.Add(jamo);
        }
        else if (IsVowel(jamo) && FinalKeyCount(_syllable) > 0)
        {
            // A vowel after a final consonant: the last consonant moves to start the next syllable (규ㄴ+ㅏ → 규나)
            var moved = _syllable[^1];
            _syllable.RemoveAt(_syllable.Count - 1);
            _committed.Append(Compose(_syllable));
            _syllable.Clear();
            _syllable.Add(moved);
            _syllable.Add(jamo);
        }
        else
        {
            _committed.Append(Compose(_syllable));
            _syllable.Clear();
            _syllable.Add(jamo);
        }

        if (Text.Length <= _maxLength)
            return true;
        _committed.Length = committedBefore;
        _syllable.Clear();
        _syllable.AddRange(syllableBefore);
        return false;
    }

    /// <summary>Adds a non-Hangul character (English mode, digits). Closes the open syllable first.</summary>
    public bool InputChar(char c)
    {
        if (Text.Length + 1 > _maxLength)
            return false;
        Commit();
        _committed.Append(c);
        return true;
    }

    /// <summary>Removes the last jamo of the open syllable, or the last committed character.</summary>
    public void Backspace()
    {
        if (_syllable.Count > 0)
            _syllable.RemoveAt(_syllable.Count - 1);
        else if (_committed.Length > 0)
            _committed.Length--;
    }

    /// <summary>Closes the open syllable.</summary>
    public void Commit()
    {
        _committed.Append(Compose(_syllable));
        _syllable.Clear();
    }

    public void Clear()
    {
        _committed.Clear();
        _syllable.Clear();
    }

    private static bool IsVowel(char c) => Jungseong.IndexOf(c) >= 0;
    private static bool IsConsonant(char c) => Choseong.IndexOf(c) >= 0;

    /// <summary>How many trailing keys of a valid syllable form its final consonant (0, 1 or 2).</summary>
    private static int FinalKeyCount(List<char> keys)
    {
        if (keys.Count < 3 || !IsConsonant(keys[0]))
            return 0;
        var vowelKeys = keys.Count >= 3 && IsVowel(keys[2]) ? 2 : 1;
        return keys.Count - 1 - vowelKeys;
    }

    /// <summary>
    /// The text for one syllable's keys: a full syllable (one char), a lone jamo, or more than one char if the keys
    /// do not form a single syllable (callers treat that as "does not fit").
    /// </summary>
    private static string Compose(List<char> keys)
    {
        if (keys.Count == 0)
            return "";
        if (keys.Count == 1)
            return keys[0].ToString();

        // Vowel-only: a composite vowel (ㅗ+ㅏ → ㅘ)
        if (IsVowel(keys[0]))
            return keys.Count == 2 && CompositeVowels.TryGetValue((keys[0], keys[1]), out var v) ? v.ToString() : "??";

        if (!IsConsonant(keys[0]) || !IsVowel(keys[1]))
            return "??";

        var i = 1;
        var vowel = keys[i++];
        if (i < keys.Count && IsVowel(keys[i]))
        {
            if (!CompositeVowels.TryGetValue((vowel, keys[i]), out vowel))
                return "??";
            i++;
        }

        var final = '\0';
        if (i < keys.Count)
        {
            final = keys[i++];
            if (Jongseong.IndexOf(final) <= 0)
                return "??"; // ㄸ ㅃ ㅉ (and vowels) cannot be a final
            if (i < keys.Count)
            {
                if (!CompositeFinals.TryGetValue((final, keys[i]), out final))
                    return "??";
                i++;
            }
        }
        if (i != keys.Count)
            return "??";

        var code = 0xAC00 + (Choseong.IndexOf(keys[0]) * 21 + Jungseong.IndexOf(vowel)) * 28 + Jongseong.IndexOf(final);
        return ((char)code).ToString();
    }
}

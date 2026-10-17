using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class QuizRulesTests
{
    [Theory]
    [InlineData(QuizLevel.Easy, 2)]
    [InlineData(QuizLevel.Normal, 4)]
    [InlineData(QuizLevel.Hard, 6)]
    public void ChoiceCountFollowsGdd(QuizLevel level, int count) => Assert.Equal(count, QuizRules.ChoiceCount(level));

    [Fact]
    public void VariantsAreOnePerLevelEasiestFirst()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, QuizRules.Variants.Select(v => v.Id));
        Assert.Equal("hard", QuizRules.VariantId(QuizLevel.Hard));
        Assert.Equal("보통", QuizRules.Variants[(int)QuizLevel.Normal].Label);
    }

    [Theory]
    [InlineData(1, "숫자 1을 찾아주세요!")]
    [InlineData(2, "숫자 2를 찾아주세요!")]
    [InlineData(3, "숫자 3을 찾아주세요!")]
    [InlineData(4, "숫자 4를 찾아주세요!")]
    [InlineData(5, "숫자 5를 찾아주세요!")]
    [InlineData(6, "숫자 6을 찾아주세요!")]
    [InlineData(9, "숫자 9를 찾아주세요!")]
    public void NumberPromptUsesTheRightParticle(int n, string text) => Assert.Equal(text, QuizRules.Prompt(QuizKind.Number, n));

    [Fact]
    public void ColorPrompt() => Assert.Equal("빨간색을 찾아주세요!", QuizRules.Prompt(QuizKind.Color, 0));

    [Theory]
    [InlineData(QuizMode.Color, QuizLevel.Easy)]
    [InlineData(QuizMode.Number, QuizLevel.Normal)]
    [InlineData(QuizMode.Number, QuizLevel.Hard)]
    [InlineData(QuizMode.Mix, QuizLevel.Hard)]
    [InlineData(QuizMode.Mix, QuizLevel.Easy)]
    public void QuestionsAreWellFormed(QuizMode mode, QuizLevel level)
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var deck = new QuizDeck(mode, level, new Random(seed));
            for (var q = 0; q < 30; q++)
            {
                var question = deck.Next();
                var pool = QuizRules.Pool(question.Kind, level);
                Assert.Equal(QuizRules.ChoiceCount(level), question.Choices.Count);
                Assert.Equal(question.Choices.Count, question.Choices.Distinct().Count());
                Assert.Single(question.Choices, c => c == question.Answer);
                Assert.All(question.Choices, c => Assert.Contains(c, pool));
                Assert.Equal(question.Answer, question.Choices[question.AnswerSlot]);
                if (mode != QuizMode.Mix)
                    Assert.Equal(mode == QuizMode.Color ? QuizKind.Color : QuizKind.Number, question.Kind);
            }
        }
    }

    [Theory]
    [InlineData(QuizMode.Color, QuizLevel.Easy)]
    [InlineData(QuizMode.Number, QuizLevel.Easy)]
    [InlineData(QuizMode.Number, QuizLevel.Hard)]
    [InlineData(QuizMode.Mix, QuizLevel.Normal)]
    public void NoImmediateRepeatsAndNoSlotStuckThreeTimes(QuizMode mode, QuizLevel level)
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var deck = new QuizDeck(mode, level, new Random(seed));
            var previous = deck.Next();
            var slotRun = 1;
            for (var q = 0; q < 40; q++)
            {
                var question = deck.Next();
                Assert.False(question.Kind == previous.Kind && question.Answer == previous.Answer,
                    $"seed {seed}: {question.Kind} {question.Answer} twice in a row");
                slotRun = question.AnswerSlot == previous.AnswerSlot ? slotRun + 1 : 1;
                Assert.True(slotRun <= 2, $"seed {seed}: answer in slot {question.AnswerSlot} three times running");
                previous = question;
            }
        }
    }

    [Fact]
    public void AnswersCycleThroughEveryValueBeforeRepeating()
    {
        var deck = new QuizDeck(QuizMode.Color, QuizLevel.Normal, new Random(3));
        var firstSix = Enumerable.Range(0, QuizRules.ColorCount).Select(_ => deck.Next().Answer).ToList();
        Assert.Equal(Enumerable.Range(0, QuizRules.ColorCount), firstSix.Order());
    }

    [Fact]
    public void HardNumbersGoUpToNine()
    {
        Assert.Equal(9, QuizRules.Pool(QuizKind.Number, QuizLevel.Hard).Max());
        Assert.Equal(5, QuizRules.Pool(QuizKind.Number, QuizLevel.Normal).Max());
        var deck = new QuizDeck(QuizMode.Number, QuizLevel.Hard, new Random(1));
        var answers = Enumerable.Range(0, 9).Select(_ => deck.Next().Answer).ToHashSet();
        Assert.Equal(9, answers.Count);
    }

    [Fact]
    public void MixNeverRunsThreeOfAKind()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var deck = new QuizDeck(QuizMode.Mix, QuizLevel.Normal, new Random(seed));
            var kinds = Enumerable.Range(0, 30).Select(_ => deck.Next().Kind).ToList();
            for (var i = 2; i < kinds.Count; i++)
                Assert.False(kinds[i] == kinds[i - 1] && kinds[i] == kinds[i - 2]);
            Assert.Contains(QuizKind.Color, kinds);
            Assert.Contains(QuizKind.Number, kinds);
        }
    }

    [Fact]
    public void ScoreCountsOnlyFirstTryAnswers()
    {
        var round = new QuizRound();
        Assert.Equal(QuizAnswer.CorrectFirstTry, round.Answer(true));
        Assert.Equal(QuizAnswer.Retry, round.Answer(false));
        Assert.Equal(1, round.Index); // A wrong pick keeps the same question
        Assert.Equal(QuizAnswer.Retry, round.Answer(false));
        Assert.Equal(2, round.Misses);
        Assert.Equal(QuizAnswer.CorrectAfterRetry, round.Answer(true));
        Assert.Equal(0, round.Misses);
        Assert.Equal(1, round.Score);
        Assert.Equal(new[] { true, false }, round.FirstTry);
    }

    [Fact]
    public void RoundEndsAfterTenQuestions()
    {
        var round = new QuizRound();
        for (var i = 0; i < QuizRules.QuestionCount; i++)
        {
            Assert.False(round.Finished);
            round.Answer(true);
        }
        Assert.True(round.Finished);
        Assert.True(round.Perfect);
        Assert.Equal(10, round.Score);
        Assert.Throws<InvalidOperationException>(() => round.Answer(true));
    }

    [Fact]
    public void TimeAddsUpUntilTheEnd()
    {
        var round = new QuizRound();
        round.AddTime(1.5);
        round.AddTime(0.25);
        round.AddTime(-1);
        Assert.Equal(1750, round.TimeMs);
        for (var i = 0; i < QuizRules.QuestionCount; i++)
            round.Answer(i % 2 == 0);
        Assert.False(round.Finished); // Wrong taps do not advance
        while (!round.Finished)
            round.Answer(true);
        round.AddTime(5);
        Assert.Equal(1750, round.TimeMs);
        Assert.False(round.Perfect);
    }
}

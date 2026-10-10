using MessagePurify.Core;
using MessagePurify.RuleBaseline;

namespace MessagePurify.Core.Tests;

public sealed class RuleFixtureTests
{
    private static string CorpusPath => Path.Combine(AppContext.BaseDirectory, "fixtures", "rules", "synthetic.json");

    [Fact]
    public void SyntheticCorpusMatchesHandwrittenGold()
    {
        var report = RuleFixtureRunner.Run(CorpusPath);
        Assert.True(report.Corpus.Synthetic);
        Assert.True(report.Results.Count >= 45);
        Assert.True(report.Success, report.ToMarkdown());
        Assert.Empty(report.HighImpactErrors);
    }

    [Fact]
    public void WrongAutomaticDateIsNotCountedAsCorrect()
    {
        var fixture = GoldTask();
        var report = Evaluate(fixture, Candidate(fixture, RuleDisposition.Task,
            new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12), null, "China Standard Time")));
        Assert.False(report.Success);
        Assert.Equal(new FixtureMetric(0, 1), report.FormalPrecision);
        Assert.Equal(new FixtureMetric(0, 1), report.ActionRecall);
        Assert.Equal(new FixtureMetric(0, 1), report.AutomaticCoverage);
        Assert.Single(report.HighImpactErrors);
    }

    [Fact]
    public void AllPendingCanRecallWithoutPretendingToAutomate()
    {
        var fixture = GoldTask();
        var report = Evaluate(fixture, Candidate(fixture, RuleDisposition.Pending, null));
        Assert.False(report.Success); // Golden Task disposition still fails strict regression comparison.
        Assert.Equal(new FixtureMetric(1, 1), report.ActionRecall);
        Assert.Equal(new FixtureMetric(0, 1), report.AutomaticCoverage);
        Assert.Equal(new FixtureMetric(1, 1), report.PendingShare);
        Assert.Equal("N/A (0/0)", report.FormalPrecision.Display);
        Assert.Empty(report.HighImpactErrors);
    }

    [Fact]
    public void EmptyBusinessResultsHaveUndefinedRatios()
    {
        var fixture = new FixtureCase { Id = "negative", Category = "negative", Message = new() { Text = "聊天" } };
        var report = RuleFixtureRunner.Evaluate(new FixtureCorpus { Synthetic = true, Cases = [fixture] },
            (_, _) => new RuleExtractionResult("test", []));
        Assert.True(report.Success);
        Assert.Equal("N/A (0/0)", report.FormalPrecision.Display);
        Assert.Equal("N/A (0/0)", report.ActionRecall.Display);
        Assert.Equal("N/A (0/0)", report.AutomaticCoverage.Display);
        Assert.Equal("N/A (0/0)", report.PendingShare.Display);
    }

    [Fact]
    public void NonGoldAutomaticChangeIsHighImpact()
    {
        var fixture = GoldTask();
        fixture.ExpectedCandidates[0].Action = RuleAction.Cancel;
        fixture.ExpectedCandidates[0].Disposition = RuleDisposition.Pending;
        fixture.ExpectedCandidates[0].RequiresAction = false;
        fixture.ExpectedCandidates[0].Date = null;
        fixture.ExpectedCandidates[0].TimeKind = null;
        fixture.ExpectedCandidates[0].Target = "right-target";
        var candidate = Candidate(fixture, RuleDisposition.Change, null) with
        { Action = RuleAction.Cancel, TargetTaskId = "wrong-target" };
        var report = Evaluate(fixture, candidate);
        Assert.False(report.Success);
        Assert.Single(report.HighImpactErrors);
        Assert.Equal(new FixtureMetric(0, 0), report.AutomaticCoverage);
    }

    [Fact]
    public void IdenticalCrossGroupTextKeepsDistinctEvidenceKeys()
    {
        var corpus = RuleFixtureRunner.Load(CorpusPath);
        var copies = corpus.Cases.Where(c => c.Id is "cross-group-copy-a" or "cross-group-copy-b").ToArray();
        Assert.Equal(2, copies.Length);
        Assert.Equal(copies[0].Message.Text, copies[1].Message.Text);
        var first = RuleFixtureRunner.BuildMessage(copies[0]);
        var second = RuleFixtureRunner.BuildMessage(copies[1]);
        Assert.NotEqual(first.Key, second.Key);
        var firstResult = MessageRuleExtractor.Extract(first, RuleFixtureRunner.BuildContext(copies[0].Context));
        var secondResult = MessageRuleExtractor.Extract(second, RuleFixtureRunner.BuildContext(copies[1].Context));
        Assert.All(firstResult.Candidates, c => Assert.Contains(first.Key, c.Evidence));
        Assert.All(secondResult.Candidates, c => Assert.Contains(second.Key, c.Evidence));
        Assert.DoesNotContain(first.Key, secondResult.Candidates.SelectMany(c => c.Evidence));
    }

    private static FixtureCase GoldTask() => new()
    {
        Id = "required-task", Category = "metric-regression", Message = new() { Text = "明天交作业" },
        ExpectedCandidates = [new() { RequiresAction = true, Action = RuleAction.Submit,
            Disposition = RuleDisposition.Task, Course = "高等数学", Date = "2026-10-11", TimeKind = TimeKind.Deadline }]
    };

    private static FixtureReport Evaluate(FixtureCase fixture, RuleCandidate candidate) =>
        RuleFixtureRunner.Evaluate(new FixtureCorpus { Synthetic = true, Cases = [fixture] },
            (_, _) => new RuleExtractionResult("metric-test", [candidate]));

    private static RuleCandidate Candidate(FixtureCase fixture, RuleDisposition disposition, TaskTime? time)
    {
        var assessment = new RuleAssessment("人工构造的评估器回归输入", true);
        return new RuleCandidate("作业", RuleAction.Submit, disposition, "高等数学", time, null, null, 1,
            new(assessment, assessment, assessment, assessment, assessment, assessment),
            disposition == RuleDisposition.Pending ? ["时间需要核对"] : [], [RuleFixtureRunner.BuildMessage(fixture).Key]);
    }
}

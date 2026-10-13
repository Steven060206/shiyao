using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Tests;

public sealed class CrossGroupMergeFinderTests
{
    private const string Account = "10000";
    private const string Text = "10月20日提交第一轮数学实验报告";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(8));
    private static readonly TaskTime Due = new(TimeKind.Deadline, new(2026, 10, 20), null, "Asia/Shanghai");
    private readonly Dictionary<MessageKey, StoredMessage> messages = [];
    private readonly List<CrossGroupMergeGroup> groups = [new("g1", "数学群", "数学", false), new("g2", "课程通知群", "数学", false)];

    private TaskItem Task(string id, string group = "g1", string text = Text, string account = Account)
    {
        var key = new MessageKey(account, group, "m-" + id);
        var reliable = new RuleAssessment("已确认", true);
        var candidate = new RuleCandidate(text, RuleAction.Submit, RuleDisposition.Task, "数学", Due,
            null, null, 2, new(reliable, reliable, reliable, reliable, new("普通行动不要求可信发布者", false), reliable), [], [key]);
        messages[key] = new(new(key, "teacher", Now, [new(SegmentKind.Text, text)]), MessageOrigin.History,
            "history", Now, 1, new("m2-local-v1", [candidate]), Now);
        return new(id, account, text, "数学", RuleAction.Submit, Due, TaskState.Active,
            false, 2, 1, 1, [key], Now, null, null, null);
    }

    private void Candidate(TaskItem task, Func<RuleCandidate, RuleCandidate> update)
    {
        var key = task.Evidence[0];
        var message = messages[key];
        messages[key] = message with { Extraction = message.Extraction! with { Candidates = [update(message.Extraction.Candidates[0])] } };
    }

    private IReadOnlyList<CrossGroupMergeMatch> Find(IEnumerable<TaskItem> tasks, params PendingReview[] reviews) =>
        CrossGroupMergeFinder.Find(tasks.ToArray(), reviews, groups, key => messages.GetValueOrDefault(key));

    [Fact]
    public void SameOriginalTextAndCandidateAcrossConfirmedGroups_OffersOneStableOrderedSuggestion()
    {
        var first = Task("a");
        var second = Task("b", "g2");
        var match = Assert.Single(Find([second, first]));
        Assert.Equal("a", match.FirstTaskId);
        Assert.Equal("b", match.SecondTaskId);
        Assert.Equal(1, match.FirstTaskVersion);
        Assert.Equal(1, match.SecondTaskVersion);
        var replay = Assert.Single(Find([first, second]));
        Assert.Equal(match.Id, replay.Id);
        Assert.Equal(match.FirstTaskId, replay.FirstTaskId);
        Assert.Equal(match.SecondTaskId, replay.SecondTaskId);
        Assert.InRange(match.Id.Length, 1, 64);
        Assert.Contains(match.Reasons, reason => reason.Contains("原文"));
        Assert.Contains(match.Reasons, reason => reason.Contains("课程"));
        Assert.Contains(match.Reasons, reason => reason.Contains("行动"));
        Assert.Contains(match.Reasons, reason => reason.Contains("时间"));
        Assert.Contains(match.Reasons, reason => reason.Contains("确认"));
    }

    [Fact]
    public void CurrentManualTitleCourseDatePriorityAndStateChanges_DoNotReplaceOriginalEvidence()
    {
        var first = Task("a");
        var second = Task("b", "g2") with { Title = "改名后的10月30日报告", Course = "自定义", Time = Due with { Date = new(2026, 10, 30) },
            TimeProtected = true, State = TaskState.Completed, PriorityOverride = 0, Version = 7 };
        var match = Assert.Single(Find([first, second]));
        Assert.Equal(7, match.SecondTaskVersion);
        Assert.NotEqual(first.Title, second.Title);
        Assert.NotEqual(first.Time, second.Time);
        Assert.NotEqual(first.State, second.State);
    }

    [Fact]
    public void UnicodeAndWhitespaceNormalization_PreservesSameOriginalMeaning()
    {
        var first = Task("a", text: "１０月２０日　提交第一轮数学实验报告");
        var second = Task("b", "g2", "10月20日  提交第一轮数学实验报告");
        Assert.Single(Find([first, second]));
    }

    [Theory]
    [InlineData("第一轮实验安排", "第二轮实验安排", true)]
    [InlineData("1班实验安排", "2班实验安排", true)]
    [InlineData("", "", true)]
    [InlineData("第一轮实验安排", "第二轮实验安排", false)]
    public void RealExtractionWithSameNewTextButDifferentQuotedContext_IsExcluded(
        string firstQuote, string secondQuote, bool hasReference)
    {
        TaskItem QuotedTask(string id, string groupId, string quoteText)
        {
            var task = Task(id, groupId, "10月20日提交数学实验报告");
            var key = task.Evidence[0];
            var reference = hasReference ? new MessageKey(Account, groupId, "original-" + id) : null;
            var original = messages[key];
            var message = original.Message with
            {
                Segments = [new(SegmentKind.Quote, quoteText, reference), .. original.Message.Segments]
            };
            var context = new RuleContext(Account, groupId, "Asia/Shanghai", Now, ConfirmedCourse: "数学");
            var extraction = MessageRuleExtractor.Extract(message, context);
            var candidate = Assert.Single(extraction.Candidates);
            Assert.Equal(RuleDisposition.Task, candidate.Disposition);
            Assert.True(candidate.Assessments.Audience.Reliable);
            messages[key] = original with { Message = message, Extraction = extraction };
            if (reference is not null)
            {
                Assert.Contains(reference, candidate.Evidence);
                var quotedMessage = new SchoolMessage(reference, "teacher", Now.AddDays(-1), [new(SegmentKind.Text, quoteText)]);
                messages[reference] = new(quotedMessage, MessageOrigin.History, "quoted-history", Now, 1,
                    MessageRuleExtractor.Extract(quotedMessage, context), Now);
            }
            return task with { Title = candidate.Title, Time = candidate.Time!, Evidence = candidate.Evidence };
        }

        var first = QuotedTask("a", "g1", firstQuote);
        var second = QuotedTask("b", "g2", secondQuote);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Time, second.Time);
        Assert.Empty(Find([first, second]));
    }

    [Theory]
    [InlineData("10月21日提交第一轮数学实验报告")]
    [InlineData("10月20日提交第二轮数学实验报告")]
    [InlineData("10月20日重新提交第一轮数学实验报告")]
    [InlineData("10月20日提交第一轮数学实验报告附件")]
    public void DifferentOriginalText_CannotMatchThroughEditedCurrentTitles(string text)
    {
        var first = Task("a");
        var second = Task("b", "g2", text) with { Title = Text };
        Assert.Empty(Find([first, second]));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("action")]
    [InlineData("course")]
    [InlineData("time")]
    [InlineData("time-kind")]
    [InlineData("time-clock")]
    [InlineData("time-zone")]
    public void DifferentOriginalCandidate_EvenWithSameFullMessageAndCurrentTaskIsExcluded(string field)
    {
        var first = Task("a");
        var second = Task("b", "g2");
        Candidate(second, candidate => field switch
        {
            "title" => candidate with { Title = "另一项实验报告" },
            "action" => candidate with { Action = RuleAction.Complete },
            "course" => candidate with { Course = "物理" },
            "time" => candidate with { Time = Due with { Date = new(2026, 10, 21) } },
            "time-kind" => candidate with { Time = Due with { Kind = TimeKind.Start } },
            "time-clock" => candidate with { Time = Due with { Time = new(10, 0) } },
            _ => candidate with { Time = Due with { TimeZoneId = "UTC" } }
        });
        Assert.Empty(Find([first, second]));
    }

    [Fact]
    public void RelativeDatesAcrossDifferentDays_AreDifferentOriginalTaskTimes()
    {
        var first = Task("a", text: "明天提交数学报告");
        var second = Task("b", "g2", "明天提交数学报告");
        Candidate(second, candidate => candidate with { Time = Due with { Date = new(2026, 10, 21) } });
        Assert.Empty(Find([first, second]));
    }

    [Theory]
    [InlineData("1班数学", "2班数学")]
    [InlineData("一班数学", "二班数学")]
    [InlineData("数学（1）班", "数学（2）班")]
    [InlineData("数学二班", "数学十一班")]
    public void DifferentNamedClasses_EvenWithIdenticalTemplateAreExcluded(string name1, string name2)
    {
        groups[0] = groups[0] with { Name = name1 };
        groups[1] = groups[1] with { Name = name2 };
        Assert.Empty(Find([Task("a"), Task("b", "g2")]));
    }

    [Fact]
    public void SameClassWrittenWithArabicAndChineseNumbers_IsNotAContradiction()
    {
        groups[0] = groups[0] with { Name = "数学01班" };
        groups[1] = groups[1] with { Name = "通知一班" };
        Assert.Single(Find([Task("a"), Task("b", "g2")]));
    }

    [Theory]
    [InlineData("same-group")]
    [InlineData("other-account")]
    [InlineData("manual")]
    [InlineData("missing-message")]
    [InlineData("missing-extraction")]
    [InlineData("mismatched-message-key")]
    [InlineData("private-message")]
    [InlineData("image-only")]
    [InlineData("multiple-candidates")]
    [InlineData("pending-candidate")]
    [InlineData("changed-candidate")]
    [InlineData("missing-candidate-time")]
    [InlineData("excluded-group")]
    [InlineData("unconfirmed-group")]
    [InlineData("changed-group-course")]
    [InlineData("missing-group")]
    public void InsufficientOrIneligibleEvidence_IsExcluded(string reason)
    {
        var first = Task("a");
        var second = Task("b", reason == "same-group" ? "g1" : "g2", account: reason == "other-account" ? "20000" : Account);
        var key = second.Evidence[0];
        var stored = messages[key];
        switch (reason)
        {
            case "manual": second = second with { Source = "manual", Evidence = [] }; break;
            case "missing-message": messages.Remove(key); break;
            case "missing-extraction": messages[key] = stored with { Extraction = null }; break;
            case "mismatched-message-key": messages[key] = stored with { Message = stored.Message with { Key = key with { GroupId = "wrong" } } }; break;
            case "private-message": messages[key] = stored with { Message = stored.Message with { IsPrivate = true } }; break;
            case "image-only": messages[key] = stored with { Message = stored.Message with { Segments = [new(SegmentKind.Image, Text)] } }; break;
            case "multiple-candidates": messages[key] = stored with { Extraction = stored.Extraction! with { Candidates = [stored.Extraction.Candidates[0], stored.Extraction.Candidates[0] with { Title = "另外的交付物" }] } }; break;
            case "pending-candidate": Candidate(second, c => c with { Disposition = RuleDisposition.Pending }); break;
            case "changed-candidate": Candidate(second, c => c with { Disposition = RuleDisposition.Change, Action = RuleAction.Postpone }); break;
            case "missing-candidate-time": Candidate(second, c => c with { Time = null }); break;
            case "excluded-group": groups[1] = groups[1] with { Excluded = true }; break;
            case "unconfirmed-group": groups[1] = groups[1] with { Course = null }; break;
            case "changed-group-course": groups[1] = groups[1] with { Course = "物理" }; break;
            case "missing-group": groups.RemoveAt(1); break;
        }
        Assert.Empty(Find([first, second]));
    }

    [Theory]
    [InlineData("action")]
    [InlineData("audience")]
    [InlineData("course")]
    [InlineData("time")]
    [InlineData("association")]
    public void UnreliableOriginalAssessment_IsExcluded(string axis)
    {
        var first = Task("a");
        var second = Task("b", "g2");
        var unreliable = new RuleAssessment("尚未确认", false);
        Candidate(second, c => c with { Assessments = axis switch
        {
            "action" => c.Assessments with { Action = unreliable },
            "audience" => c.Assessments with { Audience = unreliable },
            "course" => c.Assessments with { Course = unreliable },
            "time" => c.Assessments with { Time = unreliable },
            _ => c.Assessments with { Association = unreliable }
        } });
        Assert.Empty(Find([first, second]));
    }

    [Fact]
    public void SharedEvidenceBetweenTasksOrWithThirdTask_CannotBecomeMergeProof()
    {
        var first = Task("a");
        var second = Task("b", "g2");
        Assert.Empty(Find([first, second with { Evidence = [.. second.Evidence, .. first.Evidence] }]));
        var other = Task("c") with { Evidence = first.Evidence };
        Assert.Empty(Find([first, second, other]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeparateDecisionFromEitherSide_RemainsEffective(bool secondSide)
    {
        var first = Task("a");
        var second = Task("b", "g2");
        if (secondSide) second = second with { SeparateTaskIds = [first.Id] };
        else first = first with { SeparateTaskIds = [second.Id] };
        Assert.Empty(Find([first, second]));
    }

    [Theory]
    [InlineData("alias-decision")]
    [InlineData("alias-target")]
    [InlineData("alias-chain")]
    public void SeparateDecisionsOnMergedSourcesAndReferences_AreInherited(string kind)
    {
        var first = Task("a");
        var second = Task("b", "g2");
        var alias = Task("old") with { MergedIntoId = first.Id };
        var tasks = new List<TaskItem> { first, second, alias };
        if (kind == "alias-decision") tasks[2] = alias with { SeparateTaskIds = [second.Id] };
        else if (kind == "alias-target") tasks[1] = second with { SeparateTaskIds = [alias.Id] };
        else
        {
            tasks[2] = alias with { MergedIntoId = "middle", SeparateTaskIds = [second.Id] };
            tasks.Add(Task("middle") with { MergedIntoId = first.Id });
        }
        Assert.Empty(Find(tasks));
    }

    [Fact]
    public void OrdinaryMergedSourceIsNotAnIndependentTask_AndMergeRelationsAreNotSplitFamilies()
    {
        var first = Task("a") with { RelatedItemIds = ["old"] };
        var second = Task("b", "g2");
        var old = Task("old") with { MergedIntoId = first.Id };
        Assert.Single(Find([first, old, second]));
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("child")]
    [InlineData("pending")]
    [InlineData("accepted")]
    public void AnyTaskInSplitFamily_IsExcludedEvenIfSourceDoesNotOverlap(string member)
    {
        var parent = Task("parent") with { RelatedItemIds = ["child"] };
        var child = Task("child") with { RelatedItemIds = [parent.Id] };
        var other = Task("other", "g2");
        if (member is "parent" or "child")
        {
            Assert.Empty(Find([parent, child, other]));
            return;
        }
        var candidate = messages[parent.Evidence[0]].Extraction!.Candidates[0];
        var review = new PendingReview("review", Account, candidate, member == "accepted" ? ReviewState.Accepted : ReviewState.Pending,
            Now, null, null, 1, AcceptedTaskId: member == "accepted" ? child.Id : null, SplitParentTaskId: parent.Id);
        Assert.Empty(Find(member == "accepted" ? [parent with { RelatedItemIds = [] }, child with { RelatedItemIds = [] }, other] : [parent with { RelatedItemIds = [] }, other], review));
    }
}

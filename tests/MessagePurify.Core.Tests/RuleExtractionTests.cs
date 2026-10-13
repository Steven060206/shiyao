using MessagePurify.Core;
using Xunit;

namespace MessagePurify.Core.Tests;

public class RuleExtractionTests
{
    private static readonly DateTimeOffset SentAt = DateTimeOffset.Parse("2026-10-10T10:00:00+08:00");
    private static readonly MessageKey Original = new("a", "g", "original");
    private static SchoolMessage Message(string text, string sender = "teacher") =>
        new(new("a", "g", "message"), sender, SentAt, [new(SegmentKind.Text, text)]);
    private static RuleContext Context(string? course = "高等数学", IReadOnlyList<KnownTask>? tasks = null) =>
        new("a", "g", "China Standard Time", SentAt, course, new HashSet<string> { "teacher" }, KnownTasks: tasks);
    private static KnownTask Task(TaskState state = TaskState.Active, string title = "实验报告", string id = "report") =>
        new(id, title, "高等数学", new(TimeKind.Deadline, new(2026, 10, 11), null,
            "China Standard Time"), state, [Original], "teacher");
    private static RuleCandidate Single(string text, RuleContext? context = null, string sender = "teacher") =>
        Assert.Single(MessageRuleExtractor.Extract(Message(text, sender), context ?? Context()).Candidates);

    [Fact]
    public void ClassMonitorImageSubmissionRequirementKeepsDeadlineAsSuggestion()
    {
        var sentAt = DateTimeOffset.Parse("2026-09-26T12:43:00+08:00");
        var message = new SchoolMessage(new("a", "g", "image-notice"), "class-monitor", sentAt,
            [new(SegmentKind.Image, "10月12日之前所有人都要交齐", AssetId: "school-notice")]);

        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context() with { Now = sentAt }).Candidates);

        Assert.Equal(RuleAction.Submit, candidate.Action);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Null(candidate.Time);
        Assert.Null(candidate.TargetTaskId);
        Assert.Equal(message.Key, Assert.Single(candidate.Evidence));
        Assert.Equal(new DateOnly(2026, 10, 12), candidate.SuggestedTime!.Date);
        Assert.Equal(TimeKind.Deadline, candidate.SuggestedTime.Kind);
        Assert.Contains("图片识别仅作建议，需要查看原图确认", candidate.ReviewReasons);
    }

    [Fact]
    public void ShortClearAction_IsTaskWithDateAndSixIndependentAssessments()
    {
        var candidate = Single("明天交作业");
        Assert.Equal(RuleAction.Submit, candidate.Action);
        Assert.Equal(RuleDisposition.Task, candidate.Disposition);
        Assert.Equal(new DateOnly(2026, 10, 11), candidate.Time!.Date);
        Assert.Null(candidate.Time.Time);
        Assert.Equal(TimeKind.Deadline, candidate.Time.Kind);
        Assert.True(candidate.Assessments.Action.Reliable);
        Assert.True(candidate.Assessments.Audience.Reliable);
        Assert.True(candidate.Assessments.Course.Reliable);
        Assert.True(candidate.Assessments.Time.Reliable);
        Assert.True(candidate.Assessments.Source.Reliable);
        Assert.True(candidate.Assessments.Association.Reliable);
        Assert.Empty(candidate.ReviewReasons);
        Assert.Equal(Message("明天交作业").Key, Assert.Single(candidate.Evidence));
    }

    [Theory]
    [InlineData("明天交作业吗")]
    [InlineData("是不是明天交作业")]
    [InlineData("明天交作业？")]
    [InlineData("不需要交作业")]
    [InlineData("不用提交实验报告")]
    [InlineData("今天吃什么")]
    public void QuestionsNegationsAndChat_DoNotCreateTasks(string text)
    {
        var candidate = Single(text);
        Assert.Equal(RuleDisposition.Ignored, candidate.Disposition);
        Assert.NotEmpty(candidate.ReviewReasons);
    }

    [Theory]
    [InlineData("听说明天交作业")]
    [InlineData("可能明天交作业")]
    [InlineData("老师说明天交作业")]
    [InlineData("同学说明天取消实验报告")]
    public void SpeculationAndUnattributedRetelling_ArePending(string text)
    {
        var candidate = Single(text, Context(tasks: [Task()]));
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.False(candidate.Assessments.Action.Reliable);
    }

    [Fact]
    public void AQuestionDoesNotDiscardAnotherIndependentAction()
    {
        var candidates = MessageRuleExtractor.Extract(Message("明天交作业吗？后天交实验报告"), Context()).Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.Equal(RuleDisposition.Ignored, candidates[0].Disposition);
        Assert.Equal(RuleDisposition.Task, candidates[1].Disposition);
        Assert.Equal(new DateOnly(2026, 10, 12), candidates[1].Time!.Date);
    }

    [Theory]
    [InlineData("实验报告没有取消")]
    [InlineData("实验报告不会取消")]
    [InlineData("实验报告没有延期")]
    [InlineData("实验报告不会延期")]
    [InlineData("实验报告不要重新提交")]
    public void NegatedHighImpactActionsCannotChangeTasks(string text)
    {
        Assert.Equal(RuleDisposition.Ignored, Single(text, Context(tasks: [Task()])).Disposition);
    }

    [Fact]
    public void NegatedCancellationDoesNotDiscardFollowingSubmission()
    {
        var candidates = MessageRuleExtractor.Extract(Message("实验报告没有取消，明天仍要提交"), Context(tasks: [Task()])).Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.Equal(RuleDisposition.Ignored, candidates[0].Disposition);
        Assert.Equal(RuleDisposition.Task, candidates[1].Disposition);
        Assert.Equal(RuleAction.Submit, candidates[1].Action);
    }

    [Fact]
    public void VoluntaryOneActionDoesNotMakeAnotherActionOptional()
    {
        var candidates = MessageRuleExtractor.Extract(Message("明天必须参加考试；后天自愿参加讲座"), Context()).Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.Equal(RuleDisposition.Task, candidates[0].Disposition);
        Assert.Equal(RuleDisposition.Notice, candidates[1].Disposition);
    }

    [Theory]
    [InlineData("自愿参加", RuleAction.Attend)]
    [InlineData("自愿报名", RuleAction.Register)]
    public void BareVoluntaryActionNeedsTimeInsteadOfBeingDiscarded(string text, RuleAction action)
    {
        var candidate = Single(text);
        Assert.Equal(action, candidate.Action);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Null(candidate.Time);
    }

    [Fact]
    public void BareVoluntaryActionAfterSubmissionIsAnIndependentUnclearAction()
    {
        var candidates = MessageRuleExtractor.Extract(Message("明天交实验报告，自愿参加"), Context()).Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.Equal(RuleDisposition.Task, candidates[0].Disposition);
        Assert.Equal(RuleAction.Attend, candidates[1].Action);
        Assert.Equal(RuleDisposition.Pending, candidates[1].Disposition);
    }

    [Theory]
    [InlineData("10月8日布置的作业，10月12日提交", RuleAction.Submit)]
    [InlineData("实验报告原定10月11日截止，延期至10月12日", RuleAction.Postpone)]
    public void ExplicitBackgroundDatesDoNotReplaceActionTime(string text, RuleAction action)
    {
        var candidate = Single(text, Context(tasks: [Task()]));
        Assert.Equal(action, candidate.Action);
        Assert.Equal(new DateOnly(2026, 10, 12), candidate.Time!.Date);
        Assert.Equal(action == RuleAction.Postpone ? RuleDisposition.Change : RuleDisposition.Task, candidate.Disposition);
    }

    [Theory]
    [InlineData("明天交作业", null)]
    [InlineData("下周交作业", "高等数学")]
    [InlineData("10月12日或13日交作业", "高等数学")]
    [InlineData("2月30日交作业", "高等数学")]
    public void UnknownCourseMissingAmbiguousAndInvalidTime_ArePending(string text, string? course)
    {
        Assert.Equal(RuleDisposition.Pending, Single(text, Context(course)).Disposition);
    }

    [Theory]
    [InlineData("2025级同学明天交作业")]
    [InlineData("二年级同学明天交作业")]
    [InlineData("3班明天交作业")]
    [InlineData("选修这门课的同学明天交作业")]
    [InlineData("仅限研究生明天提交实验报告")]
    [InlineData("补考同学明天参加考试")]
    public void ExplicitAudienceCannotBeAssumedFromConfirmedCourse(string text)
    {
        var candidate = Single(text);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.False(candidate.Assessments.Audience.Reliable);
        Assert.True(candidate.Assessments.Course.Reliable);
    }

    [Fact]
    public void OrdinaryClearHomeworkCanBeTaskWithoutTrustedSender()
    {
        var candidate = Single("明天交作业", sender: "student");
        Assert.Equal(RuleDisposition.Task, candidate.Disposition);
        Assert.False(candidate.Assessments.Source.Reliable);
    }

    [Fact]
    public void MultipleDeliverablesHaveIndependentTimesAndSharedEvidence()
    {
        var candidates = MessageRuleExtractor.Extract(Message("明天交实验报告，格式为PDF；下周一交课程论文"), Context()).Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal(RuleDisposition.Task, candidate.Disposition));
        Assert.Contains("PDF", candidates[0].Title);
        Assert.Equal(new DateOnly(2026, 10, 11), candidates[0].Time!.Date);
        Assert.Equal(new DateOnly(2026, 10, 12), candidates[1].Time!.Date);
        Assert.Equal(candidates[0].Evidence, candidates[1].Evidence);
    }

    [Fact]
    public void RegistrationDeadlineAndEventStartKeepTheirTimeRoles()
    {
        var candidates = MessageRuleExtractor.Extract(Message("10月12日报名截止，10月15日活动开始，自愿参加"), Context()).Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.Equal(RuleAction.Register, candidates[0].Action);
        Assert.Equal(RuleAction.Attend, candidates[1].Action);
        Assert.All(candidates, candidate => Assert.Equal(RuleDisposition.Notice, candidate.Disposition));
        Assert.Equal(TimeKind.Deadline, candidates[0].Time!.Kind);
        Assert.Equal(TimeKind.Start, candidates[1].Time!.Kind);
        Assert.Equal(new DateOnly(2026, 10, 12), candidates[0].Time!.Date);
        Assert.Equal(new DateOnly(2026, 10, 15), candidates[1].Time!.Date);
    }

    [Theory]
    [InlineData("明天考试", RuleAction.Attend, TimeKind.Start)]
    [InlineData("明天必须参加讲座", RuleAction.Attend, TimeKind.Start)]
    [InlineData("明天完成作业", RuleAction.Complete, TimeKind.Deadline)]
    [InlineData("明天报名截止", RuleAction.Register, TimeKind.Deadline)]
    public void MandatoryActionsHaveTheAppropriateTimeKind(string text, RuleAction action, TimeKind kind)
    {
        var candidate = Single(text);
        Assert.Equal(action, candidate.Action);
        Assert.Equal(RuleDisposition.Task, candidate.Disposition);
        Assert.Equal(kind, candidate.Time!.Kind);
    }

    [Fact]
    public void TrustedCancelNeedsUniqueTargetButDoesNotRequireNewDate()
    {
        var candidate = Single("取消实验报告", Context(tasks: [Task()]));
        Assert.Equal(RuleDisposition.Change, candidate.Disposition);
        Assert.Equal(RuleAction.Cancel, candidate.Action);
        Assert.Equal("report", candidate.TargetTaskId);
        Assert.Null(candidate.Time);
        Assert.True(candidate.Assessments.Time.Reliable);
    }

    [Theory]
    [InlineData("实验报告延期至10月13日", RuleAction.Postpone)]
    [InlineData("实验报告需要重新提交，10月13日截止", RuleAction.NewRequirement)]
    public void TrustedExplicitChangesUseKnownTarget(string text, RuleAction action)
    {
        var candidate = Single(text, Context(tasks: [Task(TaskState.Completed)]));
        Assert.Equal(RuleDisposition.Change, candidate.Disposition);
        Assert.Equal(action, candidate.Action);
        Assert.Equal("report", candidate.TargetTaskId);
        Assert.Equal(new DateOnly(2026, 10, 13), candidate.Time!.Date);
    }

    [Theory]
    [InlineData("取消实验报告")]
    [InlineData("实验报告延期到明天")]
    [InlineData("实验报告明天重新提交")]
    public void UnconfirmedSenderCannotPerformHighImpactChanges(string text)
    {
        var candidate = Single(text, Context(tasks: [Task()]), "student");
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.False(candidate.Assessments.Source.Reliable);
        Assert.True(candidate.Assessments.Association.Reliable);
    }

    [Theory]
    [InlineData("取消报告")]
    [InlineData("取消作业")]
    [InlineData("延期至明天")]
    [InlineData("实验报告延期")]
    public void MissingExactTargetOrNewDateNeverChangesTask(string text)
    {
        Assert.Equal(RuleDisposition.Pending, Single(text, Context(tasks: [Task()])).Disposition);
    }

    [Fact]
    public void DuplicateCompleteTaskNamesCannotSelectArbitraryTarget()
    {
        var candidate = Single("取消实验报告", Context(tasks: [Task(), Task(id: "other")]));
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Null(candidate.TargetTaskId);
    }

    [Theory]
    [InlineData(TaskState.Archived)]
    [InlineData(TaskState.Cancelled)]
    public void NewActionForArchivedOrCancelledTaskRequiresConfirmation(TaskState state)
    {
        Assert.Equal(RuleDisposition.Pending, Single("实验报告明天重新提交", Context(tasks: [Task(state)])).Disposition);
    }

    [Fact]
    public void ExplicitQuoteCanIdentifyTargetForCurrentTrustedPublisher()
    {
        var message = Message("延期到明天") with
        {
            Segments = [new(SegmentKind.Quote, "旧通知后天交报告", Original), new(SegmentKind.Text, "延期到明天")]
        };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context(tasks: [Task()])).Candidates);
        Assert.Equal(RuleDisposition.Change, candidate.Disposition);
        Assert.Equal("report", candidate.TargetTaskId);
        Assert.Equal(2, candidate.Evidence.Count);
        Assert.Contains(Original, candidate.Evidence);
        Assert.Equal(new DateOnly(2026, 10, 11), candidate.Time!.Date);
    }

    [Theory]
    [InlineData("手动课程", "取消")]
    [InlineData(null, "取消")]
    [InlineData("手动课程", "提醒提交")]
    [InlineData(null, "提醒提交")]
    public void ExactSameGroupQuoteIdentifiesTaskAfterTitleAndCourseAreEdited(string? course, string text)
    {
        var task = Task() with { Title = "我的任务名称", Course = course! };
        var message = Message(text) with
        {
            Segments = [new(SegmentKind.Quote, "原通知", Original), new(SegmentKind.Text, text)]
        };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context(tasks: [task])).Candidates);
        Assert.Equal("report", candidate.TargetTaskId);
        Assert.Equal(RuleDisposition.Change, candidate.Disposition);
        Assert.Equal("高等数学", candidate.Course);
    }

    [Fact]
    public void QuotedEvidenceSharedByTasksWithEditedCoursesStillRequiresUniqueTarget()
    {
        var first = Task() with { Title = "第一个手动名称", Course = "手动课程" };
        var second = Task(id: "other") with { Title = "第二个手动名称", Course = null! };
        var message = Message("取消") with
        {
            Segments = [new(SegmentKind.Quote, "原通知", Original), new(SegmentKind.Text, "取消")]
        };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context(tasks: [first, second])).Candidates);
        Assert.Null(candidate.TargetTaskId);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
    }

    [Fact]
    public void OriginalConfirmedPublisherWithExactQuoteCanContinueOutsideTrustedList()
    {
        var message = Message("取消") with { Segments = [new(SegmentKind.Quote, "旧通知", Original), new(SegmentKind.Text, "取消")] };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message,
            Context(tasks: [Task()]) with { TrustedSenders = new HashSet<string>() }).Candidates);
        Assert.Equal(RuleDisposition.Change, candidate.Disposition);
        Assert.True(candidate.Assessments.Source.Reliable);
    }

    [Fact]
    public void QuotingTeacherDoesNotMakeStudentTrusted()
    {
        var message = Message("取消", "student") with { Segments = [new(SegmentKind.Quote, "老师原文", Original), new(SegmentKind.Text, "取消")] };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context(tasks: [Task()])).Candidates);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Equal("report", candidate.TargetTaskId);
        Assert.False(candidate.Assessments.Source.Reliable);
    }

    [Fact]
    public void OriginalPublisherWithoutExplicitQuoteIsNotImplicitlyTrusted()
    {
        var candidate = Single("取消实验报告", Context(tasks: [Task()]) with { TrustedSenders = new HashSet<string>() });
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.False(candidate.Assessments.Source.Reliable);
    }

    [Theory]
    [InlineData("b", "g")]
    [InlineData("a", "other")]
    public void ForeignQuoteCannotBorrowPublisherTrustFromMixedTaskEvidence(string account, string group)
    {
        var foreign = new MessageKey(account, group, "foreign");
        var task = Task() with { Evidence = [Original, foreign], Course = "手动课程" };
        var message = Message("取消") with
        {
            Segments = [new(SegmentKind.Quote, "异群或异账号通知", foreign), new(SegmentKind.Text, "取消")]
        };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message,
            Context(tasks: [task]) with { TrustedSenders = new HashSet<string>() }).Candidates);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Null(candidate.TargetTaskId);
        Assert.False(candidate.Assessments.Source.Reliable);
    }

    [Theory]
    [InlineData("除了实验报告其余都取消")]
    [InlineData("实验报告以外的任务取消")]
    [InlineData("不是实验报告取消，是别的作业取消")]
    public void ExcludedTitleIsNotAnAffirmativeChangeTarget(string text)
    {
        var candidates = MessageRuleExtractor.Extract(Message(text), Context(tasks: [Task()])).Candidates;
        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
            Assert.Null(candidate.TargetTaskId);
        });
    }

    [Theory]
    [InlineData("如果实验报告取消")]
    [InlineData("假如实验报告延期至明天")]
    [InlineData("考虑取消实验报告")]
    public void ConditionalChangesAreNotConfirmedRequirements(string text)
    {
        var candidate = Single(text, Context(tasks: [Task()]));
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.False(candidate.Assessments.Action.Reliable);
    }

    [Theory]
    [InlineData("如果明天必须参加考试，就取消实验报告")]
    [InlineData("如果明天必须参加考试，则实验报告延期至后天")]
    [InlineData("如果明天必须参加考试，取消实验报告")]
    public void ConditionScopeSurvivesCommaActionSplitting(string text)
    {
        var candidates = MessageRuleExtractor.Extract(Message(text), Context(tasks: [Task()])).Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
            Assert.False(candidate.Assessments.Action.Reliable);
        });
    }

    [Theory]
    [InlineData("取消实验报告的PDF格式要求")]
    [InlineData("取消实验报告的纸质版要求，明天提交电子版")]
    public void CancellingAFormatOrMediumCannotCancelEntireTask(string text)
    {
        var candidates = MessageRuleExtractor.Extract(Message(text), Context(tasks: [Task()])).Candidates;
        Assert.Equal(RuleDisposition.Pending, candidates[0].Disposition);
        Assert.False(candidates[0].Assessments.Action.Reliable);
        Assert.DoesNotContain(candidates, candidate => candidate.Action == RuleAction.Cancel && candidate.Disposition == RuleDisposition.Change);
    }

    [Theory]
    [InlineData("10月12日可以参加讲座", RuleAction.Attend)]
    [InlineData("有兴趣的同学10月12日提交竞赛报名表", RuleAction.Submit)]
    public void VoluntaryParticipationAndSubmissionRemainNotices(string text, RuleAction action)
    {
        var candidate = Single(text);
        Assert.Equal(action, candidate.Action);
        Assert.Equal(RuleDisposition.Notice, candidate.Disposition);
        Assert.NotNull(candidate.Time);
    }

    [Fact]
    public void QuoteOnlyDoesNotRecreateOldTasks()
    {
        var message = Message("") with { Segments = [new(SegmentKind.Quote, "明天交实验报告", Original)] };
        Assert.Equal(RuleDisposition.Ignored, Assert.Single(MessageRuleExtractor.Extract(message, Context()).Candidates).Disposition);
    }

    [Fact]
    public void OcrActionAndDateRemainSuggestion()
    {
        var message = Message("") with { Segments = [new(SegmentKind.Image, "10月12日交实验报告", AssetId: "image")] };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context()).Candidates);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Equal(RuleAction.Submit, candidate.Action);
        Assert.Null(candidate.Time);
        Assert.Equal(new DateOnly(2026, 10, 12), candidate.SuggestedTime!.Date);
        Assert.False(candidate.Assessments.Action.Reliable);
        Assert.False(candidate.Assessments.Time.Reliable);
    }

    [Fact]
    public void OcrCannotSupplyFormalTimeMissingFromText()
    {
        var message = Message("提交实验报告，时间见图片") with
        {
            Segments = [new(SegmentKind.Text, "提交实验报告，时间见图片"), new(SegmentKind.Image, "10月12日截止", AssetId: "image")]
        };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context()).Candidates);
        Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
        Assert.Null(candidate.Time);
        Assert.NotNull(candidate.SuggestedTime);
    }

    [Fact]
    public void TextDateRemainsFormalWhenOcrDateDiffers()
    {
        var message = Message("明天交作业") with { Segments = [new(SegmentKind.Text, "明天交作业"), new(SegmentKind.Image, "10月12日", AssetId: "image")] };
        var candidate = Assert.Single(MessageRuleExtractor.Extract(message, Context()).Candidates);
        Assert.Equal(RuleDisposition.Task, candidate.Disposition);
        Assert.Equal(new DateOnly(2026, 10, 11), candidate.Time!.Date);
        Assert.Null(candidate.SuggestedTime);
    }

    [Fact]
    public void AccountAndGroupMismatchCannotReuseRelations()
    {
        foreach (var context in new[] { Context() with { AccountId = "b" }, Context() with { GroupId = "other" } })
        {
            var candidate = Single("取消实验报告", context with { KnownTasks = [Task()] });
            Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
            Assert.False(candidate.Assessments.Source.Reliable);
            Assert.False(candidate.Assessments.Course.Reliable);
            Assert.Null(candidate.TargetTaskId);
        }
    }

    [Fact]
    public void PrivateConfirmedExcludedSenderAndCourseAreIgnored()
    {
        Assert.Equal(RuleDisposition.Ignored, Assert.Single(MessageRuleExtractor.Extract(Message("明天交作业") with { IsPrivate = true }, Context()).Candidates).Disposition);
        Assert.Equal(RuleDisposition.Ignored, Single("明天交作业", Context() with { ExcludedSenders = new HashSet<string> { "teacher" } }).Disposition);
        Assert.Equal(RuleDisposition.Ignored, Single("明天交作业", Context() with { CourseExcluded = true }).Disposition);
    }

    [Fact]
    public void OtherGroupOrCourseTaskCannotBeSelectedByName()
    {
        foreach (var task in new[] { Task() with { Evidence = [new("a", "other", "old")] }, Task() with { Course = "大学物理" } })
        {
            var candidate = Single("取消实验报告", Context(tasks: [task]));
            Assert.Equal(RuleDisposition.Pending, candidate.Disposition);
            Assert.Null(candidate.TargetTaskId);
        }
    }

    [Fact]
    public void CrossGroupSameTextStaysIndependent()
    {
        var first = Single("明天交作业");
        var message = Message("明天交作业") with { Key = new("a", "other", "message") };
        var second = Assert.Single(MessageRuleExtractor.Extract(message, Context() with { GroupId = "other" }).Candidates);
        Assert.Null(first.TargetTaskId);
        Assert.Null(second.TargetTaskId);
        Assert.NotEqual(first.Evidence[0], second.Evidence[0]);
    }

    [Theory]
    [InlineData("10月15日交作业，影响成绩", 1)]
    [InlineData("10月15日交作业，不影响成绩", 2)]
    [InlineData("10月15日交作业，逾期不收", 1)]
    [InlineData("10月15日交作业，不扣分", 2)]
    public void ClearConsequencesRaisePriorityButNegatedConsequencesDoNot(string text, int priority)
    {
        Assert.Equal(priority, Single(text).Priority);
    }
}

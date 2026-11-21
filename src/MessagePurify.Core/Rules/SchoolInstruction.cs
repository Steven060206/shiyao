using System.Text.RegularExpressions;

namespace MessagePurify.Core;

/// <summary>在同一句话中核对行动、对象及布置语气。只在常规提取没有命中时使用。</summary>
internal static partial class SchoolInstruction
{
    internal static RuleAction? Identify(string sentence)
    {
        if (sentence.Length == 0 || NonInstruction().IsMatch(sentence)) return null;
        var directed = Direction().IsMatch(sentence);
        if (Exercise().IsMatch(sentence)) return RuleAction.Complete;
        if (CourseSelection().IsMatch(sentence) && !SelectionDiscussion().IsMatch(sentence) &&
            (directed || SelectionWindow().IsMatch(sentence)))
            return RuleAction.Register;
        if (!directed) return null;
        if (Form().IsMatch(sentence)) return RuleAction.Register;
        if (Study().IsMatch(sentence) || Teams().IsMatch(sentence)) return RuleAction.Complete;
        if (Lesson().IsMatch(sentence)) return RuleAction.Attend;
        return null;
    }

    // An invitation can arrive as a timetable or a provider's QR card.
    internal static bool IsMeetingInvitation(string text) =>
        ((text.Contains("会议主题") && text.Contains("会议时间") &&
          (text.Contains("会议号") || text.Contains("入会链接") || text.Contains("链接入会") || text.Contains("加入会议"))) ||
         (text.Contains("腾讯会议") && text.Contains("扫码入会"))) &&
        !NonInstruction().IsMatch(text);

    internal static bool IsPageAssignment(string text) =>
        Exercise().IsMatch(text) && !NonInstruction().IsMatch(text);

    internal static bool IsExcludedCaption(string text) => NonInstruction().IsMatch(text);

    internal static string WithoutLinkTargets(string text) =>
        text.Contains("://", StringComparison.Ordinal) ? WebAddress().Replace(text, "链接") : text;

    [GeneratedRegex(@"https?://[^\s，。；！？]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebAddress();

    // A directed request with a time or a concrete operation is worth checking, even
    // if the action type cannot be inferred. Generic greetings and bare links do not qualify.
    internal static bool NeedsReview(string sentence) =>
        !NonInstruction().IsMatch(sentence) && !Promotion().IsMatch(sentence) &&
        ((Audience().IsMatch(sentence) &&
          ((Request().IsMatch(sentence) && RequestEvidence().IsMatch(sentence)) ||
           (sentence.Contains("链接") && EnterService().IsMatch(sentence)))) ||
         (Request().IsMatch(sentence) && LabProcedure().IsMatch(sentence)));

    [GeneratedRegex(@"各位.{0,6}同学|全体|全班|同学(?:们|请|注意|记得)|请.{0,8}同学|大家|每位同学")]
    private static partial Regex Audience();

    [GeneratedRegex(@"(?<!申)请(?!问|客|教|假)|务必|一定要|必须|记得|麻烦|抓紧|别忘|尽快")]
    private static partial Regex Request();

    [GeneratedRegex(@"课前|课后|截止|阅读|扫码|答题|核对|填写|选课|听课|上课|登录|实验室|查收|签字|自查|排查|核查|(?:今天|明天|[本下]周).{0,16}(?:课程|课题|作业|材料|学籍|题目)|(?:课程|课题|作业|材料|学籍|题目).{0,16}(?:今天|明天|[本下]周)")]
    private static partial Regex RequestEvidence();

    [GeneratedRegex(@"实验.{0,18}(?:操作|步骤|数据表)|(?:操作|步骤).{0,12}实验")]
    private static partial Regex LabProcedure();

    [GeneratedRegex(@"领红包|领券|抽奖|点赞|拼团|转发有奖|游戏礼包")]
    private static partial Regex Promotion();

    [GeneratedRegex(@"(?:进入|登录|登入|访问|打开).{0,10}(?:平台|系统|网址|网站|链接)|(?:网址|链接).{0,12}(?:进入|登录|访问)")]
    private static partial Regex EnterService();

    [GeneratedRegex(@"[?？]|请问|是不是|要不要|有没有|怎么|如何|为什么|谁(?:能|会|知道)|能否|能不能|(?:吗|么|呢)\s*[，,。！!\s]*$|不用|无需|不必|不需要|不要|不要求|不再|暂缓|暂不|取消|未开放|已截止|已结束|自愿|感兴趣|有兴趣|有空|欢迎|可自行|不强制|供参考|往年|去年|上次|示例|仅供|会议纪要|会议回放|我(?:刚|也|已|的|要|想|准备|昨天|明天|今天|正在)|已(?:经)?(?:填|核对|选|读|分组|组队|看完|做完)|(?:填|选|读|做|分组)(?:好了|完了|过了|成功)")]
    private static partial Regex NonInstruction();

    [GeneratedRegex(@"(?<!申)请(?!假|客|教)|务必|必须|须在|需要|记得|记住|每(?:人|组|位同学)|各位.{0,6}同学|全(?:班|体)|同学(?:们|请|注意)|大家|课前|课后|截止|最晚|之前|前(?:填|选|读|看|核对)|按(?:学号|小组)")]
    private static partial Regex Direction();

    [GeneratedRegex(@"(?:本[周次节]|今日|今天|课后)?(?:作业|课后练习|预习内容)[\s:：为是]*(?:教材|课本|书上|练习册)?\s*(?:第?\s*[0-9一二三四五六七八九十]+\s*[页章节题]|[pP]\.?\s*[0-9])")]
    private static partial Regex Exercise();

    [GeneratedRegex(@"选课|补选|退选|改选|选修课")]
    private static partial Regex CourseSelection();

    [GeneratedRegex(@"攻略|教程|分享|讨论|截图|经验|系统故障|系统崩")]
    private static partial Regex SelectionDiscussion();

    [GeneratedRegex(@"(?:选课|补选|退选|改选).{0,10}(?:将于|截止|开放时间|即将开始)|(?:开放|开始).{0,5}(?:选课|补选|退选|改选)")]
    private static partial Regex SelectionWindow();

    [GeneratedRegex(@"(?:填写|填好|填一下|补录|核对|完善).{0,16}(?:问卷|表格|表单|登记表|信息表|反馈表|申请表|调查表|学籍|名单|个人信息)|(?:问卷|登记表|反馈表|信息表).{0,12}(?:填写|填好|核对)")]
    private static partial Regex Form();

    [GeneratedRegex(@"(?:预习|研读|阅读|复习|熟悉|观看|打印|携带|带好|下载|作答).{0,12}(?:教材|课本|课文|讲义|课件|资料|指导书|任务书|导学案|教学视频|实验步骤|课堂练习|第.{0,4}[章页节题])|(?:课堂|随堂|课后|限时|扫码|在线).{0,5}答题")]
    private static partial Regex Study();

    [GeneratedRegex(@"(?:实验|实训|课程|作业|课题).{0,14}(?:分组|组队|组长|组员)|(?:分组|组队|组长|组员).{0,14}(?:实验|实训|课程|作业|课题)|(?:分组|组队).{0,10}每组.{0,5}人")]
    private static partial Regex Teams();

    [GeneratedRegex(@"(?:今天|明天|后天|[本下]周|周[一二三四五六日]|第.{1,3}节|教室|实验室|线上|到校|请|务必|准时|记得).{0,18}(?:上课|听课|上机)|(?:上课|听课|上机).{0,14}(?:教室|实验室|第.{1,3}节|按学号|准时)")]
    private static partial Regex Lesson();
}

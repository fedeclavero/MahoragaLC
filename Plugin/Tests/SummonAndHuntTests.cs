using System;
using System.Collections.Generic;
using MahoragaLC.AI;

internal static class SummonAndHuntTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool Near(float a, float b, float eps = 0.001f) => Math.Abs(a - b) <= eps;

    public static void TestTimelineReference()
    {
        var tl = new SummonTimeline(4.8f);
        Assert(Near(tl.Scale, 1f), "reference scale must be 1");
        Assert(Near(tl.ModelOffset(0f), SummonTimeline.SubmergedOffset), "starts fully submerged");
        Assert(Near(tl.ModelOffset(tl.EmergeStart), SummonTimeline.SubmergedOffset), "still submerged when the climb starts");
        Assert(Near(tl.ModelOffset(tl.EmergeEnd), 0f), "fully emerged at EmergeEnd");
        Assert(Near(tl.ModelOffset(tl.Duration), 0f), "no offset at the end");

        float prev = tl.ModelOffset(tl.EmergeStart);
        for (float t = tl.EmergeStart; t <= tl.EmergeEnd; t += 0.05f)
        {
            float o = tl.ModelOffset(t);
            Assert(o >= prev - 1e-4f, $"offset must rise monotonically (t={t})");
            Assert(o <= 0f && o >= SummonTimeline.SubmergedOffset, "offset stays within [submerged, 0]");
            prev = o;
        }

        Assert(!tl.IsModelVisible(0.1f) && tl.IsModelVisible(tl.EmergeStart + 0.01f), "model hidden until the climb starts");
        Assert(tl.PoolScale(0f) == 0f && Near(tl.PoolScale(tl.PoolOpenEnd), 1f) && Near(tl.PoolScale(tl.EmergeEnd), 1f), "pool opens to 1 and holds");
        Assert(Near(tl.PoolScale(tl.Duration), 0f) && tl.PoolScale(tl.DissolveStart + 0.4f) < 1f, "pool dissolves to 0");
        Assert(tl.WheelExtraAngle(tl.WheelStart) == 0f && tl.WheelExtraAngle(tl.WheelEnd) == 0f, "wheel extra angle returns to rest");
        Assert(tl.WheelExtraAngle((tl.WheelStart + tl.WheelEnd) * 0.5f) > 200f, "wheel is mid-spin halfway");
        Assert(tl.ShadowCoat(0f) < 0.1f && Near(tl.ShadowCoat(tl.WheelEnd + 1f), 1f), "shadow coat fades from black to normal colour");
        Assert(tl.DropletRate(0f) == 0f && tl.DropletRate(tl.EmergeEnd - 0.1f) > 0f && tl.DropletRate(tl.DissolveStart) == 0f, "droplets only while rising");
        Assert(tl.RoarTime > tl.EmergeEnd && tl.RoarTime < tl.DissolveStart, "roar lands after the boom and before the dissolve");
        Assert(tl.IsFinished(tl.Duration) && !tl.IsFinished(tl.Duration - 0.01f), "finish boundary");
    }

    public static void TestTimelineScalingAndClamp()
    {
        var slow = new SummonTimeline(9.6f);
        Assert(Near(slow.Scale, 2f) && Near(slow.EmergeEnd, 5.2f) && Near(slow.Duration, 9.6f), "timeline scales linearly with duration");

        Assert(Near(new SummonTimeline(0.1f).Duration, SummonTimeline.MinDuration), "duration clamped to minimum");
        Assert(Near(new SummonTimeline(500f).Duration, SummonTimeline.MaxDuration), "duration clamped to maximum");
        Assert(Near(new SummonTimeline(float.NaN).Duration, SummonTimeline.ReferenceDuration), "NaN falls back to reference duration");
    }

    public static void TestDomainHysteresis()
    {
        Assert(!HuntLogic.IsOutsideAt(-200f, false) && !HuntLogic.IsOutsideAt(-200f, true), "interior (y=-200) is inside for everyone");
        Assert(HuntLogic.IsOutsideAt(5f, false) && HuntLogic.IsOutsideAt(5f, true), "surface is outside for everyone");
        Assert(HuntLogic.IsOutsideAt(-60f, false), "y=-60 is no longer misread as inside (game threshold is -80)");
        Assert(!HuntLogic.IsOutsideAt(-90f, false) && HuntLogic.IsOutsideAt(-90f, true), "dead band keeps the current domain (no flip-flop)");
    }

    public static void TestCandidateOrdering()
    {
        var list = new List<HuntLogic.Candidate>
        {
            new HuntLogic.Candidate(3, 30f),
            new HuntLogic.Candidate(1, 10f),
            new HuntLogic.Candidate(2, 20f),
            new HuntLogic.Candidate(0, 5f)
        };

        var ordered = HuntLogic.OrderCandidates(list, id => id == 0, 2);
        Assert(ordered.Count == 2 && ordered[0].Id == 1 && ordered[1].Id == 2, "blocked player skipped, nearest first, capped to maxCount");
        Assert(HuntLogic.OrderCandidates(new List<HuntLogic.Candidate>(), id => false, 3).Count == 0, "no candidates -> empty (patrol fallback)");
        Assert(HuntLogic.OrderCandidates(list, id => true, 3).Count == 0, "all blocked -> empty (patrol fallback)");
    }

    public static void TestStuckDetection()
    {
        Assert(HuntLogic.IsStuck(0.1f, 3f, 20f), "no progress with a long way to go is stuck");
        Assert(!HuntLogic.IsStuck(4f, 3f, 20f), "moving is not stuck");
        Assert(!HuntLogic.IsStuck(0.1f, 3f, 1.5f), "standing at the destination is not stuck");
        Assert(!HuntLogic.IsStuck(0.1f, 1f, 20f), "window too short to judge");
    }
}

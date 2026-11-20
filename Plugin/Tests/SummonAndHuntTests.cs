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
        Assert(Near(SummonTimeline.WheelTurnDegrees, 45f) && SummonTimeline.WheelTurnDegrees < 360f, "summon wheel turn is exactly +45 deg (1 spoke: ball 1 -> ball 2, never 360)");
        Assert(tl.WheelExtraAngle(0f) == 0f && tl.WheelExtraAngle(tl.WheelStart) == 0f, "wheel extra angle starts at 0");
        float midWheel = tl.WheelExtraAngle((tl.WheelStart + tl.WheelEnd) * 0.5f);
        Assert(midWheel > 20f && midWheel < SummonTimeline.WheelTurnDegrees, $"wheel is mid-spin halfway within (20, 45) (got {midWheel})");
        float prevWheel = 0f;
        for (float t = tl.WheelStart; t <= tl.Duration; t += 0.05f)
        {
            float w = tl.WheelExtraAngle(t);
            Assert(w >= prevWheel - 1e-4f && w <= SummonTimeline.WheelTurnDegrees + 1e-4f, $"wheel angle must rise monotonically to +45 and never exceed it (t={t}, w={w})");
            prevWheel = w;
        }
        Assert(Near(tl.WheelExtraAngle(tl.WheelEnd), 45f) && Near(tl.WheelExtraAngle(tl.Duration), 45f), "wheel extra angle holds at +45 deg at WheelEnd and Duration without snapping back to 0");
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

    public static void TestShadowAmbushGating()
    {
        bool Valid(
            bool enable = true,
            float timeOfDay = 0.70f,
            float minTime = 0.62f,
            float lostSight = 15.0f,
            float lostSightDelay = 12.0f,
            float cooldown = 0f,
            bool oldBirdInDomain = false,
            bool isDead = false,
            bool isAttacking = false,
            bool isAdapting = false,
            bool isSummoning = false,
            bool isSubmerging = false,
            bool isEngagingEnemy = false,
            bool isStunned = false,
            bool isForcingDoor = false) =>
            ShadowAmbushLogic.CanTriggerAmbush(
                enable,
                timeOfDay,
                minTime,
                lostSight,
                lostSightDelay,
                cooldown,
                oldBirdInDomain,
                isDead,
                isAttacking,
                isAdapting,
                isSummoning,
                isSubmerging,
                isEngagingEnemy,
                isStunned,
                isForcingDoor);

        Assert(Valid(), "all valid late-game conditions must allow Shadow Ambush");
        Assert(Valid(timeOfDay: 0.62f, lostSight: 12.0f, cooldown: 0f), "exact threshold boundary values must allow Shadow Ambush");
        Assert(Valid(cooldown: -2.5f), "negative remaining cooldown must allow Shadow Ambush");

        // Gate 1: config toggle disabled
        Assert(!Valid(enable: false), "disabled EnableShadowAmbush must block ambush");

        // Gate 2: normalizedTimeOfDay < minTimeOfDay (0.62)
        Assert(!Valid(timeOfDay: 0.619f), "time of day < 0.62 must block ambush");
        Assert(!Valid(timeOfDay: 0.0f), "morning time of day must block ambush");
        Assert(!Valid(timeOfDay: float.NaN), "NaN time of day must block ambush");
        Assert(!Valid(minTime: float.PositiveInfinity), "infinite minTime must block ambush");

        // Gate 3: timeSinceLostSight < lostSightDelay (12s)
        Assert(!Valid(lostSight: 11.99f), "lost sight < 12s must block ambush");
        Assert(!Valid(lostSight: 0f), "just-seen player (0s) must block ambush");
        Assert(!Valid(lostSight: float.NaN), "NaN lostSight must block ambush");

        // Gate 4: active cooldown
        Assert(!Valid(cooldown: 0.01f), "positive cooldown remaining must block ambush");
        Assert(!Valid(cooldown: 45f), "full 45s cooldown must block ambush");
        Assert(!Valid(cooldown: float.NaN), "NaN cooldown must block ambush");

        // Gate 5: living Old Bird in domain (#1 priority preempts Shadow Ambush)
        Assert(!Valid(oldBirdInDomain: true), "living Old Bird in domain must block Shadow Ambush");

        // Gate 6: invalid AI states
        Assert(!Valid(isDead: true), "dead Mahoraga cannot ambush");
        Assert(!Valid(isAttacking: true), "attacking Mahoraga cannot ambush");
        Assert(!Valid(isAdapting: true), "adapting Mahoraga cannot ambush");
        Assert(!Valid(isSummoning: true), "summoning Mahoraga cannot ambush");
        Assert(!Valid(isSubmerging: true), "already-submerging Mahoraga cannot ambush");
        Assert(!Valid(isEngagingEnemy: true), "engaging-enemy Mahoraga cannot ambush");
        Assert(!Valid(isStunned: true), "stunned Mahoraga cannot ambush");
        Assert(!Valid(isForcingDoor: true), "door-forcing Mahoraga cannot ambush");
    }

    public static void TestSubmergeTimeline()
    {
        var tl = new SubmergeTimeline(SubmergeTimeline.ReferenceDuration);
        Assert(Near(tl.Duration, 2.6f) && Near(tl.Scale, 1f), "reference submerge duration is 2.6s with scale 1.0");
        Assert(Near(SubmergeTimeline.SubmergedOffset, -4.4f), "submerge target offset is -4.4m");

        // Sink curve: 0 -> -4.4m over [SinkStart, SinkEnd]
        Assert(Near(tl.ModelOffset(0f), 0f) && Near(tl.ModelOffset(tl.SinkStart), 0f), "starts at floor level (0m) until SinkStart");
        Assert(Near(tl.ModelOffset(tl.SinkEnd), -4.4f) && Near(tl.ModelOffset(tl.Duration), -4.4f), "reaches -4.4m at SinkEnd and holds");

        float prevOffset = tl.ModelOffset(tl.SinkStart);
        for (float t = tl.SinkStart; t <= tl.SinkEnd; t += 0.05f)
        {
            float o = tl.ModelOffset(t);
            Assert(o <= prevOffset + 1e-4f, $"submerge offset must sink monotonically (t={t}, o={o})");
            Assert(o <= 0f && o >= SubmergeTimeline.SubmergedOffset, "submerge offset stays within [-4.4, 0]");
            prevOffset = o;
        }

        // Visibility: visible while sinking, hidden once SinkEnd is reached
        Assert(tl.IsModelVisible(0f) && tl.IsModelVisible(tl.SinkEnd - 0.01f) && !tl.IsModelVisible(tl.SinkEnd) && !tl.IsModelVisible(tl.Duration),
            "model visible until SinkEnd, hidden once fully submerged");

        // Tint fade (ShadowCoat): 1.0 -> 0.06
        Assert(Near(tl.ShadowCoat(0f), 1.0f) && Near(tl.ShadowCoat(tl.SinkStart), 1.0f), "tint starts at 1.0 normal skin");
        Assert(Near(tl.ShadowCoat(tl.SinkEnd), 0.06f) && Near(tl.ShadowCoat(tl.Duration), 0.06f), "tint fades to 0.06 shadow ink at SinkEnd");
        float midTint = tl.ShadowCoat((tl.SinkStart + tl.SinkEnd) * 0.5f);
        Assert(midTint < 0.6f && midTint > 0.4f, $"mid-sink tint should be near midpoint (got {midTint})");

        // Pool timing: 0 -> 1 by PoolOpenEnd, holds at 1 until DissolveStart, closes to 0 at Duration
        Assert(tl.PoolScale(0f) == 0f, "pool starts closed at t=0");
        Assert(Near(tl.PoolScale(tl.PoolOpenEnd), 1f) && Near(tl.PoolScale((tl.PoolOpenEnd + tl.DissolveStart) * 0.5f), 1f),
            "pool opens to 1 at PoolOpenEnd and holds while sinking");
        Assert(tl.PoolScale(tl.DissolveStart + 0.25f) < 1f && tl.PoolScale(tl.DissolveStart + 0.25f) > 0f, "pool shrinks during dissolve phase");
        Assert(Near(tl.PoolScale(tl.Duration), 0f), "pool closes to 0 at Duration");

        // Droplets & finish boundary
        Assert(tl.DropletRate(0f) == 0f && tl.DropletRate(1.0f) > 0f && tl.DropletRate(tl.DissolveStart) == 0f, "droplets active while diving and stop at dissolve");
        Assert(!tl.IsFinished(tl.Duration - 0.01f) && tl.IsFinished(tl.Duration), "submerge finish boundary");

        // Scaling & clamping
        var slow = new SubmergeTimeline(5.2f);
        Assert(Near(slow.Scale, 2f) && Near(slow.Duration, 5.2f) && Near(slow.SinkEnd, SubmergeTimeline.SinkEndRef * 2f), "submerge timeline scales linearly");
        Assert(Near(new SubmergeTimeline(0.1f).Duration, SubmergeTimeline.MinDuration), "submerge duration clamped to MinDuration (1.0s)");
        Assert(Near(new SubmergeTimeline(99f).Duration, SubmergeTimeline.MaxDuration), "submerge duration clamped to MaxDuration (6.0s)");
        Assert(Near(new SubmergeTimeline(float.NaN).Duration, SubmergeTimeline.ReferenceDuration), "NaN submerge duration falls back to ReferenceDuration (2.6s)");
    }

}

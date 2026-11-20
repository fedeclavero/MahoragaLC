using System;
using System.Collections.Generic;
using MahoragaLC.AI;
using MahoragaLC.Config;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("Category classification (all 6 categories + Shotgun hitID=-1 trap)", TestCategoryClassification);
        Run("Solo Shovel triggers Melee adaptation + 20% heal (+4 HP clamped to 18)", TestMeleeAdaptationAndHealClamp);
        Run("Coordinated 4-player Shotgun -> Explosion kill obeys Lethal-Blow Precedence", TestLethalBlowPrecedence);
        Run("95% Adapted resistance fractional buffer accumulates 1 HP after 4x5 Shotgun blasts", TestFractionalDamageBuffer);
        Run("100% Adapted resistance yields pure 0 damage immunity", TestFullImmunityResistance);
        Run("Adapting stance grants 75% bracing armor and prevents double adaptation overlap", TestAdaptingArmor);
        Run("Explosion deduplication window (0.15s)", TestExplosionDedup);
        Run("ZapGun continuous-beam debounce (3.0s) & fresh engagement gap (1.5s)", TestZapGunDebounce);
        Run("Two full stuns, then half duration, independently for each source", TestStunGrenadeAdaptation);
        Run("Four distinct shovel users coordinate, with extra blows after adaptation", TestCoordinatedShovels);
        Run("Shovel and shotgun adaptation are independent", TestShovelIndependence);
        Run("MoonRarityParser parses vanilla, modded, whitespace, and malformed entries", TestMoonRarityParser);
        Run("Summon timeline: rise, pool, wheel, droplets, finish", SummonAndHuntTests.TestTimelineReference);
        Run("Summon timeline: scales with duration and clamps bad values", SummonAndHuntTests.TestTimelineScalingAndClamp);
        Run("Hunt: inside/outside classification with hysteresis (game thresholds)", SummonAndHuntTests.TestDomainHysteresis);
        Run("New categories: Old Bird missile, enemy melee, vehicle force no longer Shotgun", TestNewCategoryClassification);
        Run("Progressive adaptation: 0 -> 40 -> 70 -> 95 %, wheel clicks, heal only at the final stage", TestProgressiveAdaptation);
        Run("Progressive adaptation: HitsPerStage=2 stretches each stage and keeps categories independent", TestHitsPerStage);
        Run("Progressive adaptation: lethal blow mid-ramp never advances the Wheel", TestLethalMidRamp);
        Run("Adapting stance does not advance stages and stays armored", TestAdaptingDoesNotAdvance);
        Run("Zap Gun anti-spam still holds with stages (wheel clicks only on real exposures)", TestZapAntiSpamWithStages);
        Run("HP floor clips damage but never heals", TestHpFloor);
        Run("Single Old Bird can never kill Mahoraga; a flock of 3 can before adaptation", TestOldBirdBalance);
        Run("Regeneration waits for quiet, accumulates fractions, never overheals", TestRegeneration);
        Run("AggroTable decay, top selection and switch hysteresis (no oscillation)", TestAggroHysteresis);
        Run("CooldownSet blocks then expires", TestCooldownSet);
        Run("Enemy engagement always terminates (timeout, immortal, unreachable, distance) and players preempt fairly", TestEngagementPolicy);
        Run("DistinctAttackerWindow counts different attackers inside the window", TestDistinctAttackers);
        Run("GrowthMath: match scale, steps, cap, indoor reset, approach", TestGrowthMath);
        Run("Old Bird durability and knockback maths", TestDurabilityAndKnockback);
        Run("Old Bird #1 priority (EvaluateOldBird), AdvanceWheelStep post-stage-3, and +45 deg Wheel progression (0..360)", TestOldBirdEngagementAndWheelProgression);
        Run("ShadowAmbush: CanTriggerAmbush gates (toggle, time, lost sight, cooldown, Old Bird priority, AI state)", SummonAndHuntTests.TestShadowAmbushGating);
        Run("Submerge timeline: sink 0 -> -4.4m, tint fade 1.0 -> 0.06, pool timing, scaling", SummonAndHuntTests.TestSubmergeTimeline);
        Run("PluginConfig defaults (dual spawn, outdoor perception, OldBird, ShadowAmbush) & obsolete key removal", TestPluginConfigDefaultsAndOrphanedCleanup);

        Console.WriteLine($"Tests completed: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
        }
    }

    // 1.0.0-alpha behaviour: N exposures -> category adapted outright (used by the legacy-style tests below).
    private static AdaptationSettings Legacy(int threshold, float resistance, float heal) => AdaptationSettings.CreateInstant(threshold, resistance, heal);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void TestCategoryClassification()
    {
        // Shovel hitID=1
        Assert(MahoragaAdaptationSystem.ClassifyDamage(1, 1, true, true, false, false) == AdaptationCategory.Melee, "Shovel must classify as Melee");
        // Kitchen Knife hitID=5
        Assert(MahoragaAdaptationSystem.ClassifyDamage(5, 1, true, false, false, false) == AdaptationCategory.Melee, "Knife hitID=5 must classify as Melee");
        // Shotgun trap: hitID=-1, force=5, attacker holding shotgun
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 5, true, false, true, false) == AdaptationCategory.Shotgun, "Shotgun point-blank must classify as Shotgun");
        // Shotgun trap fallback: hitID=-1, force=2, attacker switched slot quickly
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 2, true, false, false, false) == AdaptationCategory.Shotgun, "Shotgun hitID=-1 force>=2 with attacker must classify as Shotgun");
        // Explosion (Landmine force=6, hitID=-1, no attacker)
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 6, false, false, false, false) == AdaptationCategory.Explosion, "Landmine must classify as Explosion");
        // Explosion via Harmony tracker
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 3, true, false, false, true) == AdaptationCategory.Explosion, "Tracked explosion nearby must classify as Explosion");
        // Cruiser / Modded catch-all
        Assert(MahoragaAdaptationSystem.ClassifyDamage(331, 2, true, false, false, false) == AdaptationCategory.VehicleImpact, "Cruiser hitID=331 must use the vehicle phenomenon");
        // Stuns
        Assert(MahoragaAdaptationSystem.ClassifyStun(true) == AdaptationCategory.ZapGun, "PatcherTool must classify as ZapGun");
        Assert(MahoragaAdaptationSystem.ClassifyStun(false) == AdaptationCategory.StunGrenade, "Flashbang must classify as StunGrenade");
    }

    private static void TestMeleeAdaptationAndHealClamp()
    {
        var sys = new MahoragaAdaptationSystem();
        var hit1 = sys.EvaluateDamage(AdaptationCategory.Melee, 1, currentHP: 18, maxHP: 18, false, Legacy(2, 0.95f, 0.20f), now: 1.0f);
        Assert(hit1.EffectiveDamage == 1 && hit1.FinalHP == 17 && !hit1.TriggeredAdaptation && !hit1.IsLethal, "1st shovel should deal 1 dmg, no adapt");

        var hit2 = sys.EvaluateDamage(AdaptationCategory.Melee, 1, currentHP: 17, maxHP: 18, false, Legacy(2, 0.95f, 0.20f), now: 2.0f);
        Assert(hit2.EffectiveDamage == 1 && hit2.TriggeredAdaptation && hit2.FinalHP == 18 && hit2.HealAmount == 2, $"2nd shovel should trigger Melee adapt and clamp +4 heal to 18 (got HP={hit2.FinalHP}, heal={hit2.HealAmount})");
        Assert(sys.IsAdapted(AdaptationCategory.Melee) && sys.WheelStepCount == 1, "WheelStepCount must be 1");
    }

    private static void TestLethalBlowPrecedence()
    {
        var sys = new MahoragaAdaptationSystem();
        // 2x Shotgun (5 + 5 = 10 -> 8 HP) -> triggers Shotgun adapt (+4 heal -> 12 HP)
        var s1 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, 18, 18, false, Legacy(2, 0.95f, 0.20f), 1f);
        Assert(s1.FinalHP == 13 && !s1.TriggeredAdaptation, "Shotgun 1 leaves 13 HP");
        var s2 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, s1.FinalHP, 18, false, Legacy(2, 0.95f, 0.20f), 2f);
        Assert(s2.TriggeredAdaptation && s2.FinalHP == 12 && s2.HealAmount == 4, $"Shotgun 2 adapts and heals 8->12 (got {s2.FinalHP})");

        // 2x Landmine (6 + 6 = 12 -> 0 HP): 2nd explosion is LETHAL so it kills before adapting!
        var e1 = sys.EvaluateDamage(AdaptationCategory.Explosion, 6, s2.FinalHP, 18, false, Legacy(2, 0.95f, 0.20f), 5f);
        Assert(e1.FinalHP == 6 && !e1.IsLethal && !e1.TriggeredAdaptation, "Explosion 1 leaves 6 HP");
        var e2 = sys.EvaluateDamage(AdaptationCategory.Explosion, 6, e1.FinalHP, 18, false, Legacy(2, 0.95f, 0.20f), 8f);
        Assert(e2.IsLethal && e2.FinalHP == 0 && !e2.TriggeredAdaptation, "Lethal 2nd explosion must kill without adapting");
        Assert(!sys.IsAdapted(AdaptationCategory.Explosion), "Dead Mahoraga must NOT mark Explosion adapted");
    }

    private static void TestFractionalDamageBuffer()
    {
        var sys = new MahoragaAdaptationSystem();
        sys.SyncFromServer(1 << (int)AdaptationCategory.Shotgun, 1);

        // 4 point-blank shotgun blasts at 95% resist: 5 * 0.05 = 0.25 each -> 4th blast deals 1 HP!
        int hp = 18;
        for (int i = 1; i <= 3; i++)
        {
            var r = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, hp, 18, false, Legacy(2, 0.95f, 0.20f), i);
            Assert(r.WasAlreadyAdapted && r.EffectiveDamage == 0 && r.FinalHP == 18, $"Blast {i} should buffer 0.25 without dealing 1 yet");
            hp = r.FinalHP;
        }
        var r4 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, hp, 18, false, Legacy(2, 0.95f, 0.20f), 4f);
        Assert(r4.WasAlreadyAdapted && r4.EffectiveDamage == 1 && r4.FinalHP == 17, $"4th blast must accumulate 1.00 and deal 1 HP (got {r4.EffectiveDamage})");
    }

    private static void TestFullImmunityResistance()
    {
        var sys = new MahoragaAdaptationSystem();
        sys.SyncFromServer(1 << (int)AdaptationCategory.Shotgun, 1);
        for (int i = 0; i < 10; i++)
        {
            var r = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, 18, 18, false, Legacy(2, 1.00f, 0.20f), i);
            Assert(r.EffectiveDamage == 0 && r.FinalHP == 18, "100% resistance must deal 0 forever");
        }
    }

    private static void TestAdaptingArmor()
    {
        var sys = new MahoragaAdaptationSystem();
        // Force 6 during Adapting = 6 * 0.25 = 1.5 -> 1 HP damage, no overlapping adaptation trigger
        var r1 = sys.EvaluateDamage(AdaptationCategory.Explosion, 6, 18, 18, isCurrentlyAdapting: true, Legacy(1, 0.95f, 0.20f), 1f);
        Assert(r1.EffectiveDamage == 1 && r1.FinalHP == 17 && !r1.TriggeredAdaptation, $"Adapting armor should reduce 6->1 and avoid overlapping adapt (got dmg={r1.EffectiveDamage})");
    }

    private static void TestExplosionDedup()
    {
        var sys = new MahoragaAdaptationSystem();
        sys.EvaluateDamage(AdaptationCategory.Explosion, 6, 18, 18, false, Legacy(2, 0.95f, 0.20f), 10.0f);
        Assert(sys.IsDuplicateExplosion(10.05f), "0.05s later must be flagged duplicate");
        Assert(!sys.IsDuplicateExplosion(10.25f), "0.25s later must NOT be flagged duplicate");
    }

    private static void TestZapGunDebounce()
    {
        var sys = new MahoragaAdaptationSystem();
        // t=1.0s initial lock-on -> exposure 1
        var z1 = sys.EvaluateStun(AdaptationCategory.ZapGun, 14, 18, false, Legacy(2, 0.95f, 0.20f), now: 1.0f);
        Assert(!z1.Negated && !z1.TriggeredAdaptation && z1.ExposureIncremented && sys.GetExposureCount(AdaptationCategory.ZapGun) == 1, "Initial zap counts as 1");

        // Rapid re-call at t=1.5s (<1.5s gap, <3.0s sustained) -> debounced!
        var zSpam = sys.EvaluateStun(AdaptationCategory.ZapGun, 14, 18, false, Legacy(2, 0.95f, 0.20f), now: 1.5f);
        Assert(!zSpam.ExposureIncremented && !zSpam.TriggeredAdaptation && sys.GetExposureCount(AdaptationCategory.ZapGun) == 1, "Rapid zap tick must debounce");

        // Sustained beam ticking until t=3.9s -> still not 3.0s since exposure at t=1.0s
        var zHoldEarly = sys.EvaluateSustainedZapBeam(14, 18, false, Legacy(2, 0.95f, 0.20f), now: 3.9f);
        Assert(!zHoldEarly.TriggeredAdaptation, "2.9s sustained must not adapt yet");

        // At t=4.05s (>= 3.0s sustained since t=1.0s) -> exposure 2 -> ADAPTS + HEALS!
        var zHoldTrigger = sys.EvaluateSustainedZapBeam(14, 18, false, Legacy(2, 0.95f, 0.20f), now: 4.05f);
        Assert(zHoldTrigger.TriggeredAdaptation && zHoldTrigger.Negated && zHoldTrigger.FinalHP == 18 && sys.IsAdapted(AdaptationCategory.ZapGun), "3.0s sustained zap must trigger ZapGun adaptation");

        // Subsequent zap is immediately negated
        var zAfter = sys.EvaluateStun(AdaptationCategory.ZapGun, 18, 18, false, Legacy(2, 0.95f, 0.20f), now: 10.0f);
        Assert(zAfter.Negated && zAfter.WasAlreadyAdapted && !zAfter.TriggeredAdaptation, "Post-adaptation zap must be negated");

        // Separate instance testing fresh engagement gap (> 1.5s gap)
        var sys2 = new MahoragaAdaptationSystem();
        var a1 = sys2.EvaluateStun(AdaptationCategory.ZapGun, 18, 18, false, Legacy(2, 0.95f, 0.20f), now: 1.0f);
        Assert(a1.ExposureIncremented && !a1.TriggeredAdaptation, "First fresh zap = 1");
        var a2 = sys2.EvaluateStun(AdaptationCategory.ZapGun, 18, 18, false, Legacy(2, 0.95f, 0.20f), now: 2.6f);
        Assert(a2.TriggeredAdaptation && sys2.IsAdapted(AdaptationCategory.ZapGun), "Second zap after 1.6s gap (>1.5s) must adapt");
    }

    private static void TestStunGrenadeAdaptation()
    {
        var stuns = new TimedStunExposure();
        Type grenade = typeof(int);
        Type otherWeapon = typeof(string);
        Assert(stuns.Register(grenade, out bool first) == 1f && !first, "first grenade stuns fully");
        Assert(stuns.Register(grenade, out bool second) == 1f && !second, "second grenade stuns fully");
        Assert(stuns.Register(grenade, out bool third) == 0.5f && third, "third grenade stuns half as long and turns the wheel");
        Assert(stuns.Register(otherWeapon, out bool other) == 1f && !other, "a different weapon has its own exposures");
        Assert(stuns.Register(grenade, out bool later) == 0.5f && !later, "later grenades still stun");
    }

    private static void TestCoordinatedShovels()
    {
        var combo = new CoordinatedShovelStrike();
        Assert(!combo.Register(1, 10f, 0), "one player is insufficient");
        Assert(!combo.Register(1, 10.4f, 1), "repeated player does not fill the opening");
        Assert(!combo.Register(2, 10.5f, 1), "two players are insufficient");
        Assert(!combo.Register(3, 10.6f, 2), "three players are insufficient");
        Assert(combo.Register(4, 10.7f, 3), "four players qualify at the opening stage");

        combo.Clear();
        for (ulong player = 1; player <= 4; player++)
        {
            Assert(!combo.Register(player, 11f + player * 0.1f, 1), "adapted boss survives the opening");
        }
        Assert(!combo.Register(4, 11.5f, 1), "rapid duplicate cannot count as another swing");
        Assert(combo.Register(4, 12.1f, 1), "one of the same four can land the extra blow");

        combo.Clear();
        Assert(!combo.Register(1, 13f, 0) && !combo.Register(2, 13.8f, 0) &&
            !combo.Register(3, 13.9f, 0) && !combo.Register(4, 14f, 0), "slow opening is not a coordinated strike");
    }

    private static void TestShovelIndependence()
    {
        var system = new MahoragaAdaptationSystem();
        var settings = AdaptationSettings.CreateProgressive();
        system.EvaluateDamage(AdaptationCategory.Shotgun, 2, 18, 18, false, settings, 1f);
        Assert(system.GetStage(AdaptationCategory.Shotgun) == 1 && system.GetStage(AdaptationCategory.Shovel) == 0,
            "gunfire cannot increase the shovel requirement");
        Assert(MahoragaAdaptationSystem.ClassifyDamage(331, 2, true, false, false, false) ==
            AdaptationCategory.VehicleImpact, "verified Cruiser hit ID uses its own phenomenon");
        var custom = (AdaptationCategory)MahoragaAdaptationSystem.FirstCustomWeaponCategory;
        system.EvaluateDamage(custom, 1, 18, 18, false, settings, 2f);
        Assert(system.GetStage(custom) == 1 && system.GetStage(AdaptationCategory.Other) == 0,
            "an identified mod weapon has its own resistance");
    }

    private static void TestMoonRarityParser()
    {
        Dictionary<string, int> parsed = MoonRarityParser.Parse("Experimentation:10, Titan: 35, Artifice:40, 46 Infernis:50, BadToken, Negative:-5, Empty:");
        Assert(parsed.Count == 5, $"Expected 5 valid entries, got {parsed.Count}");
        Assert(parsed["Experimentation"] == 10, "Experimentation=10");
        Assert(parsed["titan"] == 35, "Case-insensitive Titan=35");
        Assert(parsed["Artifice"] == 40, "Artifice=40");
        Assert(parsed["46 Infernis"] == 50, "Modded moon 46 Infernis=50");
        Assert(parsed["Negative"] == 0, "Negative clamped to 0");
        Assert(MoonRarityParser.Parse("").Count == 0, "Empty string -> empty dict");
    }

    // ------------------------------------------------------------------------------------------------
    // Progressive adaptation + new categories
    // ------------------------------------------------------------------------------------------------

    private static void TestNewCategoryClassification()
    {
        // Old Bird missile blast near us (no attacker, force 6) -> its own category, beats generic Explosion.
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 6, false, false, false, true, recentOldBirdMissileNearby: true) == AdaptationCategory.OldBirdMissile, "Tracked missile blast must be OldBirdMissile");
        // A plain mine (no missile tracked) stays Explosion.
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 6, false, false, false, true) == AdaptationCategory.Explosion, "Mine stays Explosion");
        // Creature bite: no attacker, small force, an enemy is touching us -> EnemyMelee; with nobody around -> Other.
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 1, false, false, false, false, false, enemyAttackerSuspected: true) == AdaptationCategory.EnemyMelee, "Baboon bite must be EnemyMelee");
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 4, false, false, false, false, false, enemyAttackerSuspected: true) == AdaptationCategory.EnemyMelee, "Giant Kiwi stomp (4) must be EnemyMelee");
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 1, false, false, false, false) == AdaptationCategory.Other, "Unknown source stays Other");
        // A shovel swing during somebody else's blast keeps Melee; a Cruiser hit (force 12, attacker) is Other, not Shotgun.
        Assert(MahoragaAdaptationSystem.ClassifyDamage(1, 1, true, true, false, true) == AdaptationCategory.Melee, "Shovel during a blast stays Melee");
        Assert(MahoragaAdaptationSystem.ClassifyDamage(-1, 12, true, false, false, false) == AdaptationCategory.Other, "Vehicle roadkill force 12 must not be Shotgun");
        Assert(MahoragaAdaptationSystem.CategoryCount <= 31, "categories fit in the adaptation mask");
    }

    private static void TestProgressiveAdaptation()
    {
        AdaptationSettings st = AdaptationSettings.CreateProgressive(1, 0.40f, 0.70f, 0.95f, 0.20f);
        Assert(st.StageCount == 3, "3 stages");
        var sys = new MahoragaAdaptationSystem();

        // Shotgun blasts of 5. Hit 1 at 0 % -> 5 dmg + click (stage 1).
        var h1 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, 18, 18, false, st, 1f);
        Assert(h1.EffectiveDamage == 5 && h1.TriggeredWheelClick && !h1.TriggeredAdaptation && h1.HealAmount == 0 && h1.StageAfter == 1 && h1.FinalHP == 13, $"hit1: 5 dmg + click, no heal (got dmg={h1.EffectiveDamage} hp={h1.FinalHP})");
        Assert(sys.WheelStepCount == 1 && !sys.IsAdapted(AdaptationCategory.Shotgun), "wheel at step 1, not adapted");

        // Hit 2 at 40 % -> 3 dmg + click (stage 2).
        var h2 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, h1.FinalHP, 18, false, st, 2f);
        Assert(h2.EffectiveDamage == 3 && h2.TriggeredWheelClick && h2.StageAfter == 2 && h2.FinalHP == 10 && Math.Abs(h2.ResistanceApplied - 0.40f) < 1e-4f, $"hit2: 40 % resist (got dmg={h2.EffectiveDamage})");

        // Hit 3 at 70 % -> 1.5 + 0 carried buffer -> 1 dmg (buffer 0.5 + 0.0?) ; final stage heals 20 % (round(3.6)=4) and locks the category.
        var h3 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, h2.FinalHP, 18, false, st, 3f);
        Assert(h3.TriggeredAdaptation && !h3.TriggeredWheelClick && h3.StageAfter == 3 && h3.HealAmount == 4, $"hit3 completes adaptation and heals (heal={h3.HealAmount})");
        Assert(sys.IsAdapted(AdaptationCategory.Shotgun) && sys.WheelStepCount == 3 && sys.AdaptedCategoryCount == 1, "adapted, 3 wheel steps");

        // Afterwards 95 % resistance and no more wheel movement.
        var h4 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, 18, 18, false, st, 4f);
        Assert(h4.WasAlreadyAdapted && !h4.TriggeredWheelClick && !h4.TriggeredAdaptation && h4.StageAfter == h4.StageBefore, "no more stages once adapted");
        Assert(sys.WheelStepCount == 3, "wheel does not move again");
        Assert(Math.Abs(st.ResistanceForStage(0)) < 1e-6f && Math.Abs(st.ResistanceForStage(3) - 0.95f) < 1e-4f, "stage resistances");
    }

    private static void TestHitsPerStage()
    {
        AdaptationSettings st = AdaptationSettings.CreateProgressive(2, 0.40f, 0.70f, 0.95f, 0.20f);
        var sys = new MahoragaAdaptationSystem();
        int clicks = 0;
        int hp = 18;
        for (int i = 1; i <= 6; i++)
        {
            var r = sys.EvaluateDamage(AdaptationCategory.Melee, 0, hp, 18, false, st, i); // zero-force probes: exposures only
            hp = r.FinalHP;
            if (r.TriggeredWheelClick) clicks++;
            if (i == 1) Assert(!r.TriggeredWheelClick && r.StageAfter == 0, "first exposure with HitsPerStage=2 does not click");
            if (i == 2) Assert(r.TriggeredWheelClick && r.StageAfter == 1, "second exposure clicks stage 1");
            if (i == 6) Assert(r.TriggeredAdaptation, "sixth exposure completes adaptation");
        }
        Assert(clicks == 2, "two intermediate clicks");
        // Other categories were not touched.
        Assert(sys.GetStage(AdaptationCategory.Shotgun) == 0 && !sys.IsAdapted(AdaptationCategory.Explosion), "categories are independent");
    }

    private static void TestLethalMidRamp()
    {
        AdaptationSettings st = AdaptationSettings.CreateProgressive();
        var sys = new MahoragaAdaptationSystem();
        var lethal = sys.EvaluateDamage(AdaptationCategory.Explosion, 6, 4, 18, false, st, 1f);
        Assert(lethal.IsLethal && lethal.FinalHP == 0 && !lethal.TriggeredWheelClick && sys.WheelStepCount == 0 && sys.GetStage(AdaptationCategory.Explosion) == 0, "lethal blow neither clicks nor advances");
    }

    private static void TestAdaptingDoesNotAdvance()
    {
        AdaptationSettings st = AdaptationSettings.CreateProgressive();
        var sys = new MahoragaAdaptationSystem();
        for (int i = 0; i < 5; i++)
        {
            var r = sys.EvaluateDamage(AdaptationCategory.Other, 2, 18, 18, isCurrentlyAdapting: true, st, i);
            Assert(!r.TriggeredWheelClick && !r.TriggeredAdaptation, "no stage progress while adapting");
        }
        Assert(sys.GetExposureCount(AdaptationCategory.Other) == 0 && sys.WheelStepCount == 0, "stance hits are not counted as exposures");
    }

    private static void TestZapAntiSpamWithStages()
    {
        AdaptationSettings st = AdaptationSettings.CreateProgressive();
        var sys = new MahoragaAdaptationSystem();
        var z1 = sys.EvaluateStun(AdaptationCategory.ZapGun, 18, 18, false, st, 1.0f);
        Assert(z1.ExposureIncremented && z1.TriggeredWheelClick, "fresh zap clicks once");
        for (int i = 1; i <= 10; i++) // 10 re-applications 0.1 s apart (< 1.5 s gap, < 3 s sustained)
        {
            var zs = sys.EvaluateStun(AdaptationCategory.ZapGun, 18, 18, false, st, 1.0f + i * 0.1f);
            Assert(!zs.ExposureIncremented && !zs.TriggeredWheelClick, "spam must not click");
        }
        Assert(sys.WheelStepCount == 1, "still a single wheel step after the spam");
        var tick = sys.EvaluateSustainedZapBeam(18, 18, false, st, 4.1f);
        Assert(tick.ExposureIncremented && tick.TriggeredWheelClick && tick.StageAfter == 2, "3 s sustained tick advances stage 2");
    }

    private static void TestHpFloor()
    {
        AdaptationSettings st = AdaptationSettings.CreateProgressive();
        var sys = new MahoragaAdaptationSystem();
        var r = sys.EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, 7, 18, false, st, 1f, hpFloor: 5);
        Assert(!r.IsLethal && r.EffectiveDamage == 2 && r.FinalHP == 5, $"damage clipped to the floor (got dmg={r.EffectiveDamage} hp={r.FinalHP})");
        var r2 = sys.EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, 5, 18, false, st, 2f, hpFloor: 5);
        Assert(!r2.IsLethal && r2.EffectiveDamage == 0 && r2.FinalHP == 5, "at the floor: no damage, never healed");
        var r3 = new MahoragaAdaptationSystem().EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, 4, 18, false, st, 3f, hpFloor: 5);
        Assert(r3.EffectiveDamage == 0 && r3.FinalHP == 4, "below the floor: still never healed by the clip");
    }

    private static void TestOldBirdBalance()
    {
        AdaptationSettings st = AdaptationSettings.CreateProgressive(1, 0.40f, 0.70f, 0.95f, 0.20f, oldBirdMissileFullResistance: 0.90f);
        const int maxHp = 18;
        const int floor = 5; // round(18 * 0.30)

        // --- ONE Old Bird: a missile (force 6) every 2 s for 10 minutes, regen as in the game (1 %/s after 8 s quiet -> never quiet).
        var sys = new MahoragaAdaptationSystem();
        var regen = new RegenerationTracker();
        var attackers = new DistinctAttackerWindow();
        int hp = maxHp;
        int minHp = hp;
        float lastDamage = -999f;
        for (int i = 0; i < 300; i++)
        {
            float t = i * 2f;
            attackers.Register(1, t);
            int f = attackers.Count(t, 6f) < 3 ? floor : 0;
            var r = sys.EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, hp, maxHp, false, st, t, f);
            Assert(!r.IsLethal, $"one Old Bird killed Mahoraga at missile {i}");
            if (r.EffectiveDamage > 0) lastDamage = t;
            hp = r.FinalHP;
            hp += regen.Tick(2f, t, lastDamage, hp, maxHp, 8f, 0.01f);
            minHp = Math.Min(minHp, hp);
        }
        Assert(minHp >= floor, $"a single Old Bird must never push HP below the floor (min={minHp})");

        // --- THREE Old Birds shooting together (one missile each every 2 s, staggered): the floor lifts and Mahoraga can die.
        var sys5 = new MahoragaAdaptationSystem();
        var attackers5 = new DistinctAttackerWindow();
        int hp5 = maxHp;
        bool died = false;
        float t5 = 0f;
        for (int round = 0; round < 60 && !died; round++)
        {
            for (int bird = 1; bird <= 3 && !died; bird++)
            {
                t5 = round * 2f + bird * 0.4f;
                attackers5.Register(bird, t5);
                int f = attackers5.Count(t5, 6f) < 3 ? floor : 0;
                var r = sys5.EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, hp5, maxHp, false, st, t5, f);
                hp5 = r.FinalHP;
                if (r.IsLethal) died = true;
            }
        }
        Assert(died, "three Old Birds attacking together must be able to kill Mahoraga");
        Assert(t5 < 90f, $"...within a reasonable time (died at t={t5:0.0}s)");

        // Even four Old Birds are below a fully adapted boss's six-shooter threshold.
        var sys4 = new MahoragaAdaptationSystem();
        var attackers4 = new DistinctAttackerWindow();
        int hp4 = maxHp;
        for (int round = 0; round < 200; round++)
        {
            for (int bird = 1; bird <= 4; bird++)
            {
                float t = round * 2f + bird * 0.4f;
                attackers4.Register(bird, t);
                int f = attackers4.Count(t, 6f) < 6 ? floor : 0;
                var r = sys4.EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, hp4, maxHp, false, st, t, f);
                Assert(!r.IsLethal, "four Old Birds must not be enough");
                hp4 = r.FinalHP;
            }
        }
        var simultaneous = sys4.EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, 6, maxHp,
            false, st, 450f, minimumDamage: 6);
        Assert(simultaneous.IsLethal, "a qualifying barrage can finish Mahoraga through missile resistance");
        Assert(st.OldBirdMissileFullResistance == 0.90f && st.FullResistance == 0.95f, "missile ceiling is independent of the general one");
    }

    private static void TestRegeneration()
    {
        var regen = new RegenerationTracker();
        Assert(regen.Tick(1f, 5f, lastDamageTime: 0f, currentHP: 10, maxHP: 18, quietDelaySeconds: 8f, percentOfMaxPerSecond: 0.01f) == 0, "waits for the quiet period");
        int total = 0;
        for (int i = 0; i < 100; i++)
        {
            total += regen.Tick(1f, 20f + i, lastDamageTime: 0f, currentHP: 10 + total, maxHP: 18, quietDelaySeconds: 8f, percentOfMaxPerSecond: 0.01f);
        }
        Assert(total == 8, $"heals up to max and no further (healed {total})");
        Assert(regen.Tick(1f, 200f, 0f, 18, 18, 8f, 0.01f) == 0, "nothing at full HP");
        // 0.18 HP/s -> one HP roughly every 5.6 s
        var r2 = new RegenerationTracker();
        int hp = 1;
        for (int i = 0; i < 10; i++) hp += r2.Tick(1f, 100f + i, 0f, hp, 18, 8f, 0.01f);
        Assert(hp == 2, $"fractions accumulate (hp={hp})");
    }

    private static void TestAggroHysteresis()
    {
        var table = new AggroTable(6f);
        table.Add(1, 1.5f, 0f);
        Assert(table.TryGetTop(0f, out int top, out float w, null) && top == 1 && Math.Abs(w - 1.5f) < 1e-4f, "single attacker is top");
        Assert(Math.Abs(table.Get(1, 6f) - 0.75f) < 1e-3f, "halves every half-life");
        Assert(!table.Has(1, 60f) , "fades out completely");

        // Two attackers trading hits: B must clearly out-hit A before the target flips (no oscillation).
        var t2 = new AggroTable(6f);
        t2.Add(1, 2f, 0f);   // A hits first
        t2.Add(2, 1.25f, 1f); // B hits once
        Assert(!t2.ShouldSwitch(1, 2, 1f), "single hit from B does not steal the target");
        t2.Add(2, 1.25f, 2f);
        Assert(!t2.ShouldSwitch(1, 2, 2f), "two hits from B (2.5 vs ~1.8*1.35+0.4) still not enough");
        t2.Add(2, 1.25f, 3f);
        t2.Add(2, 1.25f, 4f);
        Assert(t2.ShouldSwitch(1, 2, 4f), "sustained attack from B takes over");
        // After the swap, A needs the same margin to take it back.
        t2.Add(1, 1.25f, 4.1f);
        Assert(!t2.ShouldSwitch(2, 1, 4.1f), "A cannot immediately swap back");

        // Top selection honours the accept filter.
        var t3 = new AggroTable(6f);
        t3.Add(10, 5f, 0f);
        t3.Add(11, 1f, 0f);
        Assert(t3.TryGetTop(0f, out int pick, out _, id => id != 10) && pick == 11, "filter skips invalid targets");
        Assert(!new AggroTable(6f).TryGetTop(0f, out _, out _, null), "empty table has no top");
    }

    private static void TestCooldownSet()
    {
        var set = new CooldownSet();
        set.Block(7, 30f);
        Assert(set.IsBlocked(7, 10f) && !set.IsBlocked(8, 10f), "blocked until the deadline");
        Assert(!set.IsBlocked(7, 30.1f), "expires");
        set.Block(9, 5f);
        set.Prune(6f);
        Assert(!set.IsBlocked(9, 1f), "pruned entries are gone");
    }

    private static void TestEngagementPolicy()
    {
        EngageVerdict V(bool valid = true, bool canDie = true, float t = 1f, float unreachable = 0f, float dist = 5f, bool player = false, bool attacked = false) =>
            EnemyEngagementPolicy.Evaluate(valid, canDie, t, 20f, 6f, unreachable, 8f, dist, 120f, player, attacked, 2f);

        Assert(V() == EngageVerdict.Continue, "healthy fight continues");
        Assert(V(valid: false) == EngageVerdict.TargetGone, "dead/destroyed target ends it");
        Assert(V(t: 20.1f) == EngageVerdict.TimedOut, "hard cap");
        Assert(V(canDie: false, t: 6.1f) == EngageVerdict.ImmortalTimedOut, "immortal enemies get a short cap, never an endless brawl");
        Assert(V(canDie: false, t: 3f) == EngageVerdict.Continue, "...but a short swat is fine");
        Assert(V(unreachable: 8.1f) == EngageVerdict.Unreachable, "no path -> give up");
        Assert(V(dist: 130f) == EngageVerdict.TooFar, "too far -> give up");
        Assert(V(player: true, t: 3f) == EngageVerdict.PlayerTakesPriority, "players come first");
        Assert(V(player: true, t: 1f) == EngageVerdict.Continue, "...after the minimum commitment time");
        Assert(V(player: true, t: 3f, attacked: true) == EngageVerdict.Continue, "unless the enemy is attacking us right now");
        Assert(V(canDie: false, t: 7f, player: true, attacked: true) == EngageVerdict.ImmortalTimedOut, "the cap beats everything else");
    }

    private static void TestDistinctAttackers()
    {
        var w = new DistinctAttackerWindow();
        w.Register(1, 0f); w.Register(2, 1f); w.Register(2, 2f); w.Register(3, 3f);
        Assert(w.Count(3f, 6f) == 3, "three different attackers");
        Assert(w.Count(7.5f, 6f) == 2, "attacker 1 aged out of the window");
        Assert(w.Count(20f, 6f) == 0, "everyone aged out");
    }

    private static void TestGrowthMath()
    {
        // Old Bird 7.5 m vs Mahoraga 3.75 m -> match scale 2.0, capped by the config.
        Assert(Math.Abs(GrowthMath.ScaleToMatch(7.5f, 3.75f, 3f) - 2f) < 1e-4f, "match = height ratio");
        Assert(Math.Abs(GrowthMath.ScaleToMatch(7.5f, 3.75f, 1.5f) - 1.5f) < 1e-4f, "config cap applies");
        Assert(GrowthMath.ScaleToMatch(2f, 3.75f, 3f) == 1f, "never shrinks below base for short targets");
        // Six equal steps reach the match exactly and stop there.
        float earned = 1f;
        for (int i = 0; i < 6; i++) earned = GrowthMath.NextEarnedScale(earned, 2f, 6);
        Assert(Math.Abs(earned - 2f) < 1e-4f, "6 steps reach the Old Bird height");
        Assert(Math.Abs(GrowthMath.NextEarnedScale(earned, 2f, 6) - 2f) < 1e-4f, "never past it");
        Assert(Math.Abs(GrowthMath.NextEarnedScale(1f, 2f, 6) - (1f + 1f / 6f)) < 1e-4f, "each step is 1/6 of the way");
        // Indoors is always base size; earned growth is kept for when it leaves.
        Assert(GrowthMath.EffectiveScale(1.8f, outside: false, maxScale: 2f) == 1f, "indoors = base size");
        Assert(Math.Abs(GrowthMath.EffectiveScale(1.8f, outside: true, maxScale: 2f) - 1.8f) < 1e-4f, "outdoors = earned");
        Assert(GrowthMath.EffectiveScale(2.5f, outside: true, maxScale: 2f) == 2f, "max cap applies");
        // Approach converges and is monotonic.
        float s = 1f;
        float prev = s;
        for (int i = 0; i < 600; i++)
        {
            s = GrowthMath.Approach(s, 2f, 0.016f, 2f);
            Assert(s >= prev - 1e-6f && s <= 2f + 1e-6f, "monotonic approach");
            prev = s;
        }
        Assert(s == 2f, "converges exactly");
    }

    private static void TestDurabilityAndKnockback()
    {
        Assert(OldBirdDurability.StrikeDamage(false, 1f) == 1f && OldBirdDurability.StrikeDamage(true, 1f) == 2f, "slam counts double");
        Assert(OldBirdDurability.StrikeDamage(false, 2f) == 2f, "a twice-as-big Mahoraga hits twice as hard");
        float hp = 8f;
        int swings = 0;
        while (hp > 0f) { hp -= OldBirdDurability.StrikeDamage(false, 1f); swings++; }
        Assert(swings == 8, "8 cleaves at base size");
        hp = 8f; swings = 0;
        while (hp > 0f) { hp -= OldBirdDurability.StrikeDamage(false, 2f); swings++; }
        Assert(swings == 4, "4 cleaves at double size");

        Assert(KnockbackMath.EnemySizeFactor(0) > KnockbackMath.EnemySizeFactor(2) && KnockbackMath.EnemySizeFactor(2) > KnockbackMath.EnemySizeFactor(1), "tiny > medium > giant");
        float dist = KnockbackMath.TotalDistance(9f, 4f);
        Assert(dist > 1.5f && dist < 3f, "cleave shove of a medium enemy is a couple of meters");
        Assert(KnockbackMath.DecayingSpeed(9f, 0.6f, 4f) < 1f, "push is essentially over after its duration");
    }

    private static float ComputeWheelAngle(int adaptationWheelSteps, int summonWheelOffsetSteps = 0)
    {
        return (adaptationWheelSteps + summonWheelOffsetSteps) * SummonTimeline.WheelTurnDegrees;
    }

    private static void TestOldBirdEngagementAndWheelProgression()
    {
        // 1. EnemyEngagementPolicy.EvaluateOldBird: Old Bird is #1 priority (no player preemption, no immortal cap)
        EngageVerdict EvalOb(
            bool valid = true,
            float engaged = 5f,
            float maxEngage = 120f,
            float unreachable = 0f,
            float unreachableLimit = 8f,
            float dist = 35f,
            float maxDist = 250f) =>
            EnemyEngagementPolicy.EvaluateOldBird(valid, engaged, maxEngage, unreachable, unreachableLimit, dist, maxDist);

        Assert(EvalOb() == EngageVerdict.Continue, "active Old Bird fight continues");
        Assert(EvalOb(engaged: 119.9f, dist: 250f) == EngageVerdict.Continue, "continues up to 120s and 250m seek range");
        Assert(EvalOb(valid: false) == EngageVerdict.TargetGone, "destroyed/invalid Old Bird terminates with TargetGone");
        Assert(EvalOb(engaged: 120.0f) == EngageVerdict.TimedOut, "Old Bird engagement terminates at maxEngageSeconds (120s)");
        Assert(EvalOb(unreachable: 8.0f) == EngageVerdict.Unreachable, "unreachable Old Bird terminates at limit");
        Assert(EvalOb(dist: 250.1f) == EngageVerdict.TooFar, "Old Bird beyond maxDistance terminates with TooFar");

        // 2. MahoragaAdaptationSystem.AdvanceWheelStep & +45 deg Wheel clicks (stages 1..3 + post-stage-3 Old Bird hits)
        AdaptationSettings st = AdaptationSettings.CreateProgressive(1, 0.40f, 0.70f, 0.95f, 0.20f, oldBirdMissileFullResistance: 0.90f);
        var sys = new MahoragaAdaptationSystem();
        Assert(sys.WheelStepCount == 0 && ComputeWheelAngle(sys.WheelStepCount) == 0f, "wheel starts at step 0 (0 deg)");

        // Stage 1..3 Old Bird missile hits advance WheelStepCount via EvaluateDamage (1 -> 2 -> 3)
        int hp = 18;
        for (int hit = 1; hit <= 3; hit++)
        {
            var eval = sys.EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, hp, 18, false, st, hit * 2f, hpFloor: 5);
            hp = eval.FinalHP;
            Assert(sys.WheelStepCount == hit, $"Old Bird hit {hit} must advance WheelStepCount to {hit} (got {sys.WheelStepCount})");
            if (hit < 3)
            {
                Assert(eval.TriggeredWheelClick && !eval.TriggeredAdaptation, $"hit {hit} triggers +45 deg wheel click");
            }
            else
            {
                Assert(eval.TriggeredAdaptation && sys.IsAdapted(AdaptationCategory.OldBirdMissile), "hit 3 completes full adaptation");
            }
        }

        // Post-stage-3 Old Bird hits: EvaluateDamage reports WasAlreadyAdapted, and HandleDamageServer calls AdvanceWheelStep()
        // so every subsequent Old Bird hit / growth step still turns the Dharma Wheel +1 spoke (+45 deg).
        for (int postHit = 4; postHit <= 8; postHit++)
        {
            var eval = sys.EvaluateDamage(AdaptationCategory.OldBirdMissile, 6, hp, 18, false, st, postHit * 2f, hpFloor: 5);
            hp = eval.FinalHP;
            Assert(eval.WasAlreadyAdapted && !eval.TriggeredAdaptation && !eval.TriggeredWheelClick, "post-stage-3 hit is already adapted");
            int newStep = sys.AdvanceWheelStep();
            Assert(newStep == postHit && sys.WheelStepCount == postHit, $"AdvanceWheelStep() must advance to step {postHit} (+45 deg per step)");
        }

        // 3. ComputeWheelAngle progression across all 8 spokes: 0 -> 45 -> 90 -> 135 -> 180 -> 225 -> 270 -> 315 -> 360 (0 mod 360)
        float[] expectedAngles = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f, 360f };
        for (int step = 0; step < expectedAngles.Length; step++)
        {
            float angle = ComputeWheelAngle(step);
            Assert(Math.Abs(angle - expectedAngles[step]) < 1e-4f, $"step {step} must equal {expectedAngles[step]} deg (got {angle})");
            if (step > 0)
            {
                float delta = angle - ComputeWheelAngle(step - 1);
                Assert(Math.Abs(delta - 45f) < 1e-4f, $"each wheel step must add exactly +45 deg (1 ball increment, never 360; got {delta})");
            }
        }
        Assert(Math.Abs(ComputeWheelAngle(8) % 360f) < 1e-4f, "8 steps of +45 deg complete 360 deg (0 mod 360)");
        Assert(Math.Abs(ComputeWheelAngle(3, summonWheelOffsetSteps: 1) - 180f) < 1e-4f,
            "initial summon +45 deg offset folds seamlessly into 3 adaptation steps -> 180 deg");
        Assert(Math.Abs(ComputeWheelAngle(3, summonWheelOffsetSteps: 2) - 225f) < 1e-4f,
            "Shadow Ambush re-summon adds a second +45 deg summon offset -> 225 deg without snap-back");
    }

    private static void TestPluginConfigDefaultsAndOrphanedCleanup()
    {
        var cfgFile = new BepInEx.Configuration.ConfigFile();
        // Seed obsolete non-OldBird keys in OrphanedEntries to verify ClearOrphanedEntries prunes them on load.
        cfgFile.SeedOrphaned("EnemyInteraction", "FightEnemies", "true");
        cfgFile.SeedOrphaned("EnemyInteraction", "EngageEnemiesWhenIdle", "true");
        cfgFile.SeedOrphaned("EnemyInteraction", "ImmortalEnemyEngageSeconds", "6.0");

        MahoragaConfig cfg = MahoragaConfig.Bind(cfgFile);
        Assert(MahoragaConfig.Current == cfg, "MahoragaConfig.Bind must set MahoragaConfig.Current");

        // Verify required defaults from Wave-2 specification
        Assert(cfg.AllowOutdoorSpawn.Value == true, "AllowOutdoorSpawn default must be true");
        Assert(cfg.OutdoorRarity.Value == 14, $"OutdoorRarity default must be 14 (got {cfg.OutdoorRarity.Value})");
        Assert(cfg.OutdoorMoonRarityOverrides.Value == "", "OutdoorMoonRarityOverrides default must be empty string");
        Assert(cfg.OutdoorMaxCount.Value == 1, $"OutdoorMaxCount default must be 1 (got {cfg.OutdoorMaxCount.Value})");
        Assert(Math.Abs(cfg.OutdoorPowerLevel.Value - 3.0f) < 1e-4f, $"OutdoorPowerLevel default must be 3.0f (got {cfg.OutdoorPowerLevel.Value})");
        Assert(cfg.FightOldBirds.Value == true, "FightOldBirds default must be true");
        Assert(Math.Abs(cfg.OldBirdSeekRange.Value - 250.0f) < 1e-4f, $"OldBirdSeekRange default must be 250f (got {cfg.OldBirdSeekRange.Value})");
        var seekRange = cfg.OldBirdSeekRange.Description.AcceptableValues as BepInEx.Configuration.AcceptableValueRange<float>;
        Assert(seekRange != null && Math.Abs(seekRange.MinValue - 10.0f) < 1e-4f && Math.Abs(seekRange.MaxValue - 500.0f) < 1e-4f,
            "OldBirdSeekRange acceptable range must be 10..500");
        Assert(Math.Abs(cfg.OldBirdEngageMaxSeconds.Value - 120.0f) < 1e-4f, $"OldBirdEngageMaxSeconds default must be 120f (got {cfg.OldBirdEngageMaxSeconds.Value})");
        Assert(cfg.EnableShadowAmbush.Value == true, "EnableShadowAmbush default must be true");
        Assert(Math.Abs(cfg.ShadowAmbushMinTime.Value - 0.62f) < 1e-4f, $"ShadowAmbushMinTime default must be 0.62f (got {cfg.ShadowAmbushMinTime.Value})");
        Assert(Math.Abs(cfg.ShadowAmbushLostSightDelay.Value - 12.0f) < 1e-4f, $"ShadowAmbushLostSightDelay default must be 12f (got {cfg.ShadowAmbushLostSightDelay.Value})");
        Assert(Math.Abs(cfg.ShadowAmbushCooldown.Value - 45.0f) < 1e-4f, $"ShadowAmbushCooldown default must be 45f (got {cfg.ShadowAmbushCooldown.Value})");
        Assert(Math.Abs(cfg.ShadowAmbushSubmergeDuration.Value - 2.6f) < 1e-4f, $"ShadowAmbushSubmergeDuration default must be 2.6f (got {cfg.ShadowAmbushSubmergeDuration.Value})");

        // Verify obsolete non-OldBird keys were removed and pruned
        Type cfgType = typeof(MahoragaConfig);
        Assert(cfgType.GetProperty("FightEnemies") == null, "obsolete FightEnemies property must not exist");
        Assert(cfgType.GetProperty("EngageEnemiesWhenIdle") == null, "obsolete EngageEnemiesWhenIdle property must not exist");
        Assert(cfgType.GetProperty("ImmortalEnemyEngageSeconds") == null, "obsolete ImmortalEnemyEngageSeconds property must not exist");
        Assert(!cfgFile.ContainsKey("EnemyInteraction", "FightEnemies") &&
               !cfgFile.ContainsKey("EnemyInteraction", "EngageEnemiesWhenIdle") &&
               !cfgFile.ContainsKey("EnemyInteraction", "ImmortalEnemyEngageSeconds"),
            "obsolete non-OldBird keys must not be bound in ConfigFile");
        Assert(cfgFile.OrphanedCount == 0 && cfgFile.SaveCount == 1, "ClearOrphanedEntries must clear orphaned keys and save once");

        // Verify ApplyOutdoorMoonOverrides layers OutdoorMoonRarityOverrides on top of MoonRarityOverrides
        cfg.MoonRarityOverrides.Value = "Experimentation:10,Titan:35,46 Infernis:25";
        cfg.OutdoorMoonRarityOverrides.Value = "Experimentation:0,Artifice:40,46 Infernis:50";
        var levelRarities = new Dictionary<LethalLib.Modules.Levels.LevelTypes, int>();
        var customRarities = new Dictionary<string, int>();
        cfg.ApplyOutdoorMoonOverrides(levelRarities, customRarities);
        Assert(levelRarities[LethalLib.Modules.Levels.LevelTypes.ExperimentationLevel] == 0, "OutdoorMoonRarityOverrides overrides Experimentation to 0");
        Assert(levelRarities[LethalLib.Modules.Levels.LevelTypes.TitanLevel] == 35, "MoonRarityOverrides base kept for Titan (35)");
        Assert(levelRarities[LethalLib.Modules.Levels.LevelTypes.ArtificeLevel] == 40, "OutdoorMoonRarityOverrides adds Artifice (40)");
        Assert(customRarities["46 Infernis"] == 50, "OutdoorMoonRarityOverrides overrides modded moon 46 Infernis to 50");
    }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigDefinition : IEquatable<ConfigDefinition>
    {
        public string Section { get; }
        public string Key { get; }

        public ConfigDefinition(string section, string key)
        {
            Section = section;
            Key = key;
        }

        public bool Equals(ConfigDefinition? other) =>
            other != null && Section == other.Section && Key == other.Key;

        public override bool Equals(object? obj) => obj is ConfigDefinition other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Section, Key);
    }

    public abstract class AcceptableValueBase
    {
    }

    public sealed class AcceptableValueRange<T> : AcceptableValueBase where T : IComparable
    {
        public T MinValue { get; }
        public T MaxValue { get; }

        public AcceptableValueRange(T minValue, T maxValue)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }
    }

    public sealed class AcceptableValueList<T> : AcceptableValueBase where T : IEquatable<T>
    {
        public T[] AcceptableValues { get; }

        public AcceptableValueList(params T[] acceptableValues)
        {
            AcceptableValues = acceptableValues;
        }
    }

    public sealed class ConfigDescription
    {
        public string Description { get; }
        public AcceptableValueBase? AcceptableValues { get; }

        public ConfigDescription(string description, AcceptableValueBase? acceptableValues = null)
        {
            Description = description;
            AcceptableValues = acceptableValues;
        }
    }

    public sealed class ConfigEntry<T>
    {
        private T _value;

        public ConfigDefinition Definition { get; }
        public T DefaultValue { get; }
        public ConfigDescription Description { get; }
        public event EventHandler? SettingChanged;

        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                SettingChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ConfigEntry(ConfigDefinition definition, T defaultValue, ConfigDescription description)
        {
            Definition = definition;
            DefaultValue = defaultValue;
            _value = defaultValue;
            Description = description;
        }
    }

    public sealed class ConfigFile
    {
        private readonly Dictionary<ConfigDefinition, object> _entries = new Dictionary<ConfigDefinition, object>();
        private Dictionary<ConfigDefinition, string> OrphanedEntries { get; } = new Dictionary<ConfigDefinition, string>();

        public int SaveCount { get; private set; }
        public int OrphanedCount => OrphanedEntries.Count;

        public void SeedOrphaned(string section, string key, string rawValue)
        {
            OrphanedEntries[new ConfigDefinition(section, key)] = rawValue;
        }

        public bool ContainsKey(string section, string key) =>
            _entries.ContainsKey(new ConfigDefinition(section, key));

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description) =>
            Bind(section, key, defaultValue, new ConfigDescription(description));

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription configDescription)
        {
            var def = new ConfigDefinition(section, key);
            var entry = new ConfigEntry<T>(def, defaultValue, configDescription);
            _entries[def] = entry;
            return entry;
        }

        public void Save()
        {
            SaveCount++;
        }
    }
}

namespace LethalLib.Modules
{
    public static class Levels
    {
        [Flags]
        public enum LevelTypes
        {
            None = 0,
            ExperimentationLevel = 4,
            AssuranceLevel = 8,
            VowLevel = 16,
            OffenseLevel = 32,
            MarchLevel = 64,
            RendLevel = 128,
            DineLevel = 256,
            TitanLevel = 512,
            AdamanceLevel = 1024,
            ArtificeLevel = 2048,
            EmbrionLevel = 4096,
            LiquidationLevel = 8192
        }
    }
}

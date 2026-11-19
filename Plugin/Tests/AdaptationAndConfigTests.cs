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
        Run("StunGrenade adaptation negates subsequent flashes", TestStunGrenadeAdaptation);
        Run("MoonRarityParser parses vanilla, modded, whitespace, and malformed entries", TestMoonRarityParser);

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
        Assert(MahoragaAdaptationSystem.ClassifyDamage(331, 2, true, false, false, false) == AdaptationCategory.Other, "Cruiser hitID=331 must classify as Other");
        // Stuns
        Assert(MahoragaAdaptationSystem.ClassifyStun(true) == AdaptationCategory.ZapGun, "PatcherTool must classify as ZapGun");
        Assert(MahoragaAdaptationSystem.ClassifyStun(false) == AdaptationCategory.StunGrenade, "Flashbang must classify as StunGrenade");
    }

    private static void TestMeleeAdaptationAndHealClamp()
    {
        var sys = new MahoragaAdaptationSystem();
        var hit1 = sys.EvaluateDamage(AdaptationCategory.Melee, 1, currentHP: 18, maxHP: 18, false, threshold: 2, 0.95f, 0.20f, now: 1.0f);
        Assert(hit1.EffectiveDamage == 1 && hit1.FinalHP == 17 && !hit1.TriggeredAdaptation && !hit1.IsLethal, "1st shovel should deal 1 dmg, no adapt");

        var hit2 = sys.EvaluateDamage(AdaptationCategory.Melee, 1, currentHP: 17, maxHP: 18, false, threshold: 2, 0.95f, 0.20f, now: 2.0f);
        Assert(hit2.EffectiveDamage == 1 && hit2.TriggeredAdaptation && hit2.FinalHP == 18 && hit2.HealAmount == 2, $"2nd shovel should trigger Melee adapt and clamp +4 heal to 18 (got HP={hit2.FinalHP}, heal={hit2.HealAmount})");
        Assert(sys.IsAdapted(AdaptationCategory.Melee) && sys.WheelStepCount == 1, "WheelStepCount must be 1");
    }

    private static void TestLethalBlowPrecedence()
    {
        var sys = new MahoragaAdaptationSystem();
        // 2x Shotgun (5 + 5 = 10 -> 8 HP) -> triggers Shotgun adapt (+4 heal -> 12 HP)
        var s1 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, 18, 18, false, 2, 0.95f, 0.20f, 1f);
        Assert(s1.FinalHP == 13 && !s1.TriggeredAdaptation, "Shotgun 1 leaves 13 HP");
        var s2 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, s1.FinalHP, 18, false, 2, 0.95f, 0.20f, 2f);
        Assert(s2.TriggeredAdaptation && s2.FinalHP == 12 && s2.HealAmount == 4, $"Shotgun 2 adapts and heals 8->12 (got {s2.FinalHP})");

        // 2x Landmine (6 + 6 = 12 -> 0 HP): 2nd explosion is LETHAL so it kills before adapting!
        var e1 = sys.EvaluateDamage(AdaptationCategory.Explosion, 6, s2.FinalHP, 18, false, 2, 0.95f, 0.20f, 5f);
        Assert(e1.FinalHP == 6 && !e1.IsLethal && !e1.TriggeredAdaptation, "Explosion 1 leaves 6 HP");
        var e2 = sys.EvaluateDamage(AdaptationCategory.Explosion, 6, e1.FinalHP, 18, false, 2, 0.95f, 0.20f, 8f);
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
            var r = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, hp, 18, false, 2, 0.95f, 0.20f, i);
            Assert(r.WasAlreadyAdapted && r.EffectiveDamage == 0 && r.FinalHP == 18, $"Blast {i} should buffer 0.25 without dealing 1 yet");
            hp = r.FinalHP;
        }
        var r4 = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, hp, 18, false, 2, 0.95f, 0.20f, 4f);
        Assert(r4.WasAlreadyAdapted && r4.EffectiveDamage == 1 && r4.FinalHP == 17, $"4th blast must accumulate 1.00 and deal 1 HP (got {r4.EffectiveDamage})");
    }

    private static void TestFullImmunityResistance()
    {
        var sys = new MahoragaAdaptationSystem();
        sys.SyncFromServer(1 << (int)AdaptationCategory.Shotgun, 1);
        for (int i = 0; i < 10; i++)
        {
            var r = sys.EvaluateDamage(AdaptationCategory.Shotgun, 5, 18, 18, false, 2, 1.00f, 0.20f, i);
            Assert(r.EffectiveDamage == 0 && r.FinalHP == 18, "100% resistance must deal 0 forever");
        }
    }

    private static void TestAdaptingArmor()
    {
        var sys = new MahoragaAdaptationSystem();
        // Force 6 during Adapting = 6 * 0.25 = 1.5 -> 1 HP damage, no overlapping adaptation trigger
        var r1 = sys.EvaluateDamage(AdaptationCategory.Explosion, 6, 18, 18, isCurrentlyAdapting: true, threshold: 1, 0.95f, 0.20f, 1f);
        Assert(r1.EffectiveDamage == 1 && r1.FinalHP == 17 && !r1.TriggeredAdaptation, $"Adapting armor should reduce 6->1 and avoid overlapping adapt (got dmg={r1.EffectiveDamage})");
    }

    private static void TestExplosionDedup()
    {
        var sys = new MahoragaAdaptationSystem();
        sys.EvaluateDamage(AdaptationCategory.Explosion, 6, 18, 18, false, 2, 0.95f, 0.20f, 10.0f);
        Assert(sys.IsDuplicateExplosion(10.05f), "0.05s later must be flagged duplicate");
        Assert(!sys.IsDuplicateExplosion(10.25f), "0.25s later must NOT be flagged duplicate");
    }

    private static void TestZapGunDebounce()
    {
        var sys = new MahoragaAdaptationSystem();
        // t=1.0s initial lock-on -> exposure 1
        var z1 = sys.EvaluateStun(AdaptationCategory.ZapGun, 14, 18, false, 2, 0.20f, now: 1.0f);
        Assert(!z1.Negated && !z1.TriggeredAdaptation && z1.ExposureIncremented && sys.GetExposureCount(AdaptationCategory.ZapGun) == 1, "Initial zap counts as 1");

        // Rapid re-call at t=1.5s (<1.5s gap, <3.0s sustained) -> debounced!
        var zSpam = sys.EvaluateStun(AdaptationCategory.ZapGun, 14, 18, false, 2, 0.20f, now: 1.5f);
        Assert(!zSpam.ExposureIncremented && !zSpam.TriggeredAdaptation && sys.GetExposureCount(AdaptationCategory.ZapGun) == 1, "Rapid zap tick must debounce");

        // Sustained beam ticking until t=3.9s -> still not 3.0s since exposure at t=1.0s
        var zHoldEarly = sys.EvaluateSustainedZapBeam(14, 18, false, 2, 0.20f, now: 3.9f);
        Assert(!zHoldEarly.TriggeredAdaptation, "2.9s sustained must not adapt yet");

        // At t=4.05s (>= 3.0s sustained since t=1.0s) -> exposure 2 -> ADAPTS + HEALS!
        var zHoldTrigger = sys.EvaluateSustainedZapBeam(14, 18, false, 2, 0.20f, now: 4.05f);
        Assert(zHoldTrigger.TriggeredAdaptation && zHoldTrigger.Negated && zHoldTrigger.FinalHP == 18 && sys.IsAdapted(AdaptationCategory.ZapGun), "3.0s sustained zap must trigger ZapGun adaptation");

        // Subsequent zap is immediately negated
        var zAfter = sys.EvaluateStun(AdaptationCategory.ZapGun, 18, 18, false, 2, 0.20f, now: 10.0f);
        Assert(zAfter.Negated && zAfter.WasAlreadyAdapted && !zAfter.TriggeredAdaptation, "Post-adaptation zap must be negated");

        // Separate instance testing fresh engagement gap (> 1.5s gap)
        var sys2 = new MahoragaAdaptationSystem();
        var a1 = sys2.EvaluateStun(AdaptationCategory.ZapGun, 18, 18, false, 2, 0.20f, now: 1.0f);
        Assert(a1.ExposureIncremented && !a1.TriggeredAdaptation, "First fresh zap = 1");
        var a2 = sys2.EvaluateStun(AdaptationCategory.ZapGun, 18, 18, false, 2, 0.20f, now: 2.6f);
        Assert(a2.TriggeredAdaptation && sys2.IsAdapted(AdaptationCategory.ZapGun), "Second zap after 1.6s gap (>1.5s) must adapt");
    }

    private static void TestStunGrenadeAdaptation()
    {
        var sys = new MahoragaAdaptationSystem();
        var f1 = sys.EvaluateStun(AdaptationCategory.StunGrenade, 15, 18, false, 2, 0.20f, now: 1f);
        Assert(!f1.Negated && !f1.TriggeredAdaptation, "1st flashbang stuns normally");
        var f2 = sys.EvaluateStun(AdaptationCategory.StunGrenade, 15, 18, false, 2, 0.20f, now: 6f);
        Assert(f2.Negated && f2.TriggeredAdaptation && f2.FinalHP == 18 && sys.IsAdapted(AdaptationCategory.StunGrenade), "2nd flashbang adapts & heals");
        var f3 = sys.EvaluateStun(AdaptationCategory.StunGrenade, 18, 18, false, 2, 0.20f, now: 12f);
        Assert(f3.Negated && f3.WasAlreadyAdapted, "3rd flashbang is 100% negated");
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
}

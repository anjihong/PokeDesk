using DeskPokemon;
using System.Text.Json;

internal static class EvolutionTests
{
    public static void Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "DeskPokemon-evolution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var serial = 0;
        Settings New(int dex) => Settings.NewAt(dex, Path.Combine(folder, $"{serial++}.json"));
        void Evolve(Settings s, int from, int to, bool shiny = false)
        {
            var selected = s.PrepareEvolution(from, shiny, to);
            check(selected == to, "explicit evolution target");
            s.CompleteEvolution(from, shiny, to);
        }
        HatchResult Hatch(Settings s, int dex, bool shiny = false)
        {
            s.Eggs = 1;
            s.PendingEgg = new(EggKind.Common, dex, shiny);
            return s.Hatch()!.Value;
        }
        try
        {
            check(EvolutionData.Forms.Count == 11 && EvolutionData.Count == 1036, "regional catalog size");
            check(EvolutionData.EggPool.Count == 550, "egg pool complete");
            var reachable = EvolutionData.EggPool.ToHashSet();
            int count;
            do { count = reachable.Count; foreach (var rule in EvolutionData.Rules) if (reachable.Contains(rule.FromId)) reachable.Add(rule.ToId); } while (count != reachable.Count);
            check(reachable.Count == EvolutionData.Count, "every species and form obtainable");
            check(EvolutionData.Rules.Select(r => (r.FromId, r.ToId)).Distinct().Count() == EvolutionData.Rules.Count, "unique edges");
            foreach (var r in EvolutionData.Rules)
            {
                check(EvolutionData.Contains(r.FromId) && EvolutionData.Contains(r.ToId) && r.Level > 0, "valid evolution rule");
                var s = New(r.FromId <= 1025 ? r.FromId : 4);
                s.AddOwned(r.FromId);
                s.For(r.FromId).Level = r.Level - 1;
                check(!s.EvolutionOptions(r.FromId).Any(o => o.ToId == r.ToId), "below evolution threshold");
                s.For(r.FromId).Level = r.Level;
                check(s.EvolutionOptions(r.FromId).Any(o => o.ToId == r.ToId), "at evolution threshold");
            }
            var eeveeTargets = new[] { 134, 135, 136, 196, 197, 470, 471, 700 };
            check(EvolutionData.EggPool.Contains(133) && eeveeTargets.All(d => !EvolutionData.EggPool.Contains(d)) &&
                EvolutionData.From(133).Select(r => r.ToId).Order().SequenceEqual(eeveeTargets.Order()) &&
                EvolutionData.From(133).All(r => r.Level == 25 && r.LevelSource == "override"),
                "Eevee has eight equal-level branches, evolved forms do not hatch directly");
            check(EvolutionData.From(172).Single().Level == 25 && EvolutionData.From(25).Single().Level == 40, "friendship and item fallback depth");
            foreach (var (from, level) in new[] { (79,37), (281,30), (290,20), (361,42), (439,25) })
                check(EvolutionData.From(from).All(r => r.Level == level), "branch level agreement");
            foreach (var (from, to) in new[] { (4263,4264), (4264,862), (4052,863), (4222,864), (4083,865), (4122,866), (4562,867), (6215,903), (6211,904), (8194,980), (6550,902) })
                check(EvolutionData.From(from).Single().ToId == to, "regional relationship");
            foreach (var from in new[] {264,222,83,122,211,550}) check(EvolutionData.From(from).Length == 0, "ordinary form cannot regional evolve");

            var ordinary = New(4);
            ordinary.For(4).Level = 16;
            ordinary.For(4).Exp = 17;
            Evolve(ordinary, 4, 5);
            check(ordinary.SelectedDex == 5 && ordinary.Owned.IsSupersetOf([4,5]), "evolution selects target and preserves source");
            ordinary.SelectedDex = 4;
            ordinary.For(4).Level = 36;
            check(ordinary.EvolutionOptions(4).Single().FromId == 5, "earlier appearance finds ready intermediate");
            var invalid = false;
            try { ordinary.PrepareEvolution(4); } catch (InvalidOperationException) { invalid = true; }
            check(invalid, "cannot evolve directly from earlier appearance");
            Evolve(ordinary, 5, 6);
            check(ReferenceEquals(ordinary.For(4), ordinary.For(6)) && ordinary.For(6).Exp == 17, "run shares growth and experience");
            check(ordinary.EvolutionOptions(4).Length == 0, "no repeated completed evolution");
            var saved = Path.Combine(folder, "shared.json");
            File.WriteAllText(saved, JsonSerializer.Serialize(ordinary));
            var restored = Settings.LoadFrom(saved)!;
            check(ReferenceEquals(restored.For(4), restored.For(6)) && restored.For(4).Level == 36, "growth sharing survives JSON roundtrip");
            restored.GrowthLinks[4] = int.MaxValue;
            var brokenJson = JsonSerializer.Serialize(restored);
            File.WriteAllText(saved,brokenJson);
            invalid = false;
            try { Settings.LoadFrom(saved); } catch (InvalidDataException) { invalid = true; }
            check(invalid && File.ReadAllText(saved) == brokenJson, "broken growth link rejected without overwriting save");

            var ralts = New(280);
            ralts.For(280).Level = 20;
            Evolve(ralts,280,281);
            ralts.For(281).Level = 30;
            check(ralts.PrepareEvolution(281, random: new FixedRandom(0,0)) == 282, "first branch uses random index");
            ralts.CompleteEvolution(281,false,282);
            var gardevoir = ralts.For(282);
            gardevoir.Level = 34; gardevoir.Exp = 9;
            check(Hatch(ralts,280).IsRestart, "duplicate base starts new run after branch");
            check(ralts.For(280).Level == 1 && ralts.For(280).Exp == 0 && gardevoir.Level == 34, "new run preserves evolved growth");
            check(ReferenceEquals(ralts.For(281), gardevoir), "intermediate stays on old run until reached again");
            check(!Hatch(ralts,280).IsRestart && ralts.For(280).Level == 2, "duplicate during rearing boosts level");
            ralts.For(280).Level = 20;
            Evolve(ralts,280,281);
            check(!ReferenceEquals(ralts.For(281), gardevoir), "intermediate rebinds to new run");
            ralts.For(281).Level = 30;
            check(ralts.EvolutionOptions(281).Single().ToId == 475, "remaining branch only");
            Evolve(ralts,281,475);
            check(!Hatch(ralts,280).IsRestart && ralts.For(475).Level == 31 && gardevoir.Level == 34, "complete collection boosts latest run only");
            ralts.AddOwned(280,true);
            check(ralts.For(280,true).Level == 1 && ralts.ShinyOwned.SetEquals([280]), "shiny growth and collection independent");

            var eevee = New(133);
            eevee.For(133).Level = 24;
            check(eevee.EvolutionOptions(133).Length == 0, "Eevee below level 25 cannot evolve");
            eevee.For(133).Level = 25;
            eevee.For(133).Exp = 17;
            check(eevee.EvolutionOptions(133).Length == 8, "Eevee at level 25 has eight choices");
            var firstEevee = eevee.PrepareEvolution(133, random: new FixedRandom(0, 0));
            check(firstEevee == 134, "first Eevee branch is randomly selected");
            eevee.CompleteEvolution(133, false, firstEevee);
            var firstGrowth = eevee.For(firstEevee);
            check(eevee.SelectedDex == firstEevee && ReferenceEquals(eevee.For(133), firstGrowth),
                "Eevee evolution selects target and shares run growth");
            check(Hatch(eevee, 133).IsRestart && eevee.For(133).Level == 1 && eevee.For(133).Exp == 0 &&
                firstGrowth.Level == 25 && firstGrowth.Exp == 17,
                "Eevee duplicate restarts while preserving evolved growth");
            eevee.For(133).Level = 24;
            check(!Hatch(eevee, 133).IsRestart && eevee.For(133).Level == 25,
                "duplicate during Eevee rearing adds one level");
            check(eevee.EvolutionOptions(133).Length == 7,
                "Eevee rearing leaves seven uncollected branches");
            var secondEevee = eevee.PrepareEvolution(133, random: new FixedRandom(0, 6));
            check(secondEevee == 700, "second Eevee branch randomly selects among remaining seven");
            eevee.CompleteEvolution(133, false, secondEevee);
            foreach (var target in eeveeTargets.Skip(1).Where(d => d != secondEevee))
            {
                if (eevee.For(133).CurrentDex != 133)
                {
                    check(Hatch(eevee, 133).IsRestart, "Eevee restarts for each remaining branch");
                    eevee.For(133).Level = 25;
                }
                Evolve(eevee, 133, target);
            }
            check(eeveeTargets.All(d => eevee.HasOwned(d)) && eevee.EvolutionOptions(133).Length == 0,
                "all eight Eevee branches can be collected");
            var lastGrowth = eevee.For(eevee.SelectedDex);
            check(!Hatch(eevee, 133).IsRestart && lastGrowth.Level == 26 && firstGrowth.Level == 25,
                "Eevee duplicate after all branches gives level bonus to latest run");
            eevee.AddOwned(133, true);
            eevee.For(133, true).Level = 25;
            check(eevee.EvolutionOptions(133, true).Length == 8 && eevee.ShinyOwned.SetEquals([133]),
                "shiny Eevee branch collection is independent");

            var tyrogue = New(236);
            tyrogue.For(236).Level = 20;
            var randomTarget = tyrogue.PrepareEvolution(236, random: new FixedRandom(0,1));
            check(randomTarget == 107, "random can choose another branch");
            var pendingPath = Path.Combine(folder,"pending.json");
            File.WriteAllText(pendingPath, JsonSerializer.Serialize(tyrogue));
            var pending = Settings.LoadFrom(pendingPath)!;
            check(pending.PrepareEvolution(236, random: new FixedRandom(0,2)) == 107, "pending target never rerolled on reload");
            Evolve(pending,236,107);
            Hatch(pending,236);
            pending.For(236).Level = 20;
            check(pending.EvolutionOptions(236).Length == 2, "second run excludes collected branch");
            var secondTyrogue = pending.PrepareEvolution(236, random: new FixedRandom(0, 1));
            check(secondTyrogue == 237, "second run randomly selects among remaining branches");
            pending.CompleteEvolution(236, false, secondTyrogue);
            check(!pending.HasOwned(106), "random result does not grant another branch");
            check(Hatch(pending, 236).IsRestart, "last remaining branch starts another run");
            pending.For(236).Level = 20;
            check(pending.PrepareEvolution(236, random: new FixedRandom(0, 0)) == 106,
                "single remaining branch is selected without a dialog");
            pending.CompleteEvolution(236, false, 106);

            var wurmple = New(265);
            wurmple.For(265).Level = 7;
            Evolve(wurmple,265,266);
            Hatch(wurmple,265);
            wurmple.For(266).Level = 10;
            Evolve(wurmple,266,267);
            check(wurmple.For(265).Level == 1, "old branch may continue evolving without growing new base");
            wurmple.For(265).Level = 7;
            Evolve(wurmple,265,268);
            wurmple.For(268).Level = 10;
            Evolve(wurmple,268,269);
            check(!ReferenceEquals(wurmple.For(267), wurmple.For(269)), "post-branch evolutions remain independent");

            var boundary = New(361);
            boundary.For(361).Level = 41;
            check(!Hatch(boundary,361).IsRestart && boundary.For(361).Level == 42, "hatch crossing threshold is a bonus not reset");
            check(boundary.EvolutionOptions(361).Length == 2, "hatch crossing threshold exposes evolution");

            var legacyPath = Path.Combine(folder,"legacy.json");
            const string legacy = """{"SchemaVersion":2,"StarterDex":4,"SelectedDex":134,"Owned":[4,134],"Progress":{"4":{"Level":18,"Exp":7},"134":{"Level":8,"Exp":3}},"Eggs":1,"PendingEgg":{"Kind":0,"Dex":134,"IsShiny":false}}""";
            File.WriteAllText(legacyPath, legacy);
            var migrated = Settings.LoadFrom(legacyPath)!;
            check(migrated.SchemaVersion == 3 && migrated.SelectedDex == 134 && migrated.For(4).Level == 18 && migrated.For(134).Exp == 3, "schema2 records preserved independently");
            check(migrated.PendingEgg!.Dex == 134 && File.ReadAllText(legacyPath + ".schema2.bak") == legacy, "Eevee egg and original backup preserved");
            check(migrated.Hatch()!.Value.Dex == 134 && migrated.For(134).Level == 9,
                "already confirmed legacy Eevee evolution egg hatches once");
            migrated.AddOwned(133);
            migrated.For(133).Level = 25;
            check(migrated.EvolutionOptions(133).Length == 7 &&
                migrated.EvolutionOptions(133).All(r => r.ToId != 134),
                "legacy owned Eevee evolution is removed from future branch choices");
            Evolve(migrated, 133, 135);
            check(migrated.For(134).Level == 9 && !ReferenceEquals(migrated.For(134), migrated.For(135)),
                "legacy Eevee evolution growth stays independent after new branch");
            foreach (var form in EvolutionData.Forms.Where(f => EvolutionData.EggPool.Contains(f.Id)))
            {
                migrated.PendingEgg = new(EggKind.Common,form.Id,true); migrated.Save();
                check(Settings.LoadFrom(legacyPath)!.PendingEgg == migrated.PendingEgg, "regional egg survives load validation");
            }

            var failurePath = Path.Combine(folder,"failure.json");
            var failing = Settings.NewAt(236,failurePath);
            failing.For(236).Level = 20; failing.Save();
            using (var locked = new FileStream(failurePath,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                invalid = false;
                try { failing.PrepareEvolution(236); } catch (IOException) { invalid = true; }
                check(invalid && failing.For(236).PendingEvolution is null, "failed prepare restores pending state");
            }
            failing.PrepareEvolution(236,target:106);
            using (var locked = new FileStream(failurePath,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                invalid = false;
                try { failing.CompleteEvolution(236,false,106); } catch (IOException) { invalid = true; }
                check(invalid && !failing.HasOwned(106) && failing.SelectedDex == 236 && failing.For(236).PendingEvolution == 106 && failing.For(236).History.SequenceEqual([236]), "failed evolution rolls back all state");
            }
            failing.CompleteEvolution(236,false,106);
            failing.Eggs = 1; failing.PendingEgg = new(EggKind.Common,236,false); failing.Save();
            var before = JsonSerializer.Serialize(failing);
            using (var locked = new FileStream(failurePath,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                invalid = false;
                try { failing.Hatch(); } catch (IOException) { invalid = true; }
                check(invalid, "restart save failure reached");
            }
            check(JsonSerializer.Serialize(failing) == before, "failed restart restores links and old run");
#if DEBUG
            var livePath = Path.Combine(folder, "live.json");
            var testPath = Path.Combine(folder, "evolution-test.json");
            var live = Settings.NewAt(4, livePath);
            var test = Settings.NewAt(4, testPath);
            var liveJson = File.ReadAllText(livePath);
            test.GrantTestEgg(280, true);
            check(test.PendingEgg == new PendingEgg(EggKind.Common, 280, true) && test.Eggs == 1 &&
                Settings.LoadFrom(testPath)!.PendingEgg == test.PendingEgg, "specified shiny egg persists in test save");
            test.Hatch();
            check(test.HasOwned(280, true) && !live.HasOwned(280, true) && File.ReadAllText(livePath) == liveJson,
                "test profile does not change live save");
            check(test.NextTestEvolutionLevel(280, true) == EvolutionData.From(280).Single().Level,
                "next test level uses evolution catalog");
            var exp = test.For(280, true).Exp = 9;
            check(test.JumpToNextTestEvolutionLevel(280, true) == 20 && test.For(280, true).Exp == exp,
                "jump preserves experience and color");
            Evolve(test, 280, 281, true);
            test.SelectedDex = 280;
            check(test.JumpToNextTestEvolutionLevel(280, true) == 30 && test.SelectedDex == 280 &&
                test.EvolutionOptions(280, true).All(r => r.FromId == 281),
                "jump from earlier appearance exposes intermediate evolution");
            test.GrantTestEgg(280, true);
            check(!test.Hatch()!.Value.IsRestart, "duplicate egg before branch completion boosts same growth");
            test.GrantTestEgg(4263, false);
            test.Hatch();
            check(test.HasOwned(4263) && test.NextTestEvolutionLevel(4263, false) == EvolutionData.From(4263).Single().Level,
                "regional base can hatch and jump toward regional evolution");
            test.GrantTestEgg(133, false);
            test.Hatch();
            check(test.NextTestEvolutionLevel(133, false) == 25, "test tool reaches Eevee branch level");
            before = JsonSerializer.Serialize(test);
            using (var locked = new FileStream(testPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                invalid = false;
                try { test.GrantTestEgg(79, false); } catch (IOException) { invalid = true; }
                check(invalid && JsonSerializer.Serialize(test) == before, "failed specified egg restores test state");
                invalid = false;
                try { test.JumpToNextTestEvolutionLevel(4263, false); } catch (IOException) { invalid = true; }
                check(invalid && JsonSerializer.Serialize(test) == before, "failed level jump restores test state");
            }
#endif
            failing.UnlockAll = true;
            check(failing.EvolutionOptions(361).Length == 0, "debug unlock cannot grant permanent evolution");
            failing.For(361).Level = 80;
            failing.AddOwned(361);
            check(failing.For(361).Level == 1, "first real acquisition starts fresh after debug preview");
        }
        finally { Directory.Delete(folder, recursive:true); }
    }
}

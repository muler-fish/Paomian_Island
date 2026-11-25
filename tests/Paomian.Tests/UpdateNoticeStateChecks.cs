using System.Diagnostics;
using Paomian.Core;

internal static class UpdateNoticeStateChecks
{
    internal static void Run()
    {
        var root = Directory.CreateTempSubdirectory("Paomian-notice-");
        try
        {
            CheckTransitions(Path.Combine(root.FullName, "manual"));
            CheckLegacyProfile(Path.Combine(root.FullName, "legacy"));
            CheckBundledHistory(Path.Combine(root.FullName, "known-history"), true);
            CheckBundledHistory(Path.Combine(root.FullName, "legacy-history"), false);
            CheckInstallerEvidence(Path.Combine(root.FullName, "automatic"), true);
            CheckInstallerEvidence(Path.Combine(root.FullName, "stale"), false);
        }
        finally { root.Delete(true); }
        Console.WriteLine("PASS update notice: first launch, manual updates, retries, merged ranges, acknowledgement, rollback, old profiles and installer evidence");
    }

    private static void CheckTransitions(string profile)
    {
        Version old = new(1, 0, 9), next = new(1, 0, 10), latest = new(1, 1, 0);
        var fresh = new LocalStore(profile);
        // 真实启动在视图模型写缓存标记前留下版本；即便首帧前就退出，同版本重启也不应判为旧资料。
        fresh.InitializeNewProfileVersion(old.ToString());
        fresh.Save(new UserState { FontSize = 23 });
        fresh.Cache.Configure(null, 32);
        PendingUpdateNotice? Start(Version version) => new LocalStore(profile).BeginUpdateNotice(version.ToString());
        Require(Start(old) is null && Start(old) is null, "首次启动中断或同版本重复启动误报升级");
        var single = new PendingUpdateNotice(old, next);
        Require(Start(next) == single && Start(next) == single, "正常升级或异常退出后丢失范围");
        Require(Start(old) is null, "未确认时回退");
        var merged = new PendingUpdateNotice(old, latest);
        Require(Start(latest) == merged && merged.Includes(next) && merged.Includes(latest) && !merged.Includes(old), "未确认时继续升级");
        var store = new LocalStore(profile);
        store.AcknowledgeUpdateNotice(next);
        Require(Start(latest) == merged, "旧确认回调影响新范围");
        store.AcknowledgeUpdateNotice(latest);
        store.Cache.Clear();
        Require(Start(latest) is null, "清理缓存后重复提醒");
        // 旧版仍会完整重写自己认识的 state.json，随后重装原版本应继续记住用户的确认。
        store.Save(store.Load());
        Require(Start(old) is null && Start(next) is null && Start(latest) is null, "回退后丢失确认记录");
        Version future = new(1, 10, 0);
        Require(Start(future) == new PendingUpdateNotice(latest, future), "之后升级未使用最高已确认版本");
        var other = new LocalStore(Path.Combine(profile, "independent"));
        Require(other.BeginUpdateNotice(future.ToString()) is null, "另一份资料受原目录影响");
    }

    private static void CheckLegacyProfile(string profile)
    {
        Directory.CreateDirectory(profile);
        var path = Path.Combine(profile, "state.json");
        const string oldJson = """{"SchemaVersion":1,"FontSize":21,"Theme":"dark"}""";
        File.WriteAllText(path, oldJson);
        Version initial = new(1, 0, 14), next = new(1, 0, 16);
        var store = new LocalStore(profile);
        store.InitializeNewProfileVersion(initial.ToString());
        var pending = store.BeginUpdateNotice(initial.ToString());
        Require(pending == new PendingUpdateNotice(null, initial, initial), "猜测了旧资料来源");
        Require(store.Load().FontSize == 21 && store.Load().Theme == "dark" && File.ReadAllText(path) == oldJson, "迁移覆盖设置");
        var merged = new LocalStore(profile).BeginUpdateNotice(next.ToString());
        Require(merged == new PendingUpdateNotice(null, next, initial) && merged.Includes(initial) &&
            !merged.Includes(new(1, 0, 13)) && !merged.Includes(new(1, 0, 17)), "未知来源升级后范围错误");
        store.AcknowledgeUpdateNotice(next);
        Require(new LocalStore(profile).BeginUpdateNotice(next.ToString()) is null, "旧资料重复提醒");

        // 旧版上次使用了强制结束，只写了项目的缓存标记，还没保存窗口设置。
        var cached = new LocalStore(Path.Combine(profile, "cache-only"));
        cached.Cache.Configure(null, 32);
        Require(new LocalStore(cached.Root).BeginUpdateNotice(initial.ToString())?.FirstKnownVersion == initial, "未识别上次运行的缓存标记");
    }

    private static void CheckInstallerEvidence(string root, bool intact)
    {
        var store = new LocalStore(Path.Combine(root, "profile"));
        if (!intact) store.Save(new UserState { FontSize = 19 });
        var exe = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "app")).FullName, AppIdentity.ExecutableName);
        var oldBinary = typeof(AppIdentity).Assembly.Location;
        File.Copy(oldBinary, exe);
        var source = Version.Parse(FileVersionInfo.GetVersionInfo(oldBinary).ProductVersion!.Split('+')[0]);
        var target = new Version(source.Major + 1, 0, 0);
        var stage = Path.Combine(store.Root, "updates", Guid.NewGuid().ToString("N"));
        var package = Directory.CreateDirectory(Path.Combine(stage, "package")).FullName;
        var newExe = Path.Combine(package, AppIdentity.ExecutableName);
        File.WriteAllText(newExe, "offline replacement");
        var hash = ReleaseUpdater.Hash(newExe);
        File.WriteAllText(Path.Combine(package, "SHA256SUMS.txt"), hash + "  " + AppIdentity.ExecutableName);
        var plan = UpdateInstaller.Prepare(new StagedUpdate(package, hash), store.Root, exe, false);
        UpdateInstaller.Apply(plan, UpdateInstaller.ReadPlan(plan));
        File.WriteAllText(Path.Combine(stage, "result.txt"), "success");
        if (!intact) File.AppendAllText(exe, " replaced again");
        var installed = new LocalStore(store.Root);
        installed.InitializeNewProfileVersion(target.ToString());
        var notice = installed.BeginUpdateNotice(target.ToString(), exe);
        Require(notice == (intact ? new PendingUpdateNotice(source, target) : new PendingUpdateNotice(null, target, target)),
            "旧更新器成功计划和现有文件不一致，或未识别可验证的来源");
    }

    private static void CheckBundledHistory(string profile, bool knownSource)
    {
        static Version V(int patch) => new(1, 0, patch);
        var baseline = new LocalStore(profile);
        if (knownSource) baseline.InitializeNewProfileVersion("1.0.13");
        else baseline.Save(new UserState { FontSize = 21 });

        // 每次模拟启动都重新打开资料，且只能从当次可执行文件读取当版正文。
        PendingUpdateNotice? Start(int patch, bool shouldRead)
        {
            var current = V(patch);
            var reads = 0;
            var pending = new LocalStore(profile).BeginUpdateNotice(current.ToString(), readBundled: version =>
            {
                Require(version == current, "读取了未运行版本的内置说明");
                reads++;
                return StartupNotes.ReadBundled(new StringReader($"## {current}\n离线说明 {patch}"), current);
            });
            Require(reads == (shouldRead ? 1 : 0), "重复读取内置说明或遗漏本次运行版本");
            return pending;
        }
        Start(14, true);
        Start(16, true);
        Require(Start(15, false) is null, "回退时保存了新的说明");
        Start(18, true);
        var restored = Start(18, false) ?? throw new Exception("异常退出后丢失未确认范围");
        var local = StartupNotes.FromLocal(restored, [], []);
        Require(restored.Source == (knownSource ? V(13) : null) && local.NeedsOnline && !local.IsComplete &&
            local.Entries.Select(n => n.Body).SequenceEqual(["离线说明 18", "离线说明 16", "离线说明 14"]),
            "连续升级后未保留各次启动的正文，或把未运行版本列入本地历史");
        var published = local.Entries[1] with { Body = "正式说明 16" };
        var partial = StartupNotes.FromLocal(restored, [], [new(V(14), V(16), [published], false)]);
        Require(partial.NeedsOnline && partial.Entries[1] == published, "未优先显示已取得的正式正文");
        new LocalStore(profile).AcknowledgeUpdateNotice(V(18));
        Require(Start(18, false) is null, "确认后同版本未清除待展示说明");
        var later = Start(19, true) ?? throw new Exception("遗漏后续升级");
        Require(StartupNotes.FromLocal(later, [], []).Entries.Single().Version == V(19), "确认前的正文混入新范围");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

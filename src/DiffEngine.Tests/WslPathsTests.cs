/// <summary>
/// A path inside a WSL distribution, written the way the Windows host needs it and back.
/// <para>
/// The mount table is the text of <c>/proc/mounts</c> from an Ubuntu distribution under WSL 2,
/// with a WSL 1 drive, a drive mounted somewhere with a space in it and a share added. Its
/// backslashes are octal escapes, as that file writes them.
/// </para>
/// </summary>
public class WslPathsTests
{
    const string mountTable =
        """
        /dev/sdd / ext4 rw,relatime,discard,errors=remount-ro,data=ordered 0 0
        none /mnt/wsl tmpfs rw,relatime 0 0
        drivers /usr/lib/wsl/drivers 9p ro,nosuid,nodev,noatime,aname=drivers;fmask=222;dmask=222,cache=0x5,access=client,msize=65536,trans=fd,rfd=8,wfd=8 0 0
        C:\134 /mnt/c 9p rw,noatime,aname=drvfs;path=C:\;uid=1000;gid=1000;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=6,wfd=6 0 0
        D:\134 /mnt/d 9p rw,noatime,aname=drvfs;path=D:\;uid=1000;gid=1000;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=6,wfd=6 0 0
        E: /mnt/e drvfs rw,noatime,uid=1000,gid=1000,case=off 0 0
        F:\134 /mnt/my\040drive 9p rw,noatime,aname=drvfs;path=F:\;uid=1000;gid=1000 0 0
        \134\134server\134share /mnt/share 9p rw,noatime,aname=drvfs;path=UNC\server\share;uid=1000;gid=1000 0 0
        tmpfs /run tmpfs rw,nosuid,nodev,mode=755 0 0
        """;

    static WslPaths Paths() =>
        new(WslPaths.ParseMounts(mountTable), @"\\wsl.localhost\Ubuntu\");

    [Test]
    public async Task OnlyTheDrivesAreMounts()
    {
        var mounts = WslPaths.ParseMounts(mountTable);

        await Assert.That(mounts).IsEquivalentTo(
        [
            new WslMount("/mnt/c", @"C:\"),
            new WslMount("/mnt/d", @"D:\"),
            new WslMount("/mnt/e", @"E:\"),
            new WslMount("/mnt/my drive", @"F:\"),
            new WslMount("/mnt/share", @"\\server\share\")
        ]);
    }

    [Test]
    [Arguments("/mnt/c/Users/simon/project/Tests.Method.received.txt", @"C:\Users\simon\project\Tests.Method.received.txt")]
    [Arguments("/mnt/c", @"C:\")]
    [Arguments("/mnt/d/Code/a b.txt", @"D:\Code\a b.txt")]
    [Arguments("/mnt/e/x.txt", @"E:\x.txt")]
    [Arguments("/mnt/my drive/x.txt", @"F:\x.txt")]
    [Arguments("/mnt/share/folder/x.txt", @"\\server\share\folder\x.txt")]
    public async Task AFileOnADriveIsWrittenWithItsLetter(string linux, string windows) =>
        await Assert.That(Paths().ToWindows(linux)).IsEqualTo(windows);

    /// <summary>
    /// Everything that is not on a drive is in the distribution, which the host reaches as a
    /// share. That includes a directory whose name only begins as a mount point does.
    /// </summary>
    [Test]
    [Arguments("/home/simon/project/Tests.Method.received.txt", @"\\wsl.localhost\Ubuntu\home\simon\project\Tests.Method.received.txt")]
    [Arguments("/tmp/a b.txt", @"\\wsl.localhost\Ubuntu\tmp\a b.txt")]
    [Arguments("/mnt/cache/x.txt", @"\\wsl.localhost\Ubuntu\mnt\cache\x.txt")]
    [Arguments("/mnt/wsl/x.txt", @"\\wsl.localhost\Ubuntu\mnt\wsl\x.txt")]
    public async Task AFileInTheDistributionIsWrittenAsAShare(string linux, string windows) =>
        await Assert.That(Paths().ToWindows(linux)).IsEqualTo(windows);

    /// <summary>
    /// The share is named by whatever <c>wslpath</c> said, which on an older Windows is
    /// <c>\\wsl$</c>, and it says it with or without the trailing separator.
    /// </summary>
    [Test]
    public async Task TheShareIsWhateverTheHostCallsIt()
    {
        var paths = new WslPaths([], @"\\wsl$\Debian");

        await Assert.That(paths.ToWindows("/home/x.txt")).IsEqualTo(@"\\wsl$\Debian\home\x.txt");
    }

    /// <summary>
    /// A drive mounted beneath another mount is its own drive, whatever order the table lists
    /// them in.
    /// </summary>
    [Test]
    public async Task TheDeepestMountWins()
    {
        var paths = new WslPaths(
            [
                new("/mnt/c", @"C:\"),
                new("/mnt/c/data", @"G:\")
            ],
            @"\\wsl.localhost\Ubuntu");

        await Assert.That(paths.ToWindows("/mnt/c/data/x.txt")).IsEqualTo(@"G:\x.txt");
        await Assert.That(paths.ToWindows("/mnt/c/other/x.txt")).IsEqualTo(@"C:\other\x.txt");
    }

    [Test]
    [Arguments(@"C:\Program Files\Beyond Compare *\", "/mnt/c/Program Files/Beyond Compare */")]
    [Arguments(@"c:\Users\simon\AppData\Local", "/mnt/c/Users/simon/AppData/Local")]
    [Arguments("C:/Program Files/WinMerge", "/mnt/c/Program Files/WinMerge")]
    [Arguments(@"C:\", "/mnt/c")]
    [Arguments("C:", "/mnt/c")]
    [Arguments(@"F:\tools", "/mnt/my drive/tools")]
    [Arguments(@"\\server\share\tools", "/mnt/share/tools")]
    public async Task AHostDirectoryIsWrittenAsItsMount(string windows, string linux)
    {
        await Assert.That(Paths().TryToLinux(windows, out var result)).IsTrue();
        await Assert.That(result).IsEqualTo(linux);
    }

    /// <summary>
    /// A drive that is not mounted cannot be looked in from here, and a path that is already a
    /// Linux one is not a host directory at all.
    /// </summary>
    [Test]
    [Arguments(@"Z:\tools")]
    [Arguments("/mnt/c/Program Files")]
    [Arguments(@"%ProgramFiles%\Tool")]
    [Arguments("")]
    public async Task APathOffTheMountsHasNoLinuxSpelling(string path)
    {
        await Assert.That(Paths().TryToLinux(path, out var result)).IsFalse();
        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments("/mnt/c", true)]
    [Arguments("/mnt/c/", true)]
    [Arguments("/mnt/c/Program Files/Git/cmd", true)]
    [Arguments("/mnt/my drive/bin/", true)]
    [Arguments("/usr/bin", false)]
    [Arguments("/mnt/cache/bin", false)]
    [Arguments("/", false)]
    public async Task ADirectoryIsOnTheHostWhenItIsUnderADrive(string directory, bool expected) =>
        await Assert.That(Paths().IsOnHost(directory)).IsEqualTo(expected);
}

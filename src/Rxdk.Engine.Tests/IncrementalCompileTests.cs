using Rxdk.Engine.Build;
using Xunit;

namespace Rxdk.Engine.Tests;

/// <summary>
/// The incremental build skips a compile only when the existing object would be reproduced: it is
/// newer than its source and recorded headers, and was built by the same compiler command. Before
/// the command was recorded, a changed define (e.g. a project gaining USE_XBOX_D3D8) left every
/// unchanged source's stale object in place. The same rule backs the VS20XX MSBuild compile task,
/// which links ObjFreshness.cs in.
/// </summary>
public sealed class IncrementalCompileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rxdk-incremental-").FullName;
    private readonly string _src;
    private readonly string _header;
    private readonly string _obj;
    private static readonly string Compiler = @"C:\llvm\bin\clang.exe";
    private static readonly string[] Args = { "-c", "a.c", "-DONE" };

    public IncrementalCompileTests()
    {
        _src = Path.Combine(_dir, "a.c");
        _header = Path.Combine(_dir, "a.h");
        _obj = Path.Combine(_dir, "a.obj");
        var old = DateTime.UtcNow.AddMinutes(-10);
        File.WriteAllText(_src, "#include \"a.h\"\n");
        File.WriteAllText(_header, "\n");
        File.SetLastWriteTimeUtc(_src, old);
        File.SetLastWriteTimeUtc(_header, old);
        File.WriteAllText(_obj, "obj");
        File.WriteAllText(_obj + ".d", $"{_obj.Replace(" ", "\\ ")}: {_src.Replace(" ", "\\ ")} \\\n  {_header.Replace(" ", "\\ ")}\n");
        File.WriteAllText(_obj + ".cmd", ObjFreshness.Signature(Compiler, Args));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private bool UpToDate(params string[] args) =>
        ObjFreshness.IsUpToDate(new[] { _src }, _obj, ObjFreshness.Signature(Compiler, args));

    [Fact]
    public void SameCommand_AndOlderInputs_IsUpToDate()
    {
        Assert.True(UpToDate(Args));
    }

    [Fact]
    public void ChangedDefine_Recompiles()
    {
        Assert.False(UpToDate("-c", "a.c", "-DTWO"));
    }

    [Fact]
    public void AddedFlag_Recompiles()
    {
        Assert.False(UpToDate("-c", "a.c", "-DONE", "-O2"));
    }

    [Fact]
    public void ReorderedArguments_Recompile()
    {
        // Argument order is significant to the compiler (later -D/-U and include paths win).
        Assert.False(UpToDate("-DONE", "-c", "a.c"));
    }

    [Fact]
    public void DifferentCompiler_Recompiles()
    {
        var sig = ObjFreshness.Signature(@"C:\other\clang.exe", Args);
        Assert.False(ObjFreshness.IsUpToDate(new[] { _src }, _obj, sig));
    }

    [Fact]
    public void MissingCommandRecord_Recompiles()
    {
        // Objects from a build before commands were recorded cannot be proven current.
        File.Delete(_obj + ".cmd");
        Assert.False(UpToDate(Args));
    }

    [Fact]
    public void NewerSource_Recompiles()
    {
        File.SetLastWriteTimeUtc(_src, DateTime.UtcNow.AddMinutes(5));
        Assert.False(UpToDate(Args));
    }

    [Fact]
    public void NewerRecordedHeader_Recompiles()
    {
        File.SetLastWriteTimeUtc(_header, DateTime.UtcNow.AddMinutes(5));
        Assert.False(UpToDate(Args));
    }

    [Fact]
    public void VanishedRecordedHeader_Recompiles()
    {
        File.Delete(_header);
        Assert.False(UpToDate(Args));
    }

    [Fact]
    public void MissingDepfile_Recompiles()
    {
        File.Delete(_obj + ".d");
        Assert.False(UpToDate(Args));
    }

    [Fact]
    public void MissingObject_Recompiles()
    {
        File.Delete(_obj);
        Assert.False(UpToDate(Args));
    }

    [Fact]
    public void Depfile_SkipsTheDriveLetterTarget_AndKeepsEscapedSpaces()
    {
        var dep = Path.Combine(_dir, "x.d");
        File.WriteAllText(dep, "C:\\out\\a.obj: C:\\src\\a.c \\\n  C:\\My\\ Headers\\a.h\n");
        Assert.Equal(new[] { @"C:\src\a.c", @"C:\My Headers\a.h" }, ObjFreshness.ParseDepfile(dep));
    }

    [Fact]
    public void Signature_KeepsEveryTokenDistinct()
    {
        // One token per line: ["-DA", "B"] and ["-DA B"] are different commands.
        Assert.NotEqual(ObjFreshness.Signature(Compiler, new[] { "-DA", "B" }),
                        ObjFreshness.Signature(Compiler, new[] { "-DA B" }));
    }
}

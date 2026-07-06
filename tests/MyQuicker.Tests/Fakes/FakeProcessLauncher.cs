using System;
using System.Collections.Generic;
using MyQuicker.Services;

namespace MyQuicker.Tests.Fakes;

/// <summary>IProcessLauncher 的轻量级手写 Mock，可记录调用或抛出指定异常。</summary>
internal sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly Exception? _exceptionToThrow;

    public IReadOnlyList<(string FileName, string Arguments)> Launched => _launched;
    private readonly List<(string FileName, string Arguments)> _launched = new();

    public FakeProcessLauncher() { }

    public FakeProcessLauncher(Exception exceptionToThrow)
    {
        _exceptionToThrow = exceptionToThrow;
    }

    public void Launch(string fileName, string arguments)
    {
        if (_exceptionToThrow is not null)
            throw _exceptionToThrow;

        _launched.Add((fileName, arguments));
    }

    public void Launch(string fileName, IEnumerable<string> argumentList)
    {
        if (_exceptionToThrow is not null)
            throw _exceptionToThrow;

        string joined = string.Join(" ", argumentList);
        _launched.Add((fileName, joined));
    }
}

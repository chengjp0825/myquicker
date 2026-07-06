using System.Collections.Generic;
using System.Linq;
using MyQuicker.Domain.Runtime;

namespace MyQuicker.Tests.Fakes;

/// <summary>IScreenGeometry 的手写 Mock。</summary>
internal sealed class FakeScreenGeometry : IScreenGeometry
{
    private readonly List<ScreenInfo> _screens = new();

    public IReadOnlyList<ScreenInfo> Screens => _screens;

    public void AddScreen(ScreenInfo screen) => _screens.Add(screen);

    public ScreenInfo GetScreenContaining(Point point) =>
        _screens.FirstOrDefault(s => s.Bounds.Contains(point)) ?? _screens[0];
}

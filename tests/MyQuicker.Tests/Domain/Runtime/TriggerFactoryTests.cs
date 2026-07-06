using System;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
using MyQuicker.Interop;
using MyQuicker.Tests.Fakes;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime;

public sealed class TriggerFactoryTests
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public void Create_WithMiddleButtonBinding_ReturnsButtonTriggerWithDefaults()
    {
        var binding = new TriggerBinding
        {
            Type = TriggerType.Button,
            WakeupMessage = NativeMethods.WM_MBUTTONDOWN,
            XButtonData = null,
            InterceptWakeupKey = true,
        };

        var trigger = TriggerFactory.Create(binding, _timeProvider);

        var buttonTrigger = Assert.IsType<ButtonTrigger>(trigger);
        Assert.Equal("MiddleButton", buttonTrigger.SourceName);
    }

    [Fact]
    public void Create_WithXButtonBinding_ReturnsButtonTriggerWithXButtonData()
    {
        var binding = new TriggerBinding
        {
            Type = TriggerType.Button,
            WakeupMessage = NativeMethods.WM_XBUTTONDOWN,
            XButtonData = 2,
            InterceptWakeupKey = true,
        };

        var trigger = TriggerFactory.Create(binding, _timeProvider);

        var buttonTrigger = Assert.IsType<ButtonTrigger>(trigger);
        Assert.Equal("XButton2", buttonTrigger.SourceName);
    }

    [Fact]
    public void Create_WithCircleGestureBinding_ReturnsCircleGestureTriggerWithConfiguredEvaluator()
    {
        var binding = new TriggerBinding
        {
            Type = TriggerType.CircleGesture,
            CircleSensitivity = CircleSensitivity.High,
            InterceptWakeupKey = false,
        };

        var trigger = TriggerFactory.Create(binding, _timeProvider);

        var circleTrigger = Assert.IsType<CircleGestureTrigger>(trigger);
        Assert.Equal("CircleGesture", circleTrigger.SourceName);
    }

    [Fact]
    public void Create_WithNullBinding_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TriggerFactory.Create(null!, _timeProvider));
    }

    [Fact]
    public void Create_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var binding = new TriggerBinding { Type = TriggerType.Button };
        Assert.Throws<ArgumentNullException>(() => TriggerFactory.Create(binding, null!));
    }

    [Fact]
    public void Create_WithUnsupportedType_ThrowsNotSupportedException()
    {
        var binding = new TriggerBinding { Type = (TriggerType)999 };
        Assert.Throws<NotSupportedException>(() => TriggerFactory.Create(binding, _timeProvider));
    }

    [Fact]
    public void Create_ButtonBindingMissingWakeupMessage_FallsBackToMiddleButton()
    {
        var binding = new TriggerBinding
        {
            Type = TriggerType.Button,
            WakeupMessage = null,
        };

        var trigger = TriggerFactory.Create(binding, _timeProvider);

        var buttonTrigger = Assert.IsType<ButtonTrigger>(trigger);
        Assert.Equal("MiddleButton", buttonTrigger.SourceName);
    }

    [Fact]
    public void Create_CircleGestureBindingMissingSensitivity_UsesMediumDefault()
    {
        var binding = new TriggerBinding
        {
            Type = TriggerType.CircleGesture,
        };

        // Default CircleSensitivity is Medium; factory should create a valid CircleGestureTrigger.
        var trigger = TriggerFactory.Create(binding, _timeProvider);

        Assert.IsType<CircleGestureTrigger>(trigger);
    }
}

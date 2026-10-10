using System.Windows.Media;

namespace DynamicIsland.Animation;

/// <summary>
/// A damped spring (Hooke + viscous damping), integrated with small fixed substeps.
/// Slightly under-damped by default, which gives the iOS "blobby" overshoot.
/// </summary>
public sealed class Spring
{
    public double Value;
    public double Target;
    public double Velocity;
    public double Stiffness = 320;
    public double Damping = 25;

    public Spring(double initial, double stiffness = 320, double damping = 25)
    {
        Value = Target = initial;
        Stiffness = stiffness;
        Damping = damping;
    }

    public bool IsSettled => Math.Abs(Value - Target) < 0.02 && Math.Abs(Velocity) < 0.02;

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    public void Step(double dt)
    {
        const double h = 1.0 / 480.0;
        while (dt > 0)
        {
            var s = Math.Min(h, dt);
            var force = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += force * s;
            Value += Velocity * s;
            dt -= s;
        }
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}

/// <summary>Drives a set of springs from the WPF render loop, only while any of them is moving.</summary>
public sealed class SpringDriver
{
    private readonly Spring[] _springs;
    private readonly Action _onFrame;
    private TimeSpan _last;
    private bool _running;

    public SpringDriver(Action onFrame, params Spring[] springs)
    {
        _springs = springs;
        _onFrame = onFrame;
    }

    public void Kick()
    {
        if (_running) return;
        _running = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_last == TimeSpan.Zero || now == _last)
        {
            if (_last == TimeSpan.Zero) _last = now;
            return;
        }
        var dt = Math.Min((now - _last).TotalSeconds, 1.0 / 20.0);
        _last = now;

        var settled = true;
        foreach (var s in _springs)
        {
            s.Step(dt);
            settled &= s.IsSettled;
        }
        _onFrame();

        if (settled)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
    }
}

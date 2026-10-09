using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CTTools.Features.Terrain;

public sealed class TerrainChoice
{
    public required ElementId Id { get; init; }
    public required string Name { get; init; }
    public override string ToString() => Name;
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _can;

    public RelayCommand(Action execute, Func<bool>? can = null)
    {
        _execute = execute;
        _can = can;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Shared status and click gate for the modeless terrain windows.</summary>
public abstract class TerrainViewModel : Observable
{
    private int _gate;
    private string _status = "";
    private bool _error;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsError
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public void Report(string message, bool error)
    {
        Status = message;
        IsError = error;
        Interlocked.Exchange(ref _gate, 0);
    }

    protected void Submit(Action work)
    {
        if (Interlocked.Exchange(ref _gate, 1) == 1)
            return;
        try
        {
            work();
        }
        catch (Exception ex)
        {
            Report(ex.Message, true);
        }
    }

    protected static void Raise(ExternalEvent externalEvent)
    {
        var result = externalEvent.Raise();
        if (result is not (ExternalEventRequest.Accepted or ExternalEventRequest.Pending))
            throw new InvalidOperationException("Revit is busy. Try again in a moment.");
    }
}

internal static class TerrainHost
{
    private static readonly Dictionary<Type, (Window Window, ExternalEvent Event)> Open = new();

    public static bool TryActivate<TWindow>() where TWindow : Window
    {
        if (!Open.TryGetValue(typeof(TWindow), out var existing) || !existing.Window.IsLoaded)
            return false;
        if (existing.Window.WindowState == WindowState.Minimized)
            existing.Window.WindowState = WindowState.Normal;
        existing.Window.Show();
        existing.Window.Activate();
        return true;
    }

    public static void Show(Window window, ExternalEvent externalEvent)
    {
        var key = window.GetType();
        Open[key] = (window, externalEvent);
        window.Closed += (_, _) =>
        {
            if (Open.TryGetValue(key, out var pair) && ReferenceEquals(pair.Window, window))
            {
                Open.Remove(key);
                pair.Event.Dispose();
            }
        };
        window.Show();
    }

    public static void Picking(Window? window, Action pick)
    {
        if (window is { IsLoaded: true })
            window.Dispatcher.Invoke(window.Hide);
        try
        {
            pick();
        }
        finally
        {
            if (window is { IsLoaded: true })
            {
                window.Dispatcher.Invoke(() =>
                {
                    window.Show();
                    window.Activate();
                });
            }
        }
    }
}

internal sealed class ToposolidFilter : ISelectionFilter
{
    public bool AllowElement(Element element) => element is Toposolid;
    public bool AllowReference(Reference reference, XYZ position) => false;
}

internal sealed class BoundaryFilter : ISelectionFilter
{
    public bool AllowElement(Element element) => element is CurveElement;
    public bool AllowReference(Reference reference, XYZ position) => false;
}

internal sealed class AlignFilter : ISelectionFilter
{
    public bool AllowElement(Element element) => element is FamilyInstance or CurveElement or Wall;
    public bool AllowReference(Reference reference, XYZ position) => false;
}

using System.Windows;
using Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI.Tests;

internal sealed class CounterService
{
    internal int Count { get; private set; }
    internal void Increment() => Count++;
}

internal sealed class CounterViewModel : ObservableObject
{
    private readonly CounterService _service;

    public CounterViewModel(CounterService service)
    {
        _service = service;
        IncrementCommand = new RelayCommand(() =>
        {
            _service.Increment();
            OnPropertyChanged(nameof(Count));
        });
    }

    public int Count => _service.Count;
    public IRelayCommand IncrementCommand { get; }
}

internal sealed class CounterLens(CounterViewModel viewModel, string id = "counter", string label = "Counter") : ILens
{
    private CounterLensView? _view;
    public LensDescriptor Descriptor { get; } = new(id, label);
    public FrameworkElement View => _view ??= new CounterLensView(viewModel);
    internal int ActivationCount { get; private set; }

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        ActivationCount++;
        return Task.CompletedTask;
    }

    public Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken) =>
        Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

    public void OnContextChanged(bool hasDrawing) { }
    public void OnDrawingChanged() { }
    public void Close(bool hostTerminating) { }
}

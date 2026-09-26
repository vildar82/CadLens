using CommunityToolkit.Mvvm.ComponentModel;

namespace CadLens.UI;

/// <summary>A registered module's toolbar state, independent of its view model.</summary>
public sealed class LensOption : ObservableObject
{
    private bool _isActive;

    internal LensOption(ILens lens) => Lens = lens;

    /// <summary>Identity and title supplied by the module.</summary>
    public LensDescriptor Descriptor => Lens.Descriptor;

    /// <summary>Whether this module owns the expanded panel.</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    internal ILens Lens { get; }
}

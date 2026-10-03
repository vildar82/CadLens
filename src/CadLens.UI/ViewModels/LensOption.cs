using CommunityToolkit.Mvvm.ComponentModel;

namespace CadLens.UI;

/// <summary>A registered module's toolbar state, independent of its view model.</summary>
public sealed class LensOption : ObservableObject
{
    internal LensOption(ILens lens) => Lens = lens;

    /// <summary>Identity and title supplied by the module.</summary>
    public LensDescriptor Descriptor => Lens.Descriptor;

    /// <summary>Whether this module owns the expanded panel.</summary>
    public bool IsActive
    {
        get;
        internal set => SetProperty(ref field, value);
    }

    internal ILens Lens { get; }
}

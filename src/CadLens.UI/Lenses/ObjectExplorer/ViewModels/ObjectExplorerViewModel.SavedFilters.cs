using System.Collections.Immutable;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

public sealed partial class ObjectExplorerViewModel
{
    private const string SavedFiltersFileName = "property-filters.json";
    private List<SavedPropertyFilter> _savedFilters = [];
    private string _savedFilterMessage = "";

    /// <summary>Named conditions shared by both lenses for the current runtime type.</summary>
    public ImmutableArray<string> SavedFilterNames { get; private set; } = [];

    /// <summary>Existing condition chosen for applying or deleting.</summary>
    public string? SelectedSavedFilter
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                NotifySavedFilterCommands();
        }
    }

    /// <summary>User-provided name for saving the current valid draft condition.</summary>
    public string SavedFilterName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    /// <summary>Localized validation or persistence feedback.</summary>
    public string SavedFilterMessage => UiText.Current.Get(_savedFilterMessage);

    /// <summary>Saves a valid draft without applying it to the drawing result.</summary>
    public IRelayCommand SavePropertyFilterCommand { get; private set; } = null!;

    /// <summary>Applies a named condition to the retained inventory.</summary>
    public IAsyncRelayCommand ApplySavedPropertyFilterCommand { get; private set; } = null!;

    /// <summary>Deletes a preset without changing the applied condition.</summary>
    public IRelayCommand DeleteSavedPropertyFilterCommand { get; private set; } = null!;

    /// <summary>Reloads shared saved conditions when the filter controls are opened.</summary>
    public IRelayCommand RefreshSavedPropertyFiltersCommand { get; private set; } = null!;

    private void InitializeSavedFilters()
    {
        SavePropertyFilterCommand = new RelayCommand(SavePropertyFilter, () => CanRun() && CanGroup && _settings is not null);
        ApplySavedPropertyFilterCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ApplySavedPropertyFilterAsync),
            () => CanRun() && CanGroup && SelectedSavedFilter is not null);
        DeleteSavedPropertyFilterCommand = new RelayCommand(
            DeleteSavedPropertyFilter,
            () => CanRun() && CanGroup && SelectedSavedFilter is not null && _settings is not null);
        RefreshSavedPropertyFiltersCommand = new RelayCommand(RefreshSavedFilters);
    }

    private void RefreshSavedFilters()
    {
        _savedFilters =
        [
            .. (_settings?.Load<List<SavedPropertyFilter?>>(SavedFiltersFileName) ?? [])
                .OfType<SavedPropertyFilter>()
                .Where(preset => !string.IsNullOrWhiteSpace(preset.Name) && !string.IsNullOrWhiteSpace(preset.TypeKey) &&
                                 preset.Value.ValueKind is not (System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null))
        ];
        SavedFilterNames =
        [
            .. _savedFilters.Where(preset => preset.TypeKey == GroupingTypeKey)
                .Select(preset => preset.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        ];
        OnPropertyChanged(nameof(SavedFilterNames));

        if (SelectedSavedFilter is not null && !SavedFilterNames.Contains(SelectedSavedFilter))
            SelectedSavedFilter = null;

        NotifySavedFilterCommands();
    }

    private void SavePropertyFilter()
    {
        if (_disposed || _settings is null || GroupingTypeKey is not { } key)
            return;

        if (string.IsNullOrWhiteSpace(SavedFilterName))
        {
            SetSavedFilterMessage("Enter a name for the filter.");
            return;
        }

        if (PropertyFilter.CreateFilter() is not { } filter)
        {
            SetSavedFilterMessage("Enter a valid filter value.");
            return;
        }

        RefreshSavedFilters();

        if (_savedFilters.Any(preset => preset.TypeKey == key && preset.Name == SavedFilterName))
        {
            SetSavedFilterMessage("A filter with this name already exists for this type.");
            return;
        }

        var saved = new List<SavedPropertyFilter>(_savedFilters)
        {
            SavedPropertyFilter.Create(SavedFilterName, key, filter)
        };

        if (!SaveFilters(saved))
            return;

        SelectedSavedFilter = SavedFilterName;
        SetSavedFilterMessage("Filter saved.");
    }

    private async Task<UiMessage> ApplySavedPropertyFilterAsync(CancellationToken cancellationToken)
    {
        RefreshSavedFilters();
        var preset = _savedFilters.FirstOrDefault(item => item.TypeKey == GroupingTypeKey && item.Name == SelectedSavedFilter);
        var objects = UnfilteredType?.Children ?? [];
        var filter = preset?.Read(objects);
        var validator = new PropertyFilterEditor();
        validator.Load(objects, filter);

        if (GroupingTypeKey is not { } key || filter is null ||
            !validator.PropertyOptions.Any(option => option.Id == filter.PropertyId) || validator.CreateFilter() != filter)
        {
            SetSavedFilterMessage("This saved filter is not available for the current type.");
            return "This saved filter is not available for the current type.";
        }

        _propertyFilters[key] = filter;
        SetSavedFilterMessage("");
        return await RebuildPropertyFilterAsync(cancellationToken);
    }

    private void DeleteSavedPropertyFilter()
    {
        if (_disposed || _settings is null || SelectedSavedFilter is not { } name)
            return;

        RefreshSavedFilters();
        var remaining = _savedFilters.Where(preset => preset.TypeKey != GroupingTypeKey || preset.Name != name).ToList();

        if (SaveFilters(remaining))
            SetSavedFilterMessage("Saved filter deleted.");
    }

    private bool SaveFilters(List<SavedPropertyFilter> filters)
    {
        if (_settings?.Save(SavedFiltersFileName, filters) != true)
        {
            SetSavedFilterMessage("Saved filters could not be written. Try again.");
            return false;
        }

        RefreshSavedFilters();
        return true;
    }

    private void SetSavedFilterMessage(string message)
    {
        _savedFilterMessage = message;
        OnPropertyChanged(nameof(SavedFilterMessage));
    }

    private void NotifySavedFilterCommands()
    {
        SavePropertyFilterCommand.NotifyCanExecuteChanged();
        ApplySavedPropertyFilterCommand.NotifyCanExecuteChanged();
        DeleteSavedPropertyFilterCommand.NotifyCanExecuteChanged();
    }
}

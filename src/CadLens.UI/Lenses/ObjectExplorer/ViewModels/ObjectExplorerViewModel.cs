using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows.Threading;
using CadLens.Lenses;
using CadLens.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

/// <summary>Drawing overview and asynchronous operations for the modeless panel.</summary>
public sealed partial class ObjectExplorerViewModel : ObservableObject, IDisposable
{
    private readonly IObjectExplorerActions _actions;
    private readonly DrawingGrouping _grouping;
    private readonly SettingsService? _settings;
    private readonly string _settingsFileName;

    private readonly Dictionary<string, ImmutableArray<DrawingPropertyKey>> _propertyGrouping =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, DrawingPropertyKey> _displayProperties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DrawingPropertyFilter> _propertyFilters = new(StringComparer.Ordinal);

    private NavigationState _navigation = new();
    private DrawingInventory? _inventory;
    private ImmutableArray<IPlacedObjectId>? _selectedObjects;
    private ImmutableArray<LensNode> _unfilteredGroups = [];
    private LensNode? _propertyOptionsType;
    private LensNode? _filterOptionsType;
    private string _searchText = "";
    private ImmutableHashSet<string> _enabledFilters = [];
    private CancellationToken _activationToken;
    private string _emptyMessage = "Refresh to explore the active space.";
    private CancellationTokenSource? _pendingRequest;
    private object? _readProgressOperation;
    private CallbackProgress<OperationProgress>? _progress;
    private OperationProgressWindow? _operationWindow;
    private Action? _cancelOperation;
    private IPlacedObjectId? _availabilityTarget;
    private HostResult<bool>? _currentObjectAvailability;
    private Task _operation = Task.CompletedTask;
    private ImmutableArray<LensNode> _visibleGroups = [];
    private bool _sortDescending;
    private bool _disposed;
    private bool _needsCleanup;
    private bool _hasDrawing = true;
    private bool _spaceIsDrawingData;
    private bool _saveFailed;
    private int _autoLoadObjectLimit = LensPreferences.DefaultAutoLoadObjectLimit;
    private UiMessage _status = new("Activate a lens to explore the drawing.");

    /// <summary>Creates progress windows using the view's current appearance when a view is available.</summary>
    internal Func<Action, OperationProgressWindow> CreateProgressWindow { get; set; } =
        cancel => new OperationProgressWindow(cancel);

    /// <summary>Creates toolkit commands for the injected host operations.</summary>
    /// <param name="actions">Context-checked host operations.</param>
    /// <param name="grouping">Root organization for this lens.</param>
    /// <param name="settings">Optional persistent preferences for this lens.</param>
    public ObjectExplorerViewModel(
        IObjectExplorerActions actions,
        DrawingGrouping grouping,
        SettingsService? settings = null)
    {
        UiText.Current.PropertyChanged += OnLanguageChanged;
        _actions = actions;
        _grouping = grouping;
        _settings = settings;
        _settingsFileName = grouping == DrawingGrouping.Layers ? "lens-layers.json" : "lens-object-types.json";
        LensLabel = grouping == DrawingGrouping.Layers ? "Layers" : "Object Types";
        RootLabel = grouping == DrawingGrouping.Layers ? "All layers" : "All types";
        SearchPlaceholder = grouping == DrawingGrouping.Layers ? "Search layers" : "Search types";
        GroupLabel = grouping == DrawingGrouping.Layers ? "layers" : "types";
        CancelCommand = new RelayCommand(() => _cancelOperation?.Invoke(), () => _cancelOperation is not null);
        ReadCommand = new AsyncRelayCommand(() => ExecuteActionAsync(ReadInventoryAsync), CanRun);
        ShowAllObjectsCommand = new AsyncRelayCommand(
            () => IsAllObjects && !IsBrowsingBlockContents
                ? Task.CompletedTask
                : ExecuteActionAsync(token => ChangeScopeAsync(false, token)),
            CanRun);
        ShowSelectedObjectsCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(token => ChangeScopeAsync(true, token)),
            CanRun);
        IsolateCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(IsolateCurrentAsync),
            () => CanRun() && CanUseCurrentTarget && IsolationTarget is {Objects.IsEmpty: false});
        ResetCommand = new AsyncRelayCommand(() => ExecuteActionAsync(ResetAsync), CanRun);
        SelectCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(SelectCurrentAsync),
            () => CanRun() && CanUseCurrentTarget && Current is {Objects.IsEmpty: false});
        FocusCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(async token => await _actions.FocusAsync(Current!.Objects, token, _progress)),
            () => CanRun() && HasFocusTarget());
        EnterCommand = new AsyncRelayCommand<LensNode>(
            node => NavigateAsync(() => Enter(node)),
            node => CanRun() && node is not null && Items.Contains(node));
        BackCommand = new AsyncRelayCommand(
            () => NavigateAsync(GoBack),
            () => CanRun() && HasNavigation);
        RootCommand = new AsyncRelayCommand(
            () => NavigateAsync(GoToRoot),
            () => CanRun() && HasNavigation);
        BreadcrumbCommand = new AsyncRelayCommand<LensNode>(
            node => NavigateAsync(() => GoToBreadcrumb(node!)),
            node => CanRun() && node is not null && Breadcrumbs.Contains(node));
        PreviousCommand = new AsyncRelayCommand(
            () => NavigateAsync(() => MoveObject(-1)),
            () => CanRun() && _navigation.CanPrevious);
        NextCommand = new AsyncRelayCommand(
            () => NavigateAsync(() => MoveObject(1)),
            () => CanRun() && _navigation.CanNext);
        ToggleFilterCommand = new AsyncRelayCommand<FilterOption>(
            filter => ExecuteActionAsync(token => ToggleFilterAsync(filter!, token)),
            filter => CanRun() && filter is not null && Filters.Contains(filter));
        ToggleGroupingCommand = new AsyncRelayCommand<GroupingOption>(
            option => ExecuteActionAsync(token => ToggleGroupingAsync(option!, token)),
            option => CanRun() && CanGroup && option is not null && GroupingOptions.Contains(option));
        SelectDisplayPropertyCommand = new RelayCommand<GroupingOption>(
            SelectDisplayProperty,
            option => CanRun() && CanGroup && option is not null && DisplayPropertyOptions.Contains(option));
        ApplyPropertyFilterCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ApplyPropertyFilterAsync),
            () => CanRun() && CanGroup);
        ClearPropertyFilterCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ClearPropertyFilterAsync),
            () => CanRun() && AppliedPropertyFilter is not null);
        ToggleAutoIsolationCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoIsolationAsync),
            CanRun);
        ToggleAutoSelectCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoSelectAsync),
            CanRun);
        ToggleAutoFocusCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoFocusAsync),
            CanRun);
        SortByNameCommand = new RelayCommand(() => ChangeSort(false));
        SortByCountCommand = new RelayCommand(() => ChangeSort(true));
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        BrowseBlockContentsCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(token => ReadBlockContentsAsync(true, token)),
            () => CanRun() && CanUseCurrentTarget && CanBrowseBlockContents);
        InitializeSavedFilters();
        RestorePreferences();
        Filters =
        [
            .. DrawingLensProvider.GetFilters(grouping)
                .Select(filter => new FilterOption(filter, _enabledFilters.Contains(filter.Id)))
        ];
    }

    /// <summary>Whether the lens is expanded and allowed to access the drawing.</summary>
    public bool IsLensActive
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                NotifyCommands();
        }
    }

    /// <summary>Whether the inventory contains only captured CAD selection targets.</summary>
    public bool IsSelectedObjectsOnly
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value))
                return;

            OnPropertyChanged(nameof(IsAllObjects));
            OnPropertyChanged(nameof(RefreshLabel));
        }
    }

    /// <summary>Whether all direct active-space objects are included in the read.</summary>
    public bool IsAllObjects => !IsSelectedObjectsOnly;

    /// <summary>Action label for refreshing the chosen object scope.</summary>
    public string RefreshLabel => IsBrowsingBlockContents
        ? "Refresh block contents"
        : IsSelectedObjectsOnly
            ? "Refresh selected objects"
            : "Refresh active space";

    /// <summary>Reads all direct active-space objects.</summary>
    public IAsyncRelayCommand ShowAllObjectsCommand { get; }

    /// <summary>Captures the current CAD selection and reads those objects.</summary>
    public IAsyncRelayCommand ShowSelectedObjectsCommand { get; }

    /// <summary>Current operation result or explanation.</summary>
    public string Status => IsReading
        ? ReadProgressText
        : OperationProgress is { } progress
            ? OperationProgressWindow.FormatProgress(progress)
            : _saveFailed
                ? new UiMessage(
                    "{0} {1}",
                    _status,
                    new UiMessage("Unable to save preferences. Changes apply for this session.")).ToString()
                : _status.ToString();

    /// <summary>Numeric display precision from the most recent drawing inventory.</summary>
    public DrawingPrecision Precision
    {
        get;
        private set => SetProperty(ref field, value);
    } = DrawingPrecision.Default;

    /// <summary>Active space supplied by the lens.</summary>
    public string SpaceLabel
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                OnPropertyChanged(nameof(DisplaySpaceLabel));
        }
    } = "Active drawing";

    /// <summary>Localized app status or the unchanged active drawing-space name.</summary>
    public string DisplaySpaceLabel => _spaceIsDrawingData ? SpaceLabel : UiText.Current.Get(SpaceLabel);

    /// <summary>Title supplied by the active lens.</summary>
    public string LensLabel
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Breadcrumb label for this lens's root list.</summary>
    public string RootLabel
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Root search hint supplied by the lens.</summary>
    public string SearchPlaceholder
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Plural name of the root groups.</summary>
    public string GroupLabel
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value))
                return;

            OnPropertyChanged(nameof(GroupSummary));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(SortByNameLabel));
            OnPropertyChanged(nameof(SortByCountLabel));
        }
    }

    /// <summary>Included root count and the lens-specific group name.</summary>
    public string GroupSummary => new UiMessage(
        _grouping == DrawingGrouping.Layers ? "{0:N0} layers" : "{0:N0} types",
        GroupCount).ToString();

    /// <summary>Accessible description of name sorting.</summary>
    public string SortByNameLabel => UiText.Current.Get(
        Current is null
            ? _grouping == DrawingGrouping.Layers ? "Sort layers by name" : "Sort types by name"
            : "Sort by name");

    /// <summary>Accessible description of count sorting.</summary>
    public string SortByCountLabel =>
        Current is { } node && node.Children.All(child => child.Kind == LensNodeKind.Object)
            ? DisplayPropertyLabel
            : UiText.Current.Get("Objects");

    private LensNode? GroupingType => _navigation.Path.FirstOrDefault(node => node.Kind == LensNodeKind.Type);
    private string? GroupingTypeKey => GroupingType?.TypeKey;

    /// <summary>Whether properties of the current primitive type can be combined.</summary>
    public bool CanGroup => _inventory is not null && GroupingTypeKey is not null;

    /// <summary>Whether the current rows can display a selected object property.</summary>
    public bool CanShowProperty => CanGroup && Current is { } node &&
                                   node.Children.All(child => child.Kind != LensNodeKind.PropertyGroup);

    /// <summary>Explains where the selected object property is displayed.</summary>
    public string DisplayPropertyHint => CanShowProperty
        ? "Choose the property shown in object rows"
        : "Open a group to choose the property shown in object rows";

    /// <summary>Draft property condition, independent of the current result.</summary>
    public PropertyFilterEditor PropertyFilter { get; } = new();

    /// <summary>Applies a valid typed condition to the current type without rereading CAD.</summary>
    public IAsyncRelayCommand ApplyPropertyFilterCommand { get; }

    /// <summary>Removes the current type's applied condition.</summary>
    public IAsyncRelayCommand ClearPropertyFilterCommand { get; }

    /// <summary>Condition applied to this type in the current lens session.</summary>
    public DrawingPropertyFilter? AppliedPropertyFilter =>
        GroupingTypeKey is { } key && _propertyFilters.TryGetValue(key, out var filter) ? filter : null;

    /// <summary>Readable condition for the compact filter control.</summary>
    public string PropertyFilterSummary => AppliedPropertyFilter is { } filter
        ? $"{DrawingValueFormatter.FormatPropertyLabel(filter.PropertyId)} {FilterOperatorLabel(filter.Operator)} {DrawingValueFormatter.FormatValue(filter.Value, Precision)}"
        : UiText.Current.Get("None");

    /// <summary>Matching objects and the included type count before property filtering.</summary>
    public string PropertyFilterCount => new UiMessage(
        "{0} of {1}",
        GroupingType?.Count ?? 0,
        UnfilteredType?.Count ?? 0).ToString();

    private LensNode? UnfilteredType => GroupingType is { } type
        ? (_grouping == DrawingGrouping.Layers
            ? _unfilteredGroups.FirstOrDefault(layer => layer.Id == _navigation.Path[0].Id)?.Children ?? []
            : _unfilteredGroups).FirstOrDefault(node => node.Id == type.Id)
        : null;

    private ImmutableArray<DrawingPropertyKey> AvailableProperties
    {
        get
        {
            if (_inventory is null || UnfilteredType is not { } type)
            {
                _propertyOptionsType = null;
                field = [];
                return field;
            }

            if (ReferenceEquals(type, _propertyOptionsType))
                return field;

            var targets = new HashSet<IPlacedObjectId>(type.Objects);
            field = DrawingProperties.GetAvailableFields(
                _inventory.Entities.Where(entity => targets.Contains(entity.Id)));
            _propertyOptionsType = type;

            return field;
        }
    } = [];

    /// <summary>Property displayed beside objects and used for value sorting.</summary>
    public DrawingPropertyKey? DisplayPropertyId
    {
        get
        {
            if (GroupingTypeKey is not { } typeKey)
                return null;

            var available = AvailableProperties;

            if (_displayProperties.TryGetValue(typeKey, out var selected) && available.Contains(selected))
                return selected;

            return GroupingType?.RowMetric is { } metric && available.Contains(metric.Id)
                ? metric.Id
                : available.IsEmpty
                    ? null
                    : available[0];
        }
    }

    /// <summary>Localized caption of the currently displayed property.</summary>
    public string DisplayPropertyLabel => DisplayPropertyId is { } id
        ? DrawingValueFormatter.FormatPropertyLabel(id)
        : UiText.Current.Get("Details");

    /// <summary>Observed properties available for the single displayed column.</summary>
    public ImmutableArray<GroupingOption> DisplayPropertyOptions
    {
        get
        {
            var selected = DisplayPropertyId;

            return
            [
                .. AvailableProperties
                    .Select(id => new GroupingOption(id, id == selected))
                    .OrderBy(
                        option => option.Label,
                        StringComparer.Create(UiText.Current.Culture, true))
            ];
        }
    }

    /// <summary>Properties observed in this type's detached snapshots.</summary>
    public ImmutableArray<GroupingOption> GroupingOptions
    {
        get
        {
            if (_inventory is null || GroupingTypeKey is not { } typeKey)
                return [];

            var selected = _propertyGrouping.TryGetValue(typeKey, out var fields)
                ? fields
                : ImmutableArray<DrawingPropertyKey>.Empty;
            return
            [
                .. AvailableProperties
                    .Select(id => new GroupingOption(id, selected.Contains(id)))
                    .OrderBy(
                        option => option.Label,
                        StringComparer.Create(UiText.Current.Culture, true))
            ];
        }
    }

    /// <summary>Readable combination of the selected grouping properties.</summary>
    public string GroupingSummary
    {
        get
        {
            var labels = GroupingOptions.Where(option => option.IsSelected)
                .Select(option => option.Label).ToList();

            return labels.Count == 0 ? UiText.Current.Get("None") : string.Join(" + ", labels);
        }
    }

    /// <summary>Groups from the most recent inventory.</summary>
    public ImmutableArray<LensNode> Groups
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            UpdateVisibleGroups();
            OnPropertyChanged(nameof(GroupCount));
            OnPropertyChanged(nameof(GroupSummary));
            OnPropertyChanged(nameof(ObjectCount));
            OnPropertyChanged(nameof(HasGroups));
        }
    } = [];

    /// <summary>Children at the current exploration level.</summary>
    public ImmutableArray<LensNode> Items => Current is null ? _visibleGroups : _navigation.Items;

    /// <summary>Finds groups by name in the root list.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
                return;

            UpdateVisibleGroups();
            SavePreferences();
        }
    }

    /// <summary>Whether the name column controls root ordering.</summary>
    public bool IsNameSortActive => !IsCountSortActive;

    /// <summary>Whether the object count column controls root ordering.</summary>
    public bool IsCountSortActive { get; private set; }

    /// <summary>Current direction indicator for the name column.</summary>
    public string NameSortArrow => !IsCountSortActive ? _sortDescending ? "↓" : "↑" : "";

    /// <summary>Current direction indicator for the object count column.</summary>
    public string CountSortArrow => IsCountSortActive ? _sortDescending ? "↓" : "↑" : "";

    /// <summary>Current group or object details.</summary>
    public LensNode? Current => _navigation.Current;

    /// <summary>Alphabetical properties within the current detail sections.</summary>
    public ImmutableArray<DetailField> CurrentFields
    {
        get
        {
            var fields = Current?.Fields ?? [];
            var properties = new Queue<DetailField>(
                fields
                    .Where(detail => detail.ValueKind == DetailValueKind.TypedValue)
                    .OrderBy(
                        DrawingValueFormatter.FormatDetailLabel,
                        StringComparer.Create(UiText.Current.Culture, true)));

            return
            [
                .. fields.Select(detail =>
                    detail.ValueKind == DetailValueKind.TypedValue ? properties.Dequeue() : detail)
            ];
        }
    }

    /// <summary>
    /// Whether the individually displayed object was confirmed to no longer exist.
    /// </summary>
    public bool IsCurrentObjectMissing =>
        IsObject && _currentObjectAvailability is HostResult<bool>.Success {Value: false};

    /// <summary>
    /// Whether retained object details need an availability explanation.
    /// </summary>
    public bool HasObjectAvailabilityNotice => IsObject && _currentObjectAvailability is
        HostResult<bool>.Success {Value: false} or HostResult<bool>.Unavailable;

    /// <summary>
    /// Explains why the current object's collected details remain visible without drawing actions.
    /// </summary>
    public string ObjectAvailabilityNotice => ObjectAvailabilityMessage.ToString();

    private bool CanUseCurrentTarget =>
        !IsObject || _currentObjectAvailability is HostResult<bool>.Success {Value: true};

    private UiMessage ObjectAvailabilityMessage => _currentObjectAvailability switch
    {
        HostResult<bool>.Success {Value: false} => new UiMessage("Object no longer exists. Showing collected data."),
        HostResult<bool>.Unavailable unavailable => new UiMessage(
            "Object availability could not be verified. Showing collected data. {0}",
            new UiMessage(unavailable.Reason)),
        _ => new UiMessage("")
    };

    /// <summary>Selected ancestors including the current node.</summary>
    public IReadOnlyList<LensNode> Breadcrumbs => _blockParent is null
        ? _navigation.Path
        : [.. _blockParent.Navigation.Path, .. _navigation.Path];

    /// <summary>Whether object browsing controls apply.</summary>
    public bool IsObject => _navigation.Position > 0;

    /// <summary>One-based position within the current object set.</summary>
    public string ObjectPosition =>
        new UiMessage("{0} of {1}", _navigation.Position, _navigation.ObjectCount).ToString();

    /// <summary>Whether details replace the root list.</summary>
    public bool HasCurrent => Current is not null;

    /// <summary>Whether the current scope or path can return to a parent.</summary>
    public bool HasNavigation => HasCurrent || IsBrowsingBlockContents;

    /// <summary>Fits the camera to the current target during navigation.</summary>
    public bool IsAutoFocus
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Selects the current target during navigation without moving the camera.</summary>
    public bool IsAutoSelect
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Isolates the current target during navigation.</summary>
    public bool IsAutoIsolation
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Whether the current result has no root groups.</summary>
    public bool IsEmpty => Current is {Kind: LensNodeKind.Type, Objects.IsEmpty: true} ||
                           Current is null && _visibleGroups.IsEmpty;

    /// <summary>Lens-provided explanation for an empty inventory.</summary>
    public string EmptyMessage => Current is {Kind: LensNodeKind.Type, Objects.IsEmpty: true}
        ? UiText.Current.Get("No objects match the property filter.")
        : !Groups.IsEmpty && _visibleGroups.IsEmpty
            ? UiText.Current.Get(
                _grouping == DrawingGrouping.Layers
                    ? "No layers match your search."
                    : "No types match your search.")
            : UiText.Current.Get(_emptyMessage);

    /// <summary>Sorts root groups by name; clicking again reverses direction.</summary>
    public IRelayCommand SortByNameCommand { get; }

    /// <summary>Sorts root groups by object count; clicking again reverses direction.</summary>
    public IRelayCommand SortByCountCommand { get; }

    /// <summary>Clears the root group search.</summary>
    public IRelayCommand ClearSearchCommand { get; }

    /// <summary>Lens options and their current inclusion state.</summary>
    public ImmutableArray<FilterOption> Filters { get; private set; }

    /// <summary>Enters a displayed child and updates temporary isolation.</summary>
    public IAsyncRelayCommand<LensNode> EnterCommand { get; }

    /// <summary>Returns to the parent level.</summary>
    public IAsyncRelayCommand BackCommand { get; }

    /// <summary>Returns to the root list.</summary>
    public IAsyncRelayCommand RootCommand { get; }

    /// <summary>Returns to the chosen ancestor.</summary>
    public IAsyncRelayCommand<LensNode> BreadcrumbCommand { get; }

    /// <summary>Shows the previous object.</summary>
    public IAsyncRelayCommand PreviousCommand { get; }

    /// <summary>Shows the next object.</summary>
    public IAsyncRelayCommand NextCommand { get; }

    /// <summary>Reloads inventory with one inclusion option toggled.</summary>
    public IAsyncRelayCommand<FilterOption> ToggleFilterCommand { get; }

    /// <summary>Combines a property with the other checked properties using the retained inventory.</summary>
    public IAsyncRelayCommand<GroupingOption> ToggleGroupingCommand { get; }

    /// <summary>Changes the displayed object property without another drawing read or host effects.</summary>
    public IRelayCommand<GroupingOption> SelectDisplayPropertyCommand { get; }

    /// <summary>Switches navigation isolation on or off.</summary>
    public IAsyncRelayCommand ToggleAutoIsolationCommand { get; }

    /// <summary>Switches automatic CAD selection on or off.</summary>
    public IAsyncRelayCommand ToggleAutoSelectCommand { get; }

    /// <summary>Switches automatic camera focus on or off.</summary>
    public IAsyncRelayCommand ToggleAutoFocusCommand { get; }

    /// <summary>Selects the current targets without changing the camera or isolation.</summary>
    public IAsyncRelayCommand SelectCommand { get; }

    /// <summary>Number of included groups.</summary>
    public int GroupCount => Groups.Length;

    /// <summary>Number of included objects.</summary>
    public int ObjectCount => Groups.Sum(group => group.Count);

    /// <summary>Whether the inventory contains groups.</summary>
    public bool HasGroups => !Groups.IsEmpty;

    /// <summary>Whether a host operation is pending.</summary>
    public bool IsBusy => _pendingRequest is not null;

    /// <summary>
    /// Current drawing read progress, absent between reads.
    /// </summary>
    public DrawingReadProgress? ReadProgress
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value))
                return;

            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(ProgressMaximum));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(IsReading));
            OnPropertyChanged(nameof(IsReadProgressIndeterminate));
            OnPropertyChanged(nameof(ReadProgressText));
            OnPropertyChanged(nameof(Status));
        }
    }

    /// <summary>
    /// Whether drawing objects are being read.
    /// </summary>
    public bool IsReading => ReadProgress is not null;

    /// <summary>
    /// Whether preparation or one unfinished insertion has no useful completion fraction.
    /// </summary>
    public bool IsReadProgressIndeterminate =>
        ReadProgress is {TotalRoots: 0} or {TotalRoots: 1, CompletedRoots: 0};

    /// <summary>
    /// Localized number of drawing objects processed, including block contents.
    /// </summary>
    public string ReadProgressText => ReadProgress is {Stage: OperationStage.Building} progress
        ? OperationProgressWindow.FormatProgress(
            new OperationProgress(progress.Stage, progress.CompletedRoots, progress.TotalRoots))
        : new UiMessage("Processed {0:N0} objects", ReadProgress?.ProcessedObjects ?? 0).ToString();

    /// <summary>Progress for the current operation, including native visualization and cleanup.</summary>
    public OperationProgress? OperationProgress
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value))
                return;

            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(ProgressMaximum));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(Status));
        }
    }

    /// <summary>Completed items in the current operation step.</summary>
    public int ProgressValue => ReadProgress?.CompletedRoots ?? OperationProgress?.Completed ?? 0;

    /// <summary>Known item count in the current step.</summary>
    public int ProgressMaximum => Math.Max(1, ReadProgress?.TotalRoots ?? OperationProgress?.Total ?? 0);

    /// <summary>Whether the current step has no useful completion fraction.</summary>
    public bool IsProgressIndeterminate => IsReading
        ? IsReadProgressIndeterminate
        : OperationProgress is null or {Total: <= 0};

    /// <summary>Requests cancellation without waiting for the host dispatcher.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Refreshes the active-space inventory.</summary>
    public IAsyncRelayCommand ReadCommand { get; }

    /// <summary>Isolates the current group or object.</summary>
    public IAsyncRelayCommand IsolateCommand { get; }

    /// <summary>Clears selection and isolation, turns off Auto modes, and preserves the camera.</summary>
    public IAsyncRelayCommand ResetCommand { get; }

    /// <summary>Explicitly fits the selected group's or object's live bounds.</summary>
    public IAsyncRelayCommand FocusCommand { get; }

    /// <summary>Clears old state and reloads the active lens after the document or space changes.</summary>
    /// <param name="hasDrawing">Whether drawing-dependent commands can run.</param>
    public Task ResetContextAsync(bool hasDrawing = true)
    {
        if (_disposed)
            return Task.CompletedTask;

        _hasDrawing = hasDrawing;
        _pendingRequest?.Cancel();
        StopReadProgress();
        IsSelectedObjectsOnly = false;
        _selectedObjects = null;
        _searchText = _blockParent?.SearchText ?? _searchText;
        _blockParent = null;
        OnPropertyChanged(nameof(SearchText));
        _emptyMessage = "Refresh to explore the active space.";
        OnPropertyChanged(nameof(EmptyMessage));
        _propertyFilters.Clear();
        _unfilteredGroups = [];
        _inventory = null;
        Precision = DrawingPrecision.Default;
        Groups = [];
        _navigation.Reset([], false);

        NotifyNavigation();
        _spaceIsDrawingData = false;
        SpaceLabel = hasDrawing ? "Active drawing" : "No active drawing";
        OnPropertyChanged(nameof(DisplaySpaceLabel));
        SetStatus(
            !hasDrawing
                ? "Open a drawing to explore its objects."
                : IsLensActive
                    ? "Drawing context changed. Updating the current space."
                    : "Drawing context changed. Activate a lens to explore.");
        if (!_needsCleanup)
            return IsLensActive && hasDrawing ? RefreshContextAsync() : Task.CompletedTask;

        _actions.ClearImmediately(false);
        _needsCleanup = IsLensActive;

        return IsLensActive && hasDrawing ? RefreshContextAsync() : Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => Close(false);

    /// <summary>Cancels pending lens work and synchronously removes owned graphics.</summary>
    /// <param name="hostTerminating">Whether host shutdown forbids regeneration.</param>
    public void Close(bool hostTerminating)
    {
        if (_disposed)
            return;

        UiText.Current.PropertyChanged -= OnLanguageChanged;
        _disposed = true;
        SetCurrentObjectAvailability(null);
        IsLensActive = false;
        _pendingRequest?.Cancel();
        StopReadProgress();

        if (_needsCleanup)
            _actions.ClearImmediately(hostTerminating);
        NotifyCommands();
    }

    private bool CanRun() =>
        !_disposed && !_activationToken.IsCancellationRequested && IsLensActive && _hasDrawing && !IsBusy;

    private bool HasFocusTarget() =>
        CanUseCurrentTarget && Current is {Objects.IsEmpty: false} node && node.Actions.Contains(LensAction.Focus);

    private void ChangeSort(bool byCount)
    {
        _sortDescending = IsCountSortActive == byCount ? !_sortDescending : byCount;
        IsCountSortActive = byCount;
        NotifySorting();
        UpdateVisibleGroups();
        SavePreferences();
    }

    private void NotifySorting()
    {
        OnPropertyChanged(nameof(IsNameSortActive));
        OnPropertyChanged(nameof(IsCountSortActive));
        OnPropertyChanged(nameof(NameSortArrow));
        OnPropertyChanged(nameof(CountSortArrow));
    }

    private void RestorePreferences()
    {
        if (_settings?.Load<LensPreferences>(_settingsFileName) is not { } preferences)
            return;

        IsAutoFocus = preferences.IsAutoFocus;
        IsAutoSelect = preferences.IsAutoSelect;
        IsAutoIsolation = preferences.IsAutoIsolation;
        _enabledFilters = [.. (preferences.EnabledFilters ?? []).Where(id => !string.IsNullOrEmpty(id))];
        IsCountSortActive = preferences.IsCountSortActive;
        _sortDescending = preferences.SortDescending;
        _searchText = preferences.SearchText ?? "";
        _autoLoadObjectLimit = preferences.AutoLoadObjectLimit >= 0
            ? preferences.AutoLoadObjectLimit
            : LensPreferences.DefaultAutoLoadObjectLimit;

        foreach (var pair in preferences.PropertyGrouping ?? [])
        {
            var fields = new List<DrawingPropertyKey>();

            foreach (var name in pair.Value ?? [])
            {
                if (DrawingPropertyKey.TryParse(name, out var id))
                    fields.Add(id);
            }

            _propertyGrouping[pair.Key] = [.. fields.Distinct().OrderBy(item => item)];
        }

        foreach (var pair in preferences.DisplayProperties ?? [])
        {
            if (DrawingPropertyKey.TryParse(pair.Value, out var id))
                _displayProperties[pair.Key] = id;
        }
    }

    private void SavePreferences()
    {
        if (_disposed || _settings is null)
            return;

        var preferences = new LensPreferences(
            IsAutoFocus,
            IsAutoSelect,
            IsAutoIsolation,
            [.. _enabledFilters.OrderBy(item => item, StringComparer.Ordinal)],
            _blockParent?.SearchText ?? SearchText,
            IsCountSortActive,
            _sortDescending,
            _propertyGrouping.ToDictionary(pair => pair.Key, pair => pair.Value.Select(id => id.ToString()).ToArray()),
            _displayProperties.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()),
            _autoLoadObjectLimit);
        _saveFailed = !_settings.Save(_settingsFileName, preferences);
        OnPropertyChanged(nameof(Status));
    }

    private IEnumerable<LensNode> OrderItems(IEnumerable<LensNode> items)
    {
        if (!IsCountSortActive)
            return _sortDescending
                ? items.OrderByDescending(Label, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(node => node.Id, StringComparer.Ordinal)
                : items.OrderBy(Label, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(node => node.Id, StringComparer.Ordinal);

        var property = DisplayPropertyId;
        var comparer = Comparer<DrawingValue?>.Create(DrawingProperties.CompareValues);
        var availableFirst = items.OrderBy(node => Value(node) is null);
        var ordered = _sortDescending
            ? availableFirst.ThenByDescending(Value, comparer)
            : availableFirst.ThenBy(Value, comparer);

        return ordered.ThenBy(Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Id, StringComparer.Ordinal);

        DrawingValue? Value(LensNode node) => node.Kind == LensNodeKind.Object
            ? property is { } id ? DrawingProperties.GetValue(node, id) : node.RowMetric?.Value
            : new DrawingNumberValue(node.Count, DrawingUnit.Count);

        string Label(LensNode node) => DrawingValueFormatter.FormatLabel(node, Precision);
    }

    private void UpdateVisibleGroups()
    {
#if NETFRAMEWORK
        var groups = Groups.Where(group =>
            group.Label.IndexOf(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
#else
        var groups = Groups.Where(group => group.Label.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
#endif
        _visibleGroups = [.. OrderItems(groups)];
        _navigation.SetItemOrder(OrderItems);
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(ObjectPosition));
        EnterCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Loads the lens view for the active session.</summary>
    /// <param name="cancellationToken">Canceled when the panel deactivates this lens.</param>
    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !_hasDrawing || IsBusy)
            return Task.CompletedTask;

        _activationToken = cancellationToken;
        _needsCleanup = true;
        IsLensActive = true;

        return ExecuteActionAsync(token => _inventory is { } retained
            ? RebuildInventoryAsync(retained, token)
            : ReadAutomaticInventoryAsync(token));
    }

    /// <summary>Cancels drawing work and settles it before clearing lens effects.</summary>
    /// <param name="cancellationToken">Cleanup token independent of the canceled activation.</param>
    public async Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return new HostResult<bool>.Unavailable($"The {LensLabel} session has closed.");

        IsLensActive = false;
        // ReSharper disable once MethodHasAsyncOverload -- Cancellation must finish before replacing request ownership.
        _pendingRequest?.Cancel();
        StopReadProgress();
        using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pendingRequest = cleanup;
        NotifyBusy();

        try
        {
            await _operation;
            cleanup.Token.ThrowIfCancellationRequested();
            var result = await _actions.ClearAsync(cleanup.Token, _progress);
            cleanup.Token.ThrowIfCancellationRequested();
            SetStatus(DescribeCleanup(result));

            if (result is HostResult<bool>.Success {Value: true})
                _needsCleanup = false;

            return result;
        }
        catch (Exception exception)
        {
            if (!cleanup.IsCancellationRequested)
                SetStatus(new UiMessage("Cleanup did not complete: {0}", exception.Message));

            return new HostResult<bool>.Unavailable(exception.Message);
        }
        finally
        {
            _pendingRequest = null;
            NotifyBusy();
        }
    }

    private async Task RefreshContextAsync()
    {
        var previous = _operation;
        await previous;

        if (ReferenceEquals(previous, _operation))
            await ExecuteActionAsync(ReadAutomaticInventoryAsync);
    }

    private async Task<UiMessage> ClearEffectsAsync(CancellationToken cancellationToken) =>
        DescribeCleanup(await _actions.ClearAsync(cancellationToken, _progress));

    private async Task<UiMessage> ResetAsync(CancellationToken cancellationToken)
    {
        IsAutoFocus = false;
        IsAutoSelect = false;
        IsAutoIsolation = false;
        SavePreferences();

        var result = await _actions.ClearAsync(cancellationToken, _progress);
        return result.Match(
            cleared => new UiMessage(
                cleared
                    ? "Selection and isolation cleared. Auto modes off."
                    : "Cleanup did not complete."),
            reason => new UiMessage("Cleanup unavailable: {0}", new UiMessage(reason)));
    }

    private static UiMessage DescribeCleanup(HostResult<bool> result) => result.Match(
        cleared => new UiMessage(
            cleared
                ? "Selection and isolation cleared. Auto settings kept."
                : "Cleanup did not complete."),
        reason => new UiMessage("Cleanup unavailable: {0}", new UiMessage(reason)));

    private void NotifyCommands()
    {
        ReadCommand.NotifyCanExecuteChanged();
        ShowAllObjectsCommand.NotifyCanExecuteChanged();
        ShowSelectedObjectsCommand.NotifyCanExecuteChanged();
        IsolateCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        SelectCommand.NotifyCanExecuteChanged();
        FocusCommand.NotifyCanExecuteChanged();
        EnterCommand.NotifyCanExecuteChanged();
        BrowseBlockContentsCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        RootCommand.NotifyCanExecuteChanged();
        BreadcrumbCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        ToggleFilterCommand.NotifyCanExecuteChanged();
        ToggleGroupingCommand.NotifyCanExecuteChanged();
        SelectDisplayPropertyCommand.NotifyCanExecuteChanged();
        ApplyPropertyFilterCommand.NotifyCanExecuteChanged();
        ClearPropertyFilterCommand.NotifyCanExecuteChanged();
        NotifySavedFilterCommands();
        ToggleAutoIsolationCommand.NotifyCanExecuteChanged();
        ToggleAutoSelectCommand.NotifyCanExecuteChanged();
        ToggleAutoFocusCommand.NotifyCanExecuteChanged();
    }

    private async Task<UiMessage> ChangeScopeAsync(bool selectedOnly, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadInventoryAsync(selectedOnly, cancellationToken);
        }
        finally
        {
            OnPropertyChanged(nameof(IsSelectedObjectsOnly));
            OnPropertyChanged(nameof(IsAllObjects));
        }
    }

    private Task<UiMessage> ReadInventoryAsync(CancellationToken cancellationToken) =>
        IsBrowsingBlockContents
            ? ReadBlockContentsAsync(false, cancellationToken)
            : ReadInventoryAsync(IsSelectedObjectsOnly, cancellationToken);

    private Task<UiMessage> ReadAutomaticInventoryAsync(CancellationToken cancellationToken) =>
        ReadInventoryAsync(IsSelectedObjectsOnly, cancellationToken, _autoLoadObjectLimit);

    private async Task<UiMessage> ReadInventoryAsync(
        bool selectedOnly,
        CancellationToken cancellationToken,
        int? maximumObjects = null,
        bool requestSelection = true)
    {
        ImmutableArray<IPlacedObjectId>? selectedObjects = selectedOnly ? _selectedObjects ?? [] : null;

        if (selectedOnly && requestSelection)
        {
            var captured = await _actions.RequestObjectsAsync(cancellationToken, _progress);
            cancellationToken.ThrowIfCancellationRequested();

            if (captured is not HostResult<ImmutableArray<IPlacedObjectId>>.Success selection)
                return ((HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable) captured).Reason;

            selectedObjects = selection.Value;
        }

        var cleared = await _actions.ClearAsync(cancellationToken, _progress);
        cancellationToken.ThrowIfCancellationRequested();

        if (cleared is not HostResult<bool>.Success {Value: true})
            return DescribeCleanup(cleared);

        HostResult<LensPresentation> result;

        try
        {
            result = await _actions.ReadAsync(
                _grouping,
                _enabledFilters,
                selectedObjects,
                cancellationToken,
                maximumObjects,
                StartReadProgress(cancellationToken));
        }
        finally
        {
            StopReadProgress();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (result is HostResult<LensPresentation>.Success success)
            return await ApplyPresentationAsync(
                success.Value,
                () =>
                {
                    RestoreBlockParent();

                    if (IsSelectedObjectsOnly != selectedOnly)
                    {
                        IsSelectedObjectsOnly = selectedOnly;
                        _navigation.Reset([], false);
                    }

                    _selectedObjects = selectedObjects;
                },
                cancellationToken);

        _inventory = null;
        Precision = DrawingPrecision.Default;
        Groups = [];
        _navigation.Reset([], false);
        NotifyNavigation();
        _spaceIsDrawingData = false;
        SpaceLabel = "Unavailable";
        OnPropertyChanged(nameof(DisplaySpaceLabel));
        _emptyMessage = ((HostResult<LensPresentation>.Unavailable) result).Reason;
        OnPropertyChanged(nameof(EmptyMessage));
        return _emptyMessage;
    }

    private async Task<UiMessage> RebuildInventoryAsync(DrawingInventory inventory, CancellationToken cancellationToken)
    {
        var cleared = await _actions.ClearAsync(cancellationToken, _progress);
        cancellationToken.ThrowIfCancellationRequested();

        if (cleared is not HostResult<bool>.Success {Value: true})
            return DescribeCleanup(cleared);

        return await ApplyPresentationAsync(
            await BuildPresentationAsync(inventory, false, cancellationToken),
            null,
            cancellationToken);
    }

    private async Task<UiMessage> ApplyPresentationAsync(
        LensPresentation presentation,
        Action? beforePublish,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var groups = presentation.Inventory is { } inventory && HasPropertyOptions
            ? (await BuildPresentationAsync(inventory, true, cancellationToken)).Groups
            : presentation.Groups;
        cancellationToken.ThrowIfCancellationRequested();
        beforePublish?.Invoke();
        ApplyPresentation(presentation, groups);

        if (Current is not null)
            return await ApplySelectionAndIsolationAsync(cancellationToken);

        if (IsAutoIsolation && IsolationTarget is not null)
            return await ApplyRootEffectsAsync(cancellationToken);

        return HasGroups
            ? _grouping == DrawingGrouping.Layers
                ? "Choose a layer. Auto modes apply while browsing."
                : "Choose a type. Auto modes apply while browsing."
            : _emptyMessage;
    }

    private bool HasPropertyOptions =>
        _propertyFilters.Count != 0 || _propertyGrouping.Values.Any(fields => !fields.IsDefaultOrEmpty);

    private Task<LensPresentation> BuildPresentationAsync(
        DrawingInventory inventory,
        bool includePropertyOptions,
        CancellationToken cancellationToken)
    {
        var filters = _enabledFilters;
        var grouping = includePropertyOptions ? _propertyGrouping.ToImmutableDictionary() : null;
        var propertyFilters = includePropertyOptions ? _propertyFilters.ToImmutableDictionary() : null;
        var progress = _progress;

        return inventory.Entities.Length > 512
            ? Task.Run(Build, cancellationToken)
            : Task.FromResult(Build());

        LensPresentation Build() => DrawingLensProvider.Build(
            inventory,
            _grouping,
            filters,
            grouping,
            propertyFilters,
            progress: progress,
            cancellationToken: cancellationToken);
    }

    private void ApplyPresentation(LensPresentation presentation, ImmutableArray<LensNode> groups)
    {
        _spaceIsDrawingData = true;
        SpaceLabel = presentation.SpaceLabel;
        OnPropertyChanged(nameof(DisplaySpaceLabel));
        LensLabel = presentation.Label;
        RootLabel = presentation.RootLabel;
        SearchPlaceholder = presentation.SearchPlaceholder;
        GroupLabel = presentation.GroupLabel;
        _inventory = presentation.Inventory;
        Precision = presentation.Precision ?? _inventory?.Precision ?? DrawingPrecision.Default;
        _unfilteredGroups = _inventory is not null ? presentation.Groups : [];
        Groups = groups;
        _emptyMessage = IsBrowsingBlockContents
            ? "No included objects inside this block."
            : IsSelectedObjectsOnly
                ? _inventory is {Entities.IsEmpty: false}
                    ? "No selected objects match the inclusion filters."
                    : "No selected objects in the active space. Refresh to select objects."
                : presentation.EmptyMessage;
        _enabledFilters = _enabledFilters.Intersect(presentation.Filters.Select(filter => filter.Id));
        Filters =
        [
            .. presentation.Filters.Select(filter => new FilterOption(filter, _enabledFilters.Contains(filter.Id)))
        ];
        _navigation.Reset(Groups, true);
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(EmptyMessage));
        NotifyNavigation();
    }

    private async Task<UiMessage> ToggleAutoIsolationAsync(CancellationToken cancellationToken)
    {
        IsAutoIsolation = !IsAutoIsolation;
        SavePreferences();

        if (IsAutoIsolation && CanUseCurrentTarget && IsolationTarget is {Objects.IsEmpty: false})
            return await IsolateCurrentAsync(cancellationToken);

        return await ClearIsolationAsync(cancellationToken);
    }

    private async Task<UiMessage> ToggleAutoSelectAsync(CancellationToken cancellationToken)
    {
        IsAutoSelect = !IsAutoSelect;
        SavePreferences();

        return IsAutoSelect && CanUseCurrentTarget && Current is not null
            ? await SelectCurrentAsync(cancellationToken)
            : DescribeSelection(await _actions.SelectAsync([], cancellationToken, _progress), true);
    }

    private async Task<UiMessage> ToggleAutoFocusAsync(CancellationToken cancellationToken)
    {
        IsAutoFocus = !IsAutoFocus;
        SavePreferences();

        if (IsAutoFocus && CanUseCurrentTarget && Current is not null)
            return await FocusCurrentAsync(cancellationToken);

        return IsAutoFocus ? "Auto focus on." : "Auto focus off. Camera kept.";
    }

    private async Task<UiMessage> SelectCurrentAsync(CancellationToken cancellationToken) =>
        DescribeSelection(await _actions.SelectAsync(Current!.Objects, cancellationToken, _progress), false);

    private static UiMessage DescribeSelection(HostResult<bool> result, bool clearing)
    {
        if (result is HostResult<bool>.Unavailable unavailable)
            return new UiMessage("Selection unavailable: {0}", new UiMessage(unavailable.Reason));

        if (result is not HostResult<bool>.Success {Value: true})
            return "Selection did not complete.";

        return clearing ? "CAD selection cleared." : "CAD objects selected.";
    }

    private async Task<UiMessage> ClearIsolationAsync(CancellationToken cancellationToken) =>
        (await _actions.ClearIsolationAsync(cancellationToken, _progress)).Match(
            cleared => new UiMessage(cleared ? "Temporary isolation cleared." : "Isolation cleanup did not complete."),
            reason => new UiMessage("Isolation cleanup unavailable: {0}", new UiMessage(reason)));

    private async Task<UiMessage> FocusCurrentAsync(CancellationToken cancellationToken) => HasFocusTarget()
        ? await _actions.FocusAsync(Current!.Objects, cancellationToken, _progress)
        : "Focus unavailable: the current target has no usable bounds.";

    private async Task<UiMessage> ApplySelectionAndIsolationAsync(CancellationToken cancellationToken)
    {
        if (Current is {Objects.IsEmpty: true})
            return await ClearEffectsAsync(cancellationToken);

        if (IsObject && !await CheckCurrentObjectAsync(cancellationToken))
            return new UiMessage("{0} {1}", ObjectAvailabilityMessage, await ClearEffectsAsync(cancellationToken));

        List<UiMessage> messages = [];

        if (IsAutoSelect)
            messages.Add(await SelectCurrentAsync(cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();
        messages.Add(
            IsAutoIsolation
                ? await _actions.IsolateObjectsAsync(Current!.Objects, cancellationToken, _progress)
                : await ClearIsolationAsync(cancellationToken));

        return messages.Count == 1 ? messages[0] : new UiMessage("{0} {1}", messages[0], messages[1]);
    }

    private Task NavigateAsync(Action navigate) => ExecuteActionAsync(async token =>
    {
        navigate();
        if (Current is null)
            return await ApplyRootEffectsAsync(token);

        var message = await ApplySelectionAndIsolationAsync(token);

        if (!IsAutoFocus || !CanUseCurrentTarget || Current.Objects.IsEmpty)
            return message;

        token.ThrowIfCancellationRequested();
        return new UiMessage("{0} {1}", await FocusCurrentAsync(token), message);
    });

    private void Enter(LensNode? node)
    {
        if (node is not null && _navigation.Enter(node.Id))
            NotifyNavigation();
    }

    private void GoBackTo(int depth)
    {
        _navigation.GoBackTo(depth);
        NotifyNavigation();
    }

    private void MoveObject(int offset)
    {
        _navigation.MoveObject(offset);
        NotifyNavigation();
    }

    private async Task<UiMessage> ToggleFilterAsync(FilterOption filter, CancellationToken cancellationToken)
    {
        var inventory = _inventory;
        var groups = Groups;
        var previousFilters = _enabledFilters;
        var previousOptions = Filters;
        _enabledFilters = filter.IsEnabled
            ? _enabledFilters.Remove(filter.Descriptor.Id)
            : _enabledFilters.Add(filter.Descriptor.Id);
        Filters =
        [
            .. Filters.Select(option => option with {IsEnabled = _enabledFilters.Contains(option.Descriptor.Id)})
        ];
        OnPropertyChanged(nameof(Filters));
        SavePreferences();

        try
        {
            if (filter.Descriptor.Id == DrawingLensProvider.ShowBlockContents)
                return await ReadInventoryAsync(
                    IsSelectedObjectsOnly,
                    cancellationToken,
                    _inventory is null ? _autoLoadObjectLimit : null,
                    false);

            return _inventory is { } retained
                ? await RebuildInventoryAsync(retained, cancellationToken)
                : await ReadAutomaticInventoryAsync(cancellationToken);
        }
        catch
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (!ReferenceEquals(_inventory, inventory) || !Groups.Equals(groups))
                    throw;

                _enabledFilters = previousFilters;
                Filters = previousOptions;
                OnPropertyChanged(nameof(Filters));
                SavePreferences();
                throw;
            }

            Groups = [];
            _inventory = null;
            _navigation.Reset([], false);
            NotifyNavigation();

            throw;
        }
    }

    private async Task<bool> CheckCurrentObjectAsync(CancellationToken cancellationToken)
    {
        var current = Current!;
        var request = _pendingRequest;
        SetCurrentObjectAvailability(null);
        var result = await _actions.ExistsAsync(current.Objects[0], cancellationToken, _progress);
        cancellationToken.ThrowIfCancellationRequested();

        if (_disposed || !ReferenceEquals(request, _pendingRequest) || !ReferenceEquals(current, Current))
            throw new OperationCanceledException(cancellationToken);

        SetCurrentObjectAvailability(result);
        return result is HostResult<bool>.Success {Value: true};
    }

    private void SetCurrentObjectAvailability(HostResult<bool>? availability)
    {
        _currentObjectAvailability = availability;
        OnPropertyChanged(nameof(IsCurrentObjectMissing));
        OnPropertyChanged(nameof(HasObjectAvailabilityNotice));
        OnPropertyChanged(nameof(ObjectAvailabilityNotice));
        NotifyCommands();
    }

    private void NotifyNavigation()
    {
        var target = IsObject && Current is {Objects.IsEmpty: false} current ? current.Objects[0] : null;

        if (!Equals(target, _availabilityTarget))
        {
            _availabilityTarget = target;
            SetCurrentObjectAvailability(null);
        }

        var type = UnfilteredType;

        if (!ReferenceEquals(type, _filterOptionsType))
        {
            _filterOptionsType = type;
            PropertyFilter.Load(type?.Children ?? [], AppliedPropertyFilter);
            RefreshSavedFilters();
            SetSavedFilterMessage("");
        }

        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentFields));
        OnPropertyChanged(nameof(Breadcrumbs));
        OnPropertyChanged(nameof(HasCurrent));
        OnPropertyChanged(nameof(HasNavigation));
        OnPropertyChanged(nameof(IsObject));
        OnPropertyChanged(nameof(IsBrowsingBlockContents));
        OnPropertyChanged(nameof(CanBrowseBlockContents));
        OnPropertyChanged(nameof(RefreshLabel));
        OnPropertyChanged(nameof(ObjectPosition));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(SortByCountLabel));
        OnPropertyChanged(nameof(SortByNameLabel));
        OnPropertyChanged(nameof(CanGroup));
        OnPropertyChanged(nameof(GroupingOptions));
        OnPropertyChanged(nameof(GroupingSummary));
        OnPropertyChanged(nameof(CanShowProperty));
        OnPropertyChanged(nameof(DisplayPropertyHint));
        OnPropertyChanged(nameof(DisplayPropertyId));
        OnPropertyChanged(nameof(DisplayPropertyLabel));
        OnPropertyChanged(nameof(DisplayPropertyOptions));
        OnPropertyChanged(nameof(AppliedPropertyFilter));
        OnPropertyChanged(nameof(PropertyFilterSummary));
        OnPropertyChanged(nameof(PropertyFilterCount));
        NotifyCommands();
    }

    private void SelectDisplayProperty(GroupingOption? option)
    {
        if (option is null || GroupingTypeKey is not { } typeKey)
            return;

        _displayProperties[typeKey] = option.Id;

        if (!IsCountSortActive)
        {
            IsCountSortActive = true;
            _sortDescending = false;
        }

        NotifySorting();
        UpdateVisibleGroups();
        NotifyNavigation();
        SavePreferences();
    }

    private async Task<UiMessage> ToggleGroupingAsync(GroupingOption option, CancellationToken cancellationToken)
    {
        if (_inventory is null || GroupingTypeKey is not { } typeKey)
            return "Grouping unavailable.";

        var inventory = _inventory;
        var groups = Groups;
        var hadGrouping = _propertyGrouping.TryGetValue(typeKey, out var fields);
        var selected = hadGrouping
            ? fields
            : ImmutableArray<DrawingPropertyKey>.Empty;
        _propertyGrouping[typeKey] = option.IsSelected
            ? selected.Remove(option.Id)
            : [.. selected.Add(option.Id).Distinct().OrderBy(item => item)];
        var previousTargets = Current?.Objects ?? [];
        LensPresentation presentation;

        try
        {
            presentation = await BuildPresentationAsync(inventory, true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!ReferenceEquals(_inventory, inventory) || !Groups.Equals(groups))
                throw;

            if (hadGrouping)
                _propertyGrouping[typeKey] = selected;
            else
                _propertyGrouping.Remove(typeKey);

            NotifyNavigation();
            throw;
        }

        Groups = presentation.Groups;
        _navigation.Reset(Groups, true);
        NotifyNavigation();
        SavePreferences();

        if (Current is null || previousTargets.SequenceEqual(Current.Objects))
            return "Grouping updated.";

        if (Current.Objects.IsEmpty)
            return await ClearEffectsAsync(cancellationToken);

        List<UiMessage> messages = [];

        if (IsAutoSelect)
            messages.Add(await SelectCurrentAsync(cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();

        if (IsAutoIsolation)
            messages.Add(await _actions.IsolateObjectsAsync(Current.Objects, cancellationToken, _progress));

        cancellationToken.ThrowIfCancellationRequested();

        if (IsAutoFocus)
            messages.Add(await FocusCurrentAsync(cancellationToken));

        return messages.Count == 0 ? "Grouping updated." : new UiMessage("{0}", string.Join(" ", messages));
    }

    private Task<UiMessage> ApplyPropertyFilterAsync(CancellationToken cancellationToken)
    {
        if (GroupingTypeKey is not { } key || PropertyFilter.CreateFilter() is not { } filter)
            return Task.FromResult<UiMessage>("Enter a valid filter value.");

        _propertyFilters.TryGetValue(key, out var previousFilter);
        _propertyFilters[key] = filter;
        return RebuildPropertyFilterAsync(key, previousFilter, cancellationToken);
    }

    private Task<UiMessage> ClearPropertyFilterAsync(CancellationToken cancellationToken)
    {
        if (GroupingTypeKey is not { } key)
            return Task.FromResult<UiMessage>("Property filter updated.");

        _propertyFilters.TryGetValue(key, out var previousFilter);
        _propertyFilters.Remove(key);
        return RebuildPropertyFilterAsync(key, previousFilter, cancellationToken);
    }

    private async Task<UiMessage> RebuildPropertyFilterAsync(
        string key,
        DrawingPropertyFilter? previousFilter,
        CancellationToken cancellationToken)
    {
        if (_inventory is null)
            return "Property filter updated.";

        var previousTargets = Current?.Objects ?? [];
        var wasObject = IsObject;
        var inventory = _inventory;
        var groups = Groups;
        LensPresentation presentation;

        try
        {
            presentation = await BuildPresentationAsync(inventory, true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!ReferenceEquals(_inventory, inventory) || !Groups.Equals(groups))
                throw;

            if (previousFilter is not null)
                _propertyFilters[key] = previousFilter;
            else
                _propertyFilters.Remove(key);

            NotifyNavigation();
            throw;
        }

        Groups = presentation.Groups;
        _navigation.Reset(Groups, true);

        if (wasObject && !IsObject)
            _navigation.GoBackTo(_grouping == DrawingGrouping.Layers ? 2 : 1);

        PropertyFilter.Load(UnfilteredType?.Children ?? [], AppliedPropertyFilter);
        NotifyNavigation();

        if (Current is null || Current.Objects.IsEmpty)
            return await ClearEffectsAsync(cancellationToken);

        if (previousTargets.SequenceEqual(Current.Objects))
            return "Property filter updated.";

        var message = await ApplySelectionAndIsolationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return IsAutoFocus && CanUseCurrentTarget
            ? new UiMessage("{0} {1}", await FocusCurrentAsync(cancellationToken), message)
            : message;
    }

    private static string FilterOperatorLabel(DrawingFilterOperator comparison) => comparison switch
    {
        DrawingFilterOperator.Equal => "=",
        DrawingFilterOperator.NotEqual => "≠",
        DrawingFilterOperator.LessThan => "<",
        DrawingFilterOperator.LessThanOrEqual => "≤",
        DrawingFilterOperator.GreaterThan => ">",
        DrawingFilterOperator.GreaterThanOrEqual => "≥",
        DrawingFilterOperator.Contains => UiText.Current.Get("Contains"),
        _ => ""
    };

    private CallbackProgress<DrawingReadProgress> StartReadProgress(CancellationToken cancellationToken)
    {
        var operation = new object();
        _readProgressOperation = operation;
        ReadProgress = new DrawingReadProgress(0, 0, 0);

        var report = _progress;
        report?.Report(new OperationProgress(OperationStage.Reading));
        IProgress<DrawingReadProgress> uiProgress = new Progress<DrawingReadProgress>(progress =>
        {
            if (_disposed || cancellationToken.IsCancellationRequested ||
                !ReferenceEquals(_readProgressOperation, operation))
                return;

            ReadProgress = progress;
        });

        return new CallbackProgress<DrawingReadProgress>(progress =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            report?.Report(
                progress.Stage == OperationStage.Reading
                    ? new OperationProgress(progress.Stage, progress.ProcessedObjects)
                    : new OperationProgress(progress.Stage, progress.CompletedRoots, progress.TotalRoots));
            uiProgress.Report(progress);
        });
    }

    private void StopReadProgress()
    {
        _readProgressOperation = null;
        ReadProgress = null;
    }

    private Task ExecuteActionAsync(Func<CancellationToken, Task<UiMessage>> action)
    {
        if (!CanRun())
            return Task.CompletedTask;

        _operation = CompleteActionAsync(action);
        return _operation;
    }

    private async Task CompleteActionAsync(Func<CancellationToken, Task<UiMessage>> action)
    {
        var request = CancellationTokenSource.CreateLinkedTokenSource(_activationToken);
        _pendingRequest = request;
        NotifyBusy();
        OperationProgressWindow? window = null;

        try
        {
            window = CreateProgressWindow(request.Cancel);
            _operationWindow = window;
            _cancelOperation = window.RequestCancellation;
            CancelCommand.NotifyCanExecuteChanged();
            var currentWindow = window;
            var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
            _progress = new CallbackProgress<OperationProgress>(progress =>
            {
                currentWindow.Report(progress);
                dispatcher?.BeginInvoke(() =>
                {
                    if (!_disposed && ReferenceEquals(_operationWindow, currentWindow))
                        OperationProgress = progress;
                });
            });
            OperationProgress = new OperationProgress(OperationStage.Preparing);
            _progress.Report(OperationProgress);
            SetStatus("Working…");
            var message = await action(request.Token);
            request.Token.ThrowIfCancellationRequested();
            SetStatus(message);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (window is {IsCancellationRequested: true} && !_disposed && ReferenceEquals(_pendingRequest, request))
            {
                IsAutoIsolation = false;
                SavePreferences();
                _progress?.Report(new OperationProgress(OperationStage.Clearing));
                try
                {
                    var result = await _actions.ClearIsolationAsync(CancellationToken.None, _progress);
                    SetStatus(
                        result is HostResult<bool>.Success {Value: true}
                            ? new UiMessage("Operation canceled. Isolation cleared. Auto Isolate off.")
                            : new UiMessage("Operation canceled. {0}", DescribeCleanup(result)));
                }
                catch (Exception exception)
                {
                    SetStatus(new UiMessage("Cleanup did not complete: {0}", exception.Message));
                }
            }
        }
        catch (Exception exception)
        {
            if (!request.IsCancellationRequested)
                SetStatus(new UiMessage("Unable to complete the operation: {0}", exception.Message));
        }
        finally
        {
            // The independent window and token outlive native work and uncancellable isolation cleanup.
            _operationWindow = null;
            window?.Dispose();
            _cancelOperation = null;
            _progress = null;
            StopReadProgress();
            OperationProgress = null;
            CancelCommand.NotifyCanExecuteChanged();

            if (ReferenceEquals(_pendingRequest, request))
            {
                _pendingRequest = null;
                NotifyBusy();
            }

            _ = DisposeRequestAsync(window, request);
        }
    }

    private static async Task DisposeRequestAsync(OperationProgressWindow? window, CancellationTokenSource request)
    {
        // A callback already running on the progress thread may still be using this source.
        if (window is not null)
            await window.Closed.ConfigureAwait(false);

        request.Dispose();
    }

    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        PropertyFilter.RefreshLanguage();
        UpdateVisibleGroups();
        OnPropertyChanged(string.Empty);
        NotifyCommands();
    }

    private void SetStatus(UiMessage message)
    {
        _status = message;
        OnPropertyChanged(nameof(Status));
    }

    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommands();
    }
}
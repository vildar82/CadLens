using System.Collections.Immutable;
using System.Globalization;
using CadLens.Lenses;
using CadLens.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CadLens.UI;

/// <summary>Edits one typed condition without changing the applied drawing result.</summary>
public sealed class PropertyFilterEditor : ObservableObject
{
    private ImmutableArray<LensNode> _objects = [];
    private string _error = "";
    private DrawingPropertyFilter? _loadedFilter;
    private string _loadedInputText = "";
    private CultureInfo _inputCulture = UiText.Current.Culture;

    /// <summary>Observed properties in the unfiltered, included type scope.</summary>
    public ImmutableArray<GroupingOption> PropertyOptions { get; private set; } = [];

    /// <summary>Property being edited.</summary>
    public DrawingPropertyId? PropertyId
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
                return;

            Operator = DrawingFilterOperator.Equal;
            InputText = "";
            UpdateValueOptions();
            _error = "";
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>Comparison being edited.</summary>
    public DrawingFilterOperator Operator
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>Uncommitted numeric or text input.</summary>
    public string InputText
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    /// <summary>Uncommitted value selected from typed choices.</summary>
    public DrawingValue? SelectedValue
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>Typed choices preserving assigned identities.</summary>
    public ImmutableArray<PropertyFilterValueOption> ValueOptions { get; private set; } = [];

    /// <summary>Whether a typed choice replaces free text input.</summary>
    public bool UsesValueOptions => SampleValue is not (DrawingNumberValue or DrawingTextValue) ||
                                    ValueOptions.Any(option => option.Value is DrawingTextValue
                                    {
                                        IsApplicationText: true
                                    });

    /// <summary>Whether a text box accepts the filter value.</summary>
    public bool UsesTextInput => !UsesValueOptions;

    /// <summary>Units and numeric-input guidance.</summary>
    public string InputHint => UiText.Current.Get(
        SampleValue switch
        {
            DrawingNumberValue {Unit: DrawingUnit.Angle} => "Enter degrees without digit grouping.",
            DrawingNumberValue {Unit: DrawingUnit.Distance} => "Enter drawing units without digit grouping.",
            DrawingNumberValue {Unit: DrawingUnit.Area} => "Enter square drawing units without digit grouping.",
            DrawingNumberValue => "Enter a number without digit grouping.",
            _ => ""
        });

    /// <summary>Validation feedback; the applied condition remains unchanged.</summary>
    public string Error => UiText.Current.Get(_error);

    /// <summary>Comparisons supported by the selected property's typed values.</summary>
    public ImmutableArray<PropertyFilterOperatorOption> OperatorOptions => SampleValue switch
    {
        DrawingNumberValue when !UsesValueOptions =>
        [
            new PropertyFilterOperatorOption(DrawingFilterOperator.Equal, "=", "Equals"),
            new PropertyFilterOperatorOption(DrawingFilterOperator.NotEqual, "≠", "Does not equal"),
            new PropertyFilterOperatorOption(DrawingFilterOperator.LessThan, "<", "Less than"),
            new PropertyFilterOperatorOption(DrawingFilterOperator.LessThanOrEqual, "≤", "Less than or equal"),
            new PropertyFilterOperatorOption(DrawingFilterOperator.GreaterThan, ">", "Greater than"),
            new PropertyFilterOperatorOption(DrawingFilterOperator.GreaterThanOrEqual, "≥", "Greater than or equal")
        ],
        DrawingTextValue when !UsesValueOptions =>
        [
            new PropertyFilterOperatorOption(DrawingFilterOperator.Equal, "=", "Equals"),
            new PropertyFilterOperatorOption(DrawingFilterOperator.NotEqual, "≠", "Does not equal"),
            new PropertyFilterOperatorOption(DrawingFilterOperator.Contains, "∋", "Contains")
        ],
        _ =>
        [
            new PropertyFilterOperatorOption(DrawingFilterOperator.Equal, "=", "Equals"),
            new PropertyFilterOperatorOption(DrawingFilterOperator.NotEqual, "≠", "Does not equal")
        ]
    };

    private DrawingValue? SampleValue => PropertyId is { } id
        ? _objects.Select(node => DrawingProperties.GetValue(node, id)).FirstOrDefault(value => value is not null)
        : null;

    internal void Load(ImmutableArray<LensNode> objects, DrawingPropertyFilter? applied)
    {
        _objects = objects;
        _inputCulture = UiText.Current.Culture;
        PropertyOptions =
        [
            .. objects.SelectMany(node => node.Properties
                    .Where(property => DrawingProperties.GetValue(node, property.Id) is not null)
                    .Select(property => property.Id))
                .Distinct()
                .OrderBy(id => id)
                .Select(id => new GroupingOption(id, DrawingProperties.GetLabel(id), false))
        ];
        PropertyId = applied?.PropertyId ?? PropertyOptions.FirstOrDefault()?.Id;
        UpdateValueOptions();
        Operator = applied?.Operator ?? DrawingFilterOperator.Equal;
        InputText = applied?.Value switch
        {
            DrawingNumberValue number => (number.Unit == DrawingUnit.Angle
                ? number.Value * (180 / Math.PI)
                : number.Value).ToString("R", UiText.Current.Culture),
            DrawingTextValue text => text.Text,
            _ => ""
        };

        if (applied is not null && UsesValueOptions)
            SelectedValue = ValueOptions.FirstOrDefault(option => option.Value.Equals(applied.Value))?.Value;

        _loadedFilter = applied;
        _loadedInputText = InputText;
        _error = "";
        OnPropertyChanged(string.Empty);
    }

    internal DrawingPropertyFilter? CreateFilter()
    {
        _error = "";
        var value = ReadInput();

        if (_loadedFilter is {Value: DrawingNumberValue number} loaded && PropertyId == loaded.PropertyId &&
            InputText == _loadedInputText && value is DrawingNumberValue parsed && parsed.Unit == number.Unit && number.Value.IsFinite())
            value = number;

        if (PropertyId is not { } id || value is null || !OperatorOptions.Any(option => option.Value == Operator))
        {
            _error = "Enter a valid filter value.";
            OnPropertyChanged(nameof(Error));
            return null;
        }

        OnPropertyChanged(nameof(Error));
        return new DrawingPropertyFilter(id, Operator, value);
    }

    internal void RefreshLanguage()
    {
        var unchangedInput = InputText == _loadedInputText;

        if (SampleValue is DrawingNumberValue &&
            double.TryParse(InputText, NumberStyles.Float, _inputCulture, out var number) && number.IsFinite())
            InputText = number.ToString("R", UiText.Current.Culture);

        if (unchangedInput)
            _loadedInputText = InputText;

        _inputCulture = UiText.Current.Culture;

        foreach (var option in ValueOptions)
            option.RefreshLanguage();

        OnPropertyChanged(string.Empty);
    }

    private DrawingValue? ReadInput()
    {
        if (UsesValueOptions)
            return ValueOptions.Any(option => option.Value.Equals(SelectedValue)) ? SelectedValue : null;

        switch (SampleValue)
        {
            case DrawingNumberValue number:
                if (!double.TryParse(InputText, NumberStyles.Float, UiText.Current.Culture, out var value))
                    return null;

                if (number.Unit == DrawingUnit.Angle)
                    value *= Math.PI / 180;

                return value.IsFinite() ? new DrawingNumberValue(value, number.Unit) : null;

            case DrawingTextValue text:
                return new DrawingTextValue(InputText, text.IsApplicationText);

            default:
                return null;
        }
    }

    private void UpdateValueOptions()
    {
        var values = PropertyId is { } id
            ? _objects.Select(node => DrawingProperties.GetValue(node, id)).OfType<DrawingValue>().Distinct().ToList()
            : [];

        if (SampleValue is DrawingBooleanValue)
            values = [new DrawingBooleanValue(false), new DrawingBooleanValue(true)];

        ValueOptions =
        [
            .. values.OrderBy(value => value, Comparer<DrawingValue>.Create(DrawingProperties.CompareValues))
                .Select(value => new PropertyFilterValueOption(value))
        ];
        SelectedValue = ValueOptions.FirstOrDefault()?.Value;
    }
}

/// <summary>A comparison operator and its application label.</summary>
/// <param name="Value">Typed operator.</param>
/// <param name="Symbol">Language-independent comparison symbol.</param>
/// <param name="Label">English resource key.</param>
public sealed record PropertyFilterOperatorOption(DrawingFilterOperator Value, string Symbol, string Label);

/// <summary>A selectable assigned value; captions never determine equality.</summary>
/// <param name="value">Original detached value.</param>
public sealed class PropertyFilterValueOption(DrawingValue value) : ObservableObject
{
    /// <summary>Original detached value used for selection and comparison.</summary>
    public DrawingValue Value { get; } = value;

    /// <summary>Localized display text.</summary>
    public string Label
    {
        get;
        private set => SetProperty(ref field, value);
    } = DrawingValueFormatter.FormatValue(value);

    internal void RefreshLanguage() => Label = DrawingValueFormatter.FormatValue(Value);
}

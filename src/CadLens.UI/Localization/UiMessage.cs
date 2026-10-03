namespace CadLens.UI;

internal sealed record UiMessage(string Format, params object[] Arguments)
{
    public static implicit operator UiMessage(string text) => new(text);

    public override string ToString() => Arguments.Length == 0
        ? UiText.Current.Get(Format)
        : string.Format(
            UiText.Current.Culture,
            UiText.Current.Get(Format),
            [.. Arguments.Select(argument => argument is UiMessage message ? message.ToString() : argument)]);
}
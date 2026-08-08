/// <summary>
/// <paramref name="Command"/> travels with the button so the render loop looks up what a click
/// means rather than repeating the layout's ordering.
/// </summary>
record Button(string Label, bool Enabled, CommandKind Command);

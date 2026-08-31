/// <summary>
/// Source generated serialisation for <see cref="Settings" />, rather than the reflection based
/// serialiser.
/// <para>
/// Worth less than it looks. Reading the settings file cost 31ms of a 200ms start, and moving the
/// metadata to compile time took 4ms off that. Almost all of the rest is System.Text.Json being
/// loaded and jitted for the first time, which no amount of generated code avoids - the file
/// itself is under 200 bytes. Kept because it is strictly cheaper, and because it keeps the
/// reflection based serialiser off the startup path altogether, which is the part that grows on a
/// cold start.
/// </para>
/// <para>
/// Both directions go through it, so a save does not reach the reflection based serialiser either.
/// The written JSON is unchanged: the generator's defaults are the same defaults, which is what
/// <c>SettingsHelperTests.ReadWrite</c> pins.
/// </para>
/// </summary>
[JsonSerializable(typeof(Settings))]
partial class SettingsContext :
    JsonSerializerContext;

using System;
using System.Text.Json;

static class Serializer
{
    public static T Deserialize<T>(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload);
        }
        catch (Exception exception)
        {
            throw new Exception($@"Failed to Deserialize payload:
{payload}", exception);
        }
    }
}
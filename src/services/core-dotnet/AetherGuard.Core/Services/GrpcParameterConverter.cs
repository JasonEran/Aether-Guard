using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AetherGuard.Core.Services;

public static class GrpcParameterConverter
{
    public static Struct ParseJsonStruct(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Struct();
        }

        try
        {
            return JsonParser.Default.Parse<Struct>(json);
        }
        catch (Exception)
        {
            return new Struct();
        }
    }

    public static string FormatStruct(Struct? parameters)
    {
        if (parameters is null || parameters.Fields.Count == 0)
        {
            return "{}";
        }

        return JsonFormatter.Default.Format(parameters);
    }
}

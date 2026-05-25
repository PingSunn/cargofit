using System.Text.Encodings.Web;
using System.Text.Json;

namespace CargoFit;

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions WriteIndented = new()
    {
        WriteIndented = true,
        // Write Thai (and all Unicode) characters directly instead of \uXXXX escapes
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

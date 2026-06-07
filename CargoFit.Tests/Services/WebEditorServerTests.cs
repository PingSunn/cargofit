using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace CargoFit.Tests;

// Smoke test for the in-process static-file server that backs the web product editor.
// Guards the request-path → embedded-resource mapping (breaks if a WebEditor asset is renamed
// without updating WebEditorServer.Routes / the csproj LogicalName).
public class WebEditorServerTests
{
    [Fact]
    public async Task Serves_embedded_assets_and_404s_unknown_paths()
    {
        WebEditorServer.EnsureStarted();
        try
        {
            using var http = new HttpClient();

            var index = await http.GetAsync(WebEditorServer.Url);
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            Assert.Contains("CargoFit", await index.Content.ReadAsStringAsync());

            var js = await http.GetAsync(WebEditorServer.Url + "app.js");
            Assert.Equal(HttpStatusCode.OK, js.StatusCode);
            Assert.Contains("application/javascript", js.Content.Headers.ContentType!.ToString());

            var css = await http.GetAsync(WebEditorServer.Url + "styles.css");
            Assert.Equal(HttpStatusCode.OK, css.StatusCode);

            var three = await http.GetAsync(WebEditorServer.Url + "vendor/three.min.js");
            Assert.Equal(HttpStatusCode.OK, three.StatusCode);
            Assert.True((await three.Content.ReadAsByteArrayAsync()).Length > 100_000);

            var missing = await http.GetAsync(WebEditorServer.Url + "does-not-exist");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            WebEditorServer.Stop();
        }
    }
}

#if GA_BROWSER_CONFORMANCE
using System.Runtime.InteropServices.JavaScript;

internal static partial class BrowserVisibility
{
    internal const string Module = "skecs.ga.host.visibility";

    [JSImport("waitForVisibility", Module)]
    internal static partial Task WaitForVisibility(bool hidden);

    [JSImport("isHidden", Module)]
    internal static partial bool IsHidden();
}
#endif

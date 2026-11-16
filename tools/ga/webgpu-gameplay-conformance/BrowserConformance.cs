using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SKECS.GA;
using SKECS.GA.Backend.WebGpu;

[assembly: SupportedOSPlatform("browser")]

internal static class BrowserConformance
{
    internal static async Task<(GaWebGpuBackend Device, GaStartupResult Startup, GaArtifactCandidate Artifact, string Profile, Uri Site)> OpenAsync(
        string[] args, GaContractIdentity contract, string defaultProfile, params GaSystemRequest[] cpuStages)
    {
        var host = WebAssemblyHostBuilder.CreateDefault(args).Build();
        var navigation = host.Services.GetRequiredService<NavigationManager>();
        var site = new Uri(navigation.BaseUri);
        string query = new Uri(navigation.Uri).Query;
        string profile = query.Length == 0 ? defaultProfile : query[1..];
        using var http = new HttpClient { BaseAddress = site };
        string stem = "bundle/" + contract.SystemId;
        var manifest = await http.GetByteArrayAsync(stem + ".manifest.json");
        var contentId = await http.GetStringAsync(stem + ".content-id");
        var shader = await http.GetByteArrayAsync(stem + ".wgsl");
        var reflection = await http.GetByteArrayAsync(stem + ".layout");
        var sourceMap = await http.GetByteArrayAsync(stem + ".source-map.json");
        var artifact = GaArtifactCandidate.Load(manifest, contentId.Trim(), shader, reflection, sourceMap);

        var opened = await GaWebGpuBackend.TryOpenAsync(new Uri(site, "ga-webgpu.mjs").AbsoluteUri);
        var backend = opened.Backend ?? throw new InvalidOperationException(opened.Error);
        try
        {
            GaSystemRequest[] requests = [new(contract, [artifact]), .. cpuStages];
            var startup = await GaStartup.SelectAsync(GaPolicy.RequireGpu, requests, [backend], allowExperimental: true);
            return (backend, startup, artifact, profile, site);
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }
}

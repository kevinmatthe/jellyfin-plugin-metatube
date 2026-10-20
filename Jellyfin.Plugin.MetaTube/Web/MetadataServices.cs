#if !__EMBY__
using Jellyfin.Plugin.MetaTube.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MetaTube.Web;

public class MetadataServices : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<IMetadataBackend, MetadataBackend>();
        services.AddSingleton<IExtendedMetadataSettings, PluginMetadataSettings>();
        services.AddSingleton<IStartupFilter, MetadataStartupFilter>();
    }
}

public class MetadataStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<MetadataWebMiddleware>();
        next(app);
    };
}
#endif

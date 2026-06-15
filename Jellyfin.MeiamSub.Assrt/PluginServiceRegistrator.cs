using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Jellyfin.MeiamSub.Assrt
{
    /// <summary>
    /// 插件服务注册器
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHttpClient("MeiamSub.Assrt", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(20); // 适当的超时时间
                client.DefaultRequestHeaders.Add("User-Agent", "MeiamSub.Assrt/1.0.14.0 (Jellyfin)");
                client.DefaultRequestHeaders.Add("Accept", "*/*");
            });

            serviceCollection.AddSingleton<ISubtitleProvider, AssrtProvider>();
        }
    }
}

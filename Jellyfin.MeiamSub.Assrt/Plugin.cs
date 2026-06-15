using Jellyfin.MeiamSub.Assrt.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;

namespace Jellyfin.MeiamSub.Assrt
{
    /// <summary>
    /// Assrt 插件入口
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// 插件ID
        /// </summary>
        public override Guid Id => new Guid("E693CDAB-8E8C-4D12-BBDF-3FE564CD61F0");

        /// <summary>
        /// 插件名称
        /// </summary>
        public override string Name => "MeiamSub.Assrt";

        /// <summary>
        /// 插件描述
        /// </summary>
        public override string Description => "Download subtitles from Assrt (伪射手网)";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }

        /// <summary>
        /// 获取插件配置页面列表
        /// </summary>
        /// <returns>配置页面信息</returns>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }
    }
}

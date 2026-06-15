using MediaBrowser.Model.Plugins;

namespace Jellyfin.MeiamSub.Assrt.Configuration
{
    /// <summary>
    /// 插件配置类
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Assrt API 鉴权 Token
        /// </summary>
        public string AssrtApiToken { get; set; }

        /// <summary>
        /// 是否使用元数据中的剧集名称和季集编号搜索字幕
        /// </summary>
        public bool EnableUseMetadata { get; set; }

        public PluginConfiguration()
        {
            AssrtApiToken = string.Empty;
            EnableUseMetadata = true; // 默认为 true，因为 Assrt 是基于文本搜索，元数据匹配非常合适
        }
    }
}

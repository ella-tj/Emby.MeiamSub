using Emby.Web.GenericEdit;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;
using System.ComponentModel;
using System.IO;

namespace Emby.MeiamSub.Assrt
{
    /// <summary>
    /// Assrt 插件入口
    /// </summary>
    public class Plugin : BasePluginSimpleUI<PluginConfiguration>, IHasThumbImage
    {
        public Plugin(IApplicationPaths applicationPaths, IApplicationHost applicationHost) : base(applicationHost)
        {
            Instance = this;
        }

        /// <summary>
        /// 插件ID
        /// </summary>
        public override Guid Id => new Guid("D276A8D0-4B17-4EEF-94B6-61D646DE5A22");

        /// <summary>
        /// 插件名称
        /// </summary>
        public override string Name => "MeiamSub.Assrt";

        /// <summary>
        /// 插件描述
        /// </summary>
        public override string Description => "Download subtitles from Assrt (伪射手网)";

        /// <summary>
        /// 缩略图格式化类型
        /// </summary>
        public ImageFormat ThumbImageFormat => ImageFormat.Gif;

        /// <summary>
        /// 获取插件选项
        /// </summary>
        public PluginConfiguration Options => this.GetOptions();

        public static Plugin Instance { get; private set; }

        /// <summary>
        /// 获取插件缩略图资源流
        /// </summary>
        /// <returns>图片资源流，若不存在则返回 null</returns>
        public Stream GetThumbImage()
        {
            var type = GetType();
            var resourceName = $"{type.Namespace}.Thumb.png";
            var stream = type.Assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                return null;
            }

            return stream;
        }
    }

    /// <summary>
    /// 插件配置类
    /// </summary>
    public class PluginConfiguration : EditableOptionsBase
    {
        public override string EditorTitle => "MeiamSub Assrt Options";

        [DisplayName("Assrt API Token")]
        [Description("请输入您在 assrt.net 注册获取的 32 位 API Token")]
        public string AssrtApiToken { get; set; }

        [DisplayName("使用元数据搜索字幕")]
        [Description("勾选此项后，使用元数据中的剧集名称和季集编号搜索字幕，而非文件名。建议开启。")]
        public bool EnableUseMetadata { get; set; }

        public PluginConfiguration()
        {
            AssrtApiToken = string.Empty;
            EnableUseMetadata = true;
        }
    }
}

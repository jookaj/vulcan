﻿namespace TcbInternetSolutions.Vulcan.AttachmentIndexer.Implementation
{
    using EPiServer.ServiceLocation;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Default attachment indexer settings
    /// </summary>
    [ServiceConfiguration(typeof(IVulcanAttachmentIndexerSettings), Lifecycle = ServiceInstanceScope.Singleton)]
    public class VulcanAttachmentIndexerSettings : IVulcanAttachmentIndexerSettings
    {
        private readonly string _supportedExtensionsAsString;
        private IEnumerable<string> _supportedFileExtensions;

        /// <summary>
        /// Constructor
        /// </summary>
        public VulcanAttachmentIndexerSettings()
        {            
            _supportedExtensionsAsString = Core.Internal.AppConfigurationHelper.TryGetValueFromAppKey(key: "VulcanIndexAttachmentFileExtensions");

            EnableAttachmentPlugins =
                Core.Internal.AppConfigurationHelper.TryGetBoolFromKey(key: "VulcanIndexAttachmentPluginsEnabled", defaultValue: true);

            EnableFileSizeLimit =
                Core.Internal.AppConfigurationHelper.TryGetBoolFromKey(key: "VulcanIndexAttachmentFileLimitEnabled", defaultValue: false);
        }

        /// <summary>
        /// Determines if Elasticsearch has plugins to handle base64 data
        /// </summary>
        public virtual bool EnableAttachmentPlugins { get; }

        /// <summary>
        /// Determines if file size are considered for indexing.
        /// </summary>
        public virtual bool EnableFileSizeLimit { get; }

        /// <summary>
        /// Default max file size limit is 15 MB
        /// </summary>
        public virtual long FileSizeLimit => 15000000;

        /// <summary>
        /// Supported file extensions, by default is pdf,doc,docx,xls,xlsx,ppt,pptx,txt,rtf
        /// </summary>
        public virtual IEnumerable<string> SupportedFileExtensions
        {
            get
            {
                if (_supportedFileExtensions != null) return _supportedFileExtensions;

                var allowedExtensions = _supportedExtensionsAsString;

                _supportedFileExtensions = string.IsNullOrWhiteSpace(allowedExtensions) ?
                    new[] { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf" } :
                    allowedExtensions.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);

                return _supportedFileExtensions;
            }
        }

        
    }
}

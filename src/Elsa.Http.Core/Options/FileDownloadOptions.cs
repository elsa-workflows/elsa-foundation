using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;

namespace Elsa.Http.Core.Options
{
    /// <summary>
    /// Options for downloading a file.
    /// </summary>
    public sealed class FileDownloadOptions
    {
        /// <summary>
        /// Gets or sets the entity tag.
        /// </summary>
        public EntityTagHeaderValue? ETag { get; set; }

        /// <summary>
        /// Gets or sets the range.
        /// </summary>
        public RangeHeaderValue? Range { get; set; }
    }
}

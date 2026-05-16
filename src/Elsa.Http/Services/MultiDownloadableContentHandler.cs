using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Http.Services
{
    /// <summary>
    /// Handles content that represents a list of downloadable objects.
    /// </summary>
    internal sealed class MultiDownloadableContentHandler(IServiceProvider serviceProvider) : IDownloadableContentHandler
    {
        public float Priority => throw new NotImplementedException();

        /// <inheritdoc />
        public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
        {
            var collectedDownloadables = new List<Func<ValueTask<Downloadable>>>();
            var enumerable = (IEnumerable)content;

            var allHandlers = serviceProvider.GetServices<IDownloadableContentHandler>();
            foreach (var item in enumerable)
            {
                var handler = allHandlers.FirstOrDefault(x => x.SupportsContent(item))
                    ?? throw new InvalidOperationException($"Could not find downloadable content handler for item type '{item.GetType()}'");

                var downloadables = handler.GetDownloadablesAsync(item, cancellationToken);

                collectedDownloadables.AddRange(downloadables);
            }

            return collectedDownloadables;
        }

        /// <inheritdoc />
        public bool SupportsContent(object content) => content is IEnumerable and not string and not byte[];    
    }
}

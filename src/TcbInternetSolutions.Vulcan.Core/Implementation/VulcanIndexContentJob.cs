﻿using EPiServer.Core;
using EPiServer.Logging;
using EPiServer.PlugIn;
using EPiServer.Scheduler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TcbInternetSolutions.Vulcan.Core.Internal;

namespace TcbInternetSolutions.Vulcan.Core.Implementation
{
    /// <summary>
    /// Default index job
    /// </summary>
    [ScheduledPlugIn(DisplayName = "Vulcan Index Content")]
    public class VulcanIndexContentJob : ScheduledJobBase
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly IEnumerable<IVulcanFeature> _vulcanFeatures;
        private readonly IVulcanHandler _vulcanHandler;
        private readonly IVulcanIndexContentJobSettings _vulcanIndexContentJobSettings;
        private readonly IEnumerable<IVulcanIndexer> _vulcanIndexers;
        private readonly IVulcanPocoIndexingJob _vulcanPocoIndexHandler;
        private readonly IVulcanSearchContentLoader _vulcanSearchContentLoader;
        private bool _stopSignaled;

        /// <summary>
        /// DI Constructor
        /// </summary>
        /// <param name="vulcanSearchContentLoader"></param>
        /// <param name="vulcanHandler"></param>
        /// <param name="vulcanPocoIndexingJob"></param>
        /// <param name="vulcanIndexers"></param>
        /// <param name="vulcanIndexContentJobSettings"></param>
        /// <param name="vulcanFeatures"></param>
        public VulcanIndexContentJob
        (
            IVulcanSearchContentLoader vulcanSearchContentLoader,
            IVulcanHandler vulcanHandler,
            IVulcanPocoIndexingJob vulcanPocoIndexingJob,
            IVulcanIndexContentJobSettings vulcanIndexContentJobSettings,
            IEnumerable<IVulcanIndexer> vulcanIndexers,
            IEnumerable<IVulcanFeature> vulcanFeatures
        )
        {
            _vulcanSearchContentLoader = vulcanSearchContentLoader;
            _vulcanHandler = vulcanHandler;
            _vulcanPocoIndexHandler = vulcanPocoIndexingJob;
            _vulcanIndexers = vulcanIndexers;
            _vulcanIndexContentJobSettings = vulcanIndexContentJobSettings;
            _vulcanFeatures = vulcanFeatures;
            IsStoppable = true;
        }

        /// <summary>
        /// Execute index job
        /// </summary>
        /// <returns></returns>
        public override string Execute()
        {
            OnStatusChanged($"Starting execution of {GetType()}");
            _vulcanHandler.DeleteIndex(); // delete all language indexes
            var totalIndexedCount = 0;
            var isCacheScopeFeature = _vulcanFeatures?.LastOrDefault(x => x is IVulcanFeatureCacheScope) as IVulcanFeatureCacheScope;

            if (_vulcanIndexContentJobSettings.EnableParallelIndexers)
            {
                Parallel.ForEach(EnumerateIndexers(), new ParallelOptions() { MaxDegreeOfParallelism = _vulcanIndexContentJobSettings.ParallelDegree }, indexer => {
                    ExecuteIndexer(indexer, isCacheScopeFeature, ref totalIndexedCount);
                });
            }
            else
            {
                foreach (var indexer in EnumerateIndexers())
                {
                    ExecuteIndexer(indexer, isCacheScopeFeature, ref totalIndexedCount);
                }
            }

            return $"Vulcan successfully indexed {totalIndexedCount} item(s) across {_vulcanIndexers.Count()} indexers!";
        }

        private void ExecuteIndexer(IVulcanIndexer indexer, IVulcanFeatureCacheScope isCacheScopeFeature, ref int totalIndexedCount)
        {
            var pocoIndexer = indexer as IVulcanPocoIndexer;
            var cmsIndexer = indexer as IVulcanContentIndexer;

            if (pocoIndexer?.IncludeInDefaultIndexJob == true)
            {
                _vulcanPocoIndexHandler.Index(pocoIndexer, OnStatusChanged, ref totalIndexedCount, ref _stopSignaled);
            }
            else if (cmsIndexer != null) // default episerver content
            {
                var contentReferences = _vulcanSearchContentLoader.GetSearchContentReferences(cmsIndexer).ToList();

                var contentRecord = 0;
                var totalCount = contentReferences.Count;

                if (_vulcanIndexContentJobSettings.EnableParallelContent)
                {
                    var thisIndexerCount = 0;

                    Parallel.ForEach(contentReferences, new ParallelOptions() { MaxDegreeOfParallelism = _vulcanIndexContentJobSettings.ParallelDegree }, contentReference =>
                    {
                        if (!_stopSignaled)
                        {
                            if (IndexContent(contentReference, contentRecord, cmsIndexer, isCacheScopeFeature, totalCount))
                            {
                                Interlocked.Increment(ref thisIndexerCount);
                            }

                            Interlocked.Increment(ref contentRecord);
                        }
                    });

                    Interlocked.Exchange(ref totalIndexedCount, totalIndexedCount + thisIndexerCount);
                }
                else
                {
                    foreach (var contentReference in EnumerateContent(contentReferences))
                    {

                        if (IndexContent(contentReference, contentRecord, cmsIndexer, isCacheScopeFeature, totalCount))
                        {
                            totalIndexedCount++;
                        }

                        contentRecord++;
                    }
                }
            }
        }

        private bool IndexContent(ContentReference contentReference, int contentRecord, IVulcanContentIndexer cmsIndexer, IVulcanFeatureCacheScope isCacheScopeFeature, int totalCount)
        {
            if (isCacheScopeFeature?.Enabled != true &&
                                                cmsIndexer is IVulcanContentIndexerWithCacheClearing cacheClearingIndexer && cacheClearingIndexer.ClearCacheItemInterval >= 0)
            {
                if (contentRecord % cacheClearingIndexer.ClearCacheItemInterval == 0)
                {
                    cacheClearingIndexer.ClearCache();
                }
            }

            // only update this every 100 records (reduce load on db)
            if (contentRecord % 100 == 0)
            {
                OnStatusChanged($"{cmsIndexer.IndexerName} indexing item {contentRecord + 1} of {totalCount} items of {cmsIndexer.GetRoot().Value} content");
            }

            IContent content = null;

            try
            {
                content = LoadWithCacheScope(contentReference, isCacheScopeFeature);
            }
            catch (OutOfMemoryException)
            {
                Logger.Warning($"Vulcan encountered an OutOfMemory exception, attempting again to index content item {contentReference}...");

                // try once more
                try
                {
                    content = LoadWithCacheScope(contentReference, isCacheScopeFeature);
                }
                catch (Exception eNested)
                {
                    Logger.Error($"Vulcan could not recover from an out of memory exception when it tried again to index content item  {contentReference} : {eNested}");
                }

                return false;
            }
            catch (Exception eOther)
            {
                Logger.Error($"Vulcan could not index content item {contentReference} : {eOther}");

                return false;
            }

            if (content == null)
            {
                Logger.Error($"Vulcan could not index content item {contentReference}: content was null");

                return false;
            }
            else
            {
                Logger.Information($"Vulcan indexed content with reference: {contentReference} and name: {content.Name}");
                _vulcanHandler.IndexContentEveryLanguage(content);

                return true;
            }
        }

        /// <summary>
        /// Signal stop
        /// </summary>
        public override void Stop()
        {
            _stopSignaled = true;
        }

        private IEnumerable<ContentReference> EnumerateContent(IEnumerable<ContentReference> contentReferences) =>
            _vulcanIndexContentJobSettings.EnableParallelContent ? contentReferences.AsParallel() : contentReferences;

        private IEnumerable<IVulcanIndexer> EnumerateIndexers() =>
            _vulcanIndexContentJobSettings.EnableParallelIndexers ? _vulcanIndexers.AsParallel() : _vulcanIndexers;

        private IContent LoadWithCacheScope(ContentReference c, IVulcanFeatureCacheScope vulcanFeatureCache)
        {
            if (vulcanFeatureCache?.Enabled != true) return _vulcanSearchContentLoader.GetContent(c);

            using (new ContentCacheScope { SlidingExpiration = vulcanFeatureCache.CacheDuration })
            {
                return _vulcanSearchContentLoader.GetContent(c);
            }
        }
    }
}
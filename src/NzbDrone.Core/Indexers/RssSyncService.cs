﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers
{
    public class RssSyncService : IExecute<RssSyncCommand>
    {
        private readonly IFetchAndParseRss _rssFetcherAndParser;
        private readonly IMakeDownloadDecision _downloadDecisionMaker;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IEpisodeSearchService _episodeSearchService;
        private readonly IPendingReleaseService _pendingReleaseService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public RssSyncService(IFetchAndParseRss rssFetcherAndParser,
                              IMakeDownloadDecision downloadDecisionMaker,
                              IProcessDownloadDecisions processDownloadDecisions,
                              IEpisodeSearchService episodeSearchService,
                              IPendingReleaseService pendingReleaseService,
                              IEventAggregator eventAggregator,
                              Logger logger)
        {
            _rssFetcherAndParser = rssFetcherAndParser;
            _downloadDecisionMaker = downloadDecisionMaker;
            _processDownloadDecisions = processDownloadDecisions;
            _episodeSearchService = episodeSearchService;
            _pendingReleaseService = pendingReleaseService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }


        private ProcessedDecisions Sync()
        {
            _logger.ProgressInfo("Starting RSS Sync");

            var reports = _rssFetcherAndParser.Fetch().Concat(_pendingReleaseService.GetPending()).ToList();
            var decisions = _downloadDecisionMaker.GetRssDecision(reports);
            var processed = _processDownloadDecisions.ProcessDecisions(decisions);

            var message = String.Format("RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}", reports.Count, processed.Grabbed.Count);

            if (processed.Pending.Any())
            {
                message += ", Reports pending: " + processed.Pending.Count;
            }

            _logger.ProgressInfo(message);

            return processed;
        }

        public void Execute(RssSyncCommand message)
        {
            var processed = Sync();
            var grabbedOrPending = processed.Grabbed.Concat(processed.Pending).ToList();

            if (message.LastExecutionTime.HasValue && DateTime.UtcNow.Subtract(message.LastExecutionTime.Value).TotalHours > 3)
            {
                _logger.Info("RSS Sync hasn't run since: {0}. Searching for any missing episodes since then.", message.LastExecutionTime.Value);
                _episodeSearchService.MissingEpisodesAiredAfter(message.LastExecutionTime.Value.AddDays(-1), grabbedOrPending.SelectMany(d => d.RemoteEpisode.Episodes).Select(e => e.Id));
            }

            _eventAggregator.PublishEvent(new RssSyncCompleteEvent(processed));
        }
    }
}

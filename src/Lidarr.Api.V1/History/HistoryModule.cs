using System;
using System.Collections.Generic;
using System.Linq;
using Nancy;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using Lidarr.Api.V1.Albums;
using Lidarr.Api.V1.Artist;
using Lidarr.Api.V1.Tracks;
using Lidarr.Http;
using Lidarr.Http.Extensions;
using Lidarr.Http.REST;

namespace Lidarr.Api.V1.History
{
    public class HistoryModule : LidarrRestModule<HistoryResource>
    {
        private readonly IHistoryService _historyService;
        private readonly IUpgradableSpecification _upgradableSpecification;
        private readonly IFailedDownloadService _failedDownloadService;

        public HistoryModule(IHistoryService historyService,
                             IUpgradableSpecification upgradableSpecification,
                             IFailedDownloadService failedDownloadService)
        {
            _historyService = historyService;
            _upgradableSpecification = upgradableSpecification;
            _failedDownloadService = failedDownloadService;
            GetResourcePaged = GetHistory;

            Get["/since"] = x => GetHistorySince();
            Post["/failed"] = x => MarkAsFailed();
        }

        protected HistoryResource MapToResource(NzbDrone.Core.History.History model, bool includeArtist, bool includeAlbum, bool includeTrack)
        {
            var resource = model.ToResource();

            if (includeArtist)
            {
                resource.Artist = model.Artist.ToResource();
            }
            if (includeAlbum)
            {
                resource.Album = model.Album.ToResource();
            }
            if (includeTrack)
            {
                resource.Track = model.Track.ToResource();
            }
            

            if (model.Artist != null)
            {
                resource.QualityCutoffNotMet = _upgradableSpecification.CutoffNotMet(model.Artist.Profile.Value,
                                                                                     model.Artist.LanguageProfile,
                                                                                     model.Quality,
                                                                                     model.Language);
            }

            return resource;
        }

        private PagingResource<HistoryResource> GetHistory(PagingResource<HistoryResource> pagingResource)
        {
            var pagingSpec = pagingResource.MapToPagingSpec<HistoryResource, NzbDrone.Core.History.History>("date", SortDirection.Descending);
            var includeArtist = Request.GetBooleanQueryParameter("includeArtist");
            var includeAlbum = Request.GetBooleanQueryParameter("includeAlbum");
            var includeTrack = Request.GetBooleanQueryParameter("includeTrack");

            if (pagingResource.FilterKey == "eventType")
            {
                var filterValue = (HistoryEventType)Convert.ToInt32(pagingResource.FilterValue);
                pagingSpec.FilterExpression = v => v.EventType == filterValue;
            }

            if (pagingResource.FilterKey == "albumId")
            {
                int albumId = Convert.ToInt32(pagingResource.FilterValue);
                pagingSpec.FilterExpression = h => h.AlbumId == albumId;
            }

            return ApplyToPage(_historyService.Paged, pagingSpec, h => MapToResource(h, includeArtist, includeAlbum, includeTrack));
        }

        private List<HistoryResource> GetHistorySince()
        {
            var queryDate = Request.Query.Date;
            var queryEventType = Request.Query.EventType;

            if (!queryDate.HasValue)
            {
                throw new BadRequestException("date is missing");
            }

            DateTime date = DateTime.Parse(queryDate.Value);
            HistoryEventType? eventType = null;
            var includeArtist = Request.GetBooleanQueryParameter("includeArtist");
            var includeAlbum = Request.GetBooleanQueryParameter("includeAlbum");
            var includeTrack = Request.GetBooleanQueryParameter("includeTrack");

            if (queryEventType.HasValue)
            {
                eventType = (HistoryEventType)Convert.ToInt32(queryEventType.Value);
            }

            return _historyService.Since(date, eventType).Select(h => MapToResource(h, includeArtist, includeAlbum, includeTrack)).ToList();
        }

        private Response MarkAsFailed()
        {
            var id = (int)Request.Form.Id;
            _failedDownloadService.MarkAsFailed(id);
            return new object().AsResponse();
        }
    }
}

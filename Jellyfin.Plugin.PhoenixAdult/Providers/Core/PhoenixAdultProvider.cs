using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Plugin.PhoenixAdult.Providers.Helpers;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PhoenixAdult
{
    public class PhoenixAdultProvider : IRemoteMetadataProvider<Movie, MovieInfo>
    {
        public string Name => "PhoenixAdult";

        public static string PluginName;
        public static ILogger Log;
        public static IHttpClient Http;

        public PhoenixAdultProvider(ILoggerFactory log, IHttpClient http)
        {
            PluginName = Name;
            if (log != null)
                Log = log.CreateLogger(Name);
            Http = http;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            List<RemoteSearchResult> result = new List<RemoteSearchResult>();

            if (searchInfo == null)
                return result;

            var site = GetSiteFromTitle(searchInfo.Name);
            if (site.Key[0] > -1)
            {
                string searchTitle = GetClearTitle(searchInfo.Name, site.Value),
                       encodedTitle,
                       searchDate;
                var titleAfterDate = GetDateFromTitle(searchTitle);

                var siteNum = new int[2] {
                    site.Key[0],
                    site.Key[1]
                };
                searchTitle = titleAfterDate[0];
                searchDate = titleAfterDate[1];
                encodedTitle = HttpUtility.UrlEncode(searchTitle);

                Log.LogInformation($"siteNum: {siteNum[0]}${siteNum[1]}");
                Log.LogInformation($"searchTitle: {searchTitle}");
                Log.LogInformation($"encodedTitle: {encodedTitle}");
                Log.LogInformation($"searchDate: {searchDate}");

                var provider = PhoenixAdultList.GetProviderBySiteID(siteNum[0]);
                if (provider != null)
                {
                    result = await provider.Search(siteNum, searchTitle, encodedTitle, searchDate, cancellationToken).ConfigureAwait(false);
                    if (result.Count > 0)
                        if (DateTime.TryParse(searchDate, out DateTime searchDateObj) && result.Any(scene => (DateTime)scene.PremiereDate != null))
                            result = result.OrderByDescending(scene => DateTime.Compare(searchDateObj, (DateTime)scene.PremiereDate)).ToList();
                        else
                            result = result.OrderByDescending(scene => 100 - PhoenixAdultHelper.LevenshteinDistance(searchTitle, scene.Name)).ToList();
                }
            }

            return result;
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Movie>
            {
                HasMetadata = false,
                Item = new Movie()
            };

            if (info == null)
                return result;

            var sceneID = info.ProviderIds;
            if (!sceneID.ContainsKey(Name))
            {
                var searchResults = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
                if (searchResults.Any())
                    sceneID = searchResults.First().ProviderIds;
            }

            string[] curID = sceneID.GetValueOrDefault(Name, "-1$-1").Split('$');
            Log.LogInformation(curID[2]);
            if (curID.Length < 2)
                return result;

            var provider = PhoenixAdultList.GetProviderBySiteID(int.Parse(curID[0], PhoenixAdultHelper.Lang));
            if (provider != null)
            {
                result = await provider.Update(curID, cancellationToken).ConfigureAwait(false);
                result.HasMetadata = true;
                result.Item.OfficialRating = "XXX";
                result.Item.ProviderIds = sceneID;

                result.People = PhoenixAdultPeoples.Cleanup(result);
                result.Item.Genres = PhoenixAdultGenres.Cleanup(result.Item.Genres);
            }

            return result;
        }

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
            => PhoenixAdultHelper.GetImageResponse(url, cancellationToken);

        public static KeyValuePair<int[], string> GetSiteFromTitle(string title)
        {
            string clearName = Regex.Replace(title, @"\W", "");
            var possibleSites = new Dictionary<int[], string>();

            foreach (var site in PhoenixAdultList.SiteList)
                foreach (var siteData in site.Value)
                {
                    string clearSite = Regex.Replace(siteData.Value[0], @"\W", "");
                    if (clearName.StartsWith(clearSite, StringComparison.CurrentCultureIgnoreCase))
                        possibleSites.Add(new int[] { site.Key, siteData.Key }, clearSite);
                }

            if (possibleSites.Count > 0)
                return possibleSites.OrderByDescending(x => x.Value.Length).First();

            return new KeyValuePair<int[], string>(new int[] { -1, -1 }, "");
        }

        public static string GetClearTitle(string title, string siteName)
        {
            string clearName = PhoenixAdultHelper.Lang.TextInfo.ToTitleCase(Regex.Replace(title, @"(\d+)", @"|$1")),
                   clearSite = Regex.Replace(siteName, @"(\d+)", @"|$1"),
                   searchTitle = title;

            clearName = Regex.Replace(clearName, @"(?!\|)\W", "");
            clearSite = Regex.Replace(clearSite, @"(?!\|)\W", "");

            if (clearName.StartsWith(clearSite, StringComparison.CurrentCultureIgnoreCase))
            {
                searchTitle = Regex.Replace(clearName, clearSite, "", RegexOptions.IgnoreCase);
                searchTitle = Regex.Replace(searchTitle, @"(\w)([A-Z])", @"$1 $2");
                searchTitle = Regex.Replace(searchTitle, @"([A-Z])([A-Z])", @"$1 $2");
                searchTitle = Regex.Replace(searchTitle, @"(\d+)", @" $1");
                searchTitle = searchTitle.Replace("|", "", StringComparison.CurrentCultureIgnoreCase).Trim();
            }

            return searchTitle;
        }

        public static string[] GetDateFromTitle(string title)
        {
            string searchDate,
                   searchTitle = title,
                   regExRule = @"\b\d{4} \d{2} \d{2}\b";
            string[] searchData = new string[2] { searchTitle, "" };

            var regEx = Regex.Match(searchTitle, regExRule);
            if (regEx.Groups.Count > 0)
                if (DateTime.TryParse(regEx.Groups[0].Value, out DateTime date))
                {
                    searchDate = date.ToString("yyyy-MM-dd", PhoenixAdultHelper.Lang);
                    searchTitle = Regex.Replace(searchTitle, regExRule, "").Trim();

                    searchData = new string[2] { searchTitle, searchDate };
                }

            return searchData;
        }
    }

    public class PhoenixAdultImageProvider : IRemoteImageProvider
    {
        public string Name => PhoenixAdultProvider.PluginName;

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            if (item == null)
                return images;

            var sceneID = item.ProviderIds;
            string[] curID = sceneID.GetValueOrDefault(Name, "-1$-1").Split('$');
            if (curID.Length > 2)
            {
                var provider = PhoenixAdultList.GetProviderBySiteID(int.Parse(curID[0], PhoenixAdultHelper.Lang));
                if (provider != null)
                    images = (List<RemoteImageInfo>)await provider.GetImages(item, cancellationToken).ConfigureAwait(false);
            }

            return images;
        }

        public bool Supports(BaseItem item) => item is Movie;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => PhoenixAdultHelper.GetImageResponse(url, cancellationToken);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType> {
                    ImageType.Primary,
                    ImageType.Backdrop
            };
    }
}

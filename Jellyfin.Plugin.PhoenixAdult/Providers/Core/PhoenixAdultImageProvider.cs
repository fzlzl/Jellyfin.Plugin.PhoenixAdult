using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Providers;

#if __EMBY__

using MediaBrowser.Model.Configuration;

#else
#endif

namespace PhoenixAdult
{
    public class PhoenixAdultImageProvider : IRemoteImageProvider
    {
        public string Name => Plugin.Instance.Name;

#if __EMBY__

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
#else
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
#endif
        {
            var images = new List<RemoteImageInfo>();

            if (item == null)
                return images;

            if (!item.ProviderIds.TryGetValue(Name, out string externalID))
                return images;

            var curID = externalID.Split('#');
            if (curID.Length < 3)
                return images;

            var provider = PhoenixAdultList.GetProviderBySiteID(int.Parse(curID[0], PhoenixAdultProvider.Lang));
            if (provider != null)
            {
                images = (List<RemoteImageInfo>)await provider.GetImages(item, cancellationToken).ConfigureAwait(false);

                var clearImages = new List<RemoteImageInfo>();
                foreach (var image in images)
                {
                    if (!clearImages.Where(o => o.Url == image.Url && o.Type == image.Type).Any())
                    {
                        var imageDubl = clearImages.Where(o => o.Url == image.Url && o.Type != image.Type);
                        if (imageDubl.Any())
                        {
                            var t = imageDubl.First();
                            var img = new RemoteImageInfo
                            {
                                Url = t.Url,
                                ProviderName = t.ProviderName
                            };

                            if (Plugin.Instance.Configuration.ProvideImageSize)
                            {
                                image.Height = t.Height;
                                image.Width = t.Width;
                            }

                            if (t.Type == ImageType.Backdrop)
                                img.Type = ImageType.Primary;
                            else
                            {
                                if (t.Type == ImageType.Primary)
                                    img.Type = ImageType.Backdrop;
                            }

                            clearImages.Add(img);
                        }
                        else
                        {
                            var img = await ImageHelper.GetImageSizeAndValidate(image, cancellationToken).ConfigureAwait(false);
                            if (img != null)
                            {
                                image.ProviderName = Name;
                                if (Plugin.Instance.Configuration.ProvideImageSize)
                                {
                                    image.Height = img.Height;
                                    image.Width = img.Width;
                                }

                                clearImages.Add(image);
                            }
                        }
                    }

                    images = clearImages;
                }
            }

            return images;
        }

        public bool Supports(BaseItem item) => item is Movie;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken) => PhoenixAdultProvider.Http.GetResponse(new HttpRequestOptions
        {
            CancellationToken = cancellationToken,
            Url = url
        });

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new List<ImageType> {
                    ImageType.Primary,
                    ImageType.Backdrop
            };
    }
}

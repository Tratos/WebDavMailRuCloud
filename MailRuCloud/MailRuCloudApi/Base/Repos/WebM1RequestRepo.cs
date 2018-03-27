﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using YaR.MailRuCloud.Api.Base.Auth;
using YaR.MailRuCloud.Api.Base.Requests;
using YaR.MailRuCloud.Api.Base.Requests.Types;
using YaR.MailRuCloud.Api.Base.Requests.WebM1;
using YaR.MailRuCloud.Api.Base.Streams;
using YaR.MailRuCloud.Api.Common;
using YaR.MailRuCloud.Api.Extensions;
using YaR.MailRuCloud.Api.Links;

namespace YaR.MailRuCloud.Api.Base.Repos
{
    class WebM1RequestRepo : IRequestRepo
    {
        private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(typeof(WebV2RequestRepo));
        private readonly ShardManager _shardManager;
		private readonly int _listDepth;

		public HttpCommonSettings HttpSettings { get; } = new HttpCommonSettings
        {
            ClientId = "cloud-win",
            UserAgent = "CloudDiskOWindows 17.12.0009 beta WzBbt1Ygbm"
        };

        public WebM1RequestRepo(IWebProxy proxy, IBasicCredentials creds, AuthCodeRequiredDelegate onAuthCodeRequired, int listDepth)
        {
	        _listDepth = listDepth;

			ServicePointManager.DefaultConnectionLimit = int.MaxValue;

            HttpSettings.Proxy = proxy;
            Authent = new OAuth(HttpSettings, creds, onAuthCodeRequired);
            _shardManager = new ShardManager(HttpSettings, Authent);
        }

        public IAuth Authent { get; }

        public Stream GetDownloadStream(File afile, long? start = null, long? end = null)
        {
            var istream = GetDownloadStreamInternal(afile, start, end);
            return istream;
        }

        private DownloadStream GetDownloadStreamInternal(File afile, long? start = null, long? end = null)
        {
            bool isLinked = !string.IsNullOrEmpty(afile.PublicLink);

            Cached<Requests.WebBin.ServerRequest.Result> downServer = null;
            var pendingServers = isLinked
                ? _shardManager.WeblinkDownloadServersPending
                : _shardManager.DownloadServersPending;
            Stopwatch watch = new Stopwatch();

            HttpWebRequest request = null;
            CustomDisposable<HttpWebResponse> ResponseGenerator(long instart, long inend, File file)
            {
                var resp = Retry.Do(() =>
                {
                    downServer = pendingServers.Next(downServer);

                    string url =(isLinked
                            ? $"{downServer.Value.Url}{file.PublicLink}"
                            : $"{downServer.Value.Url}{Uri.EscapeDataString(file.FullPath.TrimStart('/'))}") +
                        $"?client_id={HttpSettings.ClientId}&token={Authent.AccessToken}";
                    var uri = new Uri(url);

                    request = (HttpWebRequest) WebRequest.Create(uri.OriginalString);

                    request.AddRange(instart, inend);
                    request.Proxy = HttpSettings.Proxy;
                    request.CookieContainer = Authent.Cookies;
                    request.Method = "GET";
                    request.Accept = "*/*";
                    request.UserAgent = HttpSettings.UserAgent;
                    request.Host = uri.Host;
                    request.AllowWriteStreamBuffering = false;

                    if (isLinked)
                    {
                        request.Headers.Add("Accept-Ranges", "bytes");
                        request.ContentType = MediaTypeNames.Application.Octet;
                        request.Referer = $"{ConstSettings.CloudDomain}/home/{Uri.EscapeDataString(file.Path)}";
                        request.Headers.Add("Origin", ConstSettings.CloudDomain);
                    }

                    request.Timeout = 15 * 1000;
                    request.ReadWriteTimeout = 15 * 1000;

                    watch.Start();
                    var response = (HttpWebResponse)request.GetResponse();
                    return new CustomDisposable<HttpWebResponse>
                    {
                        Value = response,
                        OnDispose = () =>
                        {
                            pendingServers.Free(downServer);
                            watch.Stop();
                            Logger.Debug($"HTTP:{request.Method}:{request.RequestUri.AbsoluteUri} ({watch.Elapsed.Milliseconds} ms)");
                        }
                    };
                },
                exception => 
                    ((exception as WebException)?.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound,
                exception =>
                {
                    pendingServers.Free(downServer);
                    Logger.Warn($"Retrying HTTP:{request.Method}:{request.RequestUri.AbsoluteUri} on exception {exception.Message}");
                },
                TimeSpan.FromSeconds(1), 2);

                return resp;
            }

            var stream = new DownloadStream(ResponseGenerator, afile, start, end);
            return stream;
        }


        public HttpWebRequest UploadRequest(ShardInfo shard, File file, UploadMultipartBoundary boundary)
        {
            var url = new Uri($"{shard.Url}?client_id={HttpSettings.ClientId}&token={Authent.AccessToken}");

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Proxy = HttpSettings.Proxy;
            request.CookieContainer = Authent.Cookies;
            request.Method = "PUT";
            request.ContentLength = file.OriginalSize;
            request.Accept = "*/*";
            request.UserAgent = HttpSettings.UserAgent;
            request.AllowWriteStreamBuffering = false;

            request.Timeout = 15 * 1000;
            request.ReadWriteTimeout = 15 * 1000;
            //request.ServicePoint.ConnectionLimit = int.MaxValue;

            return request;
        }

        /// <summary>
        /// Get shard info that to do post get request. Can be use for anonymous user.
        /// </summary>
        /// <param name="shardType">Shard type as numeric type.</param>
        /// <returns>Shard info.</returns>
        public async Task<ShardInfo> GetShardInfo(ShardType shardType)
        {
            bool refreshed = false;
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(80 * i);
                var ishards = await Task.Run(() => _shardManager.CachedShards.Value);
                var ishard = ishards[shardType];
                var banned = _shardManager.BannedShards.Value;
                if (banned.All(bsh => bsh.Url != ishard.Url))
                {
                    if (refreshed) Authent.ExpireDownloadToken();
                    return ishard;
                }
                _shardManager.CachedShards.Expire();
                refreshed = true;
            }

            Logger.Error("Cannot get working shard.");

            var shards = await Task.Run(() => _shardManager.CachedShards.Value);
            var shard = shards[shardType];
            return shard;
        }

        public async Task<CloneItemResult> CloneItem(string fromUrl, string toPath)
        {
            var req = await new CloneItemRequest(HttpSettings, Authent, fromUrl, toPath).MakeRequestAsync();
            var res = req.ToCloneItemResult();
            return res;
        }

        public async Task<CopyResult> Copy(string sourceFullPath, string destinationPath, ConflictResolver? conflictResolver = null)
        {
            var req = await new CopyRequest(HttpSettings, Authent, sourceFullPath, destinationPath, conflictResolver).MakeRequestAsync();
            var res = req.ToCopyResult();
            return res;
        }

        public async Task<CopyResult> Move(string sourceFullPath, string destinationPath, ConflictResolver? conflictResolver = null)
        {
            //var req = await new MoveRequest(HttpSettings, Authent, sourceFullPath, destinationPath).MakeRequestAsync();
            //var res = req.ToCopyResult();
            //return res;

            var req = await new Requests.WebBin.MoveRequest(HttpSettings, Authent, _shardManager.MetaServer.Url, sourceFullPath, destinationPath)
                .MakeRequestAsync();

            var res = req.ToCopyResult(WebDavPath.Name(destinationPath));
            return res;

        }

        public async Task<IEntry> FolderInfo(string path, Link ulink, int offset = 0, int limit = Int32.MaxValue)
        {
            IEntry entry;
            try
            {
	            entry = ulink != null
		            ? (await new FolderInfoRequest(HttpSettings, Authent, ulink.Href, true, offset, limit).MakeRequestAsync())
						.ToEntry()
		            : (await new Requests.WebBin.ListRequest(HttpSettings, Authent, _shardManager.MetaServer.Url, path, _listDepth).MakeRequestAsync())
						.ToEntry();
            }
            catch (WebException e) when ((e.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return entry;
        }

        public async Task<FolderInfoResult> ItemInfo(string path, bool isWebLink = false, int offset = 0, int limit = Int32.MaxValue)
        {
            var req = await new ItemInfoRequest(HttpSettings, Authent, path, isWebLink, offset, limit).MakeRequestAsync();
            var res = req;
            return res;
        }

        public async Task<AccountInfoResult> AccountInfo()
        {
            var req = await new AccountInfoRequest(HttpSettings, Authent).MakeRequestAsync();
            var res = req.ToAccountInfo();
            return res;
        }

        public async Task<PublishResult> Publish(string fullPath)
        {
            var req = await new PublishRequest(HttpSettings, Authent, fullPath).MakeRequestAsync();
            var res = req.ToPublishResult();
            return res;
        }

        public async Task<UnpublishResult> Unpublish(string publicLink)
        {
            var req = await new UnpublishRequest(HttpSettings, Authent, publicLink).MakeRequestAsync();
            var res = req.ToUnpublishResult();
            return res;
        }

        public async Task<RemoveResult> Remove(string fullPath)
        {
            var req = await new RemoveRequest(HttpSettings, Authent, fullPath).MakeRequestAsync();
            var res = req.ToRemoveResult();
            return res;
        }

        public async Task<RenameResult> Rename(string fullPath, string newName)
        {
            //var req = await new RenameRequest(HttpSettings, Authent, fullPath, newName).MakeRequestAsync();
            //var res = req.ToRenameResult();
            //return res;

            string newFullPath = WebDavPath.Combine(WebDavPath.Parent(fullPath), newName);
            var req = await new Requests.WebBin.MoveRequest(HttpSettings, Authent, _shardManager.MetaServer.Url, fullPath, newFullPath)
                .MakeRequestAsync();

            var res = req.ToRenameResult();
            return res;
        }

        public async Task<CreateFolderResult> CreateFolder(string path)
        {
            //return (await new CreateFolderRequest(HttpSettings, Authent, path).MakeRequestAsync())
            //    .ToCreateFolderResult();

            return (await new Requests.WebBin.CreateFolderRequest(HttpSettings, Authent, _shardManager.MetaServer.Url, path).MakeRequestAsync())
                .ToCreateFolderResult();
        }

        public async Task<AddFileResult> AddFile(string fileFullPath, string fileHash, FileSize fileSize, DateTime dateTime, ConflictResolver? conflictResolver)
        {
            //var res = await new CreateFileRequest(Proxy, Authent, fileFullPath, fileHash, fileSize, conflictResolver)
            //    .MakeRequestAsync();
            //return res.ToAddFileResult();

            //using Mobile request because of supporting file modified time

            //TODO: refact, make mixed repo
            var req = await new Requests.WebBin.MobAddFileRequest(HttpSettings, Authent, _shardManager.MetaServer.Url, fileFullPath, fileHash, fileSize, dateTime, conflictResolver)
                .MakeRequestAsync();

            var res = req.ToAddFileResult();
            return res;
        }
    }
}


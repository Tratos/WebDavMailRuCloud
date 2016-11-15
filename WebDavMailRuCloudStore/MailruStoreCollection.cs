﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using MailRuCloudApi;
using NWebDav.Server;
using NWebDav.Server.Http;
using NWebDav.Server.Locking;
using NWebDav.Server.Logging;
using NWebDav.Server.Props;
using NWebDav.Server.Stores;
using WebDavMailRuCloudStore;

namespace YaR.WebDavMailRu.CloudStore
{
    [DebuggerDisplay("{_directoryInfo.FullPath}\\")]
    public sealed class MailruStoreCollection : IMailruStoreCollection
    {
        private static readonly ILogger Logger = LoggerFactory.CreateLogger(typeof(MailruStoreCollection));
        private readonly Folder _directoryInfo;
        public Folder DirectoryInfo => _directoryInfo;

        public MailruStoreCollection(ILockingManager lockingManager, Folder directoryInfo, bool isWritable)
        {
            LockingManager = lockingManager;
            _directoryInfo = directoryInfo;
            IsWritable = isWritable;
        }

        public static PropertyManager<MailruStoreCollection> DefaultPropertyManager { get; } = new PropertyManager<MailruStoreCollection>(new DavProperty<MailruStoreCollection>[]
        {
            // RFC-2518 properties
            new DavCreationDate<MailruStoreCollection>
            {
                Getter = (context, collection) => collection._directoryInfo.CreationTimeUtc,
                Setter = (context, collection, value) =>
                {
                    collection._directoryInfo.CreationTimeUtc = value;
                    return DavStatusCode.Ok;
                }
            },
            new DavDisplayName<MailruStoreCollection>
            {
                Getter = (context, collection) => 
                collection._directoryInfo.Name
            },
            new DavGetLastModified<MailruStoreCollection>
            {
                Getter = (context, collection) => collection._directoryInfo.LastWriteTimeUtc,
                Setter = (context, collection, value) =>
                {
                    collection._directoryInfo.LastWriteTimeUtc = value;
                    return DavStatusCode.Ok;
                }
            },
            new DavGetResourceType<MailruStoreCollection>
            {
                Getter = (context, collection) => new XElement(WebDavNamespaces.DavNs + "collection")
            },

            // Default locking property handling via the LockingManager
            new DavLockDiscoveryDefault<MailruStoreCollection>(),
            new DavSupportedLockDefault<MailruStoreCollection>(),

            // Hopmann/Lippert collection properties
            new DavExtCollectionChildCount<MailruStoreCollection>
            {
                Getter = (context, collection) =>
                {
                    //collection._directoryInfo.EnumerateFiles().Count() + collection._directoryInfo.EnumerateDirectories().Count()
                    var data = Cloud.Instance.GetItems(collection.DirectoryInfo).Result;
                    int cnt = data.NumberOfItems;
                    return cnt;
                }
                
                
            },
            new DavExtCollectionIsFolder<MailruStoreCollection>
            {
                Getter = (context, collection) => true
            },
            new DavExtCollectionIsHidden<MailruStoreCollection>
            {
                Getter = (context, collection) => false
            },
            new DavExtCollectionIsStructuredDocument<MailruStoreCollection>
            {
                Getter = (context, collection) => false
            },
            new DavExtCollectionHasSubs<MailruStoreCollection>
            {
                Getter = (context, collection) => collection.Folders.Any()
            },
            new DavExtCollectionNoSubs<MailruStoreCollection>
            {
                Getter = (context, collection) => false //TODO: WTF?
            },
            new DavExtCollectionObjectCount<MailruStoreCollection>
            {
                Getter = (context, collection) => collection.Files.Count()
            },
            new DavExtCollectionReserved<MailruStoreCollection>
            {
                Getter = (context, collection) => !collection.IsWritable
            },
            new DavExtCollectionVisibleCount<MailruStoreCollection>
            {
                Getter = (context, collection) => collection.Items.Count
            },

            // Win32 extensions
            new Win32CreationTime<MailruStoreCollection>
            {
                Getter = (context, collection) => collection._directoryInfo.CreationTimeUtc,
                Setter = (context, collection, value) =>
                {
                    collection.DirectoryInfo.CreationTimeUtc = value;
                    return DavStatusCode.Ok;
                }
            },
            new Win32LastAccessTime<MailruStoreCollection>
            {
                Getter = (context, collection) => collection.DirectoryInfo.LastAccessTimeUtc,
                Setter = (context, collection, value) =>
                {
                    collection._directoryInfo.LastAccessTimeUtc = value;
                    return DavStatusCode.Ok;
                }
            },
            new Win32LastModifiedTime<MailruStoreCollection>
            {
                Getter = (context, collection) => collection.DirectoryInfo.LastWriteTimeUtc,
                Setter = (context, collection, value) =>
                {
                    collection.DirectoryInfo.LastWriteTimeUtc = value;
                    return DavStatusCode.Ok;
                }
            },
            new Win32FileAttributes<MailruStoreCollection>
            {
                Getter = (context, collection) =>  collection.DirectoryInfo.Attributes,
                Setter = (context, collection, value) =>
                {
                    collection.DirectoryInfo.Attributes = value;
                    return DavStatusCode.Ok;
                }
            }
        });

        public bool IsWritable { get; }
        public string Name => DirectoryInfo.Name;
        public string UniqueKey => DirectoryInfo.FullPath;
        public string FullPath => DirectoryInfo.FullPath;

        public IPropertyManager PropertyManager => DefaultPropertyManager;
        public ILockingManager LockingManager { get; }


        public IList<IStoreItem> Items
        {
            get
            {
                if (null == _items)
                {
                    lock (_itemsLocker)
                    {
                        if (null == _items)
                        {
                            _items = GetItemsAsync(null).Result;
                        }
                    }
                }
                return _items;
            }
        }

        private IList<IStoreItem> _items;
        private readonly object _itemsLocker = new object();

        public IEnumerable<MailruStoreCollection> Folders => Items.Where(it => it is MailruStoreCollection).Cast<MailruStoreCollection>();
        public IEnumerable<MailruStoreItem> Files => Items.Where(it => it is MailruStoreItem).Cast<MailruStoreItem>();


        public Task<IStoreItem> GetItemAsync(string name, IHttpContext httpContext)
        {
            var res = name == string.Empty 
                ? this
                : Items.FirstOrDefault(i => i.Name == name);

            return Task.FromResult(res);
        }

        public Task<IList<IStoreItem>> GetItemsAsync(IHttpContext httpContext)
        {
            var item = Cloud.Instance.GetItems(_directoryInfo).Result;

            var items = item.Folders.Select(subDirectory => new MailruStoreCollection(LockingManager, subDirectory, IsWritable)).Cast<IStoreItem>().ToList();

            items.AddRange(item.Files.Select(file => new MailruStoreItem(LockingManager, file, IsWritable)));

            return Task.FromResult<IList<IStoreItem>>(items);
        }

        public Task<StoreItemResult> CreateItemAsync(string name, bool overwrite, IHttpContext httpContext)
        {
            if (!IsWritable)
                return Task.FromResult(new StoreItemResult(DavStatusCode.PreconditionFailed));

            var destinationPath = FullPath + "/" + name;

            DavStatusCode result = DavStatusCode.Created;

            var size = httpContext.Request.ContentLength();
            var f = new MailRuCloudApi.File(destinationPath, size, FileType.SingleFile, null);

            return Task.FromResult(new StoreItemResult(result, new MailruStoreItem(LockingManager, f, IsWritable)));
        }

        public Task<StoreCollectionResult> CreateCollectionAsync(string name, bool overwrite, IHttpContext httpContext)
        {
            // Return error
            if (!IsWritable)
                return Task.FromResult(new StoreCollectionResult(DavStatusCode.PreconditionFailed));

            // Determine the destination path
            var destinationPath = Path.Combine(FullPath, name).Replace("\\", "/");

            DavStatusCode result;


            if (FindSubItem(name) != null)
            {
                if (!overwrite)
                    return Task.FromResult(new StoreCollectionResult(DavStatusCode.PreconditionFailed));

                result = DavStatusCode.NoContent;
            }
            else
                result = DavStatusCode.Created;

            try
            {
                // Attempt to create the directory
                //Directory.CreateDirectory(destinationPath);
                Cloud.Instance.CreateFolder(name, FullPath).Wait();
            }
            catch (Exception exc)
            {
                // Log exception
                Logger.Log(LogLevel.Error, () => $"Unable to create '{destinationPath}' directory.", exc);
                return null;
            }

            return Task.FromResult(new StoreCollectionResult(result, new MailruStoreCollection(LockingManager, new Folder {FullPath = destinationPath }, IsWritable)));
        }

        //public Task<Stream> GetReadableStreamAsync(IHttpContext httpContext)
        //{
        //    return new Task<Stream>(null);
        //}

        public Task<Stream> GetReadableStreamAsync(IHttpContext httpContext) => Task.FromResult((Stream)null);

        public Task<DavStatusCode> UploadFromStreamAsync(IHttpContext httpContext, Stream source)
        {
            throw new NotImplementedException();
        }

        public async Task<StoreItemResult> CopyAsync(IStoreCollection destinationCollection, string name, bool overwrite, IHttpContext httpContext)
        {
            // Just create the folder itself
            var result = await destinationCollection.CreateCollectionAsync(name, overwrite, httpContext);
            return new StoreItemResult(result.Result, result.Collection);
        }

        public async Task<StoreItemResult> MoveItemAsync(string sourceName, IStoreCollection destinationCollection, string destinationName, bool overwrite, IHttpContext httpContext)
        {
            // Return error
            if (!IsWritable)
                return new StoreItemResult(DavStatusCode.PreconditionFailed);

            // Determine the object that is being moved
            var item = await GetItemAsync(sourceName, httpContext);
            if (item == null)
                return new StoreItemResult(DavStatusCode.NotFound);

            // Check if the item is actually a file
            var storeItem = item as MailruStoreItem;
            if (storeItem != null)
            {
                // If the destination collection is a directory too, then we can simply move the file
                var destinationStoreCollection = destinationCollection as MailruStoreCollection;
                if (destinationStoreCollection != null)
                {
                    if (!destinationStoreCollection.IsWritable)
                        return new StoreItemResult(DavStatusCode.PreconditionFailed);

                    // Check if the file already exists
                    DavStatusCode result;
                    var itemexist = FindSubItem(destinationName);
                    if (itemexist != null)
                    {
                        if (!overwrite)
                            return new StoreItemResult(DavStatusCode.Forbidden);

                        await Cloud.Instance.Remove(itemexist);

                        result = DavStatusCode.NoContent;
                    }
                    else
                        result = DavStatusCode.Created;

                    // Move the file
                    await Cloud.Instance.Rename(FindSubItem(sourceName), destinationName);
                    
                    //return new StoreItemResult(result, new MailruStoreItem(LockingManager, new FileInfo(destinationPath), IsWritable));
                    return new StoreItemResult(result, new MailruStoreItem(LockingManager, null, IsWritable));
                }
                else
                {
                    // Attempt to copy the item to the destination collection
                    var result = await item.CopyAsync(destinationCollection, destinationName, overwrite, httpContext);
                    if (result.Result == DavStatusCode.Created || result.Result == DavStatusCode.NoContent)
                        await DeleteItemAsync(sourceName, httpContext);

                    // Return the result
                    return result;
                }
            }

            // If it's not a plain item, then it's a collection
            Debug.Assert(item is MailruStoreCollection);

            // Collections will never be moved, but always be created
            // (we always move the individual items to make sure locking is checked properly)
            throw new InvalidOperationException("Collections should never be moved directly.");
        }

        private IStoreItem FindSubItem(string name)
        {
            return string.IsNullOrEmpty(name) ? this : Items.FirstOrDefault(it => it.Name == name);
        }

        public Task<DavStatusCode> DeleteItemAsync(string name, IHttpContext httpContext)
        {
            if (!IsWritable)
                return Task.FromResult(DavStatusCode.PreconditionFailed);


            // Determine the full path
            var fullPath = Path.Combine(_directoryInfo.FullPath, name).Replace("\\", "/");
            try
            {
                var item = FindSubItem(name);

                if (null == item) return Task.FromResult(DavStatusCode.NotFound);

                Cloud.Instance.Remove(item).Wait();
                return Task.FromResult(DavStatusCode.Ok);
            }
            catch (Exception exc)
            {
                Logger.Log(LogLevel.Error, () => $"Unable to delete '{fullPath}' directory.", exc);
                return Task.FromResult(DavStatusCode.InternalServerError);
            }
        }

        public InfiniteDepthMode InfiniteDepthMode { get; } = InfiniteDepthMode.Allowed;

        //public bool AllowInfiniteDepthProperties => false;

        public override int GetHashCode()
        {
            return DirectoryInfo.FullPath.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var storeCollection = obj as MailruStoreCollection;
            if (storeCollection == null)
                return false;
            return storeCollection._directoryInfo.FullPath.Equals(_directoryInfo.FullPath, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
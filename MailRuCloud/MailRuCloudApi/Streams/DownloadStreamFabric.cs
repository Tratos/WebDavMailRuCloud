﻿using System.IO;
using YaR.MailRuCloud.Api.Base;
using YaR.MailRuCloud.Api.XTSSharp;
using File = YaR.MailRuCloud.Api.Base.File;

namespace YaR.MailRuCloud.Api.Streams
{
    public class DownloadStreamFabric
    {
        private readonly MailRuCloud _cloud;

        public DownloadStreamFabric(MailRuCloud cloud)
        {
            _cloud = cloud;
        }

        public Stream Create(File file, long? start = null, long? end = null)
        {
            if (file.ServiceInfo.IsCrypted)
                return CreateXTSStream(file, start, end);

            //return new DownloadStream(file, _cloud.CloudApi, start, end);
            var stream = _cloud.Account.RequestRepo.GetDownloadStream(file, start, end);
            return stream;
        }

        private Stream CreateXTSStream(File file, long? start = null, long? end = null)
        {
            var pub = CryptoUtil.GetCryptoPublicInfo(_cloud, file);
            var key = CryptoUtil.GetCryptoKey(_cloud.Account.Credentials.PasswordCrypt, pub.Salt);
            var xts = XtsAes256.Create(key, pub.IV);

            long fileLength = file.OriginalSize;
            long requestedOffset = start ?? 0;
            long requestedEnd = end ?? fileLength;

            long alignedOffset = requestedOffset / XTSSectorSize * XTSSectorSize;
            long alignedEnd = requestedEnd % XTSBlockSize == 0
                ? requestedEnd
                : (requestedEnd / XTSBlockSize + 1) * XTSBlockSize;
            if (alignedEnd == 0) alignedEnd = 16;

            var downStream = _cloud.Account.RequestRepo.GetDownloadStream(file, alignedOffset, alignedEnd);

            ulong startSector = (ulong)alignedOffset / XTSSectorSize;
            int trimStart = (int)(requestedOffset - alignedOffset);
            uint trimEnd = alignedEnd == fileLength
                ? file.ServiceInfo.CryptInfo.AlignBytes
                : (uint)(alignedEnd - requestedEnd);

            var xtsStream = new XTSReadOnlyStream(downStream, xts, XTSSectorSize, startSector, trimStart, trimEnd);

            return xtsStream;
        }

        public const int XTSSectorSize = 512;
        public const long XTSBlockSize = XTSWriteOnlyStream.BlockSize;
    }
}
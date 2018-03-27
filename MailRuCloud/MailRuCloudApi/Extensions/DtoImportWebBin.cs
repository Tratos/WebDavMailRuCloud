﻿using System;
using YaR.MailRuCloud.Api.Base.Requests.Types;
using YaR.MailRuCloud.Api.Base.Requests.WebBin;
using YaR.MailRuCloud.Api.Base.Requests.WebBin.Types;

namespace YaR.MailRuCloud.Api.Extensions
{
    internal static class DtoImportWebBin
    {
        public static CopyResult ToCopyResult(this MoveRequest.Result data, string newName)
        {
            var res = new CopyResult
            {
                IsSuccess = true,
                NewName = newName
            };
            return res;
        }

        public static RenameResult ToRenameResult(this MoveRequest.Result data)
        {
            var res = new RenameResult
            {
                IsSuccess = true
            };
            return res;
        }

        public static AddFileResult ToAddFileResult(this MobAddFileRequest.Result data)
        {
            var res = new AddFileResult
            {
                Success = data.IsSuccess,
                Path = data.Path
            };
            return res;
        }


        public static AuthTokenResult ToAuthTokenResult(this OAuthRefreshRequest.Result data, string refreshToken)
        {
            if (data.error_code > 0)
                throw new Exception($"OAuth: Error Code={data.error_code}, Value={data.error}, Description={data.error_description}");

            var res = new AuthTokenResult
            {
                IsSuccess = true,
                Token = data.access_token,
                ExpiresIn = TimeSpan.FromSeconds(data.expires_in),
                RefreshToken = refreshToken
            };

            return res;
        }

        public static AuthTokenResult ToAuthTokenResult(this OAuthRequest.Result data)
        {
            if (data.error_code > 0 && data.error_code != 15)
                throw new Exception($"OAuth: Error Code={data.error_code}, Value={data.error}, Description={data.error_description}");

            var res = new AuthTokenResult
            {
                IsSuccess = true,
                Token = data.access_token,
                ExpiresIn = TimeSpan.FromSeconds(data.expires_in),
                RefreshToken = data.refresh_token,
                IsSecondStepRequired = data.error_code == 15,
                TsaToken = data.tsa_token
            };

            return res;
        }

        public static CreateFolderResult ToCreateFolderResult(this CreateFolderRequest.Result data)
        {
            var res = new CreateFolderResult
            {
                IsSuccess = data.OperationResult == OperationResult.Ok,
                Path = data.Path
            };
            return res;
        }
    }
}
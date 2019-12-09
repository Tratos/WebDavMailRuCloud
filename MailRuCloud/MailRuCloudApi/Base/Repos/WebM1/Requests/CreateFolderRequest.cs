﻿using System;
using System.Text;
using YaR.MailRuCloud.Api.Base.Requests;
using YaR.MailRuCloud.Api.Base.Requests.Types;

namespace YaR.MailRuCloud.Api.Base.Repos.WebM1.Requests
{
   class CreateFolderRequest : BaseRequestJson<CommonOperationResult<string>>
    {
        private readonly string _fullPath;

        public CreateFolderRequest(HttpCommonSettings settings, IAuth auth, string fullPath) 
            : base(settings, auth)
        {
            _fullPath = fullPath;
        }

        protected override string RelationalUri => $"/api/m1/folder/add?access_token={Auth.AccessToken}";

        protected override byte[] CreateHttpContent()
        {
            var data = $"home={Uri.EscapeDataString(_fullPath)}&conflict=rename";
            return Encoding.UTF8.GetBytes(data);
        }
    }
}

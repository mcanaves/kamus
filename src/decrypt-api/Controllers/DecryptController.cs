﻿using System;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using Kamus.Models;
using k8s;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Rest;
using Kamus.Extensions;
using Kamus.KeyManagement;
using Serilog;

namespace Kamus.Controllers
{

    public class DecryptController : Controller
    {
        private readonly IKubernetes mKubernetes;
        private readonly IKeyManagement mKeyManagement;
        private readonly ILogger mAuditLogger = Log.ForContext<DecryptController>().AsAudit();
        private readonly ILogger mLogger = Log.ForContext<DecryptController>();

        //see: https://github.com/kubernetes/kubernetes/blob/d5803e596fc8aba17aa8c74a96aff9c73bb0f1da/staging/src/k8s.io/apiserver/pkg/authentication/serviceaccount/util.go#L27
        private const string ServiceAccountUsernamePrefix = "system:serviceaccount:";

        public DecryptController(IKubernetes kubernetes, IKeyManagement keyManagement)
        {
            mKubernetes = kubernetes;
            mKeyManagement = keyManagement;
        }

        [HttpPost]
        [Route("api/v1/decrypt")]
        [Authorize(AuthenticationSchemes = "kubernetes")]
        public async Task<ActionResult> Decrypt([FromBody] DecryptRequest body)
        {
            if (!ModelState.IsValid)
            {
                mAuditLogger.Warning("Bad request to Decrypt API: {sourceIP} {validationState}",
                    Request.HttpContext.Connection.RemoteIpAddress,
                    ModelState.ValidationState);
                return BadRequest("One or more of required fields doesn't present in the request body.");
            }

            var serviceAccountUserName = User.Claims.FirstOrDefault(claim => claim.Type == "name")?.Value;

            if (string.IsNullOrEmpty(serviceAccountUserName) ||
                !serviceAccountUserName.StartsWith(ServiceAccountUsernamePrefix, StringComparison.InvariantCulture))
            {
                mAuditLogger.Information("Unauthorized decrypt request, SourceIP: {sourceIp}, ServiceAccount User Name: {id}",
                    Request.HttpContext.Connection.RemoteIpAddress,
                    serviceAccountUserName);

                return StatusCode(403);
            }

            var id = serviceAccountUserName.Replace(ServiceAccountUsernamePrefix, "");
            int position = id.IndexOf(":");
            id = id.Substring(position + 1);

            mAuditLogger.Information("Decryption request started, SourceIP: {sourceIp}, ServiceAccount User Name: {id}",
                Request.HttpContext.Connection.RemoteIpAddress,
                id);

            try
            {
                var data = await mKeyManagement.Decrypt(body.EncryptedData, id);

                mAuditLogger.Information("Decryption request succeeded, SourceIP: {sourceIp}, ServiceAccount user Name: {sa}",
                    Request.HttpContext.Connection.RemoteIpAddress,
                    id);
                return Content(data);
            }
            catch (DecryptionFailureException e)
            {
                mLogger.Warning(e, "Decryption request failed, SourceIP: {sourceIp}, ServiceAccount: {sa}",
                    Request.HttpContext.Connection.RemoteIpAddress,
                    serviceAccountUserName);
                return StatusCode(400);
            }
        }


    }
}

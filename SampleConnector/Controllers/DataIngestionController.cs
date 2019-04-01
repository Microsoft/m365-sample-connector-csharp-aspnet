﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

namespace Sample.Connector
{
    using Microsoft.WindowsAzure.Storage.Table;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq.Expressions;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Web.Http;

    [ApiAuthorizationModule]
    public class DataIngestionController : ApiController
    {
        private readonly AzureTableProvider azureTableProvider;
        private readonly AzureStorageQueueProvider queueProvider;
        private CloudTable pageJobMappingTable;
        
        public DataIngestionController()
        {
            this.queueProvider = new AzureStorageQueueProvider(Settings.StorageAccountConnectionString, Settings.QueueName);
            this.azureTableProvider = new AzureTableProvider(Settings.StorageAccountConnectionString);
        }

        /// <summary>
        /// schedules the task for download and transform.
        /// </summary>
        /// <param name="request">Callback request body from M365 Connector platform</param>
        /// <returns></returns>
        [HttpPost]
        [ActionName("scheduletask")]
        public async Task<HttpResponseMessage> ScheduleTask([FromBody] ScheduleTaskRequest request)
        {
            Trace.TraceInformation($"Request came to Web for JobId: {request.JobId} and TaskId: {request.TaskId}");

            if (string.IsNullOrEmpty(Settings.AAdAppId) || string.IsNullOrEmpty(Settings.AAdAppSecret))
            {
                HttpResponseMessage response =  new HttpResponseMessage(HttpStatusCode.ExpectationFailed);
                response.Content = new StringContent("Connector is not configured. AAD Settings missing.");
                Trace.TraceError($"AAD Settings missing. Request failed for JobId: {request.JobId} and TaskId: {request.TaskId}.");
                return response;
            }

            PageJobEntity entity = await GetJobIdFromTable(request.JobId);
            if (entity == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            else
            {
                await queueProvider.InsertMessageAsync(JsonConvert.SerializeObject(new ConnectorTask
                {
                    TenantId = Settings.TenantId,
                    JobId = request.JobId,
                    TaskId = request.TaskId,
                    StartTime = request.StartTime,
                    EndTime = request.EndTime,
                    DirtyEntities = request.DirtyEntities,
                    BlobSasUri = request.BlobSasUri
                }));
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        private async Task<PageJobEntity> GetJobIdFromTable(string jobId)
        {
            Expression<Func<PageJobEntity, bool>> filter = (entity => entity.RowKey == jobId);
            pageJobMappingTable = azureTableProvider.GetAzureTableReference(Settings.PageJobMappingTableName);
            List<PageJobEntity> pageJobEntityList = await azureTableProvider.QueryEntitiesAsync<PageJobEntity>(pageJobMappingTable, filter);
            return pageJobEntityList?[0];
        }
    }
}
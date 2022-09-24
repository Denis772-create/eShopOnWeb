using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;
using Polly;

namespace OrderItemsReserver
{
    public class OrderItemsReserver
    {
        [FunctionName("OrderItemsReserver")]
        public async Task Run([ServiceBusTrigger("eshop")] OrderToWarehouse order)
        {
            var fallback = Policy
                .Handle<Exception>()
                .FallbackAsync(token =>
                    Task.Run(() => SendMessageToLogicApp(order.OrderId), token));

            var retry = Policy
                .Handle<Exception>()
                .RetryAsync(3);

            var result = fallback.WrapAsync(retry);

           await result.ExecuteAsync(() => SendToWarehouseBlob(order));
        }

        private async Task SendToWarehouseBlob(OrderToWarehouse order)
        {
            var blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
            var container = blobServiceClient.GetBlobContainerClient("eshop-warehouse");
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlobClient($"Order - {order.OrderId}.json");

            using var ms = new MemoryStream(
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(order)));

            await blob.UploadAsync(ms);
        }

        private async Task SendMessageToLogicApp(int orderId)
        {
            var logicAppUrl = Environment.GetEnvironmentVariable("LogicApp");
            var httpClient = new HttpClient();
            var result = await httpClient
                .PostAsync($"{logicAppUrl}",
                    JsonContent.Create(new { OrderId = orderId}));

            result.EnsureSuccessStatusCode();
        }

        public class OrderToWarehouse
        {
            public int OrderId { get; set; }
            public List<OrderItemDto> Items { get; set; }
        }

        public class OrderItemDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Units { get; set; }
        }
    }
}

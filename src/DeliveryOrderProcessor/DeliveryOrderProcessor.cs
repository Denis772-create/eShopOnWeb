using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DeliveryOrderProcessor
{
    public static class DeliveryOrderProcessor
    {
        [FunctionName("DeliveryOrderProcessor")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonConvert.DeserializeObject<DeliveryInfoDto>(requestBody);

            var mongoClient = new MongoClient(Environment.GetEnvironmentVariable("MONGO_CONNECTION"));
            var db = mongoClient.GetDatabase("eShop-DeliveryDb");

            var deliveries = db.GetCollection<DeliveryInfoDto>("delivery");
            await deliveries.InsertOneAsync(data);

            return new OkResult();
        }
    }

    public class DeliveryInfoDto
    {
        public decimal Price { get; set; }
        public Address ShippingAddress { get; set; }
        public List<OrderItem> Items { get; set; }

        public DeliveryInfoDto()
        { }
    }

    public class OrderItem
    {
        public int Id { get; set; }
        public CatalogItemOrdered ItemOrdered { get; set; }
        public decimal UnitPrice { get; set; }
        public int Units { get; set; }
    }

    public class CatalogItemOrdered
    {
        public int CatalogItemId { get; set; }
        public string ProductName { get; set; }
        public string PictureUri { get; set; }
    }

    public class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public string ZipCode { get; set; }
    }
}

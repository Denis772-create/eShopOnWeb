using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using BlazorShared;
using Microsoft.Azure.Amqp;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BaseUrlConfiguration _urls;
    private readonly IConfiguration _configuration;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusSender _busSender;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IHttpClientFactory httpClientFactory,
        IOptions<BaseUrlConfiguration> options,
        IConfiguration configuration)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _serviceBusClient = new ServiceBusClient(_configuration["ServiseBus"],
            new ServiceBusClientOptions { TransportType = ServiceBusTransportType.AmqpTcp });
        _busSender = _serviceBusClient.CreateSender("eshop");
        _urls = options.Value;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.GetBySpecAsync(basketSpec);

        Guard.Against.NullBasket(basketId, basket);
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);

        await SendToWarehouseAsync(order.Id, items);

        await SendToDeliveryProcessor(shippingAddress, items, order.Total());
    }

    private async Task SendToDeliveryProcessor(Address shippingAddress,
        List<OrderItem> items, decimal price)
    {
        var deliveryInfoDto = new DeliveryInfoDto(price, shippingAddress, items);

        var httpClient = _httpClientFactory.CreateClient();
        var result = await httpClient
            .PostAsync($"{_urls.DeliveryProcessorFunc}", 
                JsonContent.Create(deliveryInfoDto));

        result.EnsureSuccessStatusCode();
    }

    private class DeliveryInfoDto
    {
        public decimal Price { get; set; }
        public Address ShippingAddress { get; set; }
        public List<OrderItem> Items { get; set; }  

        public DeliveryInfoDto(decimal price, Address shippingAddress, List<OrderItem> items)
        {
            Price = price;
            ShippingAddress = shippingAddress;
            Items = items;
        }

        public DeliveryInfoDto()
        {

        }
    }

    private async Task SendToWarehouseAsync(int orderId, List<OrderItem> items)
    {
        var orderItemsDto = new List<OrderItemDto>();
        items.ForEach(x => orderItemsDto.Add(new OrderItemDto
        {
            Id = x.Id,
            Name = x.ItemOrdered.ProductName,
            Units = x.Units
        }));

        await _busSender.SendMessageAsync(
            new ServiceBusMessage(
                    JsonSerializer.Serialize(new OrderToWarehouse(orderItemsDto, orderId))));
    }

    private class OrderToWarehouse
    {
        public int OrderId { get; set; }
        public List<OrderItemDto> Items { get; set; }

        public OrderToWarehouse(List<OrderItemDto> items,
            int orderId)
        {
            Items = items;
            OrderId = orderId;
        }
    }

    private class OrderItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Units { get; set; }
    }


}

